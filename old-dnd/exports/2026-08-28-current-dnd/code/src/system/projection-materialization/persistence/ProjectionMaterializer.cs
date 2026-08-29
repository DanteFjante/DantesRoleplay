using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Projections;

/// <summary>Read-only structural projection engine. It deliberately has no write, cache, or rule execution dependency.</summary>
public sealed class ProjectionMaterializer(
    IProjectionDefinitionRegistry definitions,
    IEntityComponentStore components,
    IStateSpaceRegistry stateSpaces,
    IBoundedJsonSchemaValidator validator) : IProjectionMaterializer
{
    public async Task<ProjectionMaterializationResult> MaterializeAsync(ProjectionMaterializationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); request.Projection.Validate();
        if (string.IsNullOrWhiteSpace(request.StateSpaceId) || request.RoleEntityIds is null || request.RoleEntityIds.Count > 64) throw new ArgumentException("A bounded state-space and role binding are required.");
        var root = Require(request.Projection);
        var stateSpace = stateSpaces.Get(request.StateSpaceId) ?? throw new InvalidOperationException("Unknown projection state space.");
        if (stateSpace.ApplicationRevision.ApplicationId != root.Owner) throw new InvalidOperationException("A projection cannot cross an application state-space boundary.");
        var plan = new List<Node>(); var locators = new Dictionary<(string Entity, string Type), EcsComponentLocator>();
        Plan(root, request.RoleEntityIds, 0, plan, locators);
        if (locators.Count > 256) throw new InvalidOperationException("Projection component read bound exceeded.");
        var read = await components.GetComponentsAsync(request.StateSpaceId, locators.Values.ToArray(), cancellationToken);
        var values = read.ToDictionary(x => (x.EntityId, x.Type.QualifiedTypeId));
        var evaluated = new Dictionary<Node, string>();
        foreach (var node in plan)
            evaluated[node] = Evaluate(node, values, evaluated);
        var output = evaluated[plan[^1]];
        return new ProjectionMaterializationResult(root.Reference, output, Array.AsReadOnly(read.OrderBy(x => x.EntityId, StringComparer.Ordinal).ThenBy(x => x.Type.QualifiedTypeId, StringComparer.Ordinal).Select(x => new ProjectionSourceRevision(x.EntityId, x.Type, x.Revision)).ToArray()));
    }

    private void Plan(RegisteredProjectionDefinition definition, IReadOnlyDictionary<string, string> roles, int depth, List<Node> nodes, Dictionary<(string, string), EcsComponentLocator> locators)
    {
        if (depth > 16 || !definition.EntityRoles.Order().SequenceEqual(roles.Keys.Order(), StringComparer.Ordinal) || roles.Values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Projection role bindings do not exactly match its declaration.");
        var children = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var input in definition.DependencyInputs)
        {
            var childRoles = input.RoleBindings.ToDictionary(x => x.Key, x => roles[x.Value], StringComparer.Ordinal);
            var child = Require(input.Projection); Plan(child, childRoles, depth + 1, nodes, locators); children.Add(input.InputId, nodes[^1]);
        }
        foreach (var input in definition.ComponentInputs)
        {
            var entity = roles[input.EntityRole]; var key = (entity, input.Type.QualifiedTypeId);
            locators.TryAdd(key, new EcsComponentLocator(entity, input.Type.QualifiedTypeId));
        }
        nodes.Add(new Node(definition, new Dictionary<string, string>(roles, StringComparer.Ordinal), children));
    }

    private string Evaluate(Node node, IReadOnlyDictionary<(string, string), EcsComponentView> values, IReadOnlyDictionary<Node, string> evaluated)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in node.Definition.ComponentInputs)
        {
            var entity = node.Roles[input.EntityRole];
            if (!values.TryGetValue((entity, input.Type.QualifiedTypeId), out var value) || value.Type != input.Type) throw new InvalidOperationException("A declared projection component is missing or stale.");
            sources.Add(input.InputId, value.ValueJson);
        }
        foreach (var input in node.Definition.DependencyInputs) sources.Add(input.InputId, evaluated[node.Children[input.InputId]]);
        string result;
        if (node.Definition.Mappings[0].TargetPointer == "") result = Select(sources[node.Definition.Mappings[0].InputId], node.Definition.Mappings[0].SourcePointer).GetRawText();
        else
        {
            var output = new JsonObject();
            foreach (var mapping in node.Definition.Mappings) Set(output, mapping.TargetPointer, JsonNode.Parse(Select(sources[mapping.InputId], mapping.SourcePointer).GetRawText()));
            result = output.ToJsonString();
        }
        if (Encoding.UTF8.GetByteCount(result) > SystemJsonSchemaProfile.MaximumValueBytes || validator.Validate(node.Definition.ProfileId, node.Definition.OutputSchemaJson, result).Status != SchemaValueStatus.Valid) throw new InvalidOperationException("Structural projection output fails its exact schema.");
        return result;
    }

    private RegisteredProjectionDefinition Require(ProjectionReference reference)
    {
        var result = definitions.Get(reference.QualifiedId, reference.Version);
        return result is null || result.ContentHash != reference.ContentHash ? throw new InvalidOperationException("Projection reference is unknown or stale.") : result;
    }
    private static JsonElement Select(string json, string pointer)
    {
        using var document = JsonDocument.Parse(json); var current = document.RootElement;
        foreach (var token in Tokens(pointer))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(token, out var property)) current = property;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var index) && index >= 0 && index < current.GetArrayLength()) current = current[index];
            else throw new InvalidOperationException("A declared source path is absent from its value.");
        }
        using var stable = JsonDocument.Parse(current.GetRawText()); return stable.RootElement.Clone();
    }
    private static void Set(JsonObject root, string pointer, JsonNode? value)
    {
        var tokens = Tokens(pointer).ToArray(); JsonObject current = root;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (current[tokens[i]] is not JsonObject next) { next = new JsonObject(); current[tokens[i]] = next; } current = next;
        }
        current[tokens[^1]] = value?.DeepClone();
    }
    private static IEnumerable<string> Tokens(string pointer) => pointer == "" ? [] : pointer.Split('/').Skip(1).Select(x => x.Replace("~1", "/").Replace("~0", "~"));
    private sealed record Node(RegisteredProjectionDefinition Definition, IReadOnlyDictionary<string, string> Roles, IReadOnlyDictionary<string, Node> Children);
}
