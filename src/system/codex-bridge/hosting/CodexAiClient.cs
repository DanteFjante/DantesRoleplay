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
        if (request.Tools.Count > 16 || request.MaximumToolCalls is < 0 or > 16
            || request.MaximumResponseBytes is < 1 or > 1_048_576
            || request.MaximumDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)))
            return AiProviderResponse.Failure("CODEX_REQUEST_INVALID", "The Codex request exceeds its closed limits.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.MaximumDuration ?? TimeSpan.FromMinutes(10));
        if (request.Tools.Count > 0 && request.ToolExecutor is null)
            return AiProviderResponse.Failure("CODEX_TOOL_EXECUTOR_MISSING", "Codex tools require an in-process tool executor.");

        ICodexAppServerSession? session = null;
        CodexProtocolTokenUsage? highest = null;
        var usagePoisoned = false;
        var terminal = false;
        var completed = false;
        var callbackLimitReached = 0;
        var closed = 0;
        AiTokenUsageEvidence? Evidence() => highest is null ? null : new(highest.InputTokens, highest.OutputTokens,
            highest.TotalTokens, completed && !usagePoisoned);
        AiProviderResponse Failure(string code, string message) => AiProviderResponse.Failure(code, message) with
        {
            Usage = Evidence(), PromptTokens = (int)Math.Min(int.MaxValue, highest?.InputTokens ?? 0),
            OutputTokens = (int)Math.Min(int.MaxValue, highest?.OutputTokens ?? 0)
        };
        try
        {
            var models = await ListModelsAsync(timeout.Token);
            var model = models.SingleOrDefault(value => string.Equals(value.Id, request.Model, StringComparison.Ordinal));
            if (model is null)
                return Failure("CODEX_MODEL_UNAVAILABLE", $"Codex model '{request.Model}' is not available.");
            if (request.Reasoning != AiReasoningEffort.None && !model.ReasoningEfforts.Contains(request.Reasoning))
                return Failure("CODEX_REASONING_UNSUPPORTED", "The selected Codex model does not support that reasoning effort.");
            session = await factory.CreateAsync(timeout.Token);
            var callbacks = 0;
            AiToolExecutor? executor = request.ToolExecutor is null ? null : async (call, token) =>
            {
                using var callback = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
                callback.Token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref closed) != 0)
                    return AiToolResult.Failure("CODEX_REQUEST_CLOSED", "The Codex request is no longer active.");
                if (Interlocked.Increment(ref callbacks) > request.MaximumToolCalls)
                {
                    Interlocked.Exchange(ref callbackLimitReached, 1);
                    timeout.Cancel();
                    return AiToolResult.Failure("CODEX_TOOL_CALL_LIMIT", "Codex exceeded the configured tool callback limit.");
                }
                return await request.ToolExecutor(call, callback.Token);
            };
            var started = await session.StartTurnAsync(
                null,
                Prompt(request.Messages, request.Kind),
                new(
                    request.Model,
                    request.Reasoning == AiReasoningEffort.None ? "" : FormatEffort(request.Reasoning),
                    request.ResponseSchemaJson,
                    request.Tools,
                    executor),
                timeout.Token);
            var deltas = new StringBuilder();
            string reply = "";
            string terminalStatus = "";
            string terminalError = "";
            long streamedBytes = 0;
            await foreach (var value in session.ReadEventsAsync(timeout.Token))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (value.Type == "usage" && value.Usage is null
                    && (string.IsNullOrEmpty(value.ThreadId) || string.IsNullOrEmpty(value.TurnId)
                        || value.ThreadId == started.ExternalThreadId && value.TurnId == started.ExternalTurnId))
                    usagePoisoned = true;
                switch (value.Type)
                {
                    case "delta":
                        streamedBytes += Encoding.UTF8.GetByteCount(value.Delta);
                        if (streamedBytes > request.MaximumResponseBytes)
                            return Failure("CODEX_RESPONSE_OVERSIZE", "Codex response exceeded the configured byte limit.");
                        deltas.Append(value.Delta);
                        break;
                    case "reply":
                        streamedBytes += Encoding.UTF8.GetByteCount(value.Reply);
                        if (streamedBytes > request.MaximumResponseBytes)
                            return Failure("CODEX_RESPONSE_OVERSIZE", "Codex response exceeded the configured byte limit.");
                        reply = value.Reply;
                        break;
                    case "approval" when value.Approval is not null:
                        await session.RespondApprovalAsync(
                            value.Approval.ExternalRequestId,
                            DantesRoleplay.Assistants.CodexApprovalDecisions.Decline,
                            timeout.Token);
                        break;
                    case "terminal":
                        if (value.ThreadId != started.ExternalThreadId || value.TurnId != started.ExternalTurnId)
                            return Failure("CODEX_TERMINAL_IDENTITY_MISMATCH", "Codex terminal identity does not match this request.");
                        terminalStatus = value.Status;
                        terminalError = value.ErrorMessage;
                        terminal = true;
                        completed = terminalStatus is "completed" or "succeeded";
                        break;
                }
                if (value.Usage is { } usage)
                {
                    if (usage.ThreadId != started.ExternalThreadId || usage.TurnId != started.ExternalTurnId)
                        continue;
                    if (usage.InputTokens < 0 || usage.OutputTokens < 0 || usage.TotalTokens < 0
                        || usage.InputTokens > usage.TotalTokens || usage.OutputTokens > usage.TotalTokens
                        || usage.InputTokens > usage.TotalTokens - usage.OutputTokens
                        || highest is not null && (usage.TotalTokens < highest.TotalTokens || usage.InputTokens < highest.InputTokens || usage.OutputTokens < highest.OutputTokens
                            || usage.TotalTokens == highest.TotalTokens && usage != highest))
                        usagePoisoned = true;
                    else if (highest is null || usage.TotalTokens > highest.TotalTokens) highest = usage;
                }
                if (terminal) break;
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (terminalStatus is not ("completed" or "succeeded"))
                return Failure(
                    "CODEX_TURN_FAILED",
                    string.IsNullOrWhiteSpace(terminalError) ? "The Codex turn did not complete." : terminalError);
            var text = string.IsNullOrWhiteSpace(reply) ? deltas.ToString() : reply;
            if (Encoding.UTF8.GetByteCount(text) > request.MaximumResponseBytes)
                return Failure("CODEX_RESPONSE_OVERSIZE", "Codex response exceeded the configured byte limit.");
            if (string.IsNullOrWhiteSpace(text))
                return Failure("CODEX_RESPONSE_INVALID", "Codex returned no final message.");
            return new(
                true,
                model,
                text,
                string.IsNullOrWhiteSpace(request.ResponseSchemaJson) ? "" : text,
                [],
                PromptTokens: (int)Math.Min(int.MaxValue, highest?.InputTokens ?? 0),
                OutputTokens: (int)Math.Min(int.MaxValue, highest?.OutputTokens ?? 0),
                ConversationId: started.ExternalThreadId, Usage: Evidence());
        }
        catch (OperationCanceledException)
        {
            completed = false;
            return Failure(Volatile.Read(ref callbackLimitReached) != 0 ? "CODEX_TOOL_CALL_LIMIT"
                : cancellationToken.IsCancellationRequested ? "CODEX_TURN_CANCELLED" : "CODEX_TURN_TIMEOUT",
                "The Codex turn stopped at its host execution limit.");
        }
        catch (CodexBridgeException exception)
        {
            completed = false;
            return Failure(exception.Code, exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref closed, 1);
            if (!terminal && session is not null)
            {
                using var interrupt = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await session.InterruptAsync(interrupt.Token).WaitAsync(interrupt.Token); }
                catch (Exception exception) when (exception is OperationCanceledException or CodexBridgeException or IOException or ObjectDisposedException) { }
            }
            if (session is not null) await session.DisposeAsync();
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
