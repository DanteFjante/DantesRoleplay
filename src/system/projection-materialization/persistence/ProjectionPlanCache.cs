using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Projections;

/// <summary>Bounded process-wide cache of immutable, exact-version structural read plans.</summary>
public sealed class ProjectionPlanCache : IProjectionPlanCacheDiagnostics
{
    internal const int MaximumPlans = 256;
    internal const int MaximumDeclarationBytes = 2 * 1024 * 1024;
    internal const int MaximumMappingNodes = 32_000;

    private readonly object gate = new();
    private readonly Dictionary<ProjectionReference, Entry> entries = [];
    private readonly LinkedList<ProjectionReference> recency = [];
    private int declarationBytes;
    private int mappingNodes;
    private long preparations;
    private long hits;
    private long evictions;

    public ProjectionPlanCacheSnapshot Snapshot
    {
        get
        {
            lock (gate)
                return new(entries.Count, declarationBytes, mappingNodes,
                    preparations, hits, evictions);
        }
    }

    internal PreparedProjectionPlan GetOrPrepare(
        ProjectionReference reference,
        Func<PreparedProjectionPlan> prepare)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(prepare);
        Entry entry;
        lock (gate)
        {
            if (entries.TryGetValue(reference, out entry!))
            {
                while (entry.Preparing) Monitor.Wait(gate);
                if (entry.Plan is null)
                    return GetOrPrepare(reference, prepare);
                hits++;
                Touch(entry);
                return entry.Plan;
            }
            entry = new(reference, recency.AddFirst(reference));
            entries.Add(reference, entry);
        }

        PreparedProjectionPlan plan;
        try { plan = prepare(); }
        catch
        {
            lock (gate)
            {
                entries.Remove(reference);
                recency.Remove(entry.Recency);
                entry.Preparing = false;
                Monitor.PulseAll(gate);
            }
            throw;
        }

        if (plan.DeclarationBytes > MaximumDeclarationBytes || plan.MappingNodes > MaximumMappingNodes)
        {
            lock (gate)
            {
                entries.Remove(reference);
                recency.Remove(entry.Recency);
                entry.Preparing = false;
                Monitor.PulseAll(gate);
            }
            throw new InvalidOperationException("A prepared projection plan exceeds the cache resource profile.");
        }

        lock (gate)
        {
            try
            {
                EvictFor(plan.DeclarationBytes, plan.MappingNodes, entry);
                entry.Plan = plan;
                entry.Preparing = false;
                declarationBytes += plan.DeclarationBytes;
                mappingNodes += plan.MappingNodes;
                preparations++;
                Monitor.PulseAll(gate);
                return plan;
            }
            catch
            {
                entries.Remove(reference);
                recency.Remove(entry.Recency);
                entry.Preparing = false;
                Monitor.PulseAll(gate);
                throw;
            }
        }
    }

    private void EvictFor(int bytes, int nodes, Entry incoming)
    {
        while (entries.Count > MaximumPlans
               || declarationBytes + bytes > MaximumDeclarationBytes
               || mappingNodes + nodes > MaximumMappingNodes)
        {
            var candidateNode = recency.Last;
            while (candidateNode is not null
                   && (candidateNode.Value == incoming.Reference
                       || entries[candidateNode.Value].Preparing))
                candidateNode = candidateNode.Previous;
            if (candidateNode is null)
                throw new InvalidOperationException("The prepared projection cache is temporarily saturated.");
            var candidate = entries[candidateNode.Value];
            entries.Remove(candidate.Reference);
            recency.Remove(candidateNode);
            declarationBytes -= candidate.Plan!.DeclarationBytes;
            mappingNodes -= candidate.Plan.MappingNodes;
            evictions++;
        }
    }

    private void Touch(Entry entry)
    {
        recency.Remove(entry.Recency);
        recency.AddFirst(entry.Recency);
    }

    private sealed class Entry(ProjectionReference reference, LinkedListNode<ProjectionReference> recency)
    {
        public ProjectionReference Reference { get; } = reference;
        public LinkedListNode<ProjectionReference> Recency { get; } = recency;
        public bool Preparing { get; set; } = true;
        public PreparedProjectionPlan? Plan { get; set; }
    }
}

internal sealed record PreparedProjectionPlan(
    RegisteredProjectionDefinition Root,
    IReadOnlyList<PreparedProjectionNode> Nodes,
    int DeclarationBytes,
    int MappingNodes);

internal sealed record PreparedProjectionNode(
    RegisteredProjectionDefinition Definition,
    IReadOnlyDictionary<string, string> RootRoles,
    IReadOnlyDictionary<string, int> Children,
    IReadOnlySet<string> OptionalComponentInputs,
    IReadOnlySet<string> OptionalDependencyInputs);

internal static class ProjectionPlanCompiler
{
    public static PreparedProjectionPlan Compile(
        RegisteredProjectionDefinition root,
        Func<ProjectionReference, RegisteredProjectionDefinition> resolve)
    {
        var nodes = new List<PreparedProjectionNode>();
        var indexed = new Dictionary<string, int>(StringComparer.Ordinal);
        Add(root, root.EntityRoles.ToDictionary(value => value, value => value, StringComparer.Ordinal),
            resolve, nodes, indexed, new HashSet<string>(StringComparer.Ordinal), 0);
        var declarationBytes = nodes.Sum(value => DeclarationBytes(value.Definition));
        var mappingNodes = nodes.Sum(value => value.Definition.Mappings.Count
            + value.Definition.ComponentInputs.Count + value.Definition.DependencyInputs.Count);
        return new(root, Array.AsReadOnly(nodes.ToArray()), declarationBytes, mappingNodes);
    }

    private static int Add(
        RegisteredProjectionDefinition definition,
        IReadOnlyDictionary<string, string> rootRoles,
        Func<ProjectionReference, RegisteredProjectionDefinition> resolve,
        List<PreparedProjectionNode> nodes,
        Dictionary<string, int> indexed,
        HashSet<string> visiting,
        int depth)
    {
        if (depth > 16) throw new InvalidOperationException("Projection dependency depth exceeds the prepared-plan bound.");
        var identity = definition.QualifiedId + "@" + definition.Version + ":"
            + string.Join(',', rootRoles.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => value.Key + "=" + value.Value));
        if (indexed.TryGetValue(identity, out var existing)) return existing;
        if (!visiting.Add(definition.QualifiedId + "@" + definition.Version))
            throw new InvalidOperationException("A projection dependency cycle cannot be prepared.");
        var children = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var input in definition.DependencyInputs)
        {
            var child = resolve(input.Projection);
            var childRoles = input.RoleBindings.ToDictionary(value => value.Key,
                value => rootRoles[value.Value], StringComparer.Ordinal);
            children.Add(input.InputId, Add(child, childRoles, resolve, nodes, indexed, visiting, depth + 1));
        }
        visiting.Remove(definition.QualifiedId + "@" + definition.Version);
        var optionalComponents = definition.ObjectContract?.Sources.Where(value => !value.Required)
            .Select(value => value.InputId).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var optionalDependencies = definition.ObjectContract?.References.Where(value => !value.Required)
            .Select(value => value.InputId).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var index = nodes.Count;
        nodes.Add(new(definition,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(rootRoles, StringComparer.Ordinal)),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(children),
            optionalComponents, optionalDependencies));
        indexed.Add(identity, index);
        return index;
    }

    private static int DeclarationBytes(RegisteredProjectionDefinition definition)
    {
        var contract = definition.ObjectContract is null ? "" : JsonSerializer.Serialize(definition.ObjectContract);
        return Encoding.UTF8.GetByteCount(definition.OutputSchemaJson) + Encoding.UTF8.GetByteCount(contract)
            + definition.ComponentInputs.Sum(value => value.InputId.Length + value.EntityRole.Length
                + value.Type.QualifiedTypeId.Length + value.Type.SchemaHash.Length + 16)
            + definition.DependencyInputs.Sum(value => value.InputId.Length + value.Projection.QualifiedId.Length
                + value.Projection.ContentHash.Length + value.RoleBindings.Sum(role => role.Key.Length + role.Value.Length) + 16)
            + definition.Mappings.Sum(value => value.InputId.Length + value.SourcePointer.Length
                + value.TargetPointer.Length);
    }
}
