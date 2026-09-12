using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.Play;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemConversationMemoryHandler
{
    public Task<ToolEnvelope> QueryAsync(
        IConversationMemoryStore? memory,
        IConversationMemoryHostBinding? hostBinding,
        ILocalKnowledgeSeatProvider? seats,
        IStateSpaceRegistry? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string? stateSpaceId,
        string? request,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(log, "query", () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = authorization?.Authorize(PrivateOperatorCapability.Read);
        if (decision is null || !decision.Allowed)
            return Task.FromResult(Denied(decision, "system.conversation-memory"));
        if (memory is null || hostBinding is null || seats is null || stateSpaces is null)
            return Task.FromResult(Unavailable(decision));
        try
        {
            using var document = JsonDocument.Parse(request ?? "{}");
            var root = document.RootElement;
            RequireClosed(root, ["operation", "sessionContextId", "beforeOrdinal", "limit", "includeArchived"]);
            var operation = RequiredString(root, "operation");
            var scope = ResolveScope(hostBinding, seats, stateSpaces, applicationId, stateSpaceId,
                RequiredString(root, "sessionContextId"));
            var includeArchived = root.TryGetProperty("includeArchived", out var archived)
                && archived.ValueKind == JsonValueKind.True;
            object data = operation switch
            {
                "state" => memory.GetState(scope, includeArchived)
                    ?? throw new ConversationMemoryException("CONVERSATION_MEMORY_NOT_FOUND",
                        "The scoped conversation memory was not found."),
                "messages" => memory.GetMessages(scope, OptionalInt(root, "beforeOrdinal"),
                    OptionalInt(root, "limit") ?? 32, includeArchived),
                "derived" => memory.GetDerivedCandidates(scope, OptionalInt(root, "limit") ?? 20,
                    includeArchived),
                _ => throw new ConversationMemoryException("CONVERSATION_MEMORY_OPERATION_INVALID",
                    "The query operation must be state, messages, or derived.")
            };
            return Task.FromResult(new ToolOutcome(data,
                $"Returned scoped conversation memory {operation}.",
                ["query(kind: \"system.conversation-memory\", applicationId: \"...\", stateSpaceId: \"...\", request: \"{...}\")"],
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence)));
        }
        catch (Exception error) when (error is JsonException or ConversationMemoryException
            or ArgumentException or InvalidOperationException)
        {
            var code = error is ConversationMemoryException typed ? typed.Code : "CONVERSATION_MEMORY_REQUEST_INVALID";
            return Task.FromResult(Failure(decision, code, error.Message));
        }
    });

    public Task<ToolEnvelope> CommitAsync(
        IConversationMemoryStore? memory,
        IConversationMemoryDreamRecorder? dreams,
        IConversationMemoryHostBinding? hostBinding,
        ILocalKnowledgeSeatProvider? seats,
        IStateSpaceRegistry? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(
        log, "commit", intent, "commit:system.conversation-memory", proceduresUsed, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = authorization?.Authorize(PrivateOperatorCapability.Modify);
            if (decision is null || !decision.Allowed)
                return Denied(decision, "system.conversation-memory");
            if (memory is null || hostBinding is null || seats is null || stateSpaces is null)
                return Unavailable(decision);
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                RequireClosed(root,
                [
                    "operation", "applicationId", "stateSpaceId", "sessionContextId", "sourceProjectId",
                    "repositoryRoot", "sourceThreadId", "sourceTurnId", "messages", "captureProvenance",
                    "capturedAtUtc", "requestToken", "failureCode", "messageId", "taskId", "commandId",
                    "sourceRevision", "sourceMessageIds"
                ]);
                var operation = RequiredString(root, "operation");
                _ = RequiredString(root, "requestToken");
                var requestedBinding = new ConversationMemoryBinding(
                    ResolveScope(hostBinding, seats, stateSpaces, RequiredString(root, "applicationId"),
                        RequiredString(root, "stateSpaceId"), RequiredString(root, "sessionContextId")),
                    "codex", RequiredString(root, "sourceProjectId"), RequiredString(root, "repositoryRoot"),
                    RequiredString(root, "sourceThreadId"));
                var binding = ResolveBinding(hostBinding, requestedBinding);
                var scope = binding.Scope;
                object result = operation switch
                {
                    "connect" => memory.Connect(binding),
                    "append" => memory.AppendTurn(Append(binding, root)),
                    "retry" => memory.Retry(scope),
                    "disconnect" => memory.Disconnect(scope),
                    "archive" => memory.Archive(scope),
                    "delete" => memory.Delete(scope, OptionalString(root, "messageId")),
                    "derive" when dreams is not null => await dreams.RetainCompletedAsync(
                        TrustedPrincipalContext.VerifiedPrincipal(scope.PrincipalId, "private-operator"),
                        new(scope, new(RequiredString(root, "taskId"), RequiredString(root, "commandId")),
                            RequiredInt(root, "sourceRevision"), RequiredStrings(root, "sourceMessageIds"),
                            RequiredString(root, "requestToken")),
                        RequiredString(root, "requestToken"), cancellationToken),
                    "derive" => throw new ConversationMemoryException("CONVERSATION_MEMORY_DREAM_UNAVAILABLE",
                        "The focused conversation-dream recorder is unavailable."),
                    _ => throw new ConversationMemoryException("CONVERSATION_MEMORY_OPERATION_INVALID",
                        "The commit operation must be connect, append, retry, disconnect, archive, delete, or derive.")
                };
                return new ToolOutcome(result,
                    $"Applied scoped conversation memory {operation}.",
                    ["query(kind: \"system.conversation-memory\", applicationId: \"...\", stateSpaceId: \"...\", request: \"{...}\")"],
                    GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            }
            catch (Exception error) when (error is JsonException or ConversationMemoryException
                or ArgumentException or InvalidOperationException)
            {
                var code = error is ConversationMemoryException typed ? typed.Code : "CONVERSATION_MEMORY_REQUEST_INVALID";
                return Failure(decision, code, error.Message);
            }
        }, consumesReadEvidence: false);

    private static ConversationMemoryTurnAppend Append(ConversationMemoryBinding binding, JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new ConversationMemoryException("CONVERSATION_MEMORY_MESSAGES_INVALID", "messages must be an array.");
        var captured = messages.EnumerateArray().Select(value =>
        {
            RequireClosed(value, ["sourceMessageId", "role", "sourceKind", "text", "sourceAtUtc"]);
            return new ConversationMemoryCapturedMessage(
                RequiredString(value, "sourceMessageId"), RequiredString(value, "role"),
                RequiredString(value, "sourceKind"), RequiredString(value, "text"),
                RequiredUtc(value, "sourceAtUtc"));
        }).ToArray();
        return new(binding, RequiredString(root, "sourceTurnId"), captured,
            RequiredString(root, "captureProvenance"), RequiredUtc(root, "capturedAtUtc"),
            RequiredString(root, "requestToken"));
    }

    private static ConversationMemoryScope ResolveScope(
        IConversationMemoryHostBinding hostBinding,
        ILocalKnowledgeSeatProvider seats,
        IStateSpaceRegistry stateSpaces,
        string? requestedApplication,
        string? requestedState,
        string sessionContextId)
    {
        var seat = seats.Current();
        if (!seat.Enabled || string.IsNullOrWhiteSpace(seat.PrincipalId)
            || seat.ApplicationId != requestedApplication || string.IsNullOrWhiteSpace(requestedState)
            || string.IsNullOrWhiteSpace(sessionContextId))
            throw new ConversationMemoryException("CONVERSATION_MEMORY_SCOPE_DENIED",
                "The current host identity does not authorize that application conversation scope.");
        var state = stateSpaces.Get(requestedState);
        if (state is null || state.ApplicationRevision.ApplicationId.Value != seat.ApplicationId)
            throw new ConversationMemoryException("CONVERSATION_MEMORY_SCOPE_DENIED",
                "The current host identity does not authorize that application state scope.");
        var scope = new ConversationMemoryScope(seat.PrincipalId, seat.ApplicationId, requestedState, sessionContextId);
        if (hostBinding.Binding.Scope != scope)
            throw new ConversationMemoryException("CONVERSATION_MEMORY_SCOPE_DENIED",
                "The current capture host is not bound to that gameplay session.");
        return scope;
    }

    private static ConversationMemoryBinding ResolveBinding(
        IConversationMemoryHostBinding hostBinding,
        ConversationMemoryBinding requested)
    {
        var expected = hostBinding.Binding;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (expected.Scope != requested.Scope || expected.SourceClient != requested.SourceClient
            || expected.SourceProjectId != requested.SourceProjectId
            || expected.SourceThreadId != requested.SourceThreadId
            || !string.Equals(Path.GetFullPath(expected.RepositoryRoot), Path.GetFullPath(requested.RepositoryRoot), pathComparison))
            throw new ConversationMemoryException("CONVERSATION_MEMORY_SCOPE_DENIED",
                "The current capture host is not bound to that source project, repository, and thread.");
        return expected;
    }

    private static void RequireClosed(JsonElement root, IReadOnlyCollection<string> allowed)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(value => !allowed.Contains(value.Name)))
            throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID",
                "The conversation memory request has an unknown field or is not an object.");
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID",
                    $"{name} is required.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString()
                : throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} is invalid.")
            : null;

    private static int? OptionalInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
                : throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} is invalid.")
            : null;

    private static int RequiredInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} is invalid.");

    private static string[] RequiredStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} is invalid.");
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString()!
            : throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} is invalid."))
            .ToArray();
    }

    private static DateTime RequiredUtc(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTime(out var result) || result.Kind != DateTimeKind.Utc)
            throw new ConversationMemoryException("CONVERSATION_MEMORY_REQUEST_INVALID", $"{name} must be UTC.");
        return result;
    }

    private static ToolOutcome Denied(PrivateOperatorAuthorizationDecision? decision, string subject) => new(
        null, $"Denied access to {subject}.", ["orient()"],
        new(decision?.Code ?? "AUTHORIZATION_DENIED", "Private-operator authorization is required.", "orient()"),
        GuardEvidenceJson: decision is null ? string.Empty : JsonSerializer.Serialize(decision.Evidence));

    private static ToolOutcome Unavailable(PrivateOperatorAuthorizationDecision decision) => new(
        null, "Conversation memory is unavailable.", ["query(kind: \"capabilities\")"],
        new("CONVERSATION_MEMORY_UNAVAILABLE", "Conversation memory is not configured.",
            "query(kind: \"capabilities\")"), GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static ToolOutcome Failure(PrivateOperatorAuthorizationDecision decision, string code, string message) => new(
        null, "Rejected scoped conversation memory request.",
        ["query(kind: \"system.conversation-memory\", applicationId: \"...\", stateSpaceId: \"...\", request: \"{...}\")"],
        new(code, message, "query(kind: \"capabilities\")"), GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
}
