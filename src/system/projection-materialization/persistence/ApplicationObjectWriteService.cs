using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Projections;

/// <summary>
/// Ruleset-neutral reverse mapper for exact registered application objects. It can only emit
/// typed effects named by a registered write mapping; component and relationship identities are
/// never inferred from application vocabulary.
/// </summary>
public sealed class ApplicationObjectWriteService(
    IProjectionDefinitionRegistry definitions,
    IProjectionCollectionMaterializer materializer,
    IEntityComponentStore components,
    IApplicationEcsEffectApplier effects,
    IOperationLog operations,
    IBoundedJsonSchemaValidator schemas) : IApplicationObjectWriteService
{
    private const string FingerprintDomain = "dantes-roleplay/application-object-write/v1";
    private const int MaximumRelationshipEdits = 32;

    public async Task<ApplicationObjectWriteResult> WriteAsync(
        ApplicationObjectWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var definition = definitions.Get(request.Object.QualifiedId, request.Object.Version);
        if (definition is null || definition.Reference != request.Object ||
            definition.Owner != request.ApplicationId || definition.ObjectContract?.Writes is null)
            throw Failure("OBJECT_WRITE_UNKNOWN", "The exact writable application object is unavailable.");
        var contract = definition.ObjectContract;
        if (!contract.Access.WritePerspectives.Contains(request.Perspective, StringComparer.Ordinal))
            throw Failure("OBJECT_WRITE_FORBIDDEN", "The application object is not writable from this perspective.");
        if (!contract.Collections.Any(value => value.CollectionId == request.CollectionId))
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "The object collection is not declared.");

        var changes = CanonicalObject(request.ChangesJson);
        var editSchema = schemas.Compile(contract.Writes.EditSchemaJson);
        if (!editSchema.IsAccepted || editSchema.ProfileId != contract.Writes.EditSchemaProfileId ||
            editSchema.SchemaHash != contract.Writes.EditSchemaHash || schemas.Validate(
                editSchema.ProfileId, editSchema.NormalizedSchema, changes).Status != SchemaValueStatus.Valid)
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "The object changes do not satisfy the exact edit schema.");

        var identity = Identity(request, changes);
        var replay = await operations.GetAsync(identity.OperationId, cancellationToken);
        if (replay is not null)
        {
            if (replay.Tool != ApplicationEcsExecutionIdentity.AuditTool ||
                replay.Subject != identity.AuditSubject)
                throw Failure("OBJECT_WRITE_IDEMPOTENCY_CONFLICT",
                    "The idempotency key is already bound to another object edit.");
            if (!replay.Success)
                throw Failure("OBJECT_WRITE_REJECTED", "The same object edit previously failed.");
            var replayed = await ReadAsync(request, cancellationToken);
            return Result(false, true, replayed, identity.OperationId, []);
        }

        var current = await ReadAsync(request, cancellationToken);
        if (current.SourceRevisionFingerprint != request.ExpectedSourceRevisionFingerprint)
            throw Failure("OBJECT_WRITE_SOURCE_STALE", "The object sources changed. Refresh before saving.");

        var currentObject = JsonNode.Parse(current.OutputJson)?.AsObject()
            ?? throw Failure("OBJECT_WRITE_UNAVAILABLE", "The current object is unavailable.");
        var changesObject = JsonNode.Parse(changes)?.AsObject()
            ?? throw Failure("OBJECT_WRITE_REQUEST_INVALID", "Object changes must be one JSON object.");
        var fieldMappings = contract.GeneratedWriteMappings
            .Where(value => value.Operation is "set" or "clear")
            .GroupBy(value => value.ObjectPointer, StringComparer.Ordinal)
            .ToDictionary(value => value.Key, value => value.ToArray(), StringComparer.Ordinal);
        ValidateChangedPaths(changesObject, fieldMappings.Keys.ToHashSet(StringComparer.Ordinal));

        var sourceInputs = definition.ComponentInputs.ToDictionary(value => value.InputId, StringComparer.Ordinal);
        var pendingFields = new List<PendingField>();
        foreach (var field in fieldMappings)
        {
            if (!TrySelect(changesObject, field.Key, out var replacement)) continue;
            var operation = replacement is null ? "clear" : "set";
            var mapping = field.Value.SingleOrDefault(value => value.Operation == operation)
                ?? throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "The requested field operation is not declared by this object.");
            if (!sourceInputs.TryGetValue(mapping.InputId!, out var input) ||
                !request.RoleEntityIds.TryGetValue(input.EntityRole, out var entityId))
                throw Failure("OBJECT_WRITE_REQUEST_INVALID", "A writable object role is unbound.");
            if (TrySelect(currentObject, field.Key, out var before) && JsonNode.DeepEquals(before, replacement) ||
                replacement is null && before is null)
                continue;
            pendingFields.Add(new(entityId, input, mapping.SourcePointer!, operation,
                replacement?.DeepClone()));
        }

        var relationshipEffects = new List<ApplicationEcsEffect>();
        var requiredEndpoints = new List<(string EntityId, EcsComponentReference Type)>();
        foreach (var edit in request.RelationshipEdits)
        {
            var mapping = contract.GeneratedWriteMappings.SingleOrDefault(value =>
                value.ObjectPointer == edit.Path && value.Operation == edit.Operation &&
                value.RelationshipId is not null)
                ?? throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "The requested relationship operation is not declared by this object.");
            var relationship = contract.Relationships.Single(value =>
                value.RelationshipId == mapping.RelationshipId);
            var fromBound = request.RoleEntityIds.TryGetValue(relationship.FromRole, out var from);
            var toBound = request.RoleEntityIds.TryGetValue(relationship.ToRole, out var to);
            if (fromBound == toBound)
                throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "A relationship edit must bind exactly one endpoint role.");
            from ??= edit.TargetEntityId;
            to ??= edit.TargetEntityId;
            if (edit.Operation == "relationship.add" && edit.ExpectedRevision != 0 ||
                edit.Operation == "relationship.remove" && edit.ExpectedRevision < 1)
                throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "Relationship addition requires revision zero and removal requires a positive revision.");
            relationshipEffects.Add(new()
            {
                Type = edit.Operation == "relationship.add"
                    ? ApplicationEcsEffectType.RelationshipSet
                    : ApplicationEcsEffectType.RelationshipRemove,
                EntityId = from,
                TargetEntityId = to,
                QualifiedRelationshipKind = relationship.QualifiedKind,
                DataJson = edit.Operation == "relationship.add" ? "{}" : string.Empty,
                ExpectedRevision = edit.ExpectedRevision
            });
            foreach (var endpoint in relationship.RequiredEndpointComponents)
                requiredEndpoints.Add((endpoint.Endpoint == "from" ? from : to, endpoint.Type));
        }

        var locators = pendingFields.Select(value =>
                new EcsComponentLocator(value.EntityId, value.Input.Type.QualifiedTypeId))
            .Concat(requiredEndpoints.Select(value =>
                new EcsComponentLocator(value.EntityId, value.Type.QualifiedTypeId)))
            .Distinct().ToArray();
        if (locators.Length > ApplicationEcsEffectValidation.MaximumComponentExpectations)
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "The object edit exceeds its component bound.");
        var sourceComponents = await components.GetComponentsAsync(
            request.StateSpaceId, locators, cancellationToken);
        var sourceByKey = sourceComponents.ToDictionary(
            value => (value.EntityId, value.Type.QualifiedTypeId));
        var expectedSourceByKey = current.SourceRevisions.ToDictionary(
            value => (value.EntityId, value.Type.QualifiedTypeId));

        var componentEffects = new List<ApplicationEcsEffect>();
        foreach (var group in pendingFields.GroupBy(value =>
                     (value.EntityId, value.Input.InputId, value.Input.Type)))
        {
            var key = (group.Key.EntityId, group.Key.Type.QualifiedTypeId);
            if (!sourceByKey.TryGetValue(key, out var source) || source.Type != group.Key.Type ||
                !expectedSourceByKey.TryGetValue(key, out var expected) || expected.Type != source.Type ||
                expected.Revision != source.Revision)
                throw Failure("OBJECT_WRITE_SOURCE_STALE", "A mapped component source changed before saving.");
            var value = JsonNode.Parse(source.ValueJson)?.AsObject()
                ?? throw Failure("OBJECT_WRITE_REJECTED", "A mapped component source is not an object.");
            foreach (var change in group)
                if (change.Operation == "clear") Set(value, change.SourcePointer, null);
                else Set(value, change.SourcePointer, change.Value!.DeepClone());
            componentEffects.Add(new()
            {
                Type = ApplicationEcsEffectType.ComponentSet,
                EntityId = group.Key.EntityId,
                ComponentType = group.Key.Type,
                DataJson = value.ToJsonString(),
                ExpectedRevision = source.Revision
            });
        }

        foreach (var endpoint in requiredEndpoints)
            if (!sourceByKey.TryGetValue((endpoint.EntityId, endpoint.Type.QualifiedTypeId), out var source) ||
                source.Type != endpoint.Type)
                throw Failure("OBJECT_WRITE_REJECTED",
                    "A declared relationship endpoint component is missing or stale.");

        var expectations = sourceComponents.Select(value => new ApplicationEcsComponentExpectation(
                value.EntityId, value.Type, value.Revision))
            .DistinctBy(value => (value.EntityId, value.ComponentType.QualifiedTypeId))
            .OrderBy(value => value.EntityId, StringComparer.Ordinal)
            .ThenBy(value => value.ComponentType.QualifiedTypeId, StringComparer.Ordinal).ToArray();
        var batch = new ApplicationEcsEffectBatch
        {
            StateSpaceId = request.StateSpaceId,
            Intent = "Apply one explicit registered application object edit.",
            ExecutionIdentity = identity,
            ComponentExpectations = expectations,
            Effects = [.. componentEffects, .. relationshipEffects]
        };
        var applied = await effects.ApplyAsync(batch, cancellationToken: cancellationToken);
        if (!applied.Valid)
        {
            var code = applied.Problems.Any(value => value.Code == "OPERATION_ID_CONFLICT")
                ? "OBJECT_WRITE_IDEMPOTENCY_CONFLICT"
                : applied.Problems.Any(value => value.Code.Contains("STALE", StringComparison.Ordinal))
                    ? "OBJECT_WRITE_SOURCE_STALE"
                    : "OBJECT_WRITE_REJECTED";
            throw Failure(code, "The typed object effects were rejected.");
        }
        var fresh = await ReadAsync(request, cancellationToken);
        return Result(applied.Applied, applied.Replayed, fresh, applied.OperationId, applied.Receipts);
    }

    private Task<ProjectionCollectionMaterializationResult> ReadAsync(
        ApplicationObjectWriteRequest request,
        CancellationToken cancellationToken) => materializer.MaterializeAsync(new(
            request.StateSpaceId, request.Object, request.RoleEntityIds, request.CollectionId,
            request.Perspective), cancellationToken);

    private static ApplicationObjectWriteResult Result(
        bool applied,
        bool replayed,
        ProjectionCollectionMaterializationResult fresh,
        string operationId,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts) => new(
            applied && receipts.Count > 0, replayed, receipts.Count == 0, operationId, fresh.OutputJson,
            fresh.SourceRevisionFingerprint, receipts);

    private static ApplicationEcsExecutionIdentity Identity(
        ApplicationObjectWriteRequest request,
        string changes)
    {
        var scope = CanonicalObject(JsonSerializer.Serialize(new
        {
            domain = FingerprintDomain,
            applicationId = request.ApplicationId.Value,
            request.StateSpaceId,
            objectReference = request.Object,
            request.CollectionId,
            roles = request.RoleEntityIds.OrderBy(value => value.Key),
            request.Perspective,
            request.IdempotencyKey
        }));
        var requestJson = CanonicalObject(JsonSerializer.Serialize(new
        {
            scope = JsonSerializer.Deserialize<JsonElement>(scope),
            request.ExpectedSourceRevisionFingerprint,
            changes = JsonSerializer.Deserialize<JsonElement>(changes),
            relationshipEdits = request.RelationshipEdits
        }));
        var operationHash = Hash(scope);
        return new(operationHash[..32].ToLowerInvariant(), Hash(requestJson));
    }

    private static void ValidateRequest(ApplicationObjectWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        request.Object.Validate();
        if (!Token(request.StateSpaceId, 200) || !Token(request.CollectionId, 200) ||
            request.Perspective is not ("player" or "dm") || !Token(request.IdempotencyKey, 128) ||
            !HashValue(request.ExpectedSourceRevisionFingerprint) || request.RoleEntityIds is null ||
            request.RoleEntityIds.Count is < 1 or > 32 || request.RoleEntityIds.Any(value =>
                !Token(value.Key, 200) || !Token(value.Value, 200)) || request.RelationshipEdits is null ||
            request.RelationshipEdits.Count > MaximumRelationshipEdits || request.RelationshipEdits.Any(value =>
                value is null || !Pointer(value.Path) || value.Operation is not
                    ("relationship.add" or "relationship.remove") || !Token(value.TargetEntityId, 200) ||
                value.ExpectedRevision < 0) || request.RelationshipEdits.Select(value =>
                (value.Path, value.Operation, value.TargetEntityId)).Distinct().Count() != request.RelationshipEdits.Count)
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "The object write request is invalid or unbounded.");
    }

    private static void ValidateChangedPaths(JsonObject changes, IReadOnlySet<string> allowed)
    {
        if (changes.Count == 0) return;
        Visit(changes, "");
        return;

        void Visit(JsonNode? node, string pointer)
        {
            if (pointer.Length > 0 && allowed.Contains(pointer)) return;
            if (node is not JsonObject value || value.Count == 0)
                throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "The edit contains a field without a declared reverse mapping.");
            foreach (var property in value)
                Visit(property.Value, pointer + "/" + Escape(property.Key));
        }
    }

    private static bool TrySelect(JsonNode root, string pointer, out JsonNode? value)
    {
        JsonNode? current = root;
        foreach (var token in Tokens(pointer))
        {
            if (current is not JsonObject objectValue || !objectValue.TryGetPropertyValue(token, out current))
            {
                value = null;
                return false;
            }
        }
        value = current;
        return true;
    }

    private static void Set(JsonObject root, string pointer, JsonNode? value)
    {
        var tokens = Tokens(pointer).ToArray();
        if (tokens.Length == 0) throw Failure("OBJECT_WRITE_REJECTED", "A field cannot replace a component root.");
        var current = root;
        foreach (var token in tokens[..^1])
        {
            current[token] ??= new JsonObject();
            current = current[token] as JsonObject
                ?? throw Failure("OBJECT_WRITE_REJECTED", "A mapped component path is not an object.");
        }
        current[tokens[^1]] = value;
    }

    private static string CanonicalObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                Encoding.UTF8.GetByteCount(json) > 65_536)
                throw new JsonException();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException exception)
        {
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "The object edit contains invalid bounded JSON.", exception);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new JsonException();
            writer.WriteStartObject();
            foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool HashValue(string value) => value is { Length: 64 } &&
        value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static bool Token(string? value, int maximum) => value is { Length: > 0 } &&
        value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Pointer(string value) => value.Length is > 0 and <= 1_000 &&
        value.StartsWith("/", StringComparison.Ordinal) && value == value.Trim() && !value.Any(char.IsControl);
    private static IEnumerable<string> Tokens(string pointer) => pointer == "" ? [] :
        pointer.Split('/').Skip(1).Select(value => value.Replace("~1", "/").Replace("~0", "~"));
    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static ApplicationObjectWriteException Failure(
        string code, string message, Exception? inner = null) => new(code, message, inner);

    private sealed record PendingField(
        string EntityId,
        ProjectionComponentInput Input,
        string SourcePointer,
        string Operation,
        JsonNode? Value);
}
