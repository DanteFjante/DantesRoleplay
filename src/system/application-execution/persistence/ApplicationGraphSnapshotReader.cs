using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Reads explicit, bounded graph paths for a pure application mechanic.</summary>
public sealed class ApplicationGraphSnapshotReader(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    IProjectionReadTransaction? transactions = null) : IApplicationGraphSnapshotReader
{
    public Task<ApplicationGraphSnapshotReadResult> ReadAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        MechanicRequirements requirements,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyDictionary<string, string> roleAssignments,
        string inputJson = "{}",
        CancellationToken cancellationToken = default) =>
        transactions is null
            ? ReadCoreAsync(stateSpaceId, applicationId, requirements, mapping, roleAssignments, inputJson, cancellationToken)
            : transactions.ExecuteAsync(ct => ReadCoreAsync(stateSpaceId, applicationId, requirements, mapping,
                roleAssignments, inputJson, ct), cancellationToken);

    private async Task<ApplicationGraphSnapshotReadResult> ReadCoreAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        MechanicRequirements requirements,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyDictionary<string, string> roleAssignments,
        string inputJson,
        CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, MechanicGraphSnapshot>(StringComparer.Ordinal);
        var evidence = new List<MechanicGraphSnapshotEvidence>();
        var problems = new List<string>();
        if (requirements.GraphSnapshots.Count == 0)
            return new(snapshots, problems, evidence);

        var stateSpace = stateSpaces.Get(stateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != applicationId)
            return Failed("GRAPH_STATE_SPACE_MISMATCH: The graph state space is unavailable.");

        var entities = new Dictionary<string, ApplicationEcsEntityRecord>(StringComparer.Ordinal);
        var containmentByChild = new Dictionary<string, ApplicationEcsContainmentRecord[]>(StringComparer.Ordinal);
        foreach (var (name, declaration) in requirements.GraphSnapshots.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var graphProblemStart = problems.Count;
            if (!TryPageOffset(declaration.Page, inputJson, out var pageOffset))
            {
                problems.Add($"GRAPH_PAGE_INPUT_INVALID ({name}): The declared graph page cursor is invalid.");
                continue;
            }
            if (!roleAssignments.TryGetValue(declaration.RootRole, out var rootId)
                || await LoadEntity(rootId) is null)
            {
                problems.Add($"GRAPH_ROOT_UNAVAILABLE: Graph '{name}' names an unavailable root role.");
                continue;
            }
            var graphBudget = new GraphBudget();
            if (!graphBudget.TryNode(rootId, declaration, null, new HashSet<string>(StringComparer.Ordinal), out var rootReason))
            {
                problems.Add($"GRAPH_INCOMPLETE ({name}/root): {rootReason}.");
                continue;
            }
            var root = await Node(rootId, declaration.ComponentIds, declaration, null, graphBudget);
            var stepOutputs = new Dictionary<string, MechanicGraphStepSnapshot>(StringComparer.Ordinal);
            var allContainment = new List<MechanicGraphContainment>();
            var snapshotEvidence = new List<MechanicGraphSnapshotEvidence>();
            var complete = true;
            foreach (var step in declaration.Steps)
            {
                var frontier = step.From.Equals("root", StringComparison.Ordinal)
                    ? [rootId]
                    : stepOutputs.TryGetValue(step.From, out var parentStep)
                        ? parentStep.Nodes.Select(value => value.Id).ToArray()
                        : [];
                if (!step.From.Equals("root", StringComparison.Ordinal)
                    && stepOutputs.TryGetValue(step.From, out parentStep) && !parentStep.Complete)
                {
                    complete = false;
                    if (declaration.RequireComplete)
                        problems.Add($"GRAPH_INCOMPLETE ({name}/{step.Id}): UPSTREAM_INCOMPLETE.");
                    stepOutputs[step.Id] = new([], [], false, "UPSTREAM_INCOMPLETE", 0, 0, 0);
                    continue;
                }
                if (!frontier.Any() && !step.From.Equals("root", StringComparison.Ordinal))
                {
                    stepOutputs[step.Id] = new([], [], true, null, 0, 0, 0);
                    continue;
                }
                frontier = frontier.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var stepNodes = new HashSet<string>(StringComparer.Ordinal);
                var stepEdges = new List<MechanicGraphEdge>();
                string? reason = null;
                var observedDepth = 0;
                if (step.Containment == "ancestors")
                {
                    foreach (var entityId in frontier)
                    {
                        var current = entityId;
                        var ancestry = new HashSet<string>(StringComparer.Ordinal);
                        for (var depth = 1; depth <= Math.Min(step.MaxDepth, declaration.MaxDepth); depth++)
                        {
                            if (!ancestry.Add(current)) { reason = "CONTAINMENT_CYCLE"; break; }
                            var parents = await LoadParents(current);
                            if (parents.Length == 0) break;
                            if (parents.Length != 1) { reason = "CONTAINMENT_AMBIGUOUS"; break; }
                            var parent = parents[0];
                            if (await LoadEntity(parent.ContainerEntityId) is null) { reason = "CONTAINMENT_ENDPOINT_UNAVAILABLE"; break; }
                            if (!graphBudget.TryNode(parent.ContainerEntityId, declaration, step, stepNodes, out reason)) break;
                            stepNodes.Add(parent.ContainerEntityId);
                            var containment = new MechanicGraphContainment(parent.ContainerEntityId, current,
                                parent.Slot, parent.Revision);
                            var containmentBytes = JsonSerializer.SerializeToUtf8Bytes(containment).Length;
                            if (!graphBudget.TryBytes(containmentBytes, declaration, out reason)) break;
                            graphBudget.AddBytes(containmentBytes);
                            allContainment.Add(containment);
                            observedDepth = Math.Max(observedDepth, depth);
                            current = parent.ContainerEntityId;
                            if (depth == Math.Min(step.MaxDepth, declaration.MaxDepth))
                            {
                                var nextParents = await LoadParents(current);
                                if (nextParents.Length > 0)
                                {
                                    reason = ancestry.Contains(nextParents[0].ContainerEntityId)
                                        ? "CONTAINMENT_CYCLE" : "CONTAINMENT_DEPTH_LIMIT";
                                    break;
                                }
                            }
                        }
                        if (reason is not null) break;
                    }
                }
                else
                {
                    foreach (var anchor in frontier)
                    foreach (var kind in step.RelationshipKinds.Order(StringComparer.Ordinal))
                    foreach (var incoming in step.Direction == "either"
                        ? new[] { false, true }
                        : new[] { step.Direction == "incoming" })
                    {
                        if (reason is not null) break;
                        if (!mapping.Relationships.TryGetValue(kind, out var qualifiedKind))
                        {
                            reason = "GRAPH_RELATIONSHIP_MAPPING_MISSING";
                            break;
                        }
                        var remainingEdges = Math.Max(0, Math.Min(declaration.MaxEdges - graphBudget.EdgeCount,
                            step.MaxEdges - stepEdges.Count));
                        var relationshipQuery = db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
                            .Where(value => value.StateSpaceId == stateSpaceId && value.QualifiedKind == qualifiedKind
                                && (incoming ? value.ToEntityId == anchor : value.FromEntityId == anchor));
                        relationshipQuery = incoming
                            ? relationshipQuery.Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                                entity.StateSpaceId == stateSpaceId && entity.Id == value.FromEntityId
                                && entity.DeletedAtUtc == null))
                            : relationshipQuery.Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                                entity.StateSpaceId == stateSpaceId && entity.Id == value.ToEntityId
                                && entity.DeletedAtUtc == null));
                        var candidateRows = await relationshipQuery
                            .Select(value => new
                            {
                                value.StateSpaceId, value.FromEntityId, value.ToEntityId,
                                value.QualifiedKind, value.Revision, DataLength = value.Data.Length
                            })
                            .OrderBy(value => value.FromEntityId)
                            .ThenBy(value => value.ToEntityId)
                            .Take(remainingEdges + 1).ToArrayAsync(cancellationToken);
                        var edgeOverflow = candidateRows.Length > remainingEdges;
                        var rows = candidateRows.Take(remainingEdges).ToArray();
                        var collection = new MechanicGraphSnapshotEvidence(anchor, qualifiedKind, incoming,
                            rows.Select(value => new MechanicRelationshipRevision(
                                value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.Revision)).ToArray());
                        var collectionBytes = JsonSerializer.SerializeToUtf8Bytes(collection).Length;
                        if (reason is null && !graphBudget.TryBytes(collectionBytes, declaration, out reason))
                            reason = "GRAPH_BYTE_LIMIT";
                        if (reason is null) graphBudget.AddBytes(collectionBytes);
                        if (reason is null)
                        {
                            evidence.Add(collection);
                            snapshotEvidence.Add(collection);
                        }
                        foreach (var row in rows)
                        {
                            if (reason is not null) break;
                            if (stepEdges.Any(value => value.FromEntityId == row.FromEntityId
                                && value.ToEntityId == row.ToEntityId && value.Kind == row.QualifiedKind))
                                continue;
                            if (!graphBudget.TryEdge(declaration, step, stepEdges.Count, out reason)) break;
                            var endpoint = row.FromEntityId == anchor ? row.ToEntityId : row.FromEntityId;
                            if (!graphBudget.TryNode(endpoint, declaration, step, stepNodes, out reason)) break;
                            if (await LoadEntity(endpoint) is null)
                            { reason = "GRAPH_ENDPOINT_UNAVAILABLE"; break; }
                            stepNodes.Add(endpoint);
                            JsonElement? data = null;
                            if (step.IncludeEdgeData)
                            {
                                // Fetch and parse at most one selected payload at a time. The SQL
                                // length check prevents loading a payload that already exceeds the
                                // remaining budget; UTF-8 and serialized overhead are charged below.
                                if (!graphBudget.TryBytes(row.DataLength, declaration, out reason)) break;
                                var payload = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
                                    .Where(value => value.StateSpaceId == stateSpaceId && value.FromEntityId == row.FromEntityId
                                        && value.ToEntityId == row.ToEntityId && value.QualifiedKind == row.QualifiedKind)
                                    .Select(value => value.Data).SingleAsync(cancellationToken);
                                if (!graphBudget.TryBytes(Encoding.UTF8.GetByteCount(payload), declaration, out reason))
                                {
                                    reason = "GRAPH_BYTE_LIMIT";
                                    break;
                                }
                                try { using var parsed = JsonDocument.Parse(payload); data = parsed.RootElement.Clone(); }
                                catch (JsonException) { reason = "RELATIONSHIP_DATA_INVALID"; break; }
                            }
                            var edgeBytes = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                row.FromEntityId, row.ToEntityId, row.QualifiedKind, data, row.Revision
                            }).Length;
                            if (!graphBudget.TryBytes(edgeBytes, declaration, out reason)) break;
                            graphBudget.AddBytes(edgeBytes);
                            stepEdges.Add(new(row.FromEntityId, row.ToEntityId, row.QualifiedKind, data, row.Revision));
                            observedDepth = 1;
                        }
                        if (edgeOverflow && reason is null) reason = "GRAPH_EDGE_LIMIT";
                        if (reason is not null) break;
                    }
                }
                if (reason is not null)
                {
                    complete = false;
                    if (declaration.RequireComplete) problems.Add($"GRAPH_INCOMPLETE ({name}/{step.Id}): {reason}.");
                }
                var nodeProblemStart = problems.Count;
                var nodes = new List<MechanicGraphNode>(stepNodes.Count);
                foreach (var id in stepNodes.Order(StringComparer.Ordinal))
                    nodes.Add(await Node(id, step.ComponentIds, declaration, step, graphBudget));
                if (nodes.Any(node => node is null) || problems.Count != nodeProblemStart)
                {
                    complete = false;
                    reason ??= "GRAPH_NODE_INVALID";
                }
                stepOutputs[step.Id] = new(nodes!, stepEdges, reason is null, reason,
                    observedDepth, nodes!.Count, stepEdges.Count);
            }
            var coverage = new GraphSnapshotCoverage(declaration.Steps.Count,
                stepOutputs.Values.Sum(value => value.NodeCount) + 1,
                stepOutputs.Values.Sum(value => value.EdgeCount),
                stepOutputs.Values.SelectMany(value => value.Nodes).Sum(value => value.Components.Count)
                    + root.Components.Count,
                stepOutputs.Values.Count == 0 ? 0 : stepOutputs.Values.Max(value => value.Depth), 0);
            var fingerprint = Fingerprint(root, stepOutputs, allContainment, snapshotEvidence);
            var snapshot = new MechanicGraphSnapshot(root, stepOutputs, allContainment,
                complete && problems.Count == graphProblemStart, coverage, fingerprint);
            var outputBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot).Length;
            snapshot = snapshot with { Coverage = coverage with { Bytes = outputBytes } };
            outputBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot).Length;
            snapshot = snapshot with { Coverage = coverage with { Bytes = outputBytes } };
            if (outputBytes > declaration.MaxBytes)
            {
                // Partial traversal is not permission to exceed the transport bound.
                // Even an empty graph has metadata overhead; when that cannot fit,
                // withhold the snapshot rather than returning an oversized partial value.
                problems.Add($"GRAPH_INCOMPLETE ({name}): GRAPH_BYTE_LIMIT.");
                continue;
            }
            if (declaration.Page is not null)
            {
                if (!TryMaterializePage(snapshot, declaration, pageOffset, out var paged, out var pageProblem))
                {
                    problems.Add($"GRAPH_PAGE_INPUT_INVALID ({name}): {pageProblem}.");
                    continue;
                }
                snapshot = paged;
            }
            snapshots[name] = snapshot;
        }
        return new(snapshots, problems, evidence);

        async Task<ApplicationEcsEntityRecord?> LoadEntity(string id)
        {
            if (entities.TryGetValue(id, out var cached)) return cached;
            var entity = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.StateSpaceId == stateSpaceId
                    && value.Id == id && value.DeletedAtUtc == null, cancellationToken);
            if (entity is not null) entities[id] = entity;
            return entity;
        }

        async Task<ApplicationEcsContainmentRecord[]> LoadParents(string childId)
        {
            if (containmentByChild.TryGetValue(childId, out var cached)) return cached;
            var parents = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
                .Where(value => value.StateSpaceId == stateSpaceId && value.ContainedEntityId == childId)
                .OrderBy(value => value.ContainerEntityId)
                .ThenBy(value => value.Slot)
                .Take(2).ToArrayAsync(cancellationToken);
            containmentByChild[childId] = parents;
            return parents;
        }

        async Task<MechanicGraphNode> Node(string id, IReadOnlyList<string> requested,
            GraphSnapshotRequirement graph, GraphSnapshotStepRequirement? step, GraphBudget budget)
        {
            if (!entities.TryGetValue(id, out var entity))
                throw new InvalidOperationException("GRAPH_ENTITY_UNAVAILABLE");
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var revisions = new Dictionary<string, MechanicGraphComponentRevision>(StringComparer.Ordinal);
            var chargedPayloadBytes = 0;
            var mappedRequests = requested.Select(localId => mapping.Components.TryGetValue(localId, out var reference)
                ? (LocalId: localId, Reference: reference) : (LocalId: localId, Reference: null)).ToArray();
            foreach (var missing in mappedRequests.Where(value => value.Reference is null))
                problems.Add($"GRAPH_COMPONENT_MAPPING_MISSING: No exact component maps '{missing.LocalId}'.");
            var qualifiedIds = mappedRequests.Where(value => value.Reference is not null)
                .Select(value => value.Reference!.QualifiedTypeId).Distinct(StringComparer.Ordinal).ToArray();
            var componentCandidates = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
                .Where(value => value.StateSpaceId == stateSpaceId && value.EntityId == id
                    && qualifiedIds.Contains(value.QualifiedTypeId))
                .Select(value => new
                {
                    value.QualifiedTypeId, value.TypeVersion, value.SchemaHash,
                    DataLength = value.Data.Length
                }).ToArrayAsync(cancellationToken);
            foreach (var localId in requested)
            {
                if (!mapping.Components.TryGetValue(localId, out var mappedReference))
                    continue;
                var candidate = componentCandidates.FirstOrDefault(value => value.QualifiedTypeId == mappedReference.QualifiedTypeId
                    && value.TypeVersion == mappedReference.TypeVersion && value.SchemaHash == mappedReference.SchemaHash);
                if (candidate is null) continue;
                if (!budget.TryBytes(candidate.DataLength, graph, out var candidateReason))
                {
                    problems.Add($"GRAPH_INCOMPLETE: {candidateReason}.");
                    break;
                }
                var row = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking().SingleAsync(value =>
                    value.StateSpaceId == stateSpaceId && value.EntityId == id
                    && value.QualifiedTypeId == candidate.QualifiedTypeId
                    && value.TypeVersion == candidate.TypeVersion && value.SchemaHash == candidate.SchemaHash,
                    cancellationToken);
                try
                {
                    var componentBytes = Encoding.UTF8.GetByteCount(row.Data);
                    if (!budget.TryBytes(componentBytes, graph, out var dataReason))
                    {
                        problems.Add($"GRAPH_INCOMPLETE: {dataReason}.");
                        continue;
                    }
                    using var parsed = JsonDocument.Parse(row.Data);
                    if (!budget.TryComponent(graph, out var componentReason))
                    {
                        problems.Add($"GRAPH_INCOMPLETE: {componentReason}.");
                        continue;
                    }
                    values[mappedReference.QualifiedTypeId] = parsed.RootElement.Clone();
                    budget.AddBytes(componentBytes);
                    chargedPayloadBytes += componentBytes;
                }
                catch (JsonException) { problems.Add("GRAPH_COMPONENT_INVALID: A selected component is not valid JSON."); continue; }
                revisions[mappedReference.QualifiedTypeId] = new(row.TypeVersion, row.SchemaHash, row.Revision);
            }
            var node = new MechanicGraphNode(id, entity.Name, values, revisions, entity.Revision);
            var overheadBytes = Math.Max(0, JsonSerializer.SerializeToUtf8Bytes(node).Length - chargedPayloadBytes);
            if (!budget.TryBytes(overheadBytes, graph, out var byteReason))
                problems.Add($"GRAPH_INCOMPLETE: {byteReason}.");
            else
                budget.AddBytes(overheadBytes);
            return node;
        }

        ApplicationGraphSnapshotReadResult Failed(string problem) => new(
            new Dictionary<string, MechanicGraphSnapshot>(StringComparer.Ordinal), [problem], []);
    }

    private static bool TryPageOffset(GraphSnapshotPageRequirement? declaration, string inputJson, out int offset)
    {
        offset = 0;
        if (declaration is null) return true;
        try
        {
            using var input = JsonDocument.Parse(inputJson);
            if (input.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!input.RootElement.TryGetProperty(declaration.CursorInput, out var cursor)) return true;
            if (cursor.ValueKind != JsonValueKind.String) return false;
            var value = cursor.GetString();
            return value is not null && value.Length is > 0 and <= 10
                && value[0] is not '0' && value.All(character => character is >= '0' and <= '9')
                && int.TryParse(value, out offset) && offset > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryMaterializePage(MechanicGraphSnapshot snapshot, GraphSnapshotRequirement declaration,
        int offset, out MechanicGraphSnapshot paged, out string problem)
    {
        var page = declaration.Page!;
        if (!snapshot.Steps.TryGetValue(page.StepId, out var pageStep))
        {
            paged = snapshot;
            problem = "The declared page step is unavailable";
            return false;
        }
        var ordered = pageStep.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
        if (offset >= ordered.Length && (ordered.Length != 0 || offset != 0))
        {
            paged = snapshot;
            problem = "The declared graph page cursor is outside the source";
            return false;
        }
        var selected = ordered.Skip(offset).Take(page.PageSize).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var retained = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var steps = new Dictionary<string, MechanicGraphStepSnapshot>(StringComparer.Ordinal);
        var containment = new List<MechanicGraphContainment>();
        var containmentSeen = new HashSet<(string Container, string Contained, string Slot, int Revision)>();
        // The full snapshot retains provenance from every ancestor step. The same physical
        // containment link can therefore legitimately occur more than once here; only
        // distinct parents are ambiguous for one paged ancestry path.
        var parents = snapshot.Containment
            .DistinctBy(value => (value.ContainerEntityId, value.ContainedEntityId, value.Slot, value.Revision))
            .GroupBy(value => value.ContainedEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var step in declaration.Steps)
        {
            var full = snapshot.Steps[step.Id];
            var nodes = full.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            HashSet<string> nodeIds;
            IReadOnlyList<MechanicGraphEdge> edges;
            if (step.Id.Equals(page.StepId, StringComparison.Ordinal))
            {
                nodeIds = selected;
                edges = full.Edges.Where(edge => Endpoint(step, edge) is { } endpoint && selected.Contains(endpoint)).ToArray();
            }
            else if (step.Containment == "ancestors")
            {
                var frontier = step.From.Equals("root", StringComparison.Ordinal)
                    ? new HashSet<string>([snapshot.Root.Id], StringComparer.Ordinal)
                    : retained.TryGetValue(step.From, out var values) ? values : [];
                nodeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var start in frontier)
                {
                    var current = start;
                    var path = new HashSet<string>(StringComparer.Ordinal);
                    for (var depth = 0; depth < Math.Min(step.MaxDepth, declaration.MaxDepth)
                        && parents.TryGetValue(current, out var links) && links.Length == 1; depth++)
                    {
                        if (!path.Add(current)) break;
                        var link = links[0];
                        var key = (link.ContainerEntityId, link.ContainedEntityId, link.Slot, link.Revision);
                        if (containmentSeen.Add(key)) containment.Add(link);
                        nodeIds.Add(link.ContainerEntityId);
                        current = link.ContainerEntityId;
                    }
                }
                edges = [];
            }
            else
            {
                var frontier = step.From.Equals("root", StringComparison.Ordinal)
                    ? new HashSet<string>([snapshot.Root.Id], StringComparer.Ordinal)
                    : retained.TryGetValue(step.From, out var values) ? values : [];
                var endpointFilter = step.FilterEndpointStep is not null && retained.TryGetValue(step.FilterEndpointStep, out var filter)
                    ? filter : null;
                edges = full.Edges.Where(edge => Anchor(step, edge) is { } anchor && frontier.Contains(anchor)
                    && (endpointFilter is null || Endpoint(step, edge) is { } endpoint && endpointFilter.Contains(endpoint))).ToArray();
                nodeIds = edges.Select(edge => Endpoint(step, edge)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            }
            retained[step.Id] = nodeIds;
            var selectedNodes = nodeIds.Where(nodes.ContainsKey).Order(StringComparer.Ordinal).Select(id => nodes[id]).ToArray();
            if (selectedNodes.Length != nodeIds.Count)
            {
                paged = snapshot;
                problem = "The declared page closure is incomplete";
                return false;
            }
            steps[step.Id] = new(selectedNodes, edges, full.Complete, full.Reason, full.Depth,
                selectedNodes.Length, edges.Count);
        }
        var nextOffset = offset + selected.Count;
        var pageMetadata = new MechanicGraphPage(offset, ordered.Length, page.PageSize,
            nextOffset < ordered.Length ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        paged = snapshot with { Steps = steps, Containment = containment, Page = pageMetadata };
        problem = string.Empty;
        return true;

        static string? Anchor(GraphSnapshotStepRequirement step, MechanicGraphEdge edge) => step.Direction switch
        {
            "outgoing" => edge.FromEntityId,
            "incoming" => edge.ToEntityId,
            _ => null
        };

        static string? Endpoint(GraphSnapshotStepRequirement step, MechanicGraphEdge edge) => step.Direction switch
        {
            "outgoing" => edge.ToEntityId,
            "incoming" => edge.FromEntityId,
            _ => null
        };
    }

    private static string Fingerprint(MechanicGraphNode root,
        IReadOnlyDictionary<string, MechanicGraphStepSnapshot> steps,
        IReadOnlyList<MechanicGraphContainment> containment,
        IReadOnlyList<MechanicGraphSnapshotEvidence> evidence)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { root, steps, containment, evidence });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed class GraphBudget
    {
        public int Bytes { get; private set; }
        public int EdgeCount => edges;
        private readonly HashSet<string> nodes = new(StringComparer.Ordinal);
        private int edges;
        private int components;
        public void AddBytes(int count) => Bytes = checked(Bytes + count);
        public bool TryBytes(int count, GraphSnapshotRequirement graph, out string? reason)
        {
            reason = Bytes > graph.MaxBytes - count ? "GRAPH_BYTE_LIMIT" : null;
            return reason is null;
        }
        public bool TryComponent(GraphSnapshotRequirement graph, out string? reason)
        {
            reason = null;
            if (components++ >= graph.MaxComponents) { reason = "GRAPH_COMPONENT_LIMIT"; return false; }
            if (Bytes > graph.MaxBytes) { reason = "GRAPH_BYTE_LIMIT"; return false; }
            return true;
        }
        public bool TryNode(string id, GraphSnapshotRequirement graph, GraphSnapshotStepRequirement? step,
            HashSet<string> stepNodes, out string? reason)
        {
            reason = null;
            if (stepNodes.Contains(id))
            {
                return true;
            }
            if (step is not null && stepNodes.Count >= step.MaxEntities)
            { reason = "GRAPH_ENTITY_LIMIT"; return false; }
            if (nodes.Contains(id))
            {
                stepNodes.Add(id);
                return true;
            }
            if (nodes.Count >= graph.MaxEntities)
            { reason = "GRAPH_ENTITY_LIMIT"; return false; }
            nodes.Add(id);
            stepNodes.Add(id);
            if (Bytes > graph.MaxBytes) { reason = "GRAPH_BYTE_LIMIT"; return false; }
            return true;
        }
        public bool TryEdge(GraphSnapshotRequirement graph, GraphSnapshotStepRequirement step,
            int stepEdgeCount, out string? reason)
        {
            reason = null;
            if (edges >= graph.MaxEdges || stepEdgeCount >= step.MaxEdges)
            { reason = "GRAPH_EDGE_LIMIT"; return false; }
            edges++;
            return true;
        }
    }

}
