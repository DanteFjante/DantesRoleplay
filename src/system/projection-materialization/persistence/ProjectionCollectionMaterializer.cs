using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Projections;

/// <summary>
/// Expands one declared many-relationship without enumerating unrelated entities or edges.
/// Endpoint component values and declared one-hop relationship references are merged into each item.
/// </summary>
public sealed class ProjectionCollectionMaterializer(
    IProjectionDefinitionRegistry definitions,
    IProjectionMaterializer materializer,
    IRelationshipCollectionReader relationships,
    IEntityBatchReadStore entities,
    IEntityComponentStore components,
    IBoundedJsonSchemaValidator schemas,
    IProjectionReadTransaction transactions) : IProjectionCollectionMaterializer
{
    public Task<ProjectionCollectionMaterializationResult> MaterializeAsync(
        ProjectionCollectionMaterializationRequest request,
        CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(token => MaterializeSnapshotAsync(request, token), cancellationToken);

    private async Task<ProjectionCollectionMaterializationResult> MaterializeSnapshotAsync(
        ProjectionCollectionMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Projection.Validate();
        if (string.IsNullOrWhiteSpace(request.StateSpaceId) || string.IsNullOrWhiteSpace(request.CollectionId)
            || request.Perspective is not ("player" or "dm") || request.RoleEntityIds is null)
            throw new ArgumentException("A bounded object collection request is required.");

        var definition = definitions.Get(request.Projection.QualifiedId, request.Projection.Version);
        if (definition is null || definition.ContentHash != request.Projection.ContentHash
            || definition.ObjectContract is null)
            throw new InvalidOperationException("The exact application object is unknown or stale.");
        var contract = definition.ObjectContract;
        if (!contract.Access.ReadPerspectives.Contains(request.Perspective, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("The application object is unavailable to this perspective.");
        var collection = contract.Collections.SingleOrDefault(value => value.CollectionId == request.CollectionId)
            ?? throw new InvalidOperationException("The declared object collection is unavailable.");
        var relationship = contract.Relationships.Single(value => value.RelationshipId == collection.SourceId);
        var incoming = relationship.Direction == "incoming";
        var sourceRole = incoming ? relationship.ToRole : relationship.FromRole;
        var itemRole = incoming ? relationship.FromRole : relationship.ToRole;
        if (!request.RoleEntityIds.TryGetValue(sourceRole, out var fromEntityId)
            || request.RoleEntityIds.ContainsKey(itemRole))
            throw new ArgumentException("A collection request must bind its source role and leave its item role unbound.");
        var pageSize = request.PageSize ?? collection.PageSize;
        if (pageSize is < 1 || pageSize > collection.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize));
        if (contract.Limits.ItemCount > 256)
            throw new InvalidOperationException("The collection hydration bound exceeds the prepared component batch limit.");

        var root = await materializer.MaterializeAsync(new(request.StateSpaceId, request.Projection,
            request.RoleEntityIds), cancellationToken);
        var firstEdges = await relationships.ReadCollectionAsync(request.StateSpaceId, fromEntityId,
            relationship.QualifiedKind, contract.Limits.ItemCount, incoming, cancellationToken);
        var allCandidateIds = firstEdges.Select(value => ItemEntityId(value, incoming))
            .Distinct(StringComparer.Ordinal).ToArray();
        var candidateIds = allCandidateIds;
        var nestedRelationships = contract.Relationships
            .Where(value => value.RelationshipId != relationship.RelationshipId
                && (value.Direction == "incoming" ? value.ToRole : value.FromRole) == itemRole
                && value.Cardinality == "many")
            .Select(value => (Declaration: value,
                Property: NestedProperty(relationship.TargetPointer, value.TargetPointer)))
            .Where(value => value.Property is not null).ToArray();
        if (nestedRelationships.Select(value => value.Declaration.Direction ?? "outgoing")
                .Distinct(StringComparer.Ordinal).Count() > 1)
            throw new InvalidOperationException("Nested collection relationships must use one traversal direction.");
        var nestedIncoming = nestedRelationships.FirstOrDefault().Declaration?.Direction == "incoming";
        if (nestedRelationships.Length > 0 && contract.Limits.TraversalDepth < 2)
            throw new InvalidOperationException("Nested collection relationships exceed the declared traversal depth.");
        if (nestedRelationships.Any(value => value.Declaration.RequiredEndpointComponents.Count > 0
                || value.Declaration.OptionalEndpointComponents.Count > 0))
            throw new InvalidOperationException("Nested collection references cannot hydrate endpoint components.");
        IReadOnlyList<EcsRelationshipView> firstNestedEdges = [];
        if (nestedRelationships.Length > 0 && allCandidateIds.Length > 0)
        {
            var remainingItems = contract.Limits.ItemCount - allCandidateIds.Length;
            if (remainingItems < 1)
                throw new InvalidOperationException("Nested relationship references exceed the object item bound.");
            firstNestedEdges = await relationships.ReadCollectionsAsync(request.StateSpaceId, allCandidateIds,
                nestedRelationships.Select(value => value.Declaration.QualifiedKind).ToArray(), remainingItems,
                nestedIncoming, cancellationToken);
        }
        var allEntityIds = allCandidateIds.Concat(firstNestedEdges.Select(value => ItemEntityId(value, nestedIncoming)))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (allEntityIds.Length > 256)
            throw new InvalidOperationException("The collection entity read bound exceeded.");
        var firstEntities = await entities.GetEntitiesAsync(request.StateSpaceId, allEntityIds, cancellationToken);
        var entityById = firstEntities.ToDictionary(value => value.EntityId, StringComparer.Ordinal);

        var endpointTypes = relationship.RequiredEndpointComponents
            .Concat(relationship.OptionalEndpointComponents).ToArray();
        var sourceEndpoint = incoming ? "to" : "from";
        var itemEndpoint = incoming ? "from" : "to";
        var locators = endpointTypes.Where(value => value.Endpoint == sourceEndpoint)
            .Select(value => new EcsComponentLocator(fromEntityId, value.Type.QualifiedTypeId))
            .Concat(candidateIds.SelectMany(entityId => endpointTypes.Where(value => value.Endpoint == itemEndpoint)
                .Select(value => new EcsComponentLocator(entityId, value.Type.QualifiedTypeId))))
            .Distinct().ToArray();
        if (locators.Length > 256)
            throw new InvalidOperationException("The collection component read bound exceeded.");
        var firstComponents = await components.GetComponentsAsync(request.StateSpaceId, locators, cancellationToken);
        var componentByKey = firstComponents.ToDictionary(value => (value.EntityId, value.Type.QualifiedTypeId));

        foreach (var required in relationship.RequiredEndpointComponents)
        {
            var ids = required.Endpoint == sourceEndpoint ? new[] { fromEntityId } : candidateIds;
            if (ids.Any(id => !componentByKey.TryGetValue((id, required.Type.QualifiedTypeId), out var value)
                || value.Type != required.Type))
                candidateIds = required.Endpoint == itemEndpoint
                    ? candidateIds.Where(id => componentByKey.TryGetValue((id, required.Type.QualifiedTypeId), out var value)
                        && value.Type == required.Type).ToArray()
                    : throw new InvalidOperationException("A required collection source component is missing or stale.");
        }

        var items = candidateIds.Where(entityById.ContainsKey).Select(entityId =>
            Item(entityById[entityId], endpointTypes.Where(value => value.Endpoint == itemEndpoint), componentByKey)).ToList();
        ApplyNestedReferences(items, firstNestedEdges, nestedRelationships, entityById);
        items.Sort((left, right) => Compare(left, right, collection.Order));
        var sourceFingerprint = Fingerprint(firstEdges.Concat(firstNestedEdges), firstEntities, firstComponents,
            root.SourceRevisions, request, collection.CollectionId);
        var offset = DecodeCursor(request.Cursor, sourceFingerprint, items.Count);
        var page = items.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = offset + page.Length;
        var nextCursor = nextOffset < items.Count ? EncodeCursor(sourceFingerprint, nextOffset) : null;

        var output = JsonNode.Parse(root.OutputJson)?.AsObject()
            ?? throw new InvalidOperationException("The object collection root is invalid.");
        Set(output, relationship.TargetPointer, new JsonArray(page.Select(value => value.DeepClone()).ToArray()));
        SetDeclaredMetadata(output, definition.OutputSchemaJson, items.Count, nextCursor);
        var outputJson = output.ToJsonString();
        if (Encoding.UTF8.GetByteCount(outputJson) > contract.Limits.OutputBytes
            || schemas.Validate(definition.ProfileId, definition.OutputSchemaJson, outputJson).Status != SchemaValueStatus.Valid)
            throw new InvalidOperationException("The expanded object collection fails its exact schema or output bound.");

        var revisions = root.SourceRevisions.Concat(firstComponents.Select(value =>
                new ProjectionSourceRevision(value.EntityId, value.Type, value.Revision)))
            .Distinct().OrderBy(value => value.EntityId, StringComparer.Ordinal)
            .ThenBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal).ToArray();
        return new(definition.Reference, outputJson, Array.AsReadOnly(revisions), sourceFingerprint);
    }

    private static JsonObject Item(EcsEntityView entity,
        IEnumerable<ApplicationObjectEndpointComponent> declarations,
        IReadOnlyDictionary<(string, string), EcsComponentView> values)
    {
        var item = new JsonObject { ["id"] = entity.EntityId, ["name"] = entity.Name };
        foreach (var declaration in declarations)
        {
            if (!values.TryGetValue((entity.EntityId, declaration.Type.QualifiedTypeId), out var component)
                || component.Type != declaration.Type) continue;
            var source = JsonNode.Parse(component.ValueJson) as JsonObject
                ?? throw new InvalidOperationException("Collection endpoint components must be objects.");
            foreach (var property in source)
            {
                if (item.ContainsKey(property.Key))
                    throw new InvalidOperationException("Collection endpoint component fields overlap.");
                item[property.Key] = property.Value?.DeepClone();
            }
        }
        return item;
    }

    private static void ApplyNestedReferences(
        IReadOnlyList<JsonObject> items,
        IEnumerable<EcsRelationshipView> edges,
        IEnumerable<(ApplicationObjectRelationship Declaration, string? Property)> declarations,
        IReadOnlyDictionary<string, EcsEntityView> entities)
    {
        var declarationByKind = declarations.ToDictionary(value => value.Declaration.QualifiedKind,
            value => (Property: value.Property!, Incoming: value.Declaration.Direction == "incoming"),
            StringComparer.Ordinal);
        var itemById = items.ToDictionary(value => value["id"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var item in items)
            foreach (var declaration in declarationByKind.Values)
                item[declaration.Property] = new JsonArray();
        foreach (var group in edges.Where(value => declarationByKind.ContainsKey(value.QualifiedKind))
            .GroupBy(value => (Anchor: AnchorEntityId(value,
                    declarationByKind[value.QualifiedKind].Incoming),
                value.QualifiedKind)))
        {
            if (!declarationByKind.TryGetValue(group.Key.QualifiedKind, out var declaration)) continue;
            if (!itemById.ContainsKey(group.Key.Anchor)) continue;
            var references = group.Select(value => ItemEntityId(value, declaration.Incoming))
                .Where(entities.ContainsKey).Select(value => entities[value])
                .DistinctBy(value => value.EntityId)
                .OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.EntityId, StringComparer.Ordinal)
                .Select(value => (JsonNode)new JsonObject { ["id"] = value.EntityId, ["name"] = value.Name })
                .ToArray();
            itemById[group.Key.Anchor][declaration.Property] = new JsonArray(references);
        }
    }

    private static string AnchorEntityId(EcsRelationshipView value, bool incoming) =>
        incoming ? value.ToEntityId : value.FromEntityId;

    private static string ItemEntityId(EcsRelationshipView value, bool incoming) =>
        incoming ? value.FromEntityId : value.ToEntityId;

    private static int Compare(JsonObject left, JsonObject right, IReadOnlyList<ApplicationObjectOrder> order)
    {
        foreach (var rule in order)
        {
            var comparison = StringComparer.Ordinal.Compare(Scalar(left, rule.Pointer), Scalar(right, rule.Pointer));
            if (comparison != 0) return rule.Direction == "desc" ? -comparison : comparison;
        }
        return StringComparer.Ordinal.Compare(left["id"]!.GetValue<string>(), right["id"]!.GetValue<string>());
    }

    private static string Scalar(JsonNode value, string pointer)
    {
        var current = value;
        foreach (var token in Tokens(pointer))
            current = current[token] ?? throw new InvalidOperationException("A collection order path is absent.");
        return current.ToJsonString();
    }

    private static string Fingerprint(
        IEnumerable<EcsRelationshipView> edges,
        IEnumerable<EcsEntityView> entities,
        IEnumerable<EcsComponentView> components,
        IEnumerable<ProjectionSourceRevision> rootRevisions,
        ProjectionCollectionMaterializationRequest request,
        string collectionId)
    {
        var json = JsonSerializer.Serialize(new
        {
            request.StateSpaceId,
            projection = request.Projection,
            collectionId,
            roles = request.RoleEntityIds.OrderBy(value => value.Key),
            edges = edges.OrderBy(value => value.ToEntityId).Select(value => new
                { value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.Revision }),
            entities = entities.OrderBy(value => value.EntityId).Select(value =>
                new { value.EntityId, value.Name, value.Revision }),
            components = components.OrderBy(value => value.EntityId).ThenBy(value => value.Type.QualifiedTypeId)
                .Select(value => new { value.EntityId, value.Type, value.Revision }),
            rootRevisions = rootRevisions.OrderBy(value => value.EntityId)
                .ThenBy(value => value.Type.QualifiedTypeId)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static int DecodeCursor(string? cursor, string fingerprint, int count)
    {
        if (cursor is null) return 0;
        try
        {
            var bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/') + new string('=', (4 - cursor.Length % 4) % 4));
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("source").GetString() != fingerprint
                || !root.GetProperty("offset").TryGetInt32(out var offset) || offset < 0 || offset >= count)
                throw new InvalidOperationException("OBJECT_COLLECTION_CURSOR_STALE");
            return offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("OBJECT_COLLECTION_CURSOR_INVALID", exception);
        }
    }

    private static string EncodeCursor(string fingerprint, int offset)
    {
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { source = fingerprint, offset }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return value;
    }

    private static void SetDeclaredMetadata(JsonObject output, string schemaJson, int totalCount, string? nextCursor)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        if (!schema.RootElement.TryGetProperty("properties", out var properties)) return;
        if (properties.TryGetProperty("totalCount", out _)) output["totalCount"] = totalCount;
        if (properties.TryGetProperty("complete", out _)) output["complete"] = nextCursor is null;
        if (properties.TryGetProperty("nextCursor", out _)) output["nextCursor"] = nextCursor;
    }

    private static void Set(JsonObject root, string pointer, JsonNode value)
    {
        var tokens = Tokens(pointer).ToArray();
        if (tokens.Length == 0) throw new InvalidOperationException("A collection cannot replace the object root.");
        var current = root;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            current[tokens[index]] ??= new JsonObject();
            current = current[tokens[index]]!.AsObject();
        }
        current[tokens[^1]] = value;
    }

    private static string? NestedProperty(string collectionPointer, string candidatePointer)
    {
        var collection = Tokens(collectionPointer).ToArray();
        var candidate = Tokens(candidatePointer).ToArray();
        return candidate.Length == collection.Length + 2
            && candidate.Take(collection.Length).SequenceEqual(collection, StringComparer.Ordinal)
            && candidate[^2] == "*" ? candidate[^1] : null;
    }

    private static IEnumerable<string> Tokens(string pointer) => pointer == "" ? []
        : pointer.Split('/').Skip(1).Select(value => value.Replace("~1", "/").Replace("~0", "~"));
}
