using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.Applications;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Projections;

/// <summary>Prepared read-only structural projection engine. Result data is never cached.</summary>
public sealed class ProjectionMaterializer(
    IProjectionDefinitionRegistry definitions,
    IEntityComponentStore components,
    IStateSpaceRegistry stateSpaces,
    IBoundedJsonSchemaValidator validator,
    ProjectionPlanCache? planCache = null,
    IProjectionSourceSnapshotReader? snapshots = null,
    IApplicationComponentTypeRegistry? componentTypes = null) : IProjectionMaterializer
{
    private readonly ProjectionPlanCache plans = planCache ?? new ProjectionPlanCache();

    public Task<ProjectionMaterializationResult> MaterializeSnapshotAsync(
        ProjectionMaterializationRequest request, ApplicationIdentifier owner,
        IReadOnlyList<EcsComponentView> authorizedComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizedComponents);
        if (request.Purpose != ProjectionReadPurpose.Exact)
            throw new InvalidOperationException("Executable snapshots cannot use display materialization.");
        return MaterializeCoreAsync(request, null, cancellationToken, owner, authorizedComponents);
    }

    public Task<ProjectionMaterializationResult> MaterializeAsync(
        ProjectionMaterializationRequest request,
        CancellationToken cancellationToken = default) =>
        MaterializeCoreAsync(request, null, cancellationToken);

    public Task<ProjectionMaterializationResult> MaterializeExpandedAsync(
        ProjectionMaterializationRequest request,
        Func<ProjectionMaterializationResult, CancellationToken, Task<string>> completeRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completeRoot);
        return MaterializeCoreAsync(request, completeRoot, cancellationToken);
    }

    private async Task<ProjectionMaterializationResult> MaterializeCoreAsync(
        ProjectionMaterializationRequest request,
        Func<ProjectionMaterializationResult, CancellationToken, Task<string>>? completeRoot,
        CancellationToken cancellationToken,
        ApplicationIdentifier? snapshotOwner = null,
        IReadOnlyList<EcsComponentView>? authorizedComponents = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Purpose))
            throw new ArgumentException("The projection read purpose is invalid.");
        request.Projection.Validate();
        if (string.IsNullOrWhiteSpace(request.StateSpaceId) || request.RoleEntityIds is null
            || request.RoleEntityIds.Count > 64 || request.RoleEntityIds.Values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A bounded state-space and role binding are required.");

        var plan = plans.GetOrPrepare(request.Projection, () =>
        {
            var root = Require(request.Projection);
            return ProjectionPlanCompiler.Compile(root, Require);
        });
        ValidateRootRoles(plan.Root, request.RoleEntityIds);
        var active = ActiveNodes(plan, request.RoleEntityIds);
        var locators = Locators(plan, active, request.RoleEntityIds);
        if (locators.Count > 256)
            throw new InvalidOperationException("Projection component read bound exceeded.");

        IReadOnlyList<EcsComponentView> sourceComponents;
        if (authorizedComponents is not null)
        {
            if (snapshotOwner != plan.Root.Owner || authorizedComponents.Count > 256
                || authorizedComponents.Any(value => value.StateSpaceId != request.StateSpaceId))
                throw new InvalidOperationException("The supplied projection snapshot crosses its authorized boundary.");
            sourceComponents = authorizedComponents;
        }
        else if (snapshots is not null)
            sourceComponents = (await snapshots.ReadAsync(request.StateSpaceId, plan.Root.Owner,
                locators.Values.ToArray(), cancellationToken)).Components;
        else
        {
            var stateSpace = stateSpaces.Get(request.StateSpaceId)
                ?? throw new InvalidOperationException("Unknown projection state space.");
            if (stateSpace.ApplicationRevision.ApplicationId != plan.Root.Owner)
                throw new InvalidOperationException("A projection cannot cross an application state-space boundary.");
            sourceComponents = await components.GetComponentsAsync(request.StateSpaceId,
                locators.Values.ToArray(), cancellationToken);
        }

        var values = sourceComponents.ToDictionary(value =>
            (value.EntityId, value.Type.QualifiedTypeId));
        if (authorizedComponents is not null)
            foreach (var index in active)
                foreach (var input in plan.Nodes[index].Definition.ComponentInputs)
                    if (request.RoleEntityIds.TryGetValue(plan.Nodes[index].RootRoles[input.EntityRole], out var entity) &&
                        values.TryGetValue((entity, input.Type.QualifiedTypeId), out var value) && value.Type != input.Type)
                        throw new InvalidOperationException("A supplied projection component has a stale exact type.");
        var evaluated = new Dictionary<int, string>();
        var fields = new List<ProjectionMappedFieldEvidence>();
        var compatibility = new ProjectionSourceCompatibility(componentTypes);
        if (request.Purpose == ProjectionReadPurpose.Display)
            compatibility.Prepare(active.Select(index => plan.Nodes[index])
                .Where(node => node.Definition.ObjectContract?.IsFieldBased == true)
                .SelectMany(node => node.Definition.ComponentInputs.Select(input =>
                {
                    var entityId = request.RoleEntityIds.GetValueOrDefault(node.RootRoles[input.EntityRole]);
                    return (Declared: input.Type, Actual: entityId is not null &&
                        values.TryGetValue((entityId, input.Type.QualifiedTypeId), out var value) ? value.Type : input.Type);
                })));
        for (var index = 0; index < plan.Nodes.Count; index++)
        {
            if (!active.Contains(index)) continue;
            evaluated[index] = Evaluate(plan.Nodes[index], request.RoleEntityIds, values, evaluated, active,
                validateOutput: completeRoot is null || index != plan.Nodes.Count - 1,
                request.Purpose, fields, compatibility);
            if (authorizedComponents is not null && plan.Nodes[index].Definition.ObjectContract is { } contract &&
                Encoding.UTF8.GetByteCount(evaluated[index]) > contract.Limits.OutputBytes)
                throw new InvalidOperationException("A supplied snapshot object exceeds its declared byte bound.");
        }
        var rootIndex = plan.Nodes.Count - 1;
        if (!evaluated.TryGetValue(rootIndex, out var output))
            throw new InvalidOperationException("The prepared projection root did not produce a result.");
        var observed = new Dictionary<(string, string), ProjectionSourceRevision>();
        foreach (var index in active)
        {
            var node = plan.Nodes[index];
            foreach (var input in node.Definition.ComponentInputs)
            {
                if (!request.RoleEntityIds.TryGetValue(node.RootRoles[input.EntityRole], out var entityId)) continue;
                values.TryGetValue((entityId, input.Type.QualifiedTypeId), out var source);
                observed[(entityId, input.Type.QualifiedTypeId)] = new(entityId,
                    source?.Type ?? input.Type, source?.Revision ?? 0);
            }
        }
        var result = new ProjectionMaterializationResult(plan.Root.Reference, output, Array.AsReadOnly(observed.Values
            .OrderBy(value => value.EntityId, StringComparer.Ordinal)
            .ThenBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal).ToArray()))
        {
            Fields = fields.AsReadOnly(),
            ObservedSources = request.Purpose == ProjectionReadPurpose.Display
                ? ObservedSources(plan.Root, request.RoleEntityIds, observed)
                : []
        };
        if (completeRoot is not null)
        {
            var expanded = await completeRoot(result, cancellationToken);
            ValidateOutput(plan.Root, expanded);
            result = result with { OutputJson = expanded };
        }
        return result;
    }

    private static IReadOnlyList<ProjectionObservedSource> ObservedSources(
        RegisteredProjectionDefinition definition,
        IReadOnlyDictionary<string, string> roles,
        IReadOnlyDictionary<(string, string), ProjectionSourceRevision> observed)
    {
        if (definition.ObjectContract is not { IsFieldBased: true, FieldProvenance: { } provenance }) return [];
        var sources = provenance.Select(value => new
            {
                Path = string.Join("\u001F", value.InputPath),
                value.InputPath,
                value.EntityRole,
                value.Required,
                value.Component
            })
            .DistinctBy(value => (value.Path, value.EntityRole, value.Required, value.Component))
            .OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.EntityRole, StringComparer.Ordinal)
            .ThenBy(value => value.Component.QualifiedTypeId, StringComparer.Ordinal)
            .ThenBy(value => value.Component.TypeVersion)
            .Select(value =>
            {
                ProjectionSourceRevision? revision = null;
                if (roles.TryGetValue(value.EntityRole, out var entityId))
                    observed.TryGetValue((entityId, value.Component.QualifiedTypeId), out revision);
                return new ProjectionObservedSource(value.InputPath, value.EntityRole, value.Required,
                    value.Component, revision is { Revision: > 0 } ? revision.Type : null,
                    revision is { Revision: > 0 } ? "available" : "absent-source");
            }).ToArray();
        return Array.AsReadOnly(sources);
    }

    private static void ValidateRootRoles(
        RegisteredProjectionDefinition root,
        IReadOnlyDictionary<string, string> roles)
    {
        var declared = root.EntityRoles.ToHashSet(StringComparer.Ordinal);
        if (roles.Keys.Any(role => !declared.Contains(role)))
            throw new InvalidOperationException("Projection role bindings contain an undeclared role.");
        var required = root.ObjectContract?.Roles.Where(value => value.Required).Select(value => value.RoleId)
            .ToHashSet(StringComparer.Ordinal) ?? declared;
        if (required.Any(role => !roles.ContainsKey(role))
            || root.ObjectContract is null && roles.Count != declared.Count)
            throw new InvalidOperationException("Projection role bindings do not satisfy its exact declaration.");
    }

    private static HashSet<int> ActiveNodes(
        PreparedProjectionPlan plan,
        IReadOnlyDictionary<string, string> roles)
    {
        var active = new HashSet<int>();
        Activate(plan.Nodes.Count - 1);
        return active;

        void Activate(int index)
        {
            if (!active.Add(index)) return;
            var node = plan.Nodes[index];
            foreach (var input in node.Definition.DependencyInputs)
            {
                var childIndex = node.Children[input.InputId];
                var child = plan.Nodes[childIndex];
                var childRequired = child.Definition.ObjectContract?.Roles.Where(value => value.Required)
                    .Select(value => value.RoleId).ToArray() ?? child.Definition.EntityRoles.ToArray();
                var bindable = childRequired.All(role => roles.ContainsKey(child.RootRoles[role]));
                if (!bindable && node.OptionalDependencyInputs.Contains(input.InputId)) continue;
                if (!bindable)
                    throw new InvalidOperationException("A required projection dependency role is unbound.");
                Activate(childIndex);
            }
        }
    }

    private static Dictionary<(string Entity, string Type), EcsComponentLocator> Locators(
        PreparedProjectionPlan plan,
        IReadOnlySet<int> active,
        IReadOnlyDictionary<string, string> roles)
    {
        var result = new Dictionary<(string Entity, string Type), EcsComponentLocator>();
        for (var index = 0; index < plan.Nodes.Count; index++)
        {
            if (!active.Contains(index)) continue;
            var node = plan.Nodes[index];
            foreach (var input in node.Definition.ComponentInputs)
            {
                var rootRole = node.RootRoles[input.EntityRole];
                if (!roles.TryGetValue(rootRole, out var entity))
                {
                    if (node.OptionalComponentInputs.Contains(input.InputId)) continue;
                    throw new InvalidOperationException("A required projection component role is unbound.");
                }
                result.TryAdd((entity, input.Type.QualifiedTypeId),
                    new(entity, input.Type.QualifiedTypeId));
            }
        }
        return result;
    }

    private string Evaluate(
        PreparedProjectionNode node,
        IReadOnlyDictionary<string, string> roles,
        IReadOnlyDictionary<(string, string), EcsComponentView> values,
        IReadOnlyDictionary<int, string> evaluated,
        IReadOnlySet<int> active,
        bool validateOutput,
        ProjectionReadPurpose purpose,
        List<ProjectionMappedFieldEvidence> fields,
        ProjectionSourceCompatibility compatibility)
    {
        var display = purpose == ProjectionReadPurpose.Display &&
            node.Definition.ObjectContract?.IsFieldBased == true;
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in node.Definition.ComponentInputs)
        {
            var rootRole = node.RootRoles[input.EntityRole];
            if (!roles.TryGetValue(rootRole, out var entity))
            {
                if (node.OptionalComponentInputs.Contains(input.InputId)) continue;
                throw new InvalidOperationException("A required projection component role is unbound.");
            }
            if (!values.TryGetValue((entity, input.Type.QualifiedTypeId), out var value)
                || !compatibility.Matches(input.Type, value.Type, display))
            {
                if (node.OptionalComponentInputs.Contains(input.InputId)) continue;
                throw new InvalidOperationException("A declared projection component is missing or stale.");
            }
            sources.Add(input.InputId, value.ValueJson);
        }
        foreach (var input in node.Definition.DependencyInputs)
        {
            var child = node.Children[input.InputId];
            if (!active.Contains(child))
            {
                if (node.OptionalDependencyInputs.Contains(input.InputId)) continue;
                throw new InvalidOperationException("A required projection dependency is unavailable.");
            }
            sources.Add(input.InputId, evaluated[child]);
        }

        string result;
        if (node.Definition.Mappings[0].TargetPointer == "")
        {
            var mapping = node.Definition.Mappings[0];
            if (!sources.TryGetValue(mapping.InputId, out var source))
            {
                if (!display) throw new InvalidOperationException("A root projection mapping source is unavailable.");
                result = "{}";
                fields.Add(Evidence(node.Definition, mapping, "absent-source"));
            }
            else if (!TrySelect(source, mapping.SourcePointer, out var selected))
            {
                if (!display) throw new InvalidOperationException("A declared source path is absent from its value.");
                result = "{}";
                fields.Add(Evidence(node.Definition, mapping, "absent-path"));
            }
            else
            {
                result = selected.GetRawText();
                if (node.Definition.ObjectContract?.IsFieldBased == true)
                    fields.Add(Evidence(node.Definition, mapping, selected.ValueKind == JsonValueKind.Null ? "null" : "value"));
            }
        }
        else
        {
            var output = new JsonObject();
            foreach (var mapping in node.Definition.Mappings)
            {
                if (!sources.TryGetValue(mapping.InputId, out var source))
                {
                    if (node.Definition.ObjectContract?.IsFieldBased == true)
                        fields.Add(Evidence(node.Definition, mapping, "absent-source"));
                    continue;
                }
                if (!TrySelect(source, mapping.SourcePointer, out var selected))
                {
                    if (!display) throw new InvalidOperationException("A declared source path is absent from its value.");
                    fields.Add(Evidence(node.Definition, mapping, "absent-path"));
                    continue;
                }
                Set(output, mapping.TargetPointer, JsonNode.Parse(selected.GetRawText()));
                if (node.Definition.ObjectContract?.IsFieldBased == true)
                    fields.Add(Evidence(node.Definition, mapping, selected.ValueKind == JsonValueKind.Null ? "null" : "value"));
            }
            result = output.ToJsonString();
        }
        if (Encoding.UTF8.GetByteCount(result) > (node.Definition.ObjectContract?.Limits.OutputBytes
                ?? SystemJsonSchemaProfile.MaximumValueBytes))
            throw new InvalidOperationException("Structural projection output exceeds its byte bound.");
        if (validateOutput) ValidateOutput(node.Definition, result);
        return result;
    }

    private static ProjectionMappedFieldEvidence Evidence(RegisteredProjectionDefinition definition,
        StructuralProjectionMapping mapping, string availability) =>
        new(definition.Reference, mapping.InputId, mapping.SourcePointer, mapping.TargetPointer, availability);

    private void ValidateOutput(RegisteredProjectionDefinition definition, string output)
    {
        // V2's host-owned transport schema enforces only JSON object/depth/node/byte safety;
        // it is deliberately not another authored schema for the assembled domain value.
        if (Encoding.UTF8.GetByteCount(output) > (definition.ObjectContract?.Limits.OutputBytes
                ?? SystemJsonSchemaProfile.MaximumValueBytes)
            || validator.Validate(definition.ProfileId, definition.OutputSchemaJson, output).Status != SchemaValueStatus.Valid)
            throw new InvalidOperationException("Structural projection output fails its exact schema.");
    }

    private RegisteredProjectionDefinition Require(ProjectionReference reference)
    {
        var result = definitions.Get(reference.QualifiedId, reference.Version);
        return result is null || result.ContentHash != reference.ContentHash
            ? throw new InvalidOperationException("Projection reference is unknown or stale.")
            : result;
    }

    private static bool TrySelect(string json, string pointer, out JsonElement result)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        foreach (var token in Tokens(pointer))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(token, out var property))
                current = property;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var index)
                     && index >= 0 && index < current.GetArrayLength())
                current = current[index];
            else { result = default; return false; }
        }
        using var stable = JsonDocument.Parse(current.GetRawText());
        result = stable.RootElement.Clone();
        return true;
    }

    private static void Set(JsonObject root, string pointer, JsonNode? value)
    {
        var tokens = Tokens(pointer).ToArray();
        var current = root;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (current[tokens[index]] is not JsonObject next)
            {
                next = new JsonObject();
                current[tokens[index]] = next;
            }
            current = next;
        }
        current[tokens[^1]] = value?.DeepClone();
    }

    private static IEnumerable<string> Tokens(string pointer) => pointer == "" ? []
        : pointer.Split('/').Skip(1).Select(value => value.Replace("~1", "/").Replace("~0", "~"));
}
