using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

public sealed class SqliteProjectionDefinitionRegistry(
    DantesRoleplayDbContext db,
    IApplicationComponentTypeRegistry componentTypes,
    IBoundedJsonSchemaValidator validator,
    IApplicationRegistry? applications = null,
    ApplicationObjectDependencyIndexCache? objectDependencyIndices = null) : IProjectionDefinitionRegistry
{
    private const int MaximumGeneratedFieldProvenance = 1_024;
    private const int MaximumSchemaAlternatives = 32;
    private readonly Dictionary<(string QualifiedId, int Version), RegisteredProjectionDefinition> cache = [];

    public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId);
        var compilation = validator.Compile(definition.OutputSchemaJson);
        if (!compilation.IsAccepted) throw new ArgumentException("The projection output schema is not accepted by the bounded profile.");
        if (definition.ObjectContract is { } requestedContract)
        {
            if (requestedContract.ProfileId is not (RegisteredApplicationObjectContract.ContractProfileId
                or RegisteredApplicationObjectContract.FieldBasedContractProfileId))
                throw new ArgumentException("The application object contract profile is not supported.");
            if (requestedContract.ProfileId == RegisteredApplicationObjectContract.FieldBasedContractProfileId)
            {
                var transport = validator.Compile(RegisteredApplicationObjectContract.TransportSchemaJson);
                if (!transport.IsAccepted || compilation.ProfileId != transport.ProfileId
                    || compilation.SchemaHash != transport.SchemaHash
                    || compilation.NormalizedSchema != transport.NormalizedSchema)
                    throw new ArgumentException("A field-based application object must use the host-owned transport schema.");
            }
        }
        var fieldProvenance = ValidateInputs(definition);
        var objectContract = ValidateObjectContract(definition, compilation.NormalizedSchema, fieldProvenance);
        var contentHash = Hash(Canonical(definition, compilation.NormalizedSchema, objectContract));

        using var transaction = db.Database.BeginTransaction();
        if (!db.Set<ApplicationRegistryRecord>().Any(x => x.Id == definition.Owner.Value))
            throw new ArgumentException("A projection belongs to a registered application.");
        var identity = db.Set<ProjectionDefinitionRecord>().SingleOrDefault(x => x.QualifiedId == definition.QualifiedId);
        if (identity is not null && identity.ApplicationId != definition.Owner.Value)
            throw new InvalidOperationException("A qualified projection belongs to a different application.");
        var replay = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking()
            .Where(x => x.QualifiedId == definition.QualifiedId && x.ContentHash == contentHash).OrderBy(x => x.Version).FirstOrDefault();
        if (replay is not null)
        {
            if (definition.DeclaredVersion is int replayVersion && replay.Version != replayVersion)
                throw new ArgumentException("The declared object version does not match its immutable registered version.");
            transaction.Commit();
            return Remember(Read(replay, definition.Owner));
        }
        var version = db.Set<ProjectionDefinitionVersionRecord>().Where(x => x.QualifiedId == definition.QualifiedId).Max(x => (int?)x.Version).GetValueOrDefault() + 1;
        if (definition.DeclaredVersion is int declaredVersion && version != declaredVersion)
            throw new ArgumentException("Object versions must be registered contiguously from version one.");
        var now = DateTime.UtcNow;
        if (identity is null) db.Add(new ProjectionDefinitionRecord { QualifiedId = definition.QualifiedId, ApplicationId = definition.Owner.Value, CreatedAtUtc = now });
        var row = new ProjectionDefinitionVersionRecord { QualifiedId = definition.QualifiedId, Version = version, ProfileId = compilation.ProfileId, OutputSchemaJson = compilation.NormalizedSchema, OutputSchemaHash = compilation.SchemaHash, ContentHash = contentHash, ObjectContractJson = objectContract is null ? null : JsonSerializer.Serialize(objectContract), CreatedAtUtc = now };
        db.Add(row);
        foreach (var (input, ordinal) in definition.ComponentInputs.Select((x, i) => (x, i)))
            db.Add(new ProjectionComponentInputRecord { QualifiedId = definition.QualifiedId, Version = version, InputId = input.InputId, EntityRole = input.EntityRole, QualifiedTypeId = input.Type.QualifiedTypeId, TypeVersion = input.Type.TypeVersion, SchemaHash = input.Type.SchemaHash, Ordinal = ordinal });
        foreach (var (input, ordinal) in definition.DependencyInputs.Select((x, i) => (x, i)))
            db.Add(new ProjectionDependencyInputRecord { QualifiedId = definition.QualifiedId, Version = version, InputId = input.InputId, DependencyQualifiedId = input.Projection.QualifiedId, DependencyVersion = input.Projection.Version, DependencyContentHash = input.Projection.ContentHash, RoleBindingsJson = JsonSerializer.Serialize(input.RoleBindings.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value)), Ordinal = ordinal });
        foreach (var (mapping, ordinal) in definition.Mappings.Select((x, i) => (x, i)))
            db.Add(new ProjectionMappingRecord { QualifiedId = definition.QualifiedId, Version = version, TargetPointer = mapping.TargetPointer, InputId = mapping.InputId, SourcePointer = mapping.SourcePointer, Ordinal = ordinal });
        db.SaveChanges();
        db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO system_projection_registry_generation (ApplicationId, Generation)
            VALUES ({definition.Owner.Value}, 1)
            ON CONFLICT(ApplicationId) DO UPDATE SET Generation = Generation + 1;
            """);
        var generation = db.Set<ProjectionRegistryGenerationRecord>().AsNoTracking()
            .Where(x => x.ApplicationId == definition.Owner.Value).Select(x => x.Generation).Single();
        transaction.Commit();
        objectDependencyIndices?.ObserveGeneration(db.Database.GetDbConnection(), definition.Owner.Value,
            generation);
        return Remember(Read(row, definition.Owner));
    }

    public RegisteredProjectionDefinition? Get(string qualifiedId, int version)
    {
        if (string.IsNullOrWhiteSpace(qualifiedId) || version < 1) throw new ArgumentException("An exact projection ID and version are required.");
        if (cache.TryGetValue((qualifiedId, version), out var cached)) return cached;
        var row = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking().SingleOrDefault(x => x.QualifiedId == qualifiedId && x.Version == version);
        if (row is null) return null;
        var owner = db.Set<ProjectionDefinitionRecord>().AsNoTracking().Where(x => x.QualifiedId == qualifiedId).Select(x => x.ApplicationId).Single();
        return Remember(Read(row, ApplicationIdentifier.Parse(owner)));
    }

    private RegisteredProjectionDefinition Remember(RegisteredProjectionDefinition definition)
    {
        cache[(definition.QualifiedId, definition.Version)] = definition;
        return definition;
    }

    public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definitions = db.Set<ProjectionDefinitionRecord>().AsNoTracking().Where(x => x.ApplicationId == owner.Value).Select(x => x.QualifiedId).ToArray();
        var versions = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking().Where(x => definitions.Contains(x.QualifiedId)).ToArray();
        var keys = versions.Select(Key).ToHashSet(StringComparer.Ordinal);
        var forward = keys.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        var reverse = keys.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in db.Set<ProjectionDependencyInputRecord>().AsNoTracking().Where(x => definitions.Contains(x.QualifiedId)))
        {
            var from = edge.QualifiedId + "@" + edge.Version; var to = edge.DependencyQualifiedId + "@" + edge.DependencyVersion;
            if (keys.Contains(from) && keys.Contains(to)) { forward[from].Add(to); reverse[to].Add(from); }
        }
        return new ProjectionImpactGraph(Frozen(forward), Frozen(reverse));
    }

    public ApplicationObjectDiscovery? Discover(ProjectionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        var definition = Get(reference.QualifiedId, reference.Version);
        if (definition is null || definition.ContentHash != reference.ContentHash
            || definition.ObjectContract is not { IsFieldBased: true, FieldProvenance: { } fields })
            return null;

        var declaredReferences = fields.Select(value => value.Component)
            .Concat(definition.ObjectContract.Relationships.SelectMany(value =>
                value.RequiredEndpointComponents.Concat(value.OptionalEndpointComponents)
                    .Select(endpoint => endpoint.Type))).Distinct().ToArray();
        var declaredTypes = ReadRegisteredVersions(declaredReferences, definition.Owner);
        var discoveredFields = fields.Concat(CollectionFieldProvenance(definition, declaredTypes)).ToArray();
        if (discoveredFields.Length > MaximumGeneratedFieldProvenance)
            throw new InvalidOperationException("Application object discovery exceeds its field bound.");

        var sourceKeys = discoveredFields.Select(field => new
            {
                Path = string.Join("\u001F", field.InputPath),
                field.EntityRole,
                field.Required,
                field.Component.QualifiedTypeId,
                field.Component.TypeVersion,
                field.Component.SchemaHash
            })
            .Distinct()
            .OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.EntityRole, StringComparer.Ordinal)
            .ThenBy(value => value.QualifiedTypeId, StringComparer.Ordinal)
            .ThenBy(value => value.TypeVersion)
            .ThenBy(value => value.SchemaHash, StringComparer.Ordinal)
            .ThenBy(value => value.Required)
            .ToArray();
        if (sourceKeys.Length > 256)
            throw new InvalidOperationException("Application object discovery exceeds its source bound.");

        var sources = new List<ApplicationObjectSourceDiscovery>(sourceKeys.Length);
        for (var index = 0; index < sourceKeys.Length; index++)
        {
            var key = sourceKeys[index];
            var component = new EcsComponentReference(key.QualifiedTypeId, key.TypeVersion, key.SchemaHash);
            if (!declaredTypes.TryGetValue(component, out var registered))
                throw new InvalidOperationException("Application object discovery source provenance is stale.");
            var sourceFields = discoveredFields.Where(field => field.EntityRole == key.EntityRole
                    && string.Join("\u001F", field.InputPath) == key.Path
                    && field.Required == key.Required
                    && field.Component.QualifiedTypeId == key.QualifiedTypeId
                    && field.Component.TypeVersion == key.TypeVersion
                    && field.Component.SchemaHash == key.SchemaHash)
                .OrderBy(field => string.Join("/", field.InputPath), StringComparer.Ordinal).ToArray();
            var sourceId = $"source-{index + 1}";
            sources.Add(new(sourceId, sourceFields[0].InputPath, key.EntityRole, key.Required,
                sourceFields[0].Component, registered.ProfileId, registered.SchemaJson));
        }

        return new(definition.Reference, definition.ObjectContract.ProfileId,
            Array.AsReadOnly(sources.ToArray()), Array.AsReadOnly(discoveredFields),
            definition.ObjectContract.GeneratedWriteMappings);
    }

    public ApplicationObjectReadEvidence? DiscoverRead(
        ProjectionReference reference,
        IReadOnlyList<ProjectionObservedSource> observedSources,
        IReadOnlyList<ProjectionMappedFieldEvidence> fields,
        string outputJson)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(observedSources);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(outputJson);
        var discovery = Discover(reference);
        if (discovery is null || componentTypes is not IApplicationComponentTypeVersionReader versions)
            return null;
        if (observedSources.Count > 512 || fields.Count > MaximumGeneratedFieldProvenance)
            return new(reference, discovery.ProfileId, "budget-exceeded", [], []);
        if (fields.Any(value => value.Availability is not ("value" or "null" or "absent-source" or "absent-path")))
            throw new InvalidOperationException("Actual object field availability is invalid.");

        static string Path(IReadOnlyList<string> value) => string.Join("\u001F", value);
        static bool SameDeclaration(ProjectionObservedSource observed, ApplicationObjectSourceDiscovery declared) =>
            Path(observed.InputPath) == Path(declared.InputPath)
            && observed.EntityRole == declared.EntityRole
            && observed.Required == declared.Required
            && observed.DeclaredComponent == declared.Component;

        var relevantObservations = observedSources.Where(observed =>
            discovery.Sources.Any(source => SameDeclaration(observed, source))).ToArray();
        if (relevantObservations.Any(observed => observed.ActualComponent is { } actual
                && actual.QualifiedTypeId != observed.DeclaredComponent.QualifiedTypeId))
            throw new InvalidOperationException("Actual object read evidence does not match its declared source authority.");

        var actualReferences = relevantObservations.Where(value => value.ActualComponent is not null)
            .Select(value => value.ActualComponent!).Distinct().ToArray();
        if (actualReferences.Length > 512)
            return new(reference, discovery.ProfileId, "budget-exceeded", [], []);
        var registered = versions.ReadVersions(actualReferences).ToDictionary(
            value => new EcsComponentReference(value.QualifiedId, value.Version, value.SchemaHash));
        if (registered.Count != actualReferences.Length
            || actualReferences.Any(value => !registered.TryGetValue(value, out var type)
                || !OwnsOrComposes(Get(reference.QualifiedId, reference.Version)!.Owner, type.Owner)))
            throw new InvalidOperationException("Actual object read schema provenance is stale.");
        if (registered.Values.Sum(value => Encoding.UTF8.GetByteCount(value.SchemaJson)) > 2 * 1024 * 1024)
            return new(reference, discovery.ProfileId, "budget-exceeded", [], []);

        var readSources = discovery.Sources.Select(source =>
        {
            var observations = relevantObservations.Where(value => SameDeclaration(value, source)).ToArray();
            var available = observations.Where(value => value.ActualComponent is not null)
                .Select(value => value.ActualComponent!).Distinct()
                .OrderBy(value => value.QualifiedTypeId, StringComparer.Ordinal)
                .ThenBy(value => value.TypeVersion).ThenBy(value => value.SchemaHash, StringComparer.Ordinal)
                .Select(value =>
                {
                    var type = registered[value];
                    return new ApplicationObjectActualSource(value, type.ProfileId, type.SchemaJson);
                }).ToArray();
            var absent = observations.Any(value => value.ActualComponent is null);
            var collectionSource = source.InputPath.Count == 4 && source.InputPath[0] == "collection";
            var availability = observations.Length == 0 ? collectionSource ? "unobserved" : "unavailable"
                : available.Length == 0 ? "absent-source"
                : absent ? "partial" : "available";
            return new ApplicationObjectReadSourceEvidence(source.SourceId, source.InputPath,
                source.EntityRole, source.Required, source.Component, availability,
                Array.AsReadOnly(available));
        }).ToArray();

        using var output = JsonDocument.Parse(outputJson);
        var readFields = discovery.Fields.Select(field =>
        {
            var source = discovery.Sources.Single(value => Path(value.InputPath) == Path(field.InputPath)
                && value.EntityRole == field.EntityRole && value.Required == field.Required
                && value.Component == field.Component);
            var direct = fields.Where(value => value.Composition == reference
                    && value.TargetPointer == field.ObjectPointer)
                .Select(value => value.Availability).Distinct(StringComparer.Ordinal).ToArray();
            var availability = direct.Length > 0 ? direct : ReadAvailability(output.RootElement, field.ObjectPointer);
            var sourceState = readSources.Single(value => value.SourceId == source.SourceId).Availability;
            if (sourceState == "absent-source" && availability.All(value => value == "absent-path"))
                availability = ["absent-source"];
            return new ApplicationObjectReadFieldEvidence(field.ObjectPointer, source.SourceId,
                field.ComponentPointer, Array.AsReadOnly(availability.Order(StringComparer.Ordinal).ToArray()));
        }).ToArray();
        var evidenceAvailability = readSources.Any(value => value.Availability == "unavailable")
            ? "unavailable" : "available";
        return new(reference, discovery.ProfileId, evidenceAvailability,
            Array.AsReadOnly(readSources), Array.AsReadOnly(readFields));
    }

    internal static string[] ReadAvailability(JsonElement root, string pointer)
    {
        var values = new List<string>();
        Visit(root, pointer == "" ? [] : pointer[1..].Split('/'), 0, values);
        return values.Count == 0 ? ["unobserved"] : values.Distinct(StringComparer.Ordinal).ToArray();

        static void Visit(JsonElement current, IReadOnlyList<string> tokens, int index, List<string> result)
        {
            if (index == tokens.Count)
            {
                result.Add(current.ValueKind == JsonValueKind.Null ? "null" : "value");
                return;
            }
            var token = DecodePointerToken(tokens[index]);
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out var child))
                {
                    result.Add("absent-path");
                    return;
                }
                Visit(child, tokens, index + 1, result);
                return;
            }
            if (token == "*" && current.ValueKind == JsonValueKind.Array)
            {
                var any = false;
                foreach (var item in current.EnumerateArray())
                {
                    any = true;
                    Visit(item, tokens, index + 1, result);
                }
                if (!any) result.Add("unobserved");
                return;
            }
            result.Add("absent-path");
        }
    }

    private IReadOnlyDictionary<EcsComponentReference, RegisteredComponentTypeVersion> ReadRegisteredVersions(
        IReadOnlyList<EcsComponentReference> references,
        ApplicationIdentifier owner)
    {
        if (references.Count > 512)
            throw new InvalidOperationException("Application object discovery exceeds its source bound.");
        IReadOnlyList<RegisteredComponentTypeVersion> values = componentTypes is IApplicationComponentTypeVersionReader batch
            ? batch.ReadVersions(references)
            : references.Select(value => componentTypes.Get(value.QualifiedTypeId, value.TypeVersion))
                .Where(value => value is not null).Select(value => value!).ToArray();
        var result = values.ToDictionary(value =>
            new EcsComponentReference(value.QualifiedId, value.Version, value.SchemaHash));
        if (references.Any(reference => !result.TryGetValue(reference, out var value)
                || !OwnsOrComposes(owner, value.Owner)))
            throw new InvalidOperationException("Application object discovery source provenance is stale.");
        return result;
    }

    private IReadOnlyList<ApplicationObjectFieldProvenance> CollectionFieldProvenance(
        RegisteredProjectionDefinition definition,
        IReadOnlyDictionary<EcsComponentReference, RegisteredComponentTypeVersion> registeredTypes)
    {
        var contract = definition.ObjectContract!;
        var result = new List<ApplicationObjectFieldProvenance>();
        foreach (var collection in contract.Collections)
        {
            var relationship = contract.Relationships.Single(value => value.RelationshipId == collection.SourceId);
            var itemEndpoint = relationship.Direction == "incoming" ? "from" : "to";
            var itemRole = itemEndpoint == "from" ? relationship.FromRole : relationship.ToRole;
            var protectedFields = contract.Relationships
                .Where(value => value.TargetPointer.StartsWith(relationship.TargetPointer + "/*/", StringComparison.Ordinal))
                .Select(value => value.TargetPointer[(relationship.TargetPointer.Length + 3)..])
                .Where(value => !value.Contains('/')).Select(DecodePointerToken)
                .ToHashSet(StringComparer.Ordinal);
            var required = relationship.RequiredEndpointComponents
                .Where(value => value.Endpoint == itemEndpoint).Select(value => value.Type).ToHashSet();
            foreach (var endpoint in relationship.RequiredEndpointComponents
                         .Concat(relationship.OptionalEndpointComponents)
                         .Where(value => value.Endpoint == itemEndpoint))
            {
                if (!registeredTypes.TryGetValue(endpoint.Type, out var registered))
                    throw new InvalidOperationException("Application object collection discovery source is stale.");
                var properties = DeclaredTopLevelProperties(registered.SchemaJson)
                    ?? throw new InvalidOperationException("Application object collection discovery lacks bounded field provenance.");
                foreach (var property in properties.Where(value => value is not ("id" or "name")
                             && !protectedFields.Contains(value)).Order(StringComparer.Ordinal))
                    result.Add(new(
                        relationship.TargetPointer + "/*/" + EncodePointerToken(property),
                        Array.AsReadOnly(new[] { "collection", collection.CollectionId,
                            relationship.RelationshipId, itemEndpoint }),
                        itemRole, endpoint.Type, "/" + EncodePointerToken(property),
                        required.Contains(endpoint.Type)));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private RegisteredProjectionDefinition Read(ProjectionDefinitionVersionRecord row, ApplicationIdentifier owner)
    {
        var components = db.Set<ProjectionComponentInputRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal)
            .Select(x => new ProjectionComponentInput(x.InputId, x.EntityRole, new EcsComponentReference(x.QualifiedTypeId, x.TypeVersion, x.SchemaHash))).ToArray();
        var dependencies = db.Set<ProjectionDependencyInputRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal).ToArray()
            .Select(x => new ProjectionDependencyInput(x.InputId, new ProjectionReference(x.DependencyQualifiedId, x.DependencyVersion, x.DependencyContentHash), JsonSerializer.Deserialize<Dictionary<string, string>>(x.RoleBindingsJson) ?? [])).ToArray();
        var mappings = db.Set<ProjectionMappingRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal)
            .Select(x => new StructuralProjectionMapping(x.InputId, x.SourcePointer, x.TargetPointer)).ToArray();
        var objectContract = row.ObjectContractJson is null ? null
            : JsonSerializer.Deserialize<RegisteredApplicationObjectContract>(row.ObjectContractJson)
              ?? throw new InvalidOperationException("The registered application object contract is unavailable.");
        return new(owner, row.QualifiedId, row.Version, row.ProfileId, row.OutputSchemaJson, row.OutputSchemaHash, row.ContentHash, Array.AsReadOnly(components), Array.AsReadOnly(dependencies), Array.AsReadOnly(mappings), row.CreatedAtUtc, objectContract);
    }

    private IReadOnlyList<ApplicationObjectFieldProvenance>? ValidateInputs(ProjectionDefinitionRequest definition)
    {
        if (definition.ComponentInputs is null || definition.DependencyInputs is null || definition.Mappings is null || definition.ComponentInputs.Count + definition.DependencyInputs.Count > 32 || definition.Mappings.Count is < 1 or > 128)
            throw new ArgumentException("Projection inputs and mappings exceed fixed bounds.");
        var ids = definition.ComponentInputs.Select(x => x.InputId).Concat(definition.DependencyInputs.Select(x => x.InputId)).ToArray();
        if (ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || definition.ComponentInputs.Any(x => string.IsNullOrWhiteSpace(x.EntityRole) || x.EntityRole.Length > 200))
            throw new ArgumentException("Projection input IDs and roles must be unique and bounded.");
        foreach (var input in definition.ComponentInputs)
        {
            input.Type.Validate(); var type = componentTypes.Get(input.Type.QualifiedTypeId, input.Type.TypeVersion);
            if (type is null || !OwnsOrComposes(definition.Owner, type.Owner) || type.SchemaHash != input.Type.SchemaHash) throw new ArgumentException("Projection component inputs require exact owner-composed registered types.");
        }
        var fieldBased = definition.ObjectContract?.ProfileId == RegisteredApplicationObjectContract.FieldBasedContractProfileId;
        if (fieldBased && (definition.ObjectContract!.Sources is null
                || definition.ObjectContract.References is null))
            throw new ArgumentException("A field-based application object requires explicit source and reference declarations.");
        if (fieldBased && definition.ComponentInputs
                .Select(input => (input.EntityRole, input.Type.QualifiedTypeId))
                .Distinct().Count() != definition.ComponentInputs.Count)
            throw new ArgumentException("Field-based component sources must be unique by role and qualified type identity.");
        var sourceSchemas = definition.ComponentInputs.ToDictionary(x => x.InputId, x => componentTypes.Get(x.Type.QualifiedTypeId, x.Type.TypeVersion)!.SchemaJson, StringComparer.Ordinal);
        var dependencyDefinitions = new Dictionary<string, RegisteredProjectionDefinition>(StringComparer.Ordinal);
        foreach (var input in definition.DependencyInputs)
        {
            input.Projection.Validate(); if (input.RoleBindings is null || input.RoleBindings.Count > 64
                || input.RoleBindings.Values.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200)) throw new ArgumentException("Projection role bindings exceed fixed bounds.");
            var dependency = Get(input.Projection.QualifiedId, input.Projection.Version);
            if (dependency is null || dependency.Owner != definition.Owner || dependency.ContentHash != input.Projection.ContentHash || !input.RoleBindings.Keys.Order().SequenceEqual(dependency.EntityRoles.Order(), StringComparer.Ordinal) || input.RoleBindings.Values.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Projection dependencies require exact local references and closed role bindings.");
            if (1 + DependencyDepth(dependency, new Dictionary<string, int>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)) > 16)
                throw new ArgumentException("Projection dependency depth exceeds the fixed bound.");
            sourceSchemas.Add(input.InputId, dependency.OutputSchemaJson);
            dependencyDefinitions.Add(input.InputId, dependency);
        }
        if (definition.Mappings.Select(x => x.TargetPointer).Distinct(StringComparer.Ordinal).Count() != definition.Mappings.Count || definition.Mappings.Any(x => !ids.Contains(x.InputId, StringComparer.Ordinal)
                || x.SourcePointer.Length > 1000 || x.TargetPointer.Length > 1000
                || !Pointer(x.SourcePointer) || !Pointer(x.TargetPointer) || (!string.IsNullOrEmpty(x.TargetPointer) && x.TargetPointer.Split('/').Skip(1).Any(IsArrayIndex))))
            throw new ArgumentException("Mappings must structurally copy declared JSON pointers to unique object targets.");
        if (fieldBased && definition.Mappings.Any(mapping =>
                !SafePointer(mapping.SourcePointer, allowWildcard: false)
                || !SafePointer(mapping.TargetPointer, allowWildcard: false)))
            throw new ArgumentException("Field-based mappings require safe concrete JSON pointers.");
        if (definition.Mappings.Any(x => x.TargetPointer == "") && definition.Mappings.Count != 1) throw new ArgumentException("A root mapping must be the only mapping.");
        if (definition.Mappings.Any(mapping => !SourcePathExists(mapping)))
            throw new ArgumentException("Projection source pointers must exist in their exact declared schema.");
        if (!fieldBased) return null;
        var provenance = BuildFieldProvenance(definition, dependencyDefinitions);
        if (definition.Mappings.SingleOrDefault(mapping => mapping.TargetPointer == "") is { } rootMapping)
        {
            var objectSource = dependencyDefinitions.TryGetValue(rootMapping.InputId, out var dependency)
                ? DependencyPathDeclaresObject(dependency, rootMapping.SourcePointer)
                : SchemaPathDeclaresObject(sourceSchemas[rootMapping.InputId], rootMapping.SourcePointer);
            if (!objectSource)
                throw new ArgumentException("A field-based root mapping must copy a value declared as an object.");
        }
        return provenance;

        bool SourcePathExists(StructuralProjectionMapping mapping)
        {
            if (!fieldBased || !dependencyDefinitions.TryGetValue(mapping.InputId, out var dependency)
                || dependency.ObjectContract?.IsFieldBased != true)
                return ProjectionSchemaPath.Exists(sourceSchemas[mapping.InputId], mapping.SourcePointer);
            return DependencyPathExists(dependency, mapping.SourcePointer);
        }
    }

    private IReadOnlyList<ApplicationObjectFieldProvenance>? BuildFieldProvenance(
        ProjectionDefinitionRequest definition,
        IReadOnlyDictionary<string, RegisteredProjectionDefinition> dependencies)
    {
        var sourceRequired = definition.ObjectContract!.Sources
            .ToDictionary(value => value.InputId, value => value.Required, StringComparer.Ordinal);
        var referenceRequired = definition.ObjectContract.References
            .ToDictionary(value => value.InputId, value => value.Required, StringComparer.Ordinal);
        var components = definition.ComponentInputs.ToDictionary(value => value.InputId, StringComparer.Ordinal);
        var dependencyInputs = definition.DependencyInputs.ToDictionary(value => value.InputId, StringComparer.Ordinal);
        var result = new List<ApplicationObjectFieldProvenance>();
        foreach (var mapping in definition.Mappings)
        {
            if (components.TryGetValue(mapping.InputId, out var component))
            {
                result.Add(new(mapping.TargetPointer, Array.AsReadOnly(new[] { mapping.InputId }),
                    component.EntityRole, component.Type, mapping.SourcePointer,
                    sourceRequired.TryGetValue(mapping.InputId, out var required) && required));
                continue;
            }

            var dependency = dependencies[mapping.InputId];
            var input = dependencyInputs[mapping.InputId];
            var childFields = dependency.ObjectContract?.FieldProvenance;
            if (childFields is null)
            {
                // Previously registered v2 rows can still be copied at the root, but they cannot
                // acquire invented field provenance or authorize a non-root dependency selector.
                if (mapping.SourcePointer != "")
                    throw new ArgumentException("A field-based dependency path lacks generated source provenance.");
                return null;
            }
            foreach (var child in childFields)
            {
                if (!TryProjectField(mapping, child, out var objectPointer, out var componentPointer))
                    continue;
                var registered = componentTypes.Get(child.Component.QualifiedTypeId, child.Component.TypeVersion);
                if (registered is null || registered.SchemaHash != child.Component.SchemaHash
                    || !ProjectionSchemaPath.Exists(registered.SchemaJson, componentPointer))
                    throw new ArgumentException("A field-based dependency contains stale source provenance.");
                if (!input.RoleBindings.TryGetValue(child.EntityRole, out var role))
                    throw new ArgumentException("A field-based dependency provenance role is unbound.");
                var path = new[] { mapping.InputId }.Concat(child.InputPath).ToArray();
                result.Add(new(objectPointer, Array.AsReadOnly(path), role, child.Component,
                    componentPointer,
                    referenceRequired.TryGetValue(mapping.InputId, out var required) && required && child.Required));
                if (result.Count > MaximumGeneratedFieldProvenance)
                    throw new ArgumentException("Generated application object field provenance exceeds its fixed bound.");
            }
        }
        return Array.AsReadOnly(result.OrderBy(value => value.ObjectPointer, StringComparer.Ordinal)
            .ThenBy(value => string.Join("/", value.InputPath), StringComparer.Ordinal)
            .ThenBy(value => value.ComponentPointer, StringComparer.Ordinal).ToArray());
    }

    private bool DependencyPathExists(RegisteredProjectionDefinition dependency, string pointer)
    {
        var fields = dependency.ObjectContract?.FieldProvenance;
        if (fields is null) return pointer == "";
        foreach (var field in fields)
        {
            if (PointerContains(pointer, field.ObjectPointer)) return true;
            if (!PointerContains(field.ObjectPointer, pointer)) continue;
            var componentPointer = AppendPointer(field.ComponentPointer,
                RelativePointer(field.ObjectPointer, pointer));
            var type = componentTypes.Get(field.Component.QualifiedTypeId, field.Component.TypeVersion);
            if (type is not null && type.SchemaHash == field.Component.SchemaHash
                && ProjectionSchemaPath.Exists(type.SchemaJson, componentPointer)) return true;
        }
        return false;
    }

    private bool DependencyPathDeclaresObject(RegisteredProjectionDefinition dependency, string pointer)
    {
        var fields = dependency.ObjectContract?.FieldProvenance;
        if (fields is null) return pointer == "";
        if (fields.Any(field => field.ObjectPointer != pointer && PointerContains(pointer, field.ObjectPointer)))
            return true;
        foreach (var field in fields.Where(field => PointerContains(field.ObjectPointer, pointer)))
        {
            var componentPointer = AppendPointer(field.ComponentPointer,
                RelativePointer(field.ObjectPointer, pointer));
            var type = componentTypes.Get(field.Component.QualifiedTypeId, field.Component.TypeVersion);
            if (type is not null && type.SchemaHash == field.Component.SchemaHash
                && SchemaPathDeclaresObject(type.SchemaJson, componentPointer)) return true;
        }
        return false;
    }

    private static bool TryProjectField(
        StructuralProjectionMapping mapping,
        ApplicationObjectFieldProvenance child,
        out string objectPointer,
        out string componentPointer)
    {
        if (PointerContains(child.ObjectPointer, mapping.SourcePointer))
        {
            objectPointer = mapping.TargetPointer;
            componentPointer = AppendPointer(child.ComponentPointer,
                RelativePointer(child.ObjectPointer, mapping.SourcePointer));
            return true;
        }
        if (PointerContains(mapping.SourcePointer, child.ObjectPointer))
        {
            objectPointer = AppendPointer(mapping.TargetPointer,
                RelativePointer(mapping.SourcePointer, child.ObjectPointer));
            componentPointer = child.ComponentPointer;
            return true;
        }
        objectPointer = "";
        componentPointer = "";
        return false;
    }

    private static bool PointerContains(string container, string value) =>
        container == "" || value == container || value.StartsWith(container + "/", StringComparison.Ordinal);

    private static string EncodePointerToken(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static string DecodePointerToken(string value) => value
        .Replace("~1", "/", StringComparison.Ordinal)
        .Replace("~0", "~", StringComparison.Ordinal);

    private static string RelativePointer(string container, string value) =>
        container == "" ? value : value[container.Length..];

    private static string AppendPointer(string prefix, string suffix) =>
        prefix == "" ? suffix : suffix == "" ? prefix : prefix + suffix;

    private RegisteredApplicationObjectContract? ValidateObjectContract(
        ProjectionDefinitionRequest definition,
        string normalizedOutputSchema,
        IReadOnlyList<ApplicationObjectFieldProvenance>? fieldProvenance)
    {
        var value = definition.ObjectContract;
        if (value is null)
        {
            if (definition.DeclaredVersion is not null)
                throw new ArgumentException("Only application object definitions declare their catalog version.");
            return null;
        }
        if (definition.DeclaredVersion is not >= 1)
            throw new ArgumentException("An application object requires a positive declared version.");
        var fieldBased = value.ProfileId == RegisteredApplicationObjectContract.FieldBasedContractProfileId;
        if (value.Roles is null || value.Sources is null || value.Relationships is null
            || value.References is null || value.Collections is null || value.Limits is null
            || value.Access is null)
            throw new ArgumentException("An application object contract is incomplete.");

        var roles = value.Roles.ToArray();
        var roleIds = roles.Select(x => x.RoleId).ToArray();
        if (roles.Length is < 1 or > 32 || roleIds.Any(x => !Identifier(x, 200))
            || roleIds.Distinct(StringComparer.Ordinal).Count() != roleIds.Length)
            throw new ArgumentException("Application object roles must be unique and bounded.");
        var usedRoles = definition.ComponentInputs.Select(x => x.EntityRole)
            .Concat(definition.DependencyInputs.SelectMany(x => x.RoleBindings.Values))
            .Concat(value.Relationships.SelectMany(x => new[] { x.FromRole, x.ToRole }))
            .ToHashSet(StringComparer.Ordinal);
        if (!usedRoles.SetEquals(roleIds))
            throw new ArgumentException("Application object roles must exactly cover declared sources, references, and relationships.");

        var sourceIds = definition.ComponentInputs.Select(x => x.InputId).ToArray();
        if (value.Sources.Count != sourceIds.Length
            || !value.Sources.Select(x => x.InputId).ToHashSet(StringComparer.Ordinal).SetEquals(sourceIds)
            || value.Sources.Select(x => x.InputId).Distinct(StringComparer.Ordinal).Count() != value.Sources.Count)
            throw new ArgumentException("Application object sources must exactly describe the projection component inputs.");
        var referenceIds = definition.DependencyInputs.Select(x => x.InputId).ToArray();
        if (value.References.Count != referenceIds.Length
            || !value.References.Select(x => x.InputId).ToHashSet(StringComparer.Ordinal).SetEquals(referenceIds)
            || value.References.Select(x => x.InputId).Distinct(StringComparer.Ordinal).Count() != value.References.Count)
            throw new ArgumentException("Application object references must exactly describe the projection dependencies.");
        if (definition.DependencyInputs.Any(x => x.Projection.QualifiedId == definition.QualifiedId))
            throw new ArgumentException("Application object references must be acyclic.");

        var relationships = value.Relationships.ToArray();
        if (relationships.Length > 32
            || relationships.Select(x => x.RelationshipId).Distinct(StringComparer.Ordinal).Count() != relationships.Length
            || relationships.Select(x => x.TargetPointer).Distinct(StringComparer.Ordinal).Count() != relationships.Length)
            throw new ArgumentException("Application object relationships must be unique and bounded.");
        foreach (var relationship in relationships)
        {
            if (!Identifier(relationship.RelationshipId, 200) || !Identifier(relationship.QualifiedKind, 200)
                || !roleIds.Contains(relationship.FromRole, StringComparer.Ordinal)
                || !roleIds.Contains(relationship.ToRole, StringComparer.Ordinal)
                || relationship.Direction is not (null or "outgoing" or "incoming")
                || relationship.Cardinality is not ("one" or "zero-or-one" or "many")
                || !Pointer(relationship.TargetPointer)
                || fieldBased && !SafePointer(relationship.TargetPointer, allowWildcard: true)
                || fieldBased && relationship.TargetPointer == ""
                || !fieldBased && !ProjectionSchemaPath.Exists(normalizedOutputSchema, relationship.TargetPointer))
                throw new ArgumentException("An application object relationship declaration is invalid.");
            ValidateEndpointComponents(definition.Owner, relationship, fieldBased);
        }
        var objectMappings = definition.Mappings.ToArray();
        for (var left = 0; left < objectMappings.Length; left++)
        for (var right = left + 1; right < objectMappings.Length; right++)
            if (Overlaps(objectMappings[left].TargetPointer, objectMappings[right].TargetPointer))
                throw new ArgumentException("Application object mappings cannot write overlapping output paths.");

        var collections = value.Collections.ToArray();
        if (collections.Length > 8
            || collections.Select(x => x.CollectionId).Distinct(StringComparer.Ordinal).Count() != collections.Length)
            throw new ArgumentException("Application object collections must be unique and bounded.");
        foreach (var collection in collections)
        {
            if (!Identifier(collection.CollectionId, 200)
                || !relationships.Any(x => x.RelationshipId == collection.SourceId && x.Cardinality == "many")
                || collection.PageSize is < 1 or > 500 || collection.MaximumPageSize is < 1 or > 500
                || collection.PageSize > collection.MaximumPageSize || collection.Order is null
                || collection.Order.Count is < 1 or > 4
                || collection.Order.Any(x => !Pointer(x.Pointer) || x.Direction is not ("asc" or "desc"))
                || fieldBased && collection.Order.Any(x => !SafePointer(x.Pointer, allowWildcard: false))
                || collection.Order.Select(x => x.Pointer).Distinct(StringComparer.Ordinal).Count() != collection.Order.Count
                || collection.Cursor != "source-revision-bound"
                || fieldBased != (collection.Metadata is not null))
                throw new ArgumentException("An application object collection declaration is invalid or unbounded.");
            if (collection.Metadata is { } metadata)
            {
                var pointers = new[] { metadata.TotalCount, metadata.Complete, metadata.NextCursor };
                if (pointers.Any(pointer => !ConcreteObjectPointer(pointer))
                    || pointers.Distinct(StringComparer.Ordinal).Count() != pointers.Length)
                    throw new ArgumentException("Field-based collection metadata requires distinct concrete object pointers.");
                for (var left = 0; left < pointers.Length; left++)
                for (var right = left + 1; right < pointers.Length; right++)
                    if (Overlaps(pointers[left], pointers[right]))
                    throw new ArgumentException("Field-based collection metadata pointers cannot overlap.");
            }
            if (fieldBased)
                ValidateFieldBasedCollectionShape(collection,
                    relationships.Single(relationship => relationship.RelationshipId == collection.SourceId));
        }
        if (fieldBased)
        {
            if (relationships.Any(relationship => relationship.TargetPointer.Split('/').Contains("*", StringComparer.Ordinal)
                    && !relationships.Any(parent => NestedCollectionRelationship(parent, relationship, collections))))
                throw new ArgumentException("A field-based wildcard relationship must target one declared collection row property.");
            for (var left = 0; left < relationships.Length; left++)
            for (var right = left + 1; right < relationships.Length; right++)
                if (Overlaps(relationships[left].TargetPointer, relationships[right].TargetPointer)
                    && !NestedCollectionRelationship(relationships[left], relationships[right], collections)
                    && !NestedCollectionRelationship(relationships[right], relationships[left], collections))
                    throw new ArgumentException("Field-based relationship targets cannot overlap outside one declared collection row.");

            var mappingTargets = objectMappings.Select(mapping => mapping.TargetPointer).ToArray();
            var relationshipTargets = relationships.Select(relationship => relationship.TargetPointer).ToArray();
            var metadataTargets = collections.SelectMany(collection => new[]
            {
                collection.Metadata!.TotalCount,
                collection.Metadata.Complete,
                collection.Metadata.NextCursor
            }).ToArray();
            if (mappingTargets.Any(mapping => relationshipTargets.Any(relationship => Overlaps(mapping, relationship)))
                || metadataTargets.Any(metadata => mappingTargets.Concat(relationshipTargets)
                    .Any(target => Overlaps(metadata, target))))
                throw new ArgumentException("Field-based mappings, relationships, and host metadata require disjoint target ownership.");
            for (var left = 0; left < metadataTargets.Length; left++)
            for (var right = left + 1; right < metadataTargets.Length; right++)
                if (Overlaps(metadataTargets[left], metadataTargets[right]))
                    throw new ArgumentException("Field-based collection metadata targets must be globally disjoint.");
        }
        if (value.Limits.TraversalDepth is < 1 or > 16 || value.Limits.ItemCount is < 1 or > 10_000
            || value.Limits.OutputBytes is < 1 or > SystemJsonSchemaProfile.MaximumValueBytes
            || value.Limits.SqlQueries is < 1 or > 64)
            throw new ArgumentException("Application object resource limits exceed the supported profile.");
        var perspectives = new[] { "player", "dm" };
        foreach (var relationship in relationships.Where(x => x.ReadPerspectives is not null))
        {
            // Narrowing is supported only for nested reference arrays. It cannot silently
            // change a root collection, structural mapping, or its write semantics.
            if (relationship.ReadPerspectives!.Count is < 1 or > 2 ||
                relationship.ReadPerspectives.Distinct(StringComparer.Ordinal).Count() != relationship.ReadPerspectives.Count ||
                relationship.ReadPerspectives.Any(x => !perspectives.Contains(x, StringComparer.Ordinal)) ||
                relationship.Cardinality != "many" ||
                !collections.Any(collection => relationships.Any(parent => parent.RelationshipId == collection.SourceId &&
                    relationship.TargetPointer.StartsWith(parent.TargetPointer + "/*/", StringComparison.Ordinal) &&
                    relationship.TargetPointer[(parent.TargetPointer.Length + 3)..].IndexOf('/') < 0 &&
                    (relationship.Direction == "incoming" ? relationship.ToRole : relationship.FromRole) ==
                    (parent.Direction == "incoming" ? parent.FromRole : parent.ToRole))))
                throw new ArgumentException("Relationship read access must narrow a declared nested reference array.");
        }
        if (value.Access.ReadPerspectives is null || value.Access.WritePerspectives is null
            || value.Access.ReadPerspectives.Count is < 1 or > 2 || value.Access.WritePerspectives.Count > 2
            || value.Access.ReadPerspectives.Any(x => !perspectives.Contains(x, StringComparer.Ordinal))
            || value.Access.WritePerspectives.Any(x => !value.Access.ReadPerspectives.Contains(x, StringComparer.Ordinal))
            || value.Access.ReadPerspectives.Distinct(StringComparer.Ordinal).Count() != value.Access.ReadPerspectives.Count
            || value.Access.WritePerspectives.Distinct(StringComparer.Ordinal).Count() != value.Access.WritePerspectives.Count)
            throw new ArgumentException("Application object access declarations are invalid.");

        RegisteredApplicationObjectWriteContract? writes = null;
        IReadOnlyList<GeneratedApplicationObjectWriteMapping> generated = [];
        if (value.Writes is null)
        {
            if (value.Access.WritePerspectives.Count != 0)
                throw new ArgumentException("Writable access requires an explicit object edit contract.");
        }
        else
        {
            if (value.Access.WritePerspectives.Count == 0)
                throw new ArgumentException("An object edit contract requires at least one writable perspective.");
            var edit = validator.Compile(value.Writes.EditSchemaJson);
            if (!edit.IsAccepted || !ClosedObject(edit.NormalizedSchema))
                throw new ArgumentException("The object edit schema must be one accepted closed object schema.");
            generated = ValidateWrites(definition, relationships, value.Writes, edit.NormalizedSchema,
                normalizedOutputSchema, fieldBased);
            writes = new(edit.NormalizedSchema, edit.ProfileId, edit.SchemaHash,
                Array.AsReadOnly(value.Writes.Capabilities.Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(value.Writes.Paths.OrderBy(x => x.Pointer, StringComparer.Ordinal).ToArray()));
        }
        var registered = new RegisteredApplicationObjectContract(value.ProfileId,
            Array.AsReadOnly(roles.OrderBy(x => x.RoleId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(value.Sources.OrderBy(x => x.InputId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(relationships.OrderBy(x => x.RelationshipId, StringComparer.Ordinal)
                .Select(x => x.ReadPerspectives is null ? x : x with
                { ReadPerspectives = Array.AsReadOnly(x.ReadPerspectives.Order(StringComparer.Ordinal).ToArray()) }).ToArray()),
            Array.AsReadOnly(value.References.OrderBy(x => x.InputId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(collections.OrderBy(x => x.CollectionId, StringComparer.Ordinal).ToArray()),
            value.Limits,
            new(Array.AsReadOnly(value.Access.ReadPerspectives.Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(value.Access.WritePerspectives.Order(StringComparer.Ordinal).ToArray())),
            writes,
            generated);
        return fieldBased
            ? registered with { FieldProvenance = fieldProvenance }
            : registered;
    }

    private void ValidateEndpointComponents(
        ApplicationIdentifier owner,
        ApplicationObjectRelationship relationship,
        bool fieldBased)
    {
        var required = relationship.RequiredEndpointComponents ?? throw new ArgumentException("Required endpoint components are missing.");
        var optional = relationship.OptionalEndpointComponents ?? throw new ArgumentException("Optional endpoint components are missing.");
        if (required.Count + optional.Count > 32) throw new ArgumentException("Relationship endpoint components exceed the fixed bound.");
        var all = required.Concat(optional).ToArray();
        foreach (var component in all)
        {
            if (component.Endpoint is not ("from" or "to"))
                throw new ArgumentException("Relationship endpoint components require an exact endpoint.");
            component.Type.Validate();
            var registered = componentTypes.Get(component.Type.QualifiedTypeId, component.Type.TypeVersion);
            if (registered is null || !OwnsOrComposes(owner, registered.Owner) || registered.SchemaHash != component.Type.SchemaHash)
                throw new ArgumentException("Relationship endpoint components require exact owner-composed registered types.");
        }
        var keys = all.Select(x => fieldBased
            ? (x.Endpoint, x.Type.QualifiedTypeId, 0)
            : (x.Endpoint, x.Type.QualifiedTypeId, x.Type.TypeVersion)).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Required and optional relationship endpoint components must remain distinct.");
    }

    private void ValidateFieldBasedCollectionShape(
        ApplicationObjectCollection collection,
        ApplicationObjectRelationship relationship)
    {
        var itemEndpoint = relationship.Direction == "incoming" ? "from" : "to";
        var itemComponents = relationship.RequiredEndpointComponents
            .Concat(relationship.OptionalEndpointComponents)
            .Where(component => component.Endpoint == itemEndpoint).ToArray();
        var schemas = itemComponents.Select(component => componentTypes.Get(
                component.Type.QualifiedTypeId, component.Type.TypeVersion)!.SchemaJson)
            .ToArray();
        var properties = schemas.Select(DeclaredTopLevelProperties).ToArray();
        if (properties.Any(value => value is null))
            throw new ArgumentException("Field-based collection endpoint schemas require bounded object field provenance.");
        var fieldNames = properties.SelectMany(value => value!).ToArray();
        if (fieldNames.Any(name => name is "id" or "name")
            || fieldNames.Distinct(StringComparer.Ordinal).Count() != fieldNames.Length)
            throw new ArgumentException("Field-based collection endpoint fields cannot overlap each other or host identity fields.");

        foreach (var order in collection.Order)
        {
            if (order.Pointer is "/id" or "/name") continue;
            var matchingSchemas = schemas.Where(schema => ProjectionSchemaPath.Exists(schema, order.Pointer)).ToArray();
            if (matchingSchemas.Length != 1 || !SchemaPathDeclaresScalar(matchingSchemas[0], order.Pointer))
                throw new ArgumentException("A field-based collection order path requires one unambiguous scalar field source.");
        }
    }

    private static IReadOnlyList<GeneratedApplicationObjectWriteMapping> ValidateWrites(
        ProjectionDefinitionRequest definition,
        IReadOnlyList<ApplicationObjectRelationship> relationships,
        ApplicationObjectWriteContractRequest writes,
        string editSchema,
        string outputSchema,
        bool fieldBased)
    {
        var supported = new[] { "set", "clear", "relationship.add", "relationship.remove" };
        if (writes.Capabilities is null || writes.Paths is null || writes.Capabilities.Count is < 1 or > 4
            || writes.Capabilities.Any(x => !supported.Contains(x, StringComparer.Ordinal))
            || writes.Capabilities.Distinct(StringComparer.Ordinal).Count() != writes.Capabilities.Count
            || writes.Paths.Count is < 1 or > 128
            || writes.Paths.Select(x => x.Pointer).Distinct(StringComparer.Ordinal).Count() != writes.Paths.Count)
            throw new ArgumentException("Application object write capabilities and paths are invalid or unbounded.");
        if (fieldBased)
            for (var left = 0; left < writes.Paths.Count; left++)
            for (var right = left + 1; right < writes.Paths.Count; right++)
                if (Overlaps(writes.Paths[left].Pointer, writes.Paths[right].Pointer))
                    throw new ArgumentException("Field-based object write paths cannot overlap.");
        var componentInputs = definition.ComponentInputs.Select(x => x.InputId).ToHashSet(StringComparer.Ordinal);
        var reverseSources = new HashSet<(string InputId, string Pointer)>();
        var generated = new List<GeneratedApplicationObjectWriteMapping>();
        foreach (var path in writes.Paths)
        {
            var fieldWrite = path.Operations?.Any(operation => operation is "set" or "clear") == true;
            if (!Pointer(path.Pointer) || fieldBased && !SafePointer(path.Pointer, allowWildcard: false)
                || fieldWrite && !ProjectionSchemaPath.Exists(editSchema, path.Pointer)
                || !fieldBased && !ProjectionSchemaPath.Exists(outputSchema, path.Pointer) || path.Operations is null
                || path.Operations.Count is < 1 or > 4 || path.Operations.Distinct(StringComparer.Ordinal).Count() != path.Operations.Count
                || path.Operations.Any(x => !writes.Capabilities.Contains(x, StringComparer.Ordinal)))
                throw new ArgumentException("An application object write path is invalid or outside its edit schema.");
            StructuralProjectionMapping? reverse = null;
            if (path.Operations.Any(operation => operation is "set" or "clear"))
            {
                var mappings = definition.Mappings.Where(x => x.TargetPointer == path.Pointer).ToArray();
                if (mappings.Length != 1 || !componentInputs.Contains(mappings[0].InputId))
                    throw new ArgumentException("Computed, aggregate, ambiguous, or dependency-projected fields cannot be written.");
                reverse = mappings[0];
                if (!reverseSources.Add((reverse.InputId, reverse.SourcePointer)))
                    throw new ArgumentException("Writable object fields cannot target the same source path.");
            }
            foreach (var operation in path.Operations)
            {
                if (operation is "set" or "clear")
                {
                    generated.Add(new(path.Pointer, operation, reverse!.InputId, reverse.SourcePointer, null));
                }
                else if (operation is "relationship.add" or "relationship.remove")
                {
                    var matches = relationships.Where(x => x.TargetPointer == path.Pointer).ToArray();
                    if (matches.Length != 1)
                        throw new ArgumentException("Relationship writes require one unambiguous declared relationship path.");
                    generated.Add(new(path.Pointer, operation, null, null, matches[0].RelationshipId));
                }
                else throw new ArgumentException("The object write operation is not supported.");
            }
        }
        if (!writes.Capabilities.ToHashSet(StringComparer.Ordinal)
                .SetEquals(writes.Paths.SelectMany(x => x.Operations)))
            throw new ArgumentException("Every declared object write capability must be used by an edit path.");
        return Array.AsReadOnly(generated.OrderBy(x => x.ObjectPointer, StringComparer.Ordinal)
            .ThenBy(x => x.Operation, StringComparer.Ordinal).ToArray());
    }

    private static bool ClosedObject(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            && type.GetString() == "object"
            && root.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False;
    }

    private bool OwnsOrComposes(ApplicationIdentifier owner, ApplicationIdentifier componentOwner)
    {
        if (owner == componentOwner) return true;
        var revision = applications?.Get(owner);
        return revision is not null && revision.BaseApplications.Contains(componentOwner);
    }

    private static bool IsArrayIndex(string value) => int.TryParse(value, out _);
    private static bool ConcreteObjectPointer(string value) => value != "" && SafePointer(value, allowWildcard: false)
        && value.Split('/').Skip(1).All(token => token != "*" && !IsArrayIndex(token));
    private static bool SafePointer(string value, bool allowWildcard)
    {
        if (value.Length > 1_000 || value.Any(char.IsControl) || !Pointer(value)) return false;
        foreach (var raw in value.Split('/').Skip(1))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if ((!allowWildcard && token == "*") || token is "__proto__" or "prototype" or "constructor")
                return false;
        }
        return true;
    }
    private static bool NestedCollectionRelationship(
        ApplicationObjectRelationship parent,
        ApplicationObjectRelationship nested,
        IReadOnlyList<ApplicationObjectCollection> collections) =>
        collections.Any(collection => collection.SourceId == parent.RelationshipId)
        && nested.TargetPointer.StartsWith(parent.TargetPointer + "/*/", StringComparison.Ordinal)
        && nested.TargetPointer[(parent.TargetPointer.Length + 3)..].IndexOf('/') < 0
        && (nested.Direction == "incoming" ? nested.ToRole : nested.FromRole) ==
           (parent.Direction == "incoming" ? parent.FromRole : parent.ToRole);

    private static bool SchemaPathDeclaresObject(string schemaJson, string pointer)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root is null) return false;
        string[] tokens = pointer == "" ? [] : pointer.Split('/').Skip(1)
            .Select(token => token.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        return GuaranteesObject(root, root, tokens, 0, 0, new HashSet<string>(StringComparer.Ordinal));
    }

    private static bool SchemaPathDeclaresScalar(string schemaJson, string pointer)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root is null) return false;
        var current = ResolveDirectSchemaPath(root, pointer);
        return current is not null && GuaranteesScalar(root, current, 0, new HashSet<string>(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string>? DeclaredTopLevelProperties(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root is null || !GuaranteesObject(root, root, [], 0, 0, new HashSet<string>(StringComparer.Ordinal)))
            return null;
        return DeclaredTopLevelProperties(root, root, 0, new HashSet<string>(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string>? DeclaredTopLevelProperties(
        JsonNode root,
        JsonNode? value,
        int depth,
        HashSet<string> references)
    {
        if (value is not JsonObject schema || depth > 64) return null;
        if (schema["$ref"] is JsonValue referenceValue
            && referenceValue.TryGetValue<string>(out var reference))
        {
            if (!references.Add(reference)) return null;
            try
            {
                return ResolveSchemaReference(root, reference) is { } resolved
                    ? DeclaredTopLevelProperties(root, resolved, depth + 1, references)
                    : null;
            }
            finally { references.Remove(reference); }
        }
        if (schema["allOf"] is not null) return null;

        var oneOf = schema["oneOf"] as JsonArray;
        var anyOf = schema["anyOf"] as JsonArray;
        if (oneOf is not null || anyOf is not null)
        {
            if (oneOf is not null && anyOf is not null || schema["properties"] is not null)
                return null;
            var alternatives = oneOf ?? anyOf!;
            if (alternatives.Count is < 1 or > MaximumSchemaAlternatives) return null;
            var unionProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alternative in alternatives)
            {
                var branch = FollowBoundedReferences(root, alternative, depth + 1,
                    new HashSet<string>(references, StringComparer.Ordinal));
                if (branch is not JsonObject objectBranch
                    || objectBranch["type"] is not JsonValue branchType
                    || !branchType.TryGetValue<string>(out var declaredType) || declaredType != "object"
                    || objectBranch["additionalProperties"] is not JsonValue additional
                    || !additional.TryGetValue<bool>(out var allowsAdditional) || allowsAdditional
                    || objectBranch["properties"] is not JsonObject branchProperties
                    || objectBranch["patternProperties"] is not null
                    || objectBranch["allOf"] is not null || objectBranch["anyOf"] is not null
                    || objectBranch["oneOf"] is not null)
                    return null;
                foreach (var property in branchProperties)
                {
                    unionProperties.Add(property.Key);
                    if (unionProperties.Count > MaximumGeneratedFieldProvenance) return null;
                }
            }
            return unionProperties.Order(StringComparer.Ordinal).ToArray();
        }

        return schema["properties"] is JsonObject properties
            ? properties.Select(property => property.Key).ToArray()
            : [];
    }

    private static JsonNode? FollowBoundedReferences(
        JsonNode root,
        JsonNode? schema,
        int depth,
        HashSet<string> references)
    {
        var current = schema;
        while (current is JsonObject value && value["$ref"] is JsonValue referenceValue
               && referenceValue.TryGetValue<string>(out var reference))
        {
            if (depth++ > 64 || !references.Add(reference)) return null;
            current = ResolveSchemaReference(root, reference);
        }
        return depth > 64 ? null : current;
    }

    private static JsonNode? ResolveDirectSchemaPath(JsonNode root, string pointer)
    {
        JsonNode? current = root;
        foreach (var token in pointer.Split('/').Skip(1).Select(raw => raw
                     .Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)))
        {
            current = FollowDirectReferences(root, current, new HashSet<string>(StringComparer.Ordinal));
            if (current is JsonObject schema && schema["properties"] is JsonObject properties
                && properties.TryGetPropertyValue(token, out var property)) current = property;
            else if (current is JsonObject arraySchema && token == "*" && arraySchema["items"] is { } items) current = items;
            else if (current is JsonObject indexedSchema && int.TryParse(token, out var index) && index >= 0
                && indexedSchema["prefixItems"] is JsonArray prefix && index < prefix.Count) current = prefix[index];
            else if (current is JsonObject itemSchema && int.TryParse(token, out index) && index >= 0
                && itemSchema["items"] is { } indexedItems) current = indexedItems;
            else return null;
        }
        return FollowDirectReferences(root, current, new HashSet<string>(StringComparer.Ordinal));
    }

    private static JsonNode? FollowDirectReferences(JsonNode root, JsonNode? schema, HashSet<string> references)
    {
        var current = schema;
        while (current is JsonObject value && value["$ref"] is JsonValue referenceValue
               && referenceValue.TryGetValue<string>(out var reference))
        {
            if (!references.Add(reference)) return null;
            current = ResolveSchemaReference(root, reference);
        }
        return current;
    }

    private static bool GuaranteesScalar(JsonNode root, JsonNode? schema, int depth, HashSet<string> references)
    {
        if (schema is not JsonObject current || depth > 64) return false;
        if (current["type"] is JsonValue type)
        {
            if (type.TryGetValue<string>(out var declared)) return ScalarType(declared);
        }
        else if (current["type"] is JsonArray types && types.Count > 0
                 && types.All(value => value is JsonValue item && item.TryGetValue<string>(out var declared)
                     && ScalarType(declared)))
            return true;
        if (current["$ref"] is JsonValue referenceValue
            && referenceValue.TryGetValue<string>(out var reference) && references.Add(reference))
        {
            try
            {
                if (ResolveSchemaReference(root, reference) is { } resolved
                    && GuaranteesScalar(root, resolved, depth + 1, references)) return true;
            }
            finally { references.Remove(reference); }
        }
        if (current["allOf"] is JsonArray allOf
            && allOf.Any(branch => GuaranteesScalar(root, branch, depth + 1, new(references)))) return true;
        foreach (var keyword in new[] { "anyOf", "oneOf" })
            if (current[keyword] is JsonArray alternatives && alternatives.Count > 0
                && alternatives.All(branch => GuaranteesScalar(root, branch, depth + 1, new(references)))) return true;
        return false;
    }

    private static bool ScalarType(string value) => value is "string" or "number" or "integer" or "boolean" or "null";

    private static bool GuaranteesObject(
        JsonNode root,
        JsonNode? schema,
        IReadOnlyList<string> tokens,
        int tokenIndex,
        int depth,
        HashSet<string> references)
    {
        if (schema is not JsonObject current || depth > 64) return false;
        if (tokenIndex == tokens.Count)
        {
            if (current["type"] is JsonValue type && type.TryGetValue<string>(out var declared)
                && declared == "object") return true;
            if (FollowReference(root, current, tokens, tokenIndex, depth, references)) return true;
            if (current["allOf"] is JsonArray allOf
                && allOf.Any(branch => GuaranteesObject(root, branch, tokens, tokenIndex, depth + 1, new(references))))
                return true;
            foreach (var keyword in new[] { "anyOf", "oneOf" })
                if (current[keyword] is JsonArray alternatives && alternatives.Count > 0
                    && alternatives.All(branch => GuaranteesObject(root, branch, tokens, tokenIndex, depth + 1, new(references))))
                    return true;
            return false;
        }

        if (current["properties"] is JsonObject properties
            && properties.TryGetPropertyValue(tokens[tokenIndex], out var property)
            && GuaranteesObject(root, property, tokens, tokenIndex + 1, depth + 1, references))
            return true;
        if (tokens[tokenIndex] == "*" && current["items"] is JsonNode wildcardItems
            && GuaranteesObject(root, wildcardItems, tokens, tokenIndex + 1, depth + 1, references))
            return true;
        if (int.TryParse(tokens[tokenIndex], out var arrayIndex) && arrayIndex >= 0)
        {
            if (current["prefixItems"] is JsonArray prefix && arrayIndex < prefix.Count
                && GuaranteesObject(root, prefix[arrayIndex], tokens, tokenIndex + 1, depth + 1, references))
                return true;
            if (current["items"] is JsonNode items
                && GuaranteesObject(root, items, tokens, tokenIndex + 1, depth + 1, references))
                return true;
        }
        if (FollowReference(root, current, tokens, tokenIndex, depth, references)) return true;
        if (current["allOf"] is JsonArray pathAllOf
            && pathAllOf.Any(branch => GuaranteesObject(root, branch, tokens, tokenIndex, depth + 1, new(references))))
            return true;
        foreach (var keyword in new[] { "anyOf", "oneOf" })
            if (current[keyword] is JsonArray alternatives && alternatives.Count > 0
                && alternatives.All(branch => GuaranteesObject(root, branch, tokens, tokenIndex, depth + 1, new(references))))
                return true;
        return false;
    }

    private static bool FollowReference(
        JsonNode root,
        JsonObject schema,
        IReadOnlyList<string> tokens,
        int tokenIndex,
        int depth,
        HashSet<string> references)
    {
        if (schema["$ref"] is not JsonValue referenceValue
            || !referenceValue.TryGetValue<string>(out var reference)
            || !references.Add(reference)) return false;
        try
        {
            return ResolveSchemaReference(root, reference) is { } resolved
                && GuaranteesObject(root, resolved, tokens, tokenIndex, depth + 1, references);
        }
        finally { references.Remove(reference); }
    }

    private static JsonNode? ResolveSchemaReference(JsonNode root, string reference)
    {
        if (reference == "#") return root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal)) return null;
        JsonNode? current = root;
        foreach (var raw in reference[2..].Split('/'))
        {
            string token;
            try
            {
                token = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
            }
            catch (UriFormatException) { return null; }
            if (current is JsonObject value && value.TryGetPropertyValue(token, out var child)) current = child;
            else if (current is JsonArray array && int.TryParse(token, out var index)
                && index >= 0 && index < array.Count) current = array[index];
            else return null;
        }
        return current;
    }

    private int DependencyDepth(RegisteredProjectionDefinition definition, Dictionary<string, int> memo, HashSet<string> visiting)
    {
        var key = definition.QualifiedId + "@" + definition.Version;
        if (memo.TryGetValue(key, out var known)) return known;
        if (!visiting.Add(key)) throw new ArgumentException("Projection dependencies must be acyclic.");
        var depth = 0;
        foreach (var input in definition.DependencyInputs)
        {
            var dependency = Get(input.Projection.QualifiedId, input.Projection.Version);
            if (dependency is null || dependency.ContentHash != input.Projection.ContentHash)
                throw new ArgumentException("Projection dependencies require exact available versions.");
            depth = Math.Max(depth, 1 + DependencyDepth(dependency, memo, visiting));
            if (depth > 16) break;
        }
        visiting.Remove(key);
        memo[key] = depth;
        return depth;
    }
    private static bool Pointer(string value) => value == "" || (value.StartsWith("/", StringComparison.Ordinal) && !value.Split('/').Skip(1).Any(x => x.Contains('~') && x.Replace("~0", "").Replace("~1", "").Contains('~')));
    private static bool Overlaps(string left, string right) => left == right || left == "" || right == ""
        || left.StartsWith(right + "/", StringComparison.Ordinal)
        || right.StartsWith(left + "/", StringComparison.Ordinal);
    private static bool Identifier(string value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
    private static string Canonical(ProjectionDefinitionRequest d, string schema, RegisteredApplicationObjectContract? objectContract) =>
        objectContract is null
            ? JsonSerializer.Serialize(new { owner = d.Owner.Value, id = d.QualifiedId, schema, components = d.ComponentInputs, dependencies = d.DependencyInputs.Select(x => new { x.InputId, projection = x.Projection, roles = x.RoleBindings.OrderBy(p => p.Key) }), mappings = d.Mappings })
            : JsonSerializer.Serialize(new { owner = d.Owner.Value, id = d.QualifiedId, version = d.DeclaredVersion, schema, components = d.ComponentInputs, dependencies = d.DependencyInputs.Select(x => new { x.InputId, projection = x.Projection, roles = x.RoleBindings.OrderBy(p => p.Key) }), mappings = d.Mappings, objectContract });
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Key(ProjectionDefinitionVersionRecord x) => x.QualifiedId + "@" + x.Version;
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Frozen(Dictionary<string, List<string>> graph) => new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(graph.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)Array.AsReadOnly(x.Value.Order(StringComparer.Ordinal).ToArray()), StringComparer.Ordinal));
}

internal sealed class ProjectionDefinitionRecord { public required string QualifiedId { get; set; } public required string ApplicationId { get; set; } public DateTime CreatedAtUtc { get; set; } }
internal sealed class ProjectionRegistryGenerationRecord { public required string ApplicationId { get; set; } public long Generation { get; set; } }
internal sealed class ProjectionDefinitionVersionRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string ProfileId { get; set; } public required string OutputSchemaJson { get; set; } public required string OutputSchemaHash { get; set; } public required string ContentHash { get; set; } public string? ObjectContractJson { get; set; } public DateTime CreatedAtUtc { get; set; } }
internal sealed class ProjectionComponentInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string EntityRole { get; set; } public required string QualifiedTypeId { get; set; } public int TypeVersion { get; set; } public required string SchemaHash { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionDependencyInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string DependencyQualifiedId { get; set; } public int DependencyVersion { get; set; } public required string DependencyContentHash { get; set; } public required string RoleBindingsJson { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionMappingRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string TargetPointer { get; set; } public required string InputId { get; set; } public required string SourcePointer { get; set; } public int Ordinal { get; set; } }
