using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Projections;

/// <summary>Prepared read-only structural projection engine. Result data is never cached.</summary>
public sealed class ProjectionMaterializer(
    IProjectionDefinitionRegistry definitions,
    IEntityComponentStore components,
    IStateSpaceRegistry stateSpaces,
    IBoundedJsonSchemaValidator validator,
    ProjectionPlanCache? planCache = null,
    IProjectionSourceSnapshotReader? snapshots = null) : IProjectionMaterializer
{
    private readonly ProjectionPlanCache plans = planCache ?? new ProjectionPlanCache();

    public async Task<ProjectionMaterializationResult> MaterializeAsync(
        ProjectionMaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        ProjectionSourceSnapshot snapshot;
        if (snapshots is not null)
            snapshot = await snapshots.ReadAsync(request.StateSpaceId, plan.Root.Owner,
                locators.Values.ToArray(), cancellationToken);
        else
        {
            var stateSpace = stateSpaces.Get(request.StateSpaceId)
                ?? throw new InvalidOperationException("Unknown projection state space.");
            if (stateSpace.ApplicationRevision.ApplicationId != plan.Root.Owner)
                throw new InvalidOperationException("A projection cannot cross an application state-space boundary.");
            snapshot = new(stateSpace, await components.GetComponentsAsync(request.StateSpaceId,
                locators.Values.ToArray(), cancellationToken));
        }

        var values = snapshot.Components.ToDictionary(value =>
            (value.EntityId, value.Type.QualifiedTypeId));
        var evaluated = new Dictionary<int, string>();
        for (var index = 0; index < plan.Nodes.Count; index++)
        {
            if (!active.Contains(index)) continue;
            evaluated[index] = Evaluate(plan.Nodes[index], request.RoleEntityIds, values, evaluated, active);
        }
        var rootIndex = plan.Nodes.Count - 1;
        if (!evaluated.TryGetValue(rootIndex, out var output))
            throw new InvalidOperationException("The prepared projection root did not produce a result.");
        return new(plan.Root.Reference, output,
            Array.AsReadOnly(snapshot.Components.OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .ThenBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal)
                .Select(value => new ProjectionSourceRevision(value.EntityId, value.Type, value.Revision)).ToArray()));
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
        IReadOnlySet<int> active)
    {
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
                || value.Type != input.Type)
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
            if (!sources.TryGetValue(node.Definition.Mappings[0].InputId, out var source))
                throw new InvalidOperationException("A root projection mapping source is unavailable.");
            result = Select(source, node.Definition.Mappings[0].SourcePointer).GetRawText();
        }
        else
        {
            var output = new JsonObject();
            foreach (var mapping in node.Definition.Mappings)
            {
                if (!sources.TryGetValue(mapping.InputId, out var source)) continue;
                Set(output, mapping.TargetPointer,
                    JsonNode.Parse(Select(source, mapping.SourcePointer).GetRawText()));
            }
            result = output.ToJsonString();
        }
        if (Encoding.UTF8.GetByteCount(result) > SystemJsonSchemaProfile.MaximumValueBytes
            || validator.Validate(node.Definition.ProfileId, node.Definition.OutputSchemaJson, result).Status
                != SchemaValueStatus.Valid)
            throw new InvalidOperationException("Structural projection output fails its exact schema.");
        return result;
    }

    private RegisteredProjectionDefinition Require(ProjectionReference reference)
    {
        var result = definitions.Get(reference.QualifiedId, reference.Version);
        return result is null || result.ContentHash != reference.ContentHash
            ? throw new InvalidOperationException("Projection reference is unknown or stale.")
            : result;
    }

    private static JsonElement Select(string json, string pointer)
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
            else throw new InvalidOperationException("A declared source path is absent from its value.");
        }
        using var stable = JsonDocument.Parse(current.GetRawText());
        return stable.RootElement.Clone();
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
