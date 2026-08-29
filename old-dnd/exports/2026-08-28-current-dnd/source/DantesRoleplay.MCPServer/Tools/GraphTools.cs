using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Public-query adapter for the generic bounded graph materialiser.</summary>
public sealed class GraphTools
{
    public Task<ToolEnvelope> GetGraphAsync(
        IGraphProjectionReader graphs,
        IOperationLog log,
        GraphQuery query,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "get_graph", async () =>
        {
            var result = await graphs.ReadAsync(query, cancellationToken);
            if (!result.Ok)
            {
                return ToolOutcome.Fail(
                    result.ErrorCode,
                    result.ErrorMessage,
                    "query(kind: \"graph\", id: \"...\", componentIds: [\"...\"], containmentDepth: 0, relationshipKinds: [], relationshipDepth: 0)",
                    "Graph query was rejected before any partial projection was returned.");
            }

            var projection = result.Projection!;
            return ToolOutcome.Ok(
                projection,
                $"Graph rooted at '{projection.RootId}': {projection.Nodes.Count} node(s), {projection.Edges.Count} edge(s)"
                + (projection.Truncated is null ? "." : $"; truncated by {projection.Truncated.Limit}."),
                "query(kind: \"entities\", id: \"...\") — inspect one graph node in full before changing it.");
        });
}
