using DantesRoleplay.Mechanics;
using System.Text.Json;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Kernel-owned declarative composition. A parent cannot call into this object from JavaScript:
/// the host resolves every declared child first, then hands the parent an immutable JSON snapshot
/// of the results. Child effects remain proposals and are never applied here.
/// </summary>
public sealed class MechanicComposer(
    IMechanicStore mechanics,
    IProjectionResolver projections,
    IMechanicEngine engine) : IMechanicComposer
{
    private const int MaxDepth = 8;
    private const int MaxChildrenPerDeclaration = 100;

    public async Task<CompositionResult> ComposeAsync(
        string parentMechanicId,
        MechanicRequirements requirements,
        MechanicProjection projection,
        int depth = 0,
        IReadOnlySet<string>? ancestors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentMechanicId);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(projection);

        if (depth >= MaxDepth)
            return CompositionResult.Failed($"CHILD_DEPTH_LIMIT: Maximum child depth is {MaxDepth}.");

        var problems = requirements.CompositionProblems();
        if (problems.Count > 0)
            return CompositionResult.Failed($"INVALID_CHILD_DECLARATION: {string.Join(" ", problems)}");

        if (requirements.Children.Count == 0)
            return new CompositionResult(projection);

        var lineage = ancestors is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(ancestors, StringComparer.Ordinal);

        if (!lineage.Add(parentMechanicId))
            return CompositionResult.Failed($"CHILD_CYCLE: '{parentMechanicId}' is already executing.");

        var children = new Dictionary<string, IReadOnlyList<ChildMechanicResult>>(
            projection.Children,
            StringComparer.Ordinal);
        var proposal = CompositionProposal.Empty;
        var ordinal = 0;

        var orderedChildren = ResolveExecutionOrder(requirements.Children);
        if (orderedChildren.Error.Length > 0)
            return CompositionResult.Failed(orderedChildren.Error);

        foreach (var (resultKey, declaration) in orderedChildren.Items)
        {
            var invocations = BuildInvocations(resultKey, declaration, projection, children, ref ordinal);
            if (invocations.Error.Length > 0)
                return CompositionResult.Failed(invocations.Error);

            var results = new List<ChildMechanicResult>(invocations.Items.Count);

            foreach (var invocation in invocations.Items)
            {
                var child = await RunChildAsync(
                    new ChildMechanicInvocation(
                        declaration.MechanicId,
                        invocation.RoleEntityIds,
                        invocation.Input,
                        DeriveSeed(projection.Seed, invocation.Ordinal),
                        depth + 1,
                        lineage),
                    cancellationToken);

                if (!child.Ok || child.Mechanic is null || child.Run is null)
                {
                    var detail = string.IsNullOrWhiteSpace(child.Error)
                        ? "Child mechanic did not produce a result."
                        : child.Error;
                    return CompositionResult.Failed($"CHILD_FAILED ({resultKey}): {detail}");
                }
                if (declaration.MechanicVersion > 0
                    && (child.Mechanic.Version != declaration.MechanicVersion
                        || child.Mechanic.SourceHash != declaration.ContentFingerprint))
                    return CompositionResult.Failed($"CHILD_STALE ({resultKey}): '{declaration.MechanicId}' no longer matches its exact version and fingerprint.");

                results.Add(new ChildMechanicResult(
                    child.Mechanic.Id,
                    child.Mechanic.Version,
                    child.Run.Seed,
                    invocation.RoleEntityIds,
                    child.Run.Output,
                    child.Run.Log,
                    child.Run.ElapsedMilliseconds));

                // A nested child tree has already run in depth-first order. Preserve that exact
                // order, then add this child's own proposal; nothing is applied at this layer.
                proposal = proposal.Append(child.Proposal).Append(child.Run.Output);
            }

            children[resultKey] = results;
        }

        return new CompositionResult(projection with { Children = children }) { Proposal = proposal };
    }

    public async Task<ChildMechanicRun> RunChildAsync(
        ChildMechanicInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invocation.MechanicId))
            return new(null, null, null, "CHILD_MECHANIC_REQUIRED");
        if (invocation.Depth >= MaxDepth)
            return new(null, null, null, $"CHILD_DEPTH_LIMIT: Maximum child depth is {MaxDepth}.");
        if (invocation.Ancestors?.Contains(invocation.MechanicId) == true)
            return new(null, null, null, $"CHILD_CYCLE: '{invocation.MechanicId}' is already executing.");

        var mechanic = await mechanics.GetAsync(invocation.MechanicId, cancellationToken: cancellationToken);
        if (mechanic is null || mechanic.Status != MechanicStatus.Active)
            return new(mechanic, null, null, $"CHILD_NOT_ACTIVE: '{invocation.MechanicId}' must be active.");

        MechanicRequirements requirements;
        try
        {
            requirements = MechanicRequirements.Parse(mechanic.Requirements);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return new(mechanic, null, null, $"CHILD_REQUIREMENTS_INVALID: {ex.Message}");
        }

        var resolved = await projections.ResolveAsync(
            requirements,
            invocation.RoleEntityIds,
            invocation.Input,
            invocation.Seed,
            cancellationToken);
        if (!resolved.Ok || resolved.Projection is null)
            return new(mechanic, null, null, $"CHILD_PROJECTION_FAILED: {string.Join(" ", resolved.Problems)}");

        var composition = await ComposeAsync(
            mechanic.Id,
            requirements,
            resolved.Projection,
            invocation.Depth,
            invocation.Ancestors,
            cancellationToken);
        if (!composition.Ok || composition.Projection is null)
            return new(mechanic, resolved.Projection, null, composition.Error);

        var run = await engine.RunAsync(
            mechanic.Source,
            composition.Projection,
            ExecutionLimits.Default,
            cancellationToken);
        return run.Ok
            ? new(mechanic, composition.Projection, run) { Proposal = composition.Proposal }
            : new(mechanic, composition.Projection, run, $"CHILD_FAILED: {run.Error}");
    }

    private static InvocationList BuildInvocations(
        string resultKey,
        ChildMechanicRequirement declaration,
        MechanicProjection projection,
        IReadOnlyDictionary<string, IReadOnlyList<ChildMechanicResult>> completedChildren,
        ref int ordinal)
    {
        var items = new List<PendingInvocation>();

        if (!string.IsNullOrWhiteSpace(declaration.WhenParentProperty))
        {
            try
            {
                using var document = JsonDocument.Parse(projection.Input);
                if (!document.RootElement.TryGetProperty(declaration.WhenParentProperty, out var selected)
                    || selected.ValueKind == JsonValueKind.Null)
                    return new(items, string.Empty);
            }
            catch (JsonException ex)
            {
                return new([], $"CHILD_CONDITION_FAILED ({resultKey}): Parent input could not be parsed: {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(declaration.ForEachInputProperty))
        {
            try
            {
                using var document = JsonDocument.Parse(projection.Input);
                if (!document.RootElement.TryGetProperty(declaration.ForEachInputProperty, out var selected)
                    || selected.ValueKind != JsonValueKind.Array)
                    return new([], $"CHILD_INPUT_FAILED ({resultKey}): Parent input property '{declaration.ForEachInputProperty}' must be an array.");
                if (selected.GetArrayLength() > MaxChildrenPerDeclaration)
                    return new([], $"CHILD_LIMIT ({resultKey}): '{declaration.ForEachInputProperty}' has {selected.GetArrayLength()} items; the limit is {MaxChildrenPerDeclaration}.");
                foreach (var value in selected.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Object)
                        return new([], $"CHILD_INPUT_FAILED ({resultKey}): Every '{declaration.ForEachInputProperty}' item must be an object.");
                    var childInput = value;
                    if (!string.IsNullOrWhiteSpace(declaration.InputFromEachItemProperty)
                        && (!value.TryGetProperty(declaration.InputFromEachItemProperty, out childInput)
                            || childInput.ValueKind != JsonValueKind.Object))
                        return new([], $"CHILD_INPUT_FAILED ({resultKey}): Every '{declaration.ForEachInputProperty}' item must contain an object property '{declaration.InputFromEachItemProperty}'.");
                    var bindings = BindRoles(resultKey, declaration, projection, itemId: null, childInput);
                    if (bindings.Error.Length > 0) return new([], bindings.Error);
                    items.Add(new PendingInvocation(bindings.RoleEntityIds, childInput.GetRawText(), ordinal++));
                }
                return new(items, string.Empty);
            }
            catch (JsonException ex)
            {
                return new([], $"CHILD_INPUT_FAILED ({resultKey}): Parent input could not be parsed: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(declaration.ForEachContentsOf))
        {
            var bindings = BindRoles(resultKey, declaration, projection, itemId: null, inputItem: null);
            var input = ResolveInput(resultKey, declaration, projection.Input, completedChildren, itemId: null);
            if (bindings.Error.Length > 0 || input.Error.Length > 0)
                return new([], bindings.Error.Length > 0 ? bindings.Error : input.Error);

            items.Add(new PendingInvocation(bindings.RoleEntityIds, input.Value, ordinal++));
            return new(items, string.Empty);
        }

        if (!projection.Roles.TryGetValue(declaration.ForEachContentsOf, out var container))
        {
            return new([], $"CHILD_BINDING_FAILED ({resultKey}): Parent role '{declaration.ForEachContentsOf}' was not projected.");
        }

        var contents = container.Contains ?? [];
        if (contents.Count > MaxChildrenPerDeclaration)
        {
            return new(
                [],
                $"CHILD_LIMIT ({resultKey}): '{declaration.ForEachContentsOf}' has {contents.Count} contents; the limit is {MaxChildrenPerDeclaration}.");
        }

        foreach (var item in contents)
        {
            var bindings = BindRoles(resultKey, declaration, projection, item.Id, inputItem: null);
            var input = ResolveInput(resultKey, declaration, projection.Input, completedChildren, item.Id);
            if (bindings.Error.Length > 0 || input.Error.Length > 0)
                return new([], bindings.Error.Length > 0 ? bindings.Error : input.Error);

            items.Add(new PendingInvocation(bindings.RoleEntityIds, input.Value, ordinal++));
        }

        return new(items, string.Empty);
    }

    private static RoleBindings BindRoles(
        string resultKey,
        ChildMechanicRequirement declaration,
        MechanicProjection projection,
        string? itemId,
        JsonElement? inputItem)
    {
        var roleEntityIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (childRole, source) in declaration.RoleBindings)
        {
            if (source == "$item")
            {
                if (string.IsNullOrWhiteSpace(itemId))
                    return new(new Dictionary<string, string>(), $"CHILD_BINDING_FAILED ({resultKey}): '$item' is only valid while iterating contents.");

                roleEntityIds[childRole] = itemId;
                continue;
            }

            if (source.StartsWith("$input.", StringComparison.Ordinal))
            {
                var property = source[7..];
                if (inputItem is not JsonElement input || input.ValueKind != JsonValueKind.Object
                    || !input.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString()))
                    return new(new Dictionary<string, string>(),
                        $"CHILD_BINDING_FAILED ({resultKey}): Input property '{property}' must contain an entity id.");
                roleEntityIds[childRole] = value.GetString()!;
                continue;
            }

            if (!projection.Roles.TryGetValue(source, out var parentRole))
            {
                return new(new Dictionary<string, string>(), $"CHILD_BINDING_FAILED ({resultKey}): Parent role '{source}' was not projected.");
            }

            roleEntityIds[childRole] = parentRole.Id;
        }

        return new(roleEntityIds, string.Empty);
    }

    private static InputValue ResolveInput(
        string resultKey,
        ChildMechanicRequirement declaration,
        string parentInput,
        IReadOnlyDictionary<string, IReadOnlyList<ChildMechanicResult>> completedChildren,
        string? itemId)
    {
        if (declaration.InputFromChildData is { } dependent)
        {
            if (!completedChildren.TryGetValue(dependent.ResultKey, out var producerResults) || producerResults.Count != 1)
            {
                return new(
                    string.Empty,
                    $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependent.ResultKey}' did not produce exactly one result.");
            }

            var output = producerResults[0].Output;
            if (!output.HasData)
            {
                return new(
                    string.Empty,
                    $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependent.ResultKey}' did not return data.");
            }

            var data = output.Data;
            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new(
                        string.Empty,
                        $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependent.ResultKey}' data must be a JSON object.");
                }

                // GetRawText serialises a self-contained copy while the document is alive. The
                // receiver cannot alias or mutate the frozen child result held by the parent.
                return new(document.RootElement.GetRawText(), string.Empty);
            }
            catch (JsonException)
            {
                return new(
                    string.Empty,
                    $"CHILD_INPUT_FROM_DATA_FAILED ({resultKey}): Producer '{dependent.ResultKey}' data is not valid JSON.");
            }
        }

        if (string.IsNullOrWhiteSpace(declaration.InputFromParentProperty))
            return new(declaration.InheritInput ? parentInput : declaration.Input, string.Empty);

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(parentInput);
            if (!document.RootElement.TryGetProperty(declaration.InputFromParentProperty, out var selected))
            {
                return new(
                    string.Empty,
                    $"CHILD_INPUT_FAILED ({resultKey}): Parent input has no '{declaration.InputFromParentProperty}' property.");
            }

            if (!declaration.InputForEachItem)
                return new(selected.GetRawText(), string.Empty);

            if (selected.ValueKind != System.Text.Json.JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(itemId) ||
                !selected.TryGetProperty(itemId, out var perItem))
            {
                return new(
                    string.Empty,
                    $"CHILD_INPUT_FAILED ({resultKey}): '{declaration.InputFromParentProperty}' must contain an object for participant '{itemId}'.");
            }

            return new(perItem.GetRawText(), string.Empty);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return new(string.Empty, $"CHILD_INPUT_FAILED ({resultKey}): Parent input could not be parsed: {ex.Message}");
        }
    }

    private static long DeriveSeed(long parentSeed, int ordinal)
    {
        unchecked
        {
            var mixed = parentSeed ^ (long)0x9E3779B97F4A7C15UL;
            mixed += (long)ordinal * (long)0x632BE59BD9B4E019UL;
            mixed ^= mixed >> 30;
            mixed *= (long)0xBF58476D1CE4E5B9UL;
            mixed ^= mixed >> 27;
            mixed *= (long)0x94D049BB133111EBUL;
            return mixed ^ (mixed >> 31);
        }
    }

    private static OrderedChildren ResolveExecutionOrder(
        IReadOnlyDictionary<string, ChildMechanicRequirement> declarations)
    {
        var remainingDependencies = declarations.Keys.ToDictionary(
            key => key,
            key => Dependencies(declarations[key]).Distinct(StringComparer.Ordinal).Count(),
            StringComparer.Ordinal);
        var dependents = declarations.Keys.ToDictionary(
            key => key,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var (consumer, declaration) in declarations)
        {
            foreach (var dependency in Dependencies(declaration).Distinct(StringComparer.Ordinal))
                if (dependents.TryGetValue(dependency, out var sourceDependents))
                    sourceDependents.Add(consumer);
        }

        var ready = new SortedSet<string>(
            remainingDependencies.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.Ordinal);
        var ordered = new List<KeyValuePair<string, ChildMechanicRequirement>>(declarations.Count);

        while (ready.Count > 0)
        {
            var key = ready.Min!;
            ready.Remove(key);
            ordered.Add(new KeyValuePair<string, ChildMechanicRequirement>(key, declarations[key]));

            foreach (var consumer in dependents[key].OrderBy(value => value, StringComparer.Ordinal))
            {
                remainingDependencies[consumer]--;
                if (remainingDependencies[consumer] == 0)
                    ready.Add(consumer);
            }
        }

        return ordered.Count == declarations.Count
            ? new OrderedChildren(ordered, string.Empty)
            : new OrderedChildren([], "INVALID_CHILD_DECLARATION: Child input and after dependencies must form an acyclic sibling graph.");

        static IEnumerable<string> Dependencies(ChildMechanicRequirement declaration)
        {
            foreach (var dependency in declaration.After) yield return dependency;
            if (declaration.InputFromChildData is { } source) yield return source.ResultKey;
        }
    }

    private sealed record PendingInvocation(IReadOnlyDictionary<string, string> RoleEntityIds, string Input, int Ordinal);

    private sealed record InvocationList(IReadOnlyList<PendingInvocation> Items, string Error);

    private sealed record RoleBindings(IReadOnlyDictionary<string, string> RoleEntityIds, string Error);

    private sealed record InputValue(string Value, string Error);

    private sealed record OrderedChildren(
        IReadOnlyList<KeyValuePair<string, ChildMechanicRequirement>> Items,
        string Error);
}
