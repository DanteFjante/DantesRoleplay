using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Capabilities;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// Binds next actions to the live MCP capability catalog. This intentionally refuses unknown,
/// inactive, or schema-incompatible targets so response guidance cannot drift beyond discovery.
/// </summary>
internal static class McpNextActionFactory
{
    private static readonly IBoundedJsonSchemaValidator Schemas = new BoundedJsonSchemaValidator();

    public static ToolNextAction Create(
        string id,
        string description,
        string capabilityId,
        JsonObject knownArguments,
        IReadOnlyList<MissingArgument> missingArguments,
        params string[] requiredArguments)
    {
        var descriptor = McpVerbCatalog.Descriptors.SingleOrDefault(value => value.Id == capabilityId)
            ?? throw new InvalidOperationException($"Next action '{id}' targets an unregistered capability '{capabilityId}'.");
        if (descriptor.Lifecycle != CapabilityContractLifecycle.Active)
            throw new InvalidOperationException($"Next action '{id}' targets a non-active capability '{capabilityId}'.");

        var schema = JsonNode.Parse(descriptor.Input.SchemaJson) as JsonObject
            ?? throw new InvalidOperationException($"Capability '{capabilityId}' has no object input schema.");
        var properties = schema["properties"] as JsonObject ?? new JsonObject();
        var schemaRequired = (schema["required"] as JsonArray)?.Select(value => value?.GetValue<string>() ?? "")
            .Where(value => value.Length > 0).ToArray() ?? [];
        var required = requiredArguments.Concat(schemaRequired)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = missingArguments.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        var known = knownArguments.DeepClone().AsObject();
        var knownNames = known.Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
        var missingNames = missing.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);

        if (required.Any(value => !properties.ContainsKey(value))
            || knownNames.Any(value => !properties.ContainsKey(value))
            || missingNames.Any(value => !properties.ContainsKey(value))
            || knownNames.Overlaps(missingNames)
            || required.Any(value => !knownNames.Contains(value) && !missingNames.Contains(value)))
            throw new InvalidOperationException($"Next action '{id}' does not match capability '{capabilityId}'.");

        var arguments = known.DeepClone().AsObject();
        foreach (var value in missing)
            arguments[value.Name] = value.ExampleValue.DeepClone();

        foreach (var name in schemaRequired)
            if (!arguments.ContainsKey(name))
                throw new InvalidOperationException(
                    $"Next action '{id}' omits schema-required argument '{name}' for '{capabilityId}'.");

        var validation = Schemas.Validate(descriptor.Input.SchemaJson, arguments.ToJsonString());
        if (validation.Status != SchemaValueStatus.Valid)
            throw new InvalidOperationException(
                $"Next action '{id}' carries arguments that fail the current schema for '{capabilityId}'.");

        var (tool, kind) = Route(descriptor.Id);
        return new(id, description, descriptor.Id, descriptor.Fingerprint, descriptor.Input.SchemaHash,
            "mcp", tool, kind, required, known,
            missing.Select(value => new ToolNextActionMissingArgument(value.Name, value.Description)).ToArray(),
            arguments, missing.Length == 0);
    }

    public static string Advice(ToolNextAction action)
    {
        var arguments = action.Arguments.Select(value =>
            $"{value.Key}: {value.Value?.ToJsonString() ?? "null"}");
        var joined = string.Join(", ", new[] { $"kind: {JsonSerializer.Serialize(action.Kind)}" }.Concat(arguments));
        return $"{action.Tool}({joined}) — {action.Description}";
    }

    private static (string Tool, string Kind) Route(string capabilityId)
    {
        const string query = "mcp.query.";
        const string commit = "mcp.commit.";
        if (capabilityId.StartsWith(query, StringComparison.Ordinal)) return ("query", capabilityId[query.Length..]);
        if (capabilityId.StartsWith(commit, StringComparison.Ordinal)) return ("commit", capabilityId[commit.Length..]);
        throw new InvalidOperationException($"Capability '{capabilityId}' is not an MCP query or commit route.");
    }

    internal sealed record MissingArgument(string Name, string Description, JsonNode ExampleValue);
}
