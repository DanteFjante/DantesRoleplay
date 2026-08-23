using System.Text.Json;

namespace DantesRoleplay.World;

/// <summary>One closed, generic request for a bounded read-only world graph.</summary>
public sealed record GraphQuery(
    string Id,
    IReadOnlyList<string>? ComponentIds,
    int? ContainmentDepth,
    IReadOnlyList<string>? RelationshipKinds,
    int? RelationshipDepth,
    int? MaxNodes = null,
    int? MaxEdges = null);

/// <summary>One selected entity. Components are limited to the request's declared definitions.</summary>
public sealed record GraphNode(
    string Id,
    string Name,
    IReadOnlyList<ComponentView> Components,
    string? ContainerId,
    string ContainerSlot);

/// <summary>One selected relationship. Its raw data remains an authored JSON object.</summary>
public sealed record GraphEdge(
    string FromEntityId,
    string ToEntityId,
    string Kind,
    string Data);

/// <summary>States why a successful graph projection stopped before every eligible item was added.</summary>
public sealed record GraphTruncation(string Limit, int? OmittedCount);

/// <summary>A deterministic, bounded read-only projection with no game-specific vocabulary.</summary>
public sealed record GraphProjection(
    string RootId,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    GraphTruncation? Truncated);

/// <summary>Either a projection or one stable, recoverable reason it could not be materialised.</summary>
public sealed record GraphProjectionResult(
    GraphProjection? Projection,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => Projection is not null && string.IsNullOrEmpty(ErrorCode);

    public static GraphProjectionResult Fail(string code, string message) => new(null, code, message);
    public static GraphProjectionResult Success(GraphProjection projection) => new(projection);
}

public interface IGraphProjectionReader
{
    Task<GraphProjectionResult> ReadAsync(
        GraphQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic graph materialisation over the five world structures. It deliberately knows neither
/// component IDs nor relationship kinds, so product procedures own all game-specific recipes.
/// </summary>
public sealed class GraphProjectionReader(IWorldStore world) : IGraphProjectionReader
{
    private const int DefaultMaxNodes = 50;
    private const int DefaultMaxEdges = 100;

    private readonly IWorldStore _world = world;

    public async Task<GraphProjectionResult> ReadAsync(GraphQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(query, cancellationToken);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var request = validation.Request!;
        var root = await _world.GetEntityAsync(request.Id, cancellationToken);
        if (root is null)
        {
            return GraphProjectionResult.Fail("UNKNOWN_GRAPH_ROOT", $"Graph root '{request.Id}' does not exist or is deleted.");
        }

        var selected = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var containmentFrontier = new List<string> { root.Id };
        GraphTruncation? truncated = null;

        for (var depth = 0; depth < request.ContainmentDepth && containmentFrontier.Count > 0 && truncated is null; depth++)
        {
            var next = new List<string>();
            foreach (var containerId in containmentFrontier.Order(StringComparer.Ordinal))
            {
                var children = (await _world.GetContentsAsync(containerId, cancellationToken))
                    .OrderBy(child => child.ContainedId, StringComparer.Ordinal)
                    .ThenBy(child => child.Slot, StringComparer.Ordinal);

                foreach (var child in children)
                {
                    if (selected.Contains(child.ContainedId))
                    {
                        continue;
                    }

                    if (selected.Count >= request.MaxNodes)
                    {
                        truncated = new GraphTruncation("maxNodes", null);
                        break;
                    }

                    selected.Add(child.ContainedId);
                    next.Add(child.ContainedId);
                }

                if (truncated is not null)
                {
                    break;
                }
            }

            containmentFrontier = next;
        }

        var edges = new Dictionary<(string From, string To, string Kind), GraphEdge>();
        var relationshipFrontier = selected.Order(StringComparer.Ordinal).ToList();

        for (var depth = 0; depth < request.RelationshipDepth && relationshipFrontier.Count > 0 && truncated is null; depth++)
        {
            var next = new List<string>();
            foreach (var entityId in relationshipFrontier.Order(StringComparer.Ordinal))
            {
                var relationships = (await _world.GetRelationshipsAsync(entityId, includeIncoming: true, cancellationToken))
                    .Where(relationship => request.RelationshipKinds.Contains(relationship.Kind, StringComparer.Ordinal))
                    .OrderBy(relationship => relationship.Kind, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.FromEntityId, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.ToEntityId, StringComparer.Ordinal);

                foreach (var relationship in relationships)
                {
                    var key = (relationship.FromEntityId, relationship.ToEntityId, relationship.Kind);
                    if (edges.ContainsKey(key))
                    {
                        continue;
                    }

                    if (!IsObject(relationship.Data))
                    {
                        return GraphProjectionResult.Fail("CORRUPT_GRAPH_RELATIONSHIP", $"Relationship '{relationship.Kind}' from '{relationship.FromEntityId}' to '{relationship.ToEntityId}' has non-object data.");
                    }

                    var endpointId = relationship.FromEntityId == entityId
                        ? relationship.ToEntityId
                        : relationship.FromEntityId;
                    var endpoint = await _world.GetEntityAsync(endpointId, cancellationToken);
                    if (endpoint is null)
                    {
                        return GraphProjectionResult.Fail("DANGLING_GRAPH_RELATIONSHIP", $"Relationship '{relationship.Kind}' from '{relationship.FromEntityId}' to '{relationship.ToEntityId}' has a missing or deleted endpoint.");
                    }

                    if (edges.Count >= request.MaxEdges)
                    {
                        truncated = new GraphTruncation("maxEdges", null);
                        break;
                    }

                    if (!selected.Contains(endpoint.Id) && selected.Count >= request.MaxNodes)
                    {
                        truncated = new GraphTruncation("maxNodes", null);
                        break;
                    }

                    edges[key] = new GraphEdge(relationship.FromEntityId, relationship.ToEntityId, relationship.Kind, relationship.Data);
                    if (selected.Add(endpoint.Id))
                    {
                        next.Add(endpoint.Id);
                    }
                }

                if (truncated is not null)
                {
                    break;
                }
            }

            relationshipFrontier = next;
        }

        var entities = await _world.GetEntitiesAsync(selected, request.ComponentIds, cancellationToken);
        if (entities.Count != selected.Count)
        {
            return GraphProjectionResult.Fail("MISSING_GRAPH_NODE", "A selected graph node was deleted before it could be materialised.");
        }

        var nodes = entities
            .Select(entity => new GraphNode(entity.Id, entity.Name, entity.Components, entity.ContainerId, entity.ContainerSlot))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        return GraphProjectionResult.Success(new GraphProjection(
            root.Id,
            nodes,
            edges.Values
                .OrderBy(edge => edge.Kind, StringComparer.Ordinal)
                .ThenBy(edge => edge.FromEntityId, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToEntityId, StringComparer.Ordinal)
                .ToList(),
            truncated));
    }

    private async Task<(ValidatedGraphQuery? Request, GraphProjectionResult? Error)> ValidateAsync(GraphQuery query, CancellationToken cancellationToken)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.Id) || query.Id != query.Id.Trim())
        {
            return (null, GraphProjectionResult.Fail("INVALID_GRAPH_QUERY", "id must be one nonblank, trimmed graph-root entity id."));
        }

        if (query.ComponentIds is null || query.ComponentIds.Count is < 1 or > 12 || !DistinctNonblank(query.ComponentIds))
        {
            return (null, GraphProjectionResult.Fail("INVALID_GRAPH_QUERY", "componentIds must contain 1–12 distinct nonblank declared component-definition ids."));
        }

        var declared = (await _world.GetDefinitionsAsync(cancellationToken)).Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = query.ComponentIds.Where(id => !declared.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return (null, GraphProjectionResult.Fail("UNKNOWN_GRAPH_COMPONENT", $"componentIds names undeclared component definitions: {string.Join(", ", unknown)}."));
        }

        if (query.RelationshipKinds is null || query.RelationshipKinds.Count > 12 || !DistinctNonblank(query.RelationshipKinds))
        {
            return (null, GraphProjectionResult.Fail("INVALID_GRAPH_QUERY", "relationshipKinds must contain 0–12 distinct nonblank relationship kinds."));
        }

        if (query.ContainmentDepth is not (>= 0 and <= 2) || query.RelationshipDepth is not (>= 0 and <= 2))
        {
            return (null, GraphProjectionResult.Fail("INVALID_GRAPH_QUERY", "containmentDepth and relationshipDepth must each be integers from 0 through 2."));
        }

        var maxNodes = query.MaxNodes ?? DefaultMaxNodes;
        var maxEdges = query.MaxEdges ?? DefaultMaxEdges;
        if (maxNodes is < 1 or > 100 || maxEdges is < 0 or > 200)
        {
            return (null, GraphProjectionResult.Fail("INVALID_GRAPH_QUERY", "maxNodes must be 1–100 and maxEdges must be 0–200."));
        }

        return (new ValidatedGraphQuery(
            query.Id,
            query.ComponentIds.Order(StringComparer.Ordinal).ToArray(),
            query.ContainmentDepth.Value,
            query.RelationshipKinds.Order(StringComparer.Ordinal).ToArray(),
            query.RelationshipDepth.Value,
            maxNodes,
            maxEdges), null);
    }

    private static bool DistinctNonblank(IReadOnlyList<string> values) =>
        values.All(value => !string.IsNullOrWhiteSpace(value) && value == value.Trim())
        && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool IsObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ValidatedGraphQuery(
        string Id,
        IReadOnlyList<string> ComponentIds,
        int ContainmentDepth,
        IReadOnlyList<string> RelationshipKinds,
        int RelationshipDepth,
        int MaxNodes,
        int MaxEdges);
}
