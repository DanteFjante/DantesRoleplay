using DantesRoleplay.SystemTasks;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Play;

public static class ConversationMemoryRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public static bool IsKnown(string? value) => value is User or Assistant;
}

public static class ConversationMemoryStatuses
{
    public const string Connected = "connected";
    public const string RetryPending = "retry-pending";
    public const string Disconnected = "disconnected";
    public const string Archived = "archived";
    public const string Deleted = "deleted";
    public static bool IsKnown(string? value) => value is Connected or RetryPending or Disconnected or Archived or Deleted;
}

public static class ConversationMemoryMessageKinds
{
    public const string UserPrompt = "user-prompt";
    public const string AssistantCommentary = "assistant-commentary";
    public const string AssistantFinal = "assistant-final";
    public static bool IsKnown(string? value) => value is UserPrompt or AssistantCommentary or AssistantFinal;
}

public sealed record ConversationMemoryScope(
    string PrincipalId,
    string ApplicationId,
    string StateSpaceId,
    string SessionContextId);

public sealed record ConversationMemoryBinding(
    ConversationMemoryScope Scope,
    string SourceClient,
    string SourceProjectId,
    string RepositoryRoot,
    string SourceThreadId);

/// <summary>
/// Immutable host-owned binding for one explicitly linked capture session. Transport fields are
/// matched against this value; they never select the owner, gameplay session, repository, or thread.
/// </summary>
public interface IConversationMemoryHostBinding
{
    ConversationMemoryBinding Binding { get; }
}

public sealed class ConversationMemoryHostBinding(ConversationMemoryBinding binding) : IConversationMemoryHostBinding
{
    public ConversationMemoryBinding Binding { get; } = binding ?? throw new ArgumentNullException(nameof(binding));
}

public sealed record ConversationMemoryCapturedMessage(
    string SourceMessageId,
    string Role,
    string SourceKind,
    string Text,
    DateTime SourceAtUtc);

public sealed record ConversationMemoryTurnAppend(
    ConversationMemoryBinding Binding,
    string SourceTurnId,
    IReadOnlyList<ConversationMemoryCapturedMessage> Messages,
    string CaptureProvenance,
    DateTime CapturedAtUtc,
    string RequestToken);

public sealed record ConversationMemoryMessageDocument(
    string Id,
    int Ordinal,
    string SourceTurnId,
    string SourceMessageId,
    string Role,
    string SourceKind,
    string? Text,
    string TextFingerprint,
    string CaptureProvenance,
    DateTime SourceAtUtc,
    DateTime CapturedAtUtc,
    string Status);

public sealed record ConversationMemoryJournalDocument(
    string Id,
    ConversationMemoryBinding Binding,
    string Status,
    int Revision,
    int TotalMessageCount,
    string? LastSourceTurnId,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ConversationMemoryAppendResult(
    ConversationMemoryJournalDocument Journal,
    IReadOnlyList<ConversationMemoryMessageDocument> Messages,
    string ReceiptId,
    bool Replayed);

public sealed record ConversationMemoryMessagePage(
    IReadOnlyList<ConversationMemoryMessageDocument> Messages,
    int? NextBeforeOrdinal,
    int JournalRevision);

public sealed record ConversationMemoryDeleteResult(
    bool Deleted,
    int TombstonedMessages,
    IReadOnlyList<string> RetainedDerivedReferences);

public sealed record ConversationMemoryDerivedCandidateAppend(
    ConversationMemoryScope Scope,
    SystemTaskDurableHandle Task,
    int SourceRevision,
    IReadOnlyList<string> SourceMessageIds,
    string CandidateJson,
    string ResultSchemaFingerprint,
    string CompletionEvidenceReference,
    DateTime CreatedAtUtc);

public sealed record ConversationMemoryDerivedCandidateDocument(
    string Id,
    SystemTaskDurableHandle Task,
    int SourceRevision,
    IReadOnlyList<string> SourceMessageIds,
    string SourceFingerprint,
    string CandidateJson,
    string ResultSchemaFingerprint,
    string CompletionEvidenceReference,
    string Audience,
    string Status,
    DateTime CreatedAtUtc);

public sealed record ConversationMemoryDreamRecordRequest(
    ConversationMemoryScope Scope,
    SystemTaskDurableHandle Task,
    int SourceRevision,
    IReadOnlyList<string> SourceMessageIds,
    string RequestToken);

public sealed class ConversationMemoryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IConversationMemoryStore
{
    ConversationMemoryJournalDocument Connect(ConversationMemoryBinding binding);
    ConversationMemoryJournalDocument? GetState(ConversationMemoryScope scope, bool includeArchived = false);
    ConversationMemoryAppendResult AppendTurn(ConversationMemoryTurnAppend append);
    ConversationMemoryMessagePage GetMessages(ConversationMemoryScope scope, int? beforeOrdinal, int limit,
        bool includeArchived = false);
    IReadOnlyList<ConversationMemoryMessageDocument> GetSourceMessages(
        ConversationMemoryScope scope, int sourceRevision, IReadOnlyList<string> sourceMessageIds);
    ConversationMemoryJournalDocument MarkRetryPending(ConversationMemoryScope scope, string failureCode);
    ConversationMemoryJournalDocument Retry(ConversationMemoryScope scope);
    ConversationMemoryJournalDocument Disconnect(ConversationMemoryScope scope);
    ConversationMemoryJournalDocument Archive(ConversationMemoryScope scope);
    ConversationMemoryDeleteResult Delete(ConversationMemoryScope scope, string? messageId = null);
    ConversationMemoryDerivedCandidateDocument AppendDerivedCandidate(ConversationMemoryDerivedCandidateAppend append);
    IReadOnlyList<ConversationMemoryDerivedCandidateDocument> GetDerivedCandidates(
        ConversationMemoryScope scope, int limit = 20, bool includeArchived = false);
}

public interface IConversationMemoryDreamRecorder
{
    Task<ConversationMemoryDerivedCandidateDocument> RetainCompletedAsync(
        TrustedPrincipalContext principal,
        ConversationMemoryDreamRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class ApplicationConversationMemoryJournalRecord
{
    public required string Id { get; set; }
    public required string PrincipalId { get; set; }
    public required string ApplicationId { get; set; }
    public required string StateSpaceId { get; set; }
    public required string SessionContextId { get; set; }
    public required string SourceClient { get; set; }
    public required string SourceProjectId { get; set; }
    public required string RepositoryRoot { get; set; }
    public required string SourceThreadId { get; set; }
    public required string Status { get; set; }
    public int Revision { get; set; }
    public int NextOrdinal { get; set; }
    public string? LastSourceTurnId { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ApplicationConversationMemoryMessageRecord> Messages { get; } = [];
    public ICollection<ApplicationConversationMemoryDeliveryRecord> Deliveries { get; } = [];
    public ICollection<ApplicationConversationMemoryDerivedRecord> DerivedCandidates { get; } = [];
}

public sealed class ApplicationConversationMemoryMessageRecord
{
    public required string Id { get; set; }
    public required string JournalId { get; set; }
    public int Ordinal { get; set; }
    public required string SourceTurnId { get; set; }
    public required string SourceMessageId { get; set; }
    public required string Role { get; set; }
    public required string SourceKind { get; set; }
    public required string Text { get; set; }
    public required string TextFingerprint { get; set; }
    public required string CaptureProvenance { get; set; }
    public DateTime SourceAtUtc { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public required string Status { get; set; }
    public ApplicationConversationMemoryJournalRecord? Journal { get; set; }
    public ICollection<ApplicationConversationMemoryDerivedSourceRecord> DerivedSources { get; } = [];
}

public sealed class ApplicationConversationMemoryDeliveryRecord
{
    public required string Id { get; set; }
    public required string JournalId { get; set; }
    public required string RequestToken { get; set; }
    public required string SourceTurnId { get; set; }
    public required string PayloadFingerprint { get; set; }
    public required string MessageIdsJson { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public ApplicationConversationMemoryJournalRecord? Journal { get; set; }
}

public sealed class ApplicationConversationMemoryDerivedRecord
{
    public required string Id { get; set; }
    public required string JournalId { get; set; }
    public required string TaskId { get; set; }
    public required string CommandId { get; set; }
    public int SourceRevision { get; set; }
    public required string SourceFingerprint { get; set; }
    public required string CandidateJson { get; set; }
    public required string ResultSchemaFingerprint { get; set; }
    public required string CompletionEvidenceReference { get; set; }
    public required string Audience { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationConversationMemoryJournalRecord? Journal { get; set; }
    public ICollection<ApplicationConversationMemoryDerivedSourceRecord> Sources { get; } = [];
}

public sealed class ApplicationConversationMemoryDerivedSourceRecord
{
    public required string DerivedId { get; set; }
    public required string MessageId { get; set; }
    public ApplicationConversationMemoryDerivedRecord? Derived { get; set; }
    public ApplicationConversationMemoryMessageRecord? Message { get; set; }
}
