using DantesRoleplay.AI;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>
/// Materializes the capabilities visible to one trusted invocation and gives only those tools to
/// the selected model. This is the direct application integration used instead of MCP.
/// </summary>
public sealed class SystemAiAgentService(
    IEnumerable<ISystemAiToolSource> toolSources,
    IAiService? ai = null) : ISystemAiAgentService
{
    public Task<AiResponse> SendAsync(
        AiAgentProfile profile,
        AiRequest request,
        SystemCapabilityInvocationContext context,
        ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
        IAiToolApprovalGate? toolApprovalGate = null,
        CancellationToken cancellationToken = default)
    {
        if (ai is null)
            return Task.FromResult(AiResponse.Failure(
                "AI_SERVICE_UNAVAILABLE", "No local AI service is configured for this host."));
        var tools = new List<IAiTool>();
        var sourceContext = new SystemAiToolSourceContext(
            profile,
            request,
            context,
            writeApprovalGate,
            toolApprovalGate,
            () => tools.AsReadOnly());
        foreach (var source in toolSources)
            tools.AddRange(source.CreateTools(sourceContext));
        if (context.ApplicationId is not null || !string.IsNullOrEmpty(context.StateSpaceId))
            tools = tools.Select(tool => (IAiTool)new ContextBoundTool(tool, context)).ToList();
        return ai.SendAgentRequestAsync(profile, request, tools, cancellationToken);
    }

    private sealed class ContextBoundTool(
        IAiTool inner,
        SystemCapabilityInvocationContext context) : IAiTool
    {
        public AiToolDefinition Definition => inner.Definition;

        public Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (context.ApplicationId is not null &&
                invocation.Arguments.TryGetProperty("applicationId", out var application) &&
                (application.ValueKind != System.Text.Json.JsonValueKind.String ||
                 !string.Equals(application.GetString(), context.ApplicationId.Value, StringComparison.Ordinal)))
                return Task.FromResult(AiToolResult.Failure(
                    "AI_APPLICATION_CONTEXT_DENIED",
                    "The direct tool request targets a different application than the originating AI context."));
            if (!string.IsNullOrEmpty(context.StateSpaceId) &&
                invocation.Arguments.TryGetProperty("stateSpaceId", out var stateSpace) &&
                (stateSpace.ValueKind != System.Text.Json.JsonValueKind.String ||
                 !string.Equals(stateSpace.GetString(), context.StateSpaceId, StringComparison.Ordinal)))
                return Task.FromResult(AiToolResult.Failure(
                    "AI_STATE_SPACE_CONTEXT_DENIED",
                    "The direct tool request targets a different runtime state space than the originating AI context."));
            return inner.InvokeAsync(invocation, cancellationToken);
        }
    }
}
