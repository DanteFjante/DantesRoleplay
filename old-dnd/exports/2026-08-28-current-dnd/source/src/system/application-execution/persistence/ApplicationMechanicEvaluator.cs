using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Evaluates an immutable active application mechanic. Child invocation is host-owned and uses
/// only declared roles, input, catalog records, and deterministic derived seeds.
/// </summary>
public sealed class ApplicationMechanicEvaluator(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationMechanicProjectionResolver projections,
    IMechanicEngine engine) : IApplicationMechanicEvaluator
{
    private const int MaxDepth = 8;
    private const int MaxChildrenPerDeclaration = 100;
    private const int MaxChildDeclarations = 64;
    private const int MaxChildInvocations = 256;

    public Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
        ApplicationMechanicEvaluationRequest request,
        CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(request, 0, new HashSet<string>(StringComparer.Ordinal), new CompositionBudget(), cancellationToken);

    private async Task<ApplicationMechanicEvaluationResult> EvaluateCoreAsync(
        ApplicationMechanicEvaluationRequest request,
        int depth,
        IReadOnlySet<string> ancestors,
        CompositionBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!catalogs.TryGet(request.ApplicationId, out var catalog))
            return Failed(request, "APPLICATION_CATALOG_UNAVAILABLE: The exact active application catalog is unavailable.");
        CatalogRecordView record;
        try { record = catalog.Inspect(new(request.ApplicationId, request.ApplicationId.Value, request.QualifiedMechanicId)); }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        { return Failed(request, "MECHANIC_UNKNOWN: The requested application mechanic is unavailable."); }
        if (record.Summary.Kind != "mechanic" || record.Summary.Status != "active"
            || record.Summary.ContentFingerprint != request.ContentFingerprint)
            return Failed(request, "MECHANIC_STALE: The mechanic does not match the requested exact fingerprint.");
        MechanicDocument document;
        try
        {
            document = JsonSerializer.Deserialize<MechanicDocument>(record.ContentJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
        }
        catch (JsonException) { return Failed(request, "MECHANIC_INVALID: The active mechanic contract is malformed."); }
        MechanicRequirements requirements;
        try { requirements = MechanicRequirements.Parse(document.Requirements ?? "{}"); }
        catch (JsonException) { return Failed(request, "MECHANIC_INVALID: The active mechanic requirements are malformed."); }
        if (requirements.ProjectionProblems().Count > 0 || requirements.CompositionProblems().Count > 0)
            return Failed(request, "MECHANIC_INVALID: The active mechanic requirements are invalid.");
        var projection = await projections.ResolveAsync(request.StateSpaceId, request.ApplicationId,
            requirements, request.Mapping, request.RoleEntityIds, request.InputJson, request.Seed, cancellationToken);
        if (!projection.Ok)
            return new(request.QualifiedMechanicId, request.ContentFingerprint, null, null, projection.Problems);
        var composed = await ComposeAsync(request, requirements, projection.Projection!, depth, ancestors, budget, cancellationToken);
        if (composed.Projection is null) return Failed(request, composed.Error);
        var run = await engine.RunAsync(document.Source ?? "", composed.Projection, ExecutionLimits.Default, cancellationToken);
        return new(request.QualifiedMechanicId, request.ContentFingerprint, composed.Projection, run, []);
    }

    private async Task<(MechanicProjection? Projection, string Error)> ComposeAsync(
        ApplicationMechanicEvaluationRequest parent,
        MechanicRequirements requirements,
        MechanicProjection projection,
        int depth,
        IReadOnlySet<string> ancestors,
        CompositionBudget budget,
        CancellationToken cancellationToken)
    {
        if (requirements.Children.Count == 0) return (projection, "");
        if (depth >= MaxDepth) return (null, $"CHILD_DEPTH_LIMIT: Maximum child depth is {MaxDepth}.");
        if (requirements.Children.Count > MaxChildDeclarations)
            return (null, $"CHILD_DECLARATION_LIMIT: At most {MaxChildDeclarations} child declarations are permitted.");
        if (!catalogs.TryGet(parent.ApplicationId, out var catalog))
            return (null, "APPLICATION_CATALOG_UNAVAILABLE: The exact active application catalog is unavailable.");
        var lineage = new HashSet<string>(ancestors, StringComparer.Ordinal);
        if (!lineage.Add(parent.QualifiedMechanicId))
            return (null, $"CHILD_CYCLE: '{parent.QualifiedMechanicId}' is already executing.");
        var children = new Dictionary<string, IReadOnlyList<ChildMechanicResult>>(StringComparer.Ordinal);
        var componentRevisions = projection.ComponentRevisions.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, int?>(pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var containmentRevisions = projection.ContainmentRevisions.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var ordinal = 0;
        var ordered = ResolveExecutionOrder(requirements.Children);
        if (ordered.Count != requirements.Children.Count)
            return (null, "INVALID_CHILD_DECLARATION: inputFromChildData declarations must form an acyclic sibling graph.");
        foreach (var pair in ordered)
        {
            var invocations = BuildInvocations(pair.Key, pair.Value, projection, children, ref ordinal);
            if (invocations.Error.Length > 0) return (null, invocations.Error);
            var results = new List<ChildMechanicResult>(invocations.Items.Count);
            foreach (var invocation in invocations.Items)
            {
                if (!budget.TryReserve())
                    return (null, $"CHILD_INVOCATION_LIMIT: At most {MaxChildInvocations} child invocations are permitted per root action.");
                var childMechanicId = QualifyMechanicId(parent.ApplicationId, pair.Value.MechanicId);
                if (lineage.Contains(childMechanicId))
                    return (null, $"CHILD_CYCLE: '{childMechanicId}' is already executing.");
                CatalogRecordView childRecord;
                try { childRecord = catalog.Inspect(new(parent.ApplicationId, parent.ApplicationId.Value, childMechanicId)); }
                catch (Exception) { return (null, $"CHILD_NOT_ACTIVE ({pair.Key}): '{childMechanicId}' is unavailable."); }
                if (childRecord.Summary.Kind != "mechanic" || childRecord.Summary.Status != "active")
                    return (null, $"CHILD_NOT_ACTIVE ({pair.Key}): '{pair.Value.MechanicId}' is inactive.");
                var child = await EvaluateCoreAsync(new(parent.StateSpaceId, parent.ApplicationId,
                    childRecord.Summary.QualifiedId, childRecord.Summary.ContentFingerprint, parent.Mapping,
                    invocation.RoleEntityIds, invocation.Input, DeriveSeed(parent.Seed, invocation.Ordinal)),
                    depth + 1, lineage, budget, cancellationToken);
                if (!child.Ok || child.Projection is null || child.Run is null)
                    return (null, $"CHILD_FAILED ({pair.Key}): " + (child.Problems.FirstOrDefault() ?? child.Run?.Error ?? "Child did not produce a result."));
                if (child.Run.Output.Effects.Count > 0 || child.Run.Output.Events.Count > 0 || child.Run.Output.Notifications.Count > 0)
                    return (null, $"CHILD_OUTPUT_UNSUPPORTED ({pair.Key}): A direct application child must return data only.");
                var snapshotProblem = MergeSnapshots(
                    componentRevisions, containmentRevisions, child.Projection);
                if (snapshotProblem.Length > 0)
                    return (null, $"CHILD_SNAPSHOT_CONFLICT ({pair.Key}): {snapshotProblem}");
                results.Add(new ChildMechanicResult(child.QualifiedMechanicId, childRecord.Summary.Version,
                    child.Projection.Seed, invocation.RoleEntityIds, child.Run.Output, child.Run.Log,
                    child.Run.ElapsedMilliseconds));
            }
            children[pair.Key] = results;
        }
        return (projection with
        {
            Children = children,
            ComponentRevisions = componentRevisions,
            ContainmentRevisions = containmentRevisions
        }, "");
    }

    private static string MergeSnapshots(
        Dictionary<string, Dictionary<string, int?>> componentRevisions,
        Dictionary<string, IReadOnlyList<ContainmentRevision>> containmentRevisions,
        MechanicProjection child)
    {
        foreach (var (entityId, observed) in child.ComponentRevisions)
        {
            if (!componentRevisions.TryGetValue(entityId, out var merged))
                componentRevisions[entityId] = merged = new(StringComparer.Ordinal);
            foreach (var (componentId, revision) in observed)
            {
                if (merged.TryGetValue(componentId, out var prior) && prior != revision)
                    return $"Component '{componentId}' on '{entityId}' was observed at conflicting revisions.";
                merged[componentId] = revision;
            }
        }

        foreach (var (containerId, observed) in child.ContainmentRevisions)
        {
            if (containmentRevisions.TryGetValue(containerId, out var prior)
                && !prior.SequenceEqual(observed))
                return $"Containment for '{containerId}' was observed at conflicting revisions.";
            containmentRevisions[containerId] = observed;
        }

        return "";
    }

    private static IReadOnlyList<KeyValuePair<string, ChildMechanicRequirement>> ResolveExecutionOrder(
        IReadOnlyDictionary<string, ChildMechanicRequirement> declarations)
    {
        var remaining = declarations.Keys.ToDictionary(key => key,
            key => declarations[key].InputFromChildData is null ? 0 : 1, StringComparer.Ordinal);
        var dependents = declarations.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (consumer, declaration) in declarations)
            if (declaration.InputFromChildData is { } source && dependents.TryGetValue(source.ResultKey, out var values)) values.Add(consumer);
        var ready = new SortedSet<string>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        var ordered = new List<KeyValuePair<string, ChildMechanicRequirement>>(declarations.Count);
        while (ready.Count > 0)
        {
            var key = ready.Min!;
            ready.Remove(key);
            ordered.Add(new(key, declarations[key]));
            foreach (var consumer in dependents[key].OrderBy(value => value, StringComparer.Ordinal))
                if (--remaining[consumer] == 0) ready.Add(consumer);
        }
        return ordered.Count == declarations.Count ? ordered : [];
    }

    private static InvocationList BuildInvocations(string resultKey, ChildMechanicRequirement declaration,
        MechanicProjection projection, IReadOnlyDictionary<string, IReadOnlyList<ChildMechanicResult>> completed, ref int ordinal)
    {
        var items = new List<PendingInvocation>();
        if (string.IsNullOrWhiteSpace(declaration.ForEachContentsOf))
        {
            var roles = BindRoles(resultKey, declaration, projection, null);
            var input = ResolveInput(resultKey, declaration, projection.Input, completed, null);
            return roles.Error.Length > 0 || input.Error.Length > 0
                ? new([], roles.Error.Length > 0 ? roles.Error : input.Error)
                : new([new(roles.Value, input.Value, ordinal++)], "");
        }
        if (!projection.Roles.TryGetValue(declaration.ForEachContentsOf, out var container))
            return new([], $"CHILD_BINDING_FAILED ({resultKey}): Parent role '{declaration.ForEachContentsOf}' was not projected.");
        var contents = container.Contains ?? [];
        if (contents.Count > MaxChildrenPerDeclaration)
            return new([], $"CHILD_LIMIT ({resultKey}): '{declaration.ForEachContentsOf}' has {contents.Count} contents; the limit is {MaxChildrenPerDeclaration}.");
        foreach (var item in contents)
        {
            var roles = BindRoles(resultKey, declaration, projection, item.Id);
            var input = ResolveInput(resultKey, declaration, projection.Input, completed, item.Id);
            if (roles.Error.Length > 0 || input.Error.Length > 0)
                return new([], roles.Error.Length > 0 ? roles.Error : input.Error);
            items.Add(new(roles.Value, input.Value, ordinal++));
        }
        return new(items, "");
    }

    private static ValueResult<IReadOnlyDictionary<string, string>> BindRoles(string resultKey,
        ChildMechanicRequirement declaration, MechanicProjection projection, string? itemId)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (childRole, source) in declaration.RoleBindings)
        {
            if (source == "$item")
            {
                if (string.IsNullOrWhiteSpace(itemId))
                    return new(new Dictionary<string, string>(StringComparer.Ordinal),
                        $"CHILD_BINDING_FAILED ({resultKey}): '$item' is only valid while iterating contents.");
                roles[childRole] = itemId;
            }
            else if (projection.Roles.TryGetValue(source, out var parentRole)) roles[childRole] = parentRole.Id;
            else return new(new Dictionary<string, string>(StringComparer.Ordinal),
                $"CHILD_BINDING_FAILED ({resultKey}): Parent role '{source}' was not projected.");
        }
        return new(roles, "");
    }

    private static ValueResult<string> ResolveInput(string resultKey, ChildMechanicRequirement declaration,
        string parentInput, IReadOnlyDictionary<string, IReadOnlyList<ChildMechanicResult>> completed, string? itemId)
    {
        if (declaration.InputFromChildData is { } dependency)
        {
            if (!completed.TryGetValue(dependency.ResultKey, out var producer) || producer.Count != 1 || !producer[0].Output.HasData)
                return new("", $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependency.ResultKey}' did not return one data result.");
            try
            {
                using var document = JsonDocument.Parse(producer[0].Output.Data);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    ? new(document.RootElement.GetRawText(), "")
                    : new("", $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependency.ResultKey}' data must be a JSON object.");
            }
            catch (JsonException) { return new("", $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependency.ResultKey}' data is not valid JSON."); }
        }
        if (string.IsNullOrWhiteSpace(declaration.InputFromParentProperty))
            return new(declaration.InheritInput ? parentInput : declaration.Input, "");
        try
        {
            using var document = JsonDocument.Parse(parentInput);
            if (!document.RootElement.TryGetProperty(declaration.InputFromParentProperty, out var selected))
                return new("", $"CHILD_INPUT_FAILED ({resultKey}): Parent input has no '{declaration.InputFromParentProperty}' property.");
            if (!declaration.InputForEachItem) return new(selected.GetRawText(), "");
            return selected.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(itemId)
                && selected.TryGetProperty(itemId, out var perItem)
                ? new(perItem.GetRawText(), "")
                : new("", $"CHILD_INPUT_FAILED ({resultKey}): '{declaration.InputFromParentProperty}' must contain an object for participant '{itemId}'.");
        }
        catch (JsonException exception) { return new("", $"CHILD_INPUT_FAILED ({resultKey}): Parent input could not be parsed: {exception.Message}"); }
    }

    private static long DeriveSeed(long parentSeed, int ordinal)
    {
        unchecked
        {
            var value = parentSeed ^ (long)0x9E3779B97F4A7C15UL;
            value += (long)ordinal * (long)0x632BE59BD9B4E019UL;
            value ^= value >> 30; value *= (long)0xBF58476D1CE4E5B9UL;
            value ^= value >> 27; value *= (long)0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static string QualifyMechanicId(ApplicationIdentifier applicationId, string mechanicId) =>
        mechanicId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? mechanicId : applicationId.Value + "." + mechanicId;

    private static ApplicationMechanicEvaluationResult Failed(ApplicationMechanicEvaluationRequest request, string problem) =>
        new(request.QualifiedMechanicId, request.ContentFingerprint, null, null, [problem]);
    private sealed record MechanicDocument(string? Requirements, string? Source);
    private sealed record PendingInvocation(IReadOnlyDictionary<string, string> RoleEntityIds, string Input, int Ordinal);
    private sealed record InvocationList(IReadOnlyList<PendingInvocation> Items, string Error);
    private sealed record ValueResult<T>(T Value, string Error);
    private sealed class CompositionBudget
    {
        private int _invocations;
        public bool TryReserve() => ++_invocations <= MaxChildInvocations;
    }
}
