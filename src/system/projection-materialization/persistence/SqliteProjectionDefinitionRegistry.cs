using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    IApplicationRegistry? applications = null) : IProjectionDefinitionRegistry
{
    private readonly Dictionary<(string QualifiedId, int Version), RegisteredProjectionDefinition> cache = [];

    public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId);
        var compilation = validator.Compile(definition.OutputSchemaJson);
        if (!compilation.IsAccepted) throw new ArgumentException("The projection output schema is not accepted by the bounded profile.");
        ValidateInputs(definition);
        var objectContract = ValidateObjectContract(definition, compilation.NormalizedSchema);
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
        db.SaveChanges(); transaction.Commit(); return Remember(Read(row, definition.Owner));
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

    private void ValidateInputs(ProjectionDefinitionRequest definition)
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
        var sourceSchemas = definition.ComponentInputs.ToDictionary(x => x.InputId, x => componentTypes.Get(x.Type.QualifiedTypeId, x.Type.TypeVersion)!.SchemaJson, StringComparer.Ordinal);
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
        }
        if (definition.Mappings.Select(x => x.TargetPointer).Distinct(StringComparer.Ordinal).Count() != definition.Mappings.Count || definition.Mappings.Any(x => !ids.Contains(x.InputId, StringComparer.Ordinal)
                || x.SourcePointer.Length > 1000 || x.TargetPointer.Length > 1000
                || !Pointer(x.SourcePointer) || !Pointer(x.TargetPointer) || (!string.IsNullOrEmpty(x.TargetPointer) && x.TargetPointer.Split('/').Skip(1).Any(IsArrayIndex))))
            throw new ArgumentException("Mappings must structurally copy declared JSON pointers to unique object targets.");
        if (definition.Mappings.Any(x => x.TargetPointer == "") && definition.Mappings.Count != 1) throw new ArgumentException("A root mapping must be the only mapping.");
        if (definition.Mappings.Any(x => !ProjectionSchemaPath.Exists(sourceSchemas[x.InputId], x.SourcePointer)))
            throw new ArgumentException("Projection source pointers must exist in their exact declared schema.");
    }

    private RegisteredApplicationObjectContract? ValidateObjectContract(
        ProjectionDefinitionRequest definition,
        string normalizedOutputSchema)
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
                || !ProjectionSchemaPath.Exists(normalizedOutputSchema, relationship.TargetPointer))
                throw new ArgumentException("An application object relationship declaration is invalid.");
            ValidateEndpointComponents(definition.Owner, relationship);
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
                || collection.Order.Select(x => x.Pointer).Distinct(StringComparer.Ordinal).Count() != collection.Order.Count
                || collection.Cursor != "source-revision-bound")
                throw new ArgumentException("An application object collection declaration is invalid or unbounded.");
        }
        if (value.Limits.TraversalDepth is < 1 or > 16 || value.Limits.ItemCount is < 1 or > 10_000
            || value.Limits.OutputBytes is < 1 or > SystemJsonSchemaProfile.MaximumValueBytes
            || value.Limits.SqlQueries is < 1 or > 64)
            throw new ArgumentException("Application object resource limits exceed the supported profile.");
        var perspectives = new[] { "player", "dm" };
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
                normalizedOutputSchema);
            writes = new(edit.NormalizedSchema, edit.ProfileId, edit.SchemaHash,
                Array.AsReadOnly(value.Writes.Capabilities.Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(value.Writes.Paths.OrderBy(x => x.Pointer, StringComparer.Ordinal).ToArray()));
        }
        return new(RegisteredApplicationObjectContract.ContractProfileId,
            Array.AsReadOnly(roles.OrderBy(x => x.RoleId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(value.Sources.OrderBy(x => x.InputId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(relationships.OrderBy(x => x.RelationshipId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(value.References.OrderBy(x => x.InputId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(collections.OrderBy(x => x.CollectionId, StringComparer.Ordinal).ToArray()),
            value.Limits,
            new(Array.AsReadOnly(value.Access.ReadPerspectives.Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(value.Access.WritePerspectives.Order(StringComparer.Ordinal).ToArray())),
            writes,
            generated);
    }

    private void ValidateEndpointComponents(ApplicationIdentifier owner, ApplicationObjectRelationship relationship)
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
        var keys = all.Select(x => (x.Endpoint, x.Type.QualifiedTypeId, x.Type.TypeVersion)).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Required and optional relationship endpoint components must remain distinct.");
    }

    private static IReadOnlyList<GeneratedApplicationObjectWriteMapping> ValidateWrites(
        ProjectionDefinitionRequest definition,
        IReadOnlyList<ApplicationObjectRelationship> relationships,
        ApplicationObjectWriteContractRequest writes,
        string editSchema,
        string outputSchema)
    {
        var supported = new[] { "set", "clear", "relationship.add", "relationship.remove" };
        if (writes.Capabilities is null || writes.Paths is null || writes.Capabilities.Count is < 1 or > 4
            || writes.Capabilities.Any(x => !supported.Contains(x, StringComparer.Ordinal))
            || writes.Capabilities.Distinct(StringComparer.Ordinal).Count() != writes.Capabilities.Count
            || writes.Paths.Count is < 1 or > 128
            || writes.Paths.Select(x => x.Pointer).Distinct(StringComparer.Ordinal).Count() != writes.Paths.Count)
            throw new ArgumentException("Application object write capabilities and paths are invalid or unbounded.");
        var componentInputs = definition.ComponentInputs.Select(x => x.InputId).ToHashSet(StringComparer.Ordinal);
        var reverseSources = new HashSet<(string InputId, string Pointer)>();
        var generated = new List<GeneratedApplicationObjectWriteMapping>();
        foreach (var path in writes.Paths)
        {
            var fieldWrite = path.Operations?.Any(operation => operation is "set" or "clear") == true;
            if (!Pointer(path.Pointer) || fieldWrite && !ProjectionSchemaPath.Exists(editSchema, path.Pointer)
                || !ProjectionSchemaPath.Exists(outputSchema, path.Pointer) || path.Operations is null
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
    private static bool Overlaps(string left, string right) => left == "" || right == ""
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
internal sealed class ProjectionDefinitionVersionRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string ProfileId { get; set; } public required string OutputSchemaJson { get; set; } public required string OutputSchemaHash { get; set; } public required string ContentHash { get; set; } public string? ObjectContractJson { get; set; } public DateTime CreatedAtUtc { get; set; } }
internal sealed class ProjectionComponentInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string EntityRole { get; set; } public required string QualifiedTypeId { get; set; } public int TypeVersion { get; set; } public required string SchemaHash { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionDependencyInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string DependencyQualifiedId { get; set; } public int DependencyVersion { get; set; } public required string DependencyContentHash { get; set; } public required string RoleBindingsJson { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionMappingRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string TargetPointer { get; set; } public required string InputId { get; set; } public required string SourcePointer { get; set; } public int Ordinal { get; set; } }
