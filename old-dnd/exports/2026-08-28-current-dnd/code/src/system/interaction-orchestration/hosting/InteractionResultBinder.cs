using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace DantesRoleplay.Interactions;

internal sealed record InteractionBoundStepInput(
    IReadOnlyDictionary<string, string> RoleBindings,
    string InputJson,
    IReadOnlyList<string> SourceResultFingerprints);

internal static class InteractionResultBinder
{
    public static InteractionBoundStepInput Bind(
        InteractionPlanStep step,
        IReadOnlyDictionary<string, InteractionQueryExecutionResult> queryResults)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(queryResults);
        var roles = new Dictionary<string, string>(step.RoleBindings, StringComparer.Ordinal);
        JsonNode? input = JsonNode.Parse(step.InputJson)
            ?? throw Failure("RESULT_BINDING_INPUT_INVALID", "The step input is unavailable.");
        var fingerprints = new List<string>();
        foreach (var binding in step.ResultBindings)
        {
            if (!queryResults.TryGetValue(binding.FromStepId, out var result))
                throw Failure("RESULT_BINDING_SOURCE_UNAVAILABLE", "A required query result is unavailable.");
            var selected = Select(result.OutputJson, binding.FromPointer);
            fingerprints.Add(result.ResultFingerprint);
            if (binding.ToRole is not null)
            {
                if (selected is not JsonValue value || !value.TryGetValue<string>(out var entityId)
                    || string.IsNullOrWhiteSpace(entityId) || !roles.TryAdd(binding.ToRole, entityId))
                    throw Failure("RESULT_BINDING_ROLE_INVALID",
                        "A query result cannot fill the declared role as an entity id.");
                continue;
            }
            input = Set(input, binding.ToInputPointer!, selected);
        }
        var json = InteractionCanonicalJson.CanonicalizeObject(input.ToJsonString());
        return new(new ReadOnlyDictionary<string, string>(roles), json,
            Array.AsReadOnly(fingerprints.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
    }

    private static JsonNode Select(string json, string pointer)
    {
        JsonNode? current = JsonNode.Parse(json)
            ?? throw Failure("RESULT_BINDING_SOURCE_INVALID", "A query result is invalid.");
        foreach (var token in Tokens(pointer))
        {
            if (current is JsonObject value && value.TryGetPropertyValue(token, out var child)
                && child is not null) current = child;
            else if (current is JsonArray array && int.TryParse(token, out var index)
                && index >= 0 && index < array.Count && array[index] is { } item) current = item;
            else throw Failure("RESULT_BINDING_SOURCE_PATH_MISSING",
                "A declared query result path is absent at execution.");
        }
        return current.DeepClone();
    }

    private static JsonNode Set(JsonNode input, string pointer, JsonNode value)
    {
        if (pointer == "")
        {
            if (input is not JsonObject current || current.Count != 0 || value is not JsonObject)
                throw Failure("RESULT_BINDING_INPUT_TARGET_INVALID",
                    "A root input binding requires empty object input and object output.");
            return value.DeepClone();
        }
        if (input is not JsonObject root)
            throw Failure("RESULT_BINDING_INPUT_TARGET_INVALID", "A result binding target is not an object.");
        var tokens = Tokens(pointer).ToArray();
        JsonObject parent = root;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (parent[tokens[index]] is not JsonObject child)
                throw Failure("RESULT_BINDING_INPUT_TARGET_INVALID",
                    "A result binding target parent is absent or not an object.");
            parent = child;
        }
        if (parent.ContainsKey(tokens[^1]))
            throw Failure("RESULT_BINDING_INPUT_TARGET_OCCUPIED",
                "A result binding cannot overwrite static input.");
        parent[tokens[^1]] = value.DeepClone();
        return root;
    }

    private static IEnumerable<string> Tokens(string pointer) => pointer == "" ? []
        : pointer.Split('/').Skip(1).Select(value => value.Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal));

    private static InteractionContractException Failure(string code, string message) => new(code, message);
}
