using System.Text;
using DantesRoleplay.AI;
using DantesRoleplay.AI.Codex;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Adapts the Codex app-server process to the provider-neutral AI surface. Dynamic tools are
/// executed through the request's in-process delegate, so Codex never needs an MCP round trip.
/// </summary>
public sealed class CodexAiClient(ICodexAppServerFactory factory) : ICodexAiClient
{
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var models = await factory.ListModelsAsync(cancellationToken);
        return models.Select(value => new AiModel(
                "codex",
                value.Id,
                value.DisplayName,
                AiModelCapabilities.Messages | AiModelCapabilities.Tasks |
                AiModelCapabilities.Reasoning | AiModelCapabilities.StructuredOutput |
                AiModelCapabilities.Tools,
                value.SupportedReasoningEfforts.Select(ParseEffort).Distinct().ToArray(),
                IsDefault: value.IsDefault))
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AiProviderResponse> SendAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var models = await ListModelsAsync(cancellationToken);
        var model = models.SingleOrDefault(value => string.Equals(value.Id, request.Model, StringComparison.Ordinal));
        if (model is null)
            return AiProviderResponse.Failure("CODEX_MODEL_UNAVAILABLE", $"Codex model '{request.Model}' is not available.");
        if (request.Reasoning != AiReasoningEffort.None && !model.ReasoningEfforts.Contains(request.Reasoning))
            return AiProviderResponse.Failure("CODEX_REASONING_UNSUPPORTED", "The selected Codex model does not support that reasoning effort.");
        if (request.Tools.Count > 0 && request.ToolExecutor is null)
            return AiProviderResponse.Failure("CODEX_TOOL_EXECUTOR_MISSING", "Codex tools require an in-process tool executor.");

        try
        {
            await using var session = await factory.CreateAsync(cancellationToken);
            var started = await session.StartTurnAsync(
                null,
                Prompt(request.Messages, request.Kind),
                new(
                    request.Model,
                    request.Reasoning == AiReasoningEffort.None ? "" : FormatEffort(request.Reasoning),
                    request.ResponseSchemaJson,
                    request.Tools,
                    request.ToolExecutor),
                cancellationToken);
            var deltas = new StringBuilder();
            string reply = "";
            string terminalStatus = "";
            string terminalError = "";
            await foreach (var value in session.ReadEventsAsync(cancellationToken))
            {
                switch (value.Type)
                {
                    case "delta":
                        deltas.Append(value.Delta);
                        break;
                    case "reply":
                        reply = value.Reply;
                        break;
                    case "approval" when value.Approval is not null:
                        await session.RespondApprovalAsync(
                            value.Approval.ExternalRequestId,
                            DantesRoleplay.Assistants.CodexApprovalDecisions.Decline,
                            cancellationToken);
                        break;
                    case "terminal":
                        terminalStatus = value.Status;
                        terminalError = value.ErrorMessage;
                        break;
                }
            }
            if (terminalStatus is not ("completed" or "succeeded"))
                return AiProviderResponse.Failure(
                    "CODEX_TURN_FAILED",
                    string.IsNullOrWhiteSpace(terminalError) ? "The Codex turn did not complete." : terminalError);
            var text = string.IsNullOrWhiteSpace(reply) ? deltas.ToString() : reply;
            if (string.IsNullOrWhiteSpace(text))
                return AiProviderResponse.Failure("CODEX_RESPONSE_INVALID", "Codex returned no final message.");
            return new(
                true,
                model,
                text,
                string.IsNullOrWhiteSpace(request.ResponseSchemaJson) ? "" : text,
                [],
                ConversationId: started.ExternalThreadId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CodexBridgeException exception)
        {
            return AiProviderResponse.Failure(exception.Code, exception.Message);
        }
    }

    private static string Prompt(IReadOnlyList<AiMessage> messages, AiRequestKind kind)
    {
        var builder = new StringBuilder();
        if (AiRequestKinds.IsBackground(kind))
            builder.AppendLine("Complete the following task and report the result.").AppendLine();
        else if (kind == AiRequestKind.StructuredRequest)
            builder.AppendLine("Answer the following request using the required structured output.").AppendLine();
        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                AiMessageRole.System => "System instruction",
                AiMessageRole.User => "User",
                AiMessageRole.Assistant => "Assistant",
                AiMessageRole.Tool => "Tool result",
                _ => "Message"
            };
            builder.Append(role).Append(':').AppendLine().AppendLine(message.Content).AppendLine();
        }
        return builder.ToString().Trim();
    }

    internal static AiReasoningEffort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "minimal" => AiReasoningEffort.Minimal,
        "low" => AiReasoningEffort.Low,
        "medium" => AiReasoningEffort.Medium,
        "high" => AiReasoningEffort.High,
        "xhigh" => AiReasoningEffort.XHigh,
        "max" => AiReasoningEffort.Max,
        "ultra" => AiReasoningEffort.Ultra,
        _ => AiReasoningEffort.None
    };

    internal static string FormatEffort(AiReasoningEffort value) => value switch
    {
        AiReasoningEffort.Minimal => "minimal",
        AiReasoningEffort.Low => "low",
        AiReasoningEffort.Medium => "medium",
        AiReasoningEffort.High => "high",
        AiReasoningEffort.XHigh => "xhigh",
        AiReasoningEffort.Max => "max",
        AiReasoningEffort.Ultra => "ultra",
        _ => ""
    };
}
