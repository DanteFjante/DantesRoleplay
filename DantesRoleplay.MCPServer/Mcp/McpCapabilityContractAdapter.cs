using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Capabilities;

namespace DantesRoleplay.MCPServer.Mcp;

internal static class McpCapabilityContractAdapter
{
    private const string EnvelopeSchema = """
        {"type":"object","additionalProperties":true,"required":["ok"],"properties":{"ok":{"type":"boolean"},"data":{},"error":{"type":["object","null"]}}}
        """;

    public static CapabilityContractDescriptor Query(
        string name,
        string description,
        IReadOnlyList<string> inputNames,
        IReadOnlyList<string> procedureIds)
    {
        var schema = QueryInputSchema(inputNames);
        return CapabilityContractBuilder.Create(
            $"mcp.query.{name}", 1, "mcp-query", $"mcp.query.{name}.v1",
            "mcp-server", name, description, CapabilityContractLifecycle.Active,
            new(true, false, false), schema, CapabilityContractSchemaStatus.Generated,
            EnvelopeSchema, CapabilityContractSchemaStatus.Generated, Scope(inputNames), [],
            new("private-operator", "read-system-state", "private-system-state"),
            false, false, procedureIds,
            [new("Read through MCP", CapabilityContractBuilder.MinimalExample(schema))],
            [new("MCP_QUERY_INVALID", "The query request is invalid or unavailable.",
                "Inspect the capability catalog and retry with the declared input schema.")],
            [new("mcp.query.capabilities", "Read the current capability catalog.", "{}")]);
    }

    public static CapabilityContractDescriptor Commit(
        string name,
        string description,
        string payloadExample,
        bool supportsPreview,
        IReadOnlyList<string> procedureIds,
        string? inputSchemaJson = null,
        string? outputSchemaJson = null,
        string lifecycle = CapabilityContractLifecycle.Active,
        string? replacementCapabilityId = null)
    {
        var payload = JsonNode.Parse(payloadExample) as JsonObject
            ?? throw new InvalidOperationException($"Commit example '{name}' must be an object.");
        var schema = inputSchemaJson ?? CommitInputSchema(payload);
        var idempotent = payload.ContainsKey("requestToken") || payload.ContainsKey("idempotencyKey");
        var replacement = replacementCapabilityId;
        var errors = new List<CapabilityErrorContract>
        {
            new("MCP_COMMIT_INVALID", "The change request does not match the declared contract.",
                "Correct the request using the capability input schema and retry."),
            new("AI_TOOL_CONFIRMATION_REQUIRED", "Trusted confirmation is required before changing state.",
                "Request confirmation or use preview when this capability supports it.")
        };
        if (lifecycle == CapabilityContractLifecycle.Deprecated)
            errors.Add(new("CAPABILITY_DEPRECATED", "This callable compatibility route is deprecated.",
                replacement is null ? "Read the current capability catalog before choosing a route."
                    : $"Use replacement capability '{replacement}'."));
        return CapabilityContractBuilder.Create(
            $"mcp.commit.{name}", 1, "mcp-commit", $"mcp.commit.{name}.v1",
            "mcp-server", name, description, lifecycle,
            new(true, supportsPreview, true), schema,
            inputSchemaJson is null ? CapabilityContractSchemaStatus.Generated : CapabilityContractSchemaStatus.Authored,
            outputSchemaJson ?? EnvelopeSchema,
            outputSchemaJson is null ? CapabilityContractSchemaStatus.Generated : CapabilityContractSchemaStatus.Authored,
            Scope(payload.Select(value => value.Key)), [],
            new("private-operator", "change-system-state", "private-system-state"),
            true, idempotent, procedureIds,
            [new("Invoke through MCP", new JsonObject { ["payload"] = payload.DeepClone() }.ToJsonString())],
            errors,
            replacement is null
                ? [new("mcp.query.capabilities", "Read the current capability catalog before retrying.", "{}")]
                : [new(replacement, "Use the registered replacement for this deprecated route.", "{}")]);
    }

    private static string QueryInputSchema(IReadOnlyList<string> names)
    {
        var properties = new JsonObject();
        foreach (var name in names.Order(StringComparer.Ordinal)) properties[name] = ParameterSchema(name);
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        }.ToJsonString();
    }

    private static string CommitInputSchema(JsonObject example)
    {
        var payload = InferObject(example);
        payload["additionalProperties"] = true;
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("payload"),
            ["properties"] = new JsonObject
            {
                ["payload"] = payload,
                ["intent"] = new JsonObject { ["type"] = "string" },
                ["proceduresUsed"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                },
                ["dryRun"] = new JsonObject { ["type"] = "boolean" }
            }
        }.ToJsonString();
    }

    private static JsonObject InferObject(JsonObject value)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in value)
        {
            properties[property.Key] = Infer(property.Value);
            if (property.Value is not null) required.Add(property.Key);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static JsonNode Infer(JsonNode? value) => value switch
    {
        JsonObject objectValue => InferObject(objectValue),
        JsonArray arrayValue => new JsonObject
        {
            ["type"] = "array",
            ["items"] = arrayValue.FirstOrDefault() is JsonNode item ? Infer(item) : new JsonObject()
        },
        JsonValue scalar when scalar.TryGetValue<bool>(out _) => new JsonObject { ["type"] = "boolean" },
        JsonValue scalar when scalar.TryGetValue<int>(out _) => new JsonObject { ["type"] = "integer" },
        JsonValue scalar when scalar.TryGetValue<long>(out _) => new JsonObject { ["type"] = "integer" },
        JsonValue scalar when scalar.TryGetValue<double>(out _) => new JsonObject { ["type"] = "number" },
        JsonValue => new JsonObject { ["type"] = "string" },
        _ => new JsonObject()
    };

    private static JsonNode ParameterSchema(string name) => name switch
    {
        "ids" or "sourceIds" or "extensionIds" or "componentIds" or "relationshipKinds"
            or "kinds" or "statuses" => new JsonObject
            {
                ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }
            },
        "includeInactive" or "includeShadowed" or "failuresOnly" or "transitive" =>
            new JsonObject { ["type"] = "boolean" },
        "version" or "limit" or "sample" or "pageSize" or "containmentDepth"
            or "relationshipDepth" or "maxNodes" or "maxEdges" or "afterSequence" =>
            new JsonObject { ["type"] = "integer" },
        _ => new JsonObject { ["type"] = "string" }
    };

    private static CapabilityScopeContract Scope(IEnumerable<string> names)
    {
        var fields = names.ToHashSet(StringComparer.Ordinal);
        return new("private-operator",
            fields.Contains("applicationId"), fields.Contains("stateSpaceId"),
            fields.Where(value => value is "applicationId" or "stateSpaceId" or "scopeId")
                .Order(StringComparer.Ordinal).ToArray());
    }
}
