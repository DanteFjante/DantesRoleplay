using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.SystemConversations;

public sealed partial class SystemConversationService : ISystemConversationService
{
    public const string TaskClass = "control.system.read-chat";
    public const int MaximumMessageLength = 8_000;
    public const int MaximumIdempotencyKeyLength = 100;
    private const int MaximumTranscriptCharacters = 20_000;
    private const int MaximumTranscriptMessages = 20;
    private const string Provider = "local";
    private const string SystemPrompt = """
        You are the read-only system assistant for the private operator of DantesRoleplay. You have
        no tools and cannot change applications, game state, settings, files, pages, the database,
        or external systems. Answer only from the supplied SYSTEM CONTEXT and visible transcript.
        Never infer application ECS state or private catalog/source content. For an application-
        scoped question, use needs-application and direct the operator to that application's chat.
        For a write or action request, use unsupported. When evidence is insufficient, say so.
        Evidence entries must be copied verbatim as complete strings from the SYSTEM CONTEXT
        evidenceReferences array; never construct or alter a reference. If disposition is answered,
        evidence must contain at least one of those exact supplied strings. If no supplied reference
        supports the reply, use unknown with empty evidence. Return only JSON matching the supplied
        schema. Do not include hidden reasoning.
        """;
    private const string ResponseSchema = """
        {
          "$schema":"https://json-schema.org/draft/2020-12/schema",
          "type":"object",
          "additionalProperties":false,
          "required":["disposition","reply","evidence"],
          "properties":{
            "disposition":{"enum":["answered","unknown","unsupported","needs-input","needs-application","unavailable"]},
            "reply":{"type":"string","minLength":1,"maxLength":8000},
            "evidence":{"type":"array","maxItems":24,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":320}}
          }
        }
        """;

    private readonly IAssistantConversationStore _store;
    private readonly ILocalStructuredCompletionProvider _provider;
    private readonly ISystemConversationContextMaterializer _context;
    private readonly IPrivateOperatorAuthorizationPolicy _authorization;
    private readonly IBoundedJsonSchemaValidator _schemas;
    private readonly string _normalizedResponseSchema;

    public SystemConversationService(
        IAssistantConversationStore store,
        ILocalStructuredCompletionProvider provider,
        ISystemConversationContextMaterializer context,
        IPrivateOperatorAuthorizationPolicy authorization,
        IBoundedJsonSchemaValidator schemas)
    {
        _store = store;
        _provider = provider;
        _context = context;
        _authorization = authorization;
        _schemas = schemas;
        var compiled = schemas.Compile(ResponseSchema);
        if (!compiled.IsAccepted)
            throw new InvalidOperationException("The system conversation response schema is invalid.");
        _normalizedResponseSchema = compiled.NormalizedSchema;
    }

    public async Task<AssistantProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _provider.CheckAsync(cancellationToken);
        return new(status.Ready, status.Identity?.Provider ?? "ollama", status.Identity?.Model ?? "",
            status.Identity?.Revision ?? "", status.Identity?.Profile ?? "",
            status.ErrorCode, status.ErrorMessage);
    }

    public Task<AssistantConversationDocument> CreateAsync(
        SystemConversationRequestContext context,
        SystemConversationCreate request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(context, null, null, request.Message, request.IdempotencyKey, cancellationToken);

    public Task<AssistantConversationDocument> SendAsync(
        SystemConversationRequestContext context,
        string conversationId,
        AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);
        return ExecuteAsync(context, conversationId, request.ExpectedRevision,
            request.Message, request.IdempotencyKey, cancellationToken);
    }

    public async Task<AssistantConversationDocument?> GetAsync(
        SystemConversationRequestContext context,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        Authorize(context, PrivateOperatorCapability.ControlRead);
        ValidateConversationId(conversationId);
        return await _store.GetAsync(
            context.Principal.PrincipalId, conversationId, cancellationToken,
            AssistantConversationScopes.System);
    }

    public async Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        SystemConversationRequestContext context,
        DateTime? beforeUpdatedAtUtc,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        Authorize(context, PrivateOperatorCapability.ControlRead);
        if (limit is < 1 or > 101) throw Error(
            "SYSTEM_CHAT_LIMIT_INVALID", "The conversation limit is invalid.");
        if (beforeUpdatedAtUtc.HasValue) ValidateConversationId(beforeId ?? string.Empty);
        return await _store.ListAsync(
            context.Principal.PrincipalId, Provider, beforeUpdatedAtUtc, beforeId, limit,
            cancellationToken, AssistantConversationScopes.System);
    }

    private async Task<AssistantConversationDocument> ExecuteAsync(
        SystemConversationRequestContext context,
        string? conversationId,
        int? expectedRevision,
        string message,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Authorize(context, PrivateOperatorCapability.ControlAiMessage);
        message = NormalizeMessage(message);
        ValidateIdempotencyKey(idempotencyKey);
        if (expectedRevision is < 1)
            throw Error("ASSISTANT_REVISION_INVALID", "expectedRevision must be positive.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AssistantConversationScopes.System + "\0" + Provider + "\0" + message)));
        var begin = await _store.BeginTurnAsync(new(
            context.Principal.PrincipalId, Provider, conversationId, expectedRevision,
            message, idempotencyKey, hash, AssistantConversationScopes.System), cancellationToken);
        if (begin.Replay)
            return await ExactAsync(context, begin.ConversationId, cancellationToken);

        var current = await ExactAsync(context, begin.ConversationId, cancellationToken);
        SystemConversationContextSnapshot snapshot;
        try
        {
            snapshot = await _context.MaterializeAsync(message, context, cancellationToken);
            await _store.MarkRunningAsync(begin.TurnId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Cancelled,
                "ASSISTANT_REQUEST_CANCELLED", "The system assistant request was cancelled.");
            return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
        }
        catch (SystemConversationException exception)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                exception.Code, exception.Message);
            return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
        }
        catch (Exception)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                "SYSTEM_CHAT_CONTEXT_UNAVAILABLE", "Authorized system context is unavailable.");
            return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
        }

        StructuredCompletionResult result;
        try
        {
            result = await _provider.CompleteAsync(new(
                TaskClass,
                SystemPrompt,
                Prompt(snapshot.Json, current.Messages),
                _normalizedResponseSchema,
                LocalModelPriority.Interactive), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Cancelled,
                "ASSISTANT_REQUEST_CANCELLED", "The system assistant request was cancelled.");
            return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
        }
        catch (Exception)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                "ASSISTANT_PROVIDER_FAILURE", "The local system assistant failed unexpectedly.");
            return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
        }

        if (!result.Ok)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                SafeCode(result.ErrorCode), SafeMessage(result.ErrorMessage), result);
        }
        else if (result.Identity is null)
        {
            await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                "LOCAL_MODEL_RESPONSE_INVALID", "The local system assistant did not report its model identity.", result);
        }
        else
        {
            var parsed = Parse(result.Json, snapshot.SourceReferences);
            if (!parsed.Ok)
            {
                await CompleteFailureAsync(begin.TurnId, AssistantConversationStatuses.Failed,
                    parsed.Code, parsed.Message, result);
            }
            else
            {
                await _store.CompleteTurnAsync(new(
                    begin.TurnId, AssistantConversationStatuses.Completed, parsed.Reply, "", "",
                    result.Identity.Provider, result.Identity.Model, result.Identity.Revision,
                    result.Identity.Profile, result.ElapsedMilliseconds, result.PromptTokens,
                    result.OutputTokens,
                    Context: new(
                        snapshot.Profile, snapshot.Fingerprint, parsed.Evidence, parsed.Disposition)),
                    CancellationToken.None);
            }
        }
        return await ExactAsync(context, begin.ConversationId, CancellationToken.None);
    }

    private async Task<AssistantConversationDocument> ExactAsync(
        SystemConversationRequestContext context,
        string conversationId,
        CancellationToken cancellationToken) =>
        await _store.GetAsync(context.Principal.PrincipalId, conversationId, cancellationToken,
            AssistantConversationScopes.System)
        ?? throw Error("ASSISTANT_CONVERSATION_UNKNOWN", "The system conversation was not found.");

    private async Task CompleteFailureAsync(
        string turnId,
        string status,
        string code,
        string message,
        StructuredCompletionResult? result = null) =>
        await _store.CompleteTurnAsync(new(
            turnId, status, null, SafeCode(code), SafeMessage(message),
            result?.Identity?.Provider ?? "", result?.Identity?.Model ?? "",
            result?.Identity?.Revision ?? "", result?.Identity?.Profile ?? "",
            result?.ElapsedMilliseconds ?? 0, result?.PromptTokens ?? 0, result?.OutputTokens ?? 0),
            CancellationToken.None);

    private Response Parse(string json, IReadOnlyList<string> allowedReferences)
    {
        var validation = _schemas.Validate(_normalizedResponseSchema, json);
        if (validation.Status != SchemaValueStatus.Valid)
            return Response.Failure("SYSTEM_CHAT_RESPONSE_INVALID",
                "The local system assistant response did not match its closed schema.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var disposition = root.GetProperty("disposition").GetString()!;
            var reply = root.GetProperty("reply").GetString()!;
            var evidence = root.GetProperty("evidence").EnumerateArray()
                .Select(value => value.GetString()!).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var allowed = allowedReferences.ToHashSet(StringComparer.Ordinal);
            if (evidence.Any(value => !allowed.Contains(value)))
                return Response.Failure("SYSTEM_CHAT_EVIDENCE_INVALID",
                    "The local system assistant cited evidence outside its supplied context.");
            if (disposition == AssistantTurnResponseDispositions.Answered && evidence.Length == 0)
                return Response.Failure("SYSTEM_CHAT_EVIDENCE_INVALID",
                    "An answered system response requires supplied evidence.");
            return Response.Success(disposition, reply, Array.AsReadOnly(evidence));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Response.Failure("SYSTEM_CHAT_RESPONSE_INVALID",
                "The local system assistant response was invalid.");
        }
    }

    private void Authorize(SystemConversationRequestContext context, PrivateOperatorCapability capability)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = _authorization.Evaluate(new(
            context.Principal, capability, context.Scope, context.CorrelationId));
        if (!decision.Allowed)
            throw Error(decision.Code, "Private-operator authorization is required for system chat.");
    }

    private static string Prompt(string contextJson, IReadOnlyList<AssistantMessageDocument> messages) =>
        "SYSTEM CONTEXT\n" + contextJson + "\n\nVISIBLE TRANSCRIPT\n" + Transcript(messages);

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
        if (value is null) throw Error("ASSISTANT_MESSAGE_INVALID", "A message is required.");
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length is 0 or > MaximumMessageLength)
            throw Error("ASSISTANT_MESSAGE_INVALID",
                $"The message must contain 1 to {MaximumMessageLength} characters.");
        return normalized;
    }

    private static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdempotencyKeyLength ||
            !IdempotencyPattern().IsMatch(value))
            throw Error("ASSISTANT_IDEMPOTENCY_KEY_INVALID", "The idempotency key is invalid.");
    }

    private static void ValidateConversationId(string value)
    {
        if (value?.Length != 45 || !value.StartsWith("conversation.", StringComparison.Ordinal) ||
            value[13..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Error("ASSISTANT_CONVERSATION_ID_INVALID", "The conversation ID is invalid.");
    }

    private static string SafeCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_')
            ? value
            : "SYSTEM_CHAT_UNAVAILABLE";

    private static string SafeMessage(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl)
            ? "The local system assistant is unavailable."
            : value;

    private static SystemConversationException Error(string code, string message) => new(code, message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyPattern();

    private sealed record Response(
        bool Ok, string Disposition, string Reply, IReadOnlyList<string> Evidence,
        string Code, string Message)
    {
        public static Response Success(
            string disposition, string reply, IReadOnlyList<string> evidence) =>
            new(true, disposition, reply, evidence, "", "");
        public static Response Failure(string code, string message) =>
            new(false, "", "", [], code, message);
    }
}
