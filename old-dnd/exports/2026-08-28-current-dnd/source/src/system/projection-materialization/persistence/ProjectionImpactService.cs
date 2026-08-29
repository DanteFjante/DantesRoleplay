using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Projections;

/// <summary>Pure read-side traversal over declared structural dependencies.</summary>
public sealed class ProjectionImpactService(
    IApplicationRegistry applications,
    IProjectionImpactSnapshotReader snapshots) : IProjectionImpactService
{
    public ProjectionImpactReport Analyze(
        ApplicationIdentifier applicationId,
        string? rootId = null,
        bool transitive = true)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (applications.Get(applicationId) is null)
            throw new ProjectionImpactException(
                "APPLICATION_UNKNOWN", "The requested application is not registered.");

        var snapshot = snapshots.Read(applicationId);
        var fingerprint = Fingerprint(snapshot);
        if (rootId is null)
            return new(applicationId, fingerprint, null, transitive,
                snapshot.Nodes, snapshot.Edges, Array.Empty<ProjectionImpactDependent>());

        if (!ValidRoot(rootId))
            throw new ProjectionImpactException(
                "INVALID_DEPENDENCY_NODE", "id is not a valid canonical dependency node identifier.");
        var seeds = ResolveSeeds(snapshot.Nodes, rootId);
        if (seeds.Count == 0)
            throw new ProjectionImpactException(
                "DEPENDENCY_NODE_UNKNOWN", "The requested exact dependency node is not declared for this application.");

        var root = Root(rootId, seeds);
        var dependents = Traverse(snapshot, seeds.Select(node => node.Id), transitive);
        return new(applicationId, fingerprint, root, transitive,
            snapshot.Nodes, snapshot.Edges, dependents);
    }

    private static IReadOnlyList<ProjectionImpactNode> ResolveSeeds(
        IReadOnlyList<ProjectionImpactNode> nodes,
        string rootId)
    {
        var exact = nodes.Where(node => node.Id == rootId).ToArray();
        if (exact.Length > 0) return exact;
        if (!rootId.StartsWith("component:", StringComparison.Ordinal) || rootId.Contains('#'))
            return [];
        var prefix = rootId + "#";
        return nodes.Where(node => node.Id.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
    }

    private static ProjectionImpactRoot Root(string rootId, IReadOnlyList<ProjectionImpactNode> seeds)
    {
        var first = seeds[0];
        return new(rootId,
            rootId.Contains('#') ? "component-field" : first.Kind == "component-field" ? "component" : first.Kind,
            first.QualifiedId, first.Version, first.ContractHash,
            rootId.Contains('#') ? first.Pointer : null);
    }

    private static IReadOnlyList<ProjectionImpactDependent> Traverse(
        ProjectionImpactSnapshot snapshot,
        IEnumerable<string> seedIds,
        bool transitive)
    {
        var nodes = snapshot.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var adjacency = snapshot.Edges.GroupBy(edge => edge.DependencyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(edge => edge.ConsumerId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.Reason, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var frontier = seedIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var currentDepth = 0;
        while (frontier.Length > 0)
        {
            currentDepth++;
            var next = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var source in frontier)
            {
                if (!adjacency.TryGetValue(source, out var outgoing)) continue;
                foreach (var edge in outgoing)
                {
                    if (!depth.TryGetValue(edge.ConsumerId, out var knownDepth))
                    {
                        depth[edge.ConsumerId] = currentDepth;
                        reasons[edge.ConsumerId] = new(StringComparer.Ordinal) { edge.Reason };
                        next.Add(edge.ConsumerId);
                    }
                    else if (knownDepth == currentDepth)
                    {
                        reasons[edge.ConsumerId].Add(edge.Reason);
                    }
                }
            }
            if (!transitive) break;
            frontier = next.ToArray();
        }

        return Array.AsReadOnly(depth.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ProjectionImpactDependent(nodes[pair.Key], pair.Value,
                Array.AsReadOnly(reasons[pair.Key].Order(StringComparer.Ordinal).ToArray())))
            .ToArray());
    }

    private static bool ValidRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500
            || !(value.StartsWith("component:", StringComparison.Ordinal)
                 || value.StartsWith("projection:", StringComparison.Ordinal))) return false;
        var body = value[(value.IndexOf(':') + 1)..];
        if (value.StartsWith("projection:", StringComparison.Ordinal) && body.Contains('#')) return false;
        var identity = body.Split('#', 2)[0];
        var separator = identity.LastIndexOf('@');
        return separator > 0 && int.TryParse(identity[(separator + 1)..], out var version)
            && version > 0 && !string.IsNullOrWhiteSpace(identity[..separator])
            && (!body.Contains('#') || body[(body.IndexOf('#') + 1)..] is "" or ['/', ..]);
    }

    private static string Fingerprint(ProjectionImpactSnapshot snapshot)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = snapshot.ApplicationId.Value,
            nodes = snapshot.Nodes.Select(node => new
            {
                node.Id, node.Kind, node.QualifiedId, node.Version, node.ContractHash, node.Pointer
            }),
            edges = snapshot.Edges.Select(edge => new
            {
                edge.DependencyId, edge.ConsumerId, edge.Reason
            })
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}
