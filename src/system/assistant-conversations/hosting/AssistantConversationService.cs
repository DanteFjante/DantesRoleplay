using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.Assistants;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.DataAccess;

public sealed partial class AssistantConversationService(
    IAssistantConversationStore store,
    ILocalStructuredCompletionProvider provider) : IAssistantConversationService
{
    public const string LocalProvider = "local";
    public const string TaskClass = "control.assistant.advisory";
    public const int MaximumMessageLength = 8_000;
    public const int MaximumIdempotencyKeyLength = 100;
    private const int MaximumTranscriptCharacters = 20_000;
    private const int MaximumTranscriptMessages = 20;
    private const string SystemPrompt = """
        You are a local, advisory-only assistant for the operator of DantesRoleplay. You have no
        tools and cannot change game state, settings, files, pages, or external systems. Answer the
        visible request concisely. Return only JSON matching the supplied schema. Do not include
        hidden reasoning.
        """;
    private const string ResponseSchema = """
        {"type":"object","additionalProperties":false,"required":["reply"],"properties":{"reply":{"type":"string","minLength":1,"maxLength":8000}}}
        """;

    public async Task<AssistantProviderStatus> GetLocalStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await provider.CheckAsync(cancellationToken);
        return new(status.Ready, status.Identity?.Provider ?? "ollama", status.Identity?.Model ?? "",
            status.Identity?.Revision ?? "", status.Identity?.Profile ?? "", status.ErrorCode, status.ErrorMessage);
    }

    public Task<AssistantConversationDocument> CreateAsync(
        string operatorId, AssistantConversationCreate request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(operatorId, null, request.Provider, null, request.Message, request.IdempotencyKey, cancellationToken);

    public async Task<AssistantConversationDocument> SendAsync(
        string operatorId, string conversationId, AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);
        return await ExecuteAsync(operatorId, conversationId, LocalProvider, request.ExpectedRevision,
            request.Message, request.IdempotencyKey, cancellationToken);
    }

    public Task<AssistantConversationDocument?> GetAsync(
        string operatorId, string conversationId, CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        ValidateConversationId(conversationId);
        return store.GetAsync(operatorId, conversationId, cancellationToken);
    }

    public Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        string operatorId, string providerName, DateTime? beforeUpdatedAtUtc, string? beforeId, int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        if (providerName is not (LocalProvider or "codex")) throw Invalid(
            "ASSISTANT_PROVIDER_INVALID", "The assistant provider must be 'local' or 'codex'.");
        if (limit is < 1 or > 101) throw Invalid("ASSISTANT_LIMIT_INVALID", "The conversation limit is invalid.");
        if (beforeUpdatedAtUtc.HasValue) ValidateConversationId(beforeId ?? "");
        return store.ListAsync(operatorId, providerName, beforeUpdatedAtUtc, beforeId, limit, cancellationToken);
    }

    public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) =>
        store.RecoverInterruptedAsync(cancellationToken);

    private async Task<AssistantConversationDocument> ExecuteAsync(
        string operatorId, string? conversationId, string providerName, int? expectedRevision,
        string message, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateOperator(operatorId);
        if (providerName != LocalProvider) throw Invalid("ASSISTANT_PROVIDER_INVALID", "Only provider 'local' is available.");
        message = NormalizeMessage(message);
        ValidateIdempotencyKey(idempotencyKey);
        if (expectedRevision is < 1) throw Invalid("ASSISTANT_REVISION_INVALID", "expectedRevision must be positive.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerName + "\0" + message)));
        var begin = await store.BeginTurnAsync(new(
            operatorId, providerName, conversationId, expectedRevision, message, idempotencyKey, hash), cancellationToken);
        if (begin.Replay)
            return await store.GetAsync(operatorId, begin.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException("The replayed assistant conversation disappeared.");

        await store.MarkRunningAsync(begin.TurnId, cancellationToken);
        var current = await store.GetAsync(operatorId, begin.ConversationId, cancellationToken)
            ?? throw new InvalidOperationException("The assistant conversation disappeared before provider dispatch.");
        StructuredCompletionResult result;
        try
        {
            result = await provider.CompleteAsync(new(
                TaskClass, SystemPrompt, Transcript(current.Messages), ResponseSchema,
                LocalModelPriority.Interactive), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await store.CompleteTurnAsync(new(
                begin.TurnId, AssistantConversationStatuses.Cancelled, null,
                "ASSISTANT_REQUEST_CANCELLED", "The local assistant request was cancelled.",
                "", "", "", "", 0, 0, 0), CancellationToken.None);
            return (await store.GetAsync(operatorId, begin.ConversationId, CancellationToken.None))!;
        }
        catch (Exception)
        {
            await store.CompleteTurnAsync(new(
                begin.TurnId, AssistantConversationStatuses.Failed, null,
                "ASSISTANT_PROVIDER_FAILURE", "The local assistant provider failed unexpectedly.",
                "", "", "", "", 0, 0, 0), CancellationToken.None);
            return (await store.GetAsync(operatorId, begin.ConversationId, CancellationToken.None))!;
        }

        if (!result.Ok)
        {
            await store.CompleteTurnAsync(new(
                begin.TurnId, AssistantConversationStatuses.Failed, null,
                result.ErrorCode, result.ErrorMessage, "", "", "", "",
                result.ElapsedMilliseconds, result.PromptTokens, result.OutputTokens), CancellationToken.None);
        }
        else if (result.Identity is null)
        {
            await store.CompleteTurnAsync(new(
                begin.TurnId, AssistantConversationStatuses.Failed, null,
                "LOCAL_MODEL_RESPONSE_INVALID", "The local assistant did not report its model identity.",
                "", "", "", "", result.ElapsedMilliseconds, result.PromptTokens, result.OutputTokens),
                CancellationToken.None);
        }
        else
        {
            string? reply = null;
            try
            {
                using var output = JsonDocument.Parse(result.Json);
                reply = output.RootElement.GetProperty("reply").GetString();
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
            if (string.IsNullOrWhiteSpace(reply) || reply.Length > MaximumMessageLength)
            {
                await store.CompleteTurnAsync(new(
                    begin.TurnId, AssistantConversationStatuses.Failed, null,
                    "LOCAL_MODEL_SCHEMA_MISMATCH", "The local assistant reply did not match its fixed response schema.",
                    "", "", "", "", result.ElapsedMilliseconds, result.PromptTokens, result.OutputTokens),
                    CancellationToken.None);
            }
            else
            {
                await store.CompleteTurnAsync(new(
                    begin.TurnId, AssistantConversationStatuses.Completed, reply, "", "",
                    result.Identity.Provider, result.Identity.Model, result.Identity.Revision, result.Identity.Profile,
                    result.ElapsedMilliseconds, result.PromptTokens, result.OutputTokens), CancellationToken.None);
            }
        }
        return (await store.GetAsync(operatorId, begin.ConversationId, CancellationToken.None))!;
    }

    private static string Transcript(IReadOnlyList<AssistantMessageDocument> messages)
    {
        var selected = messages.TakeLast(MaximumTranscriptMessages)
            .Select(message => $"{message.Role}: {message.Content}").ToList();
        while (selected.Count > 1 && selected.Sum(value => value.Length + 1) > MaximumTranscriptCharacters)
            selected.RemoveAt(0);
        var transcript = string.Join('\n', selected);
        return transcript.Length <= MaximumTranscriptCharacters
            ? transcript : transcript[^MaximumTranscriptCharacters..];
    }

    private static string NormalizeMessage(string value)
    {
        if (value is null) throw Invalid("ASSISTANT_MESSAGE_INVALID", "A message is required.");
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length is 0 or > MaximumMessageLength)
            throw Invalid("ASSISTANT_MESSAGE_INVALID", $"The message must contain 1 to {MaximumMessageLength} characters.");
        return normalized;
    }

    private static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdempotencyKeyLength || !IdempotencyPattern().IsMatch(value))
            throw Invalid("ASSISTANT_IDEMPOTENCY_KEY_INVALID", "The idempotency key is invalid.");
    }
    private static void ValidateOperator(string value)
    {
        if (value?.Length != 74 || !value.StartsWith("principal.", StringComparison.Ordinal))
            throw new ArgumentException("The assistant operator identity is invalid.", nameof(value));
    }
    private static void ValidateConversationId(string value)
    {
        if (value?.Length != 45 || !value.StartsWith("conversation.", StringComparison.Ordinal) ||
            value[13..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Invalid("ASSISTANT_CONVERSATION_ID_INVALID", "The conversation ID is invalid.");
    }
    private static AssistantConversationException Invalid(string code, string message) => new(code, message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyPattern();
}
