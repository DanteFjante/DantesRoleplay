using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Gives a selected-application AI the same standing-grant-backed capability surface as the
/// website. Writes carry their own stable idempotency key and need no per-call operator approval.
/// </summary>
public sealed class ApplicationCandidateCapabilityAiToolSource(
    IApplicationCandidateCapabilityGateway gateway) : ISystemAiToolSource
{
    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context)
    {
        if (context.Invocation.ApplicationId is not { IsSystem: false } applicationId) return [];
        var discovery = gateway.Discover(context.Invocation.Principal, applicationId,
            context.Invocation.CorrelationId);
        if (!discovery.Ok) return [];
        return discovery.Capabilities.Select(descriptor => (IAiTool)new Tool(
            gateway, context.Invocation, descriptor)).ToArray();
    }

    private sealed class Tool(
        IApplicationCandidateCapabilityGateway gateway,
        SystemCapabilityInvocationContext context,
        ApplicationCandidateCapabilityDescriptor descriptor) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            Name(descriptor.Id),
            descriptor.Mode == "write"
                ? descriptor.Description + " Authorization comes from the current application standing grant; supply a stable idempotency key."
                : descriptor.Description + " Authorization comes from the current application standing grant.",
            descriptor.Mode == "write"
                ? WriteSchema(descriptor.InputSchemaJson)
                : descriptor.InputSchemaJson);

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (context.ApplicationId is not { IsSystem: false } applicationId)
                return AiToolResult.Failure("APPLICATION_CONTEXT_REQUIRED",
                    "A selected current application is required.");
            string input;
            string? idempotencyKey = null;
            if (descriptor.Mode == "write")
            {
                if (!invocation.Arguments.TryGetProperty("idempotencyKey", out var key)
                    || key.ValueKind != JsonValueKind.String
                    || !invocation.Arguments.TryGetProperty("input", out var value)
                    || value.ValueKind != JsonValueKind.Object)
                    return AiToolResult.Failure("APPLICATION_AUTHORING_INPUT_INVALID",
                    "Selected-application writes require idempotencyKey and one input object.");
                idempotencyKey = key.GetString();
                input = value.GetRawText();
            }
            else input = invocation.Arguments.GetRawText();

            var result = await gateway.InvokeAsync(
                context.Principal,
                applicationId,
                descriptor.Id,
                input,
                idempotencyKey,
                Correlation(invocation.CallId),
                cancellationToken);
            if (!result.Ok)
                return AiToolResult.Failure(result.Error?.Code ?? "APPLICATION_AUTHORING_UNAVAILABLE",
                    result.Error?.Message ?? "Application candidate authoring is unavailable.");
            return AiToolResult.Success(JsonSerializer.Serialize(new
            {
                data = result.Data,
                result.OperationId,
                result.ReadBackFingerprint
            }));
        }
    }

    private static string WriteSchema(string inputSchema)
    {
        var input = JsonNode.Parse(inputSchema) ?? throw new InvalidOperationException(
            "The application candidate input schema is unavailable.");
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("idempotencyKey", "input"),
            ["properties"] = new JsonObject
            {
                ["idempotencyKey"] = new JsonObject
                {
                    ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200
                },
                ["input"] = input
            }
        }.ToJsonString();
    }

    private static string Name(string capabilityId)
    {
        var normalized = new string(capabilityId.Select(value => char.IsLetterOrDigit(value)
            || value is '_' or '-' ? value : '_').ToArray());
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private static string Correlation(string callId)
    {
        var value = "application-authoring-ai:" + callId;
        return value.Length <= 128 ? value : value[..128];
    }
}
