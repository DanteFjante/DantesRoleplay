namespace DantesRoleplay.Assistants;

public static class AssistantConversationStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string AwaitingApproval = "awaiting-approval";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class AssistantConversation
{
    public required string Id { get; set; }
    public required string OperatorId { get; set; }
    public required string Provider { get; set; }
    public required string Title { get; set; }
    public int Revision { get; set; }
    public required string Status { get; set; }
    public string? ExternalThreadId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<AssistantTurn> Turns { get; } = new List<AssistantTurn>();
    public ICollection<AssistantMessage> Messages { get; } = new List<AssistantMessage>();
    public ICollection<AssistantTurnActivity> Activities { get; } = new List<AssistantTurnActivity>();
    public ICollection<AssistantTurnApproval> Approvals { get; } = new List<AssistantTurnApproval>();
}

public sealed class AssistantTurn
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public required string OperatorId { get; set; }
    public required string Provider { get; set; }
    public int TurnNumber { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Status { get; set; }
    public string? ExternalTurnId { get; set; }
    public string? ExternalStatus { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ModelProvider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ModelRevision { get; set; } = string.Empty;
    public string ModelProfile { get; set; } = string.Empty;
    public long ElapsedMilliseconds { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public AssistantConversation? Conversation { get; set; }
    public ICollection<AssistantMessage> Messages { get; } = new List<AssistantMessage>();
    public ICollection<AssistantTurnActivity> Activities { get; } = new List<AssistantTurnActivity>();
    public ICollection<AssistantTurnApproval> Approvals { get; } = new List<AssistantTurnApproval>();
}

public sealed class AssistantMessage
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public required string TurnId { get; set; }
    public int Ordinal { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AssistantConversation? Conversation { get; set; }
    public AssistantTurn? Turn { get; set; }
}

public sealed class AssistantTurnActivity
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public required string TurnId { get; set; }
    public required string ExternalItemId { get; set; }
    public int Sequence { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }
    public required string Summary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AssistantConversation? Conversation { get; set; }
    public AssistantTurn? Turn { get; set; }
}

public static class CodexApprovalKinds
{
    public const string Command = "command";
    public const string FileChange = "file-change";
    public const string Network = "network";
    public const string Permissions = "permissions";
}

public static class CodexApprovalStatuses
{
    public const string Pending = "pending";
    public const string Decided = "decided";
    public const string Dispatched = "dispatched";
    public const string Resolved = "resolved";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public static class CodexApprovalDecisions
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Cancel = "cancel";
}

public sealed class AssistantTurnApproval
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public required string TurnId { get; set; }
    public required string OperatorId { get; set; }
    public required string ExternalRequestId { get; set; }
    public required string ExternalItemId { get; set; }
    public string? ExternalApprovalId { get; set; }
    public required string Kind { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string Summary { get; set; }
    public required string DetailsJson { get; set; }
    public bool CanAccept { get; set; }
    public required string Status { get; set; }
    public string? Decision { get; set; }
    public int Revision { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public AssistantConversation? Conversation { get; set; }
    public AssistantTurn? Turn { get; set; }
}

public sealed record AssistantConversationSummary(
    string Id, string Provider, string Title, int Revision, string Status,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record AssistantTurnDocument(
    string Id, int TurnNumber, string Status, string? ExternalTurnId, string? ExternalStatus,
    string ErrorCode, string ErrorMessage,
    string ModelProvider, string Model, string ModelRevision, string ModelProfile,
    long ElapsedMilliseconds, int PromptTokens, int OutputTokens,
    DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc);

public sealed record AssistantMessageDocument(
    string Id, string TurnId, int Ordinal, string Role, string Content, DateTime CreatedAtUtc);

public sealed record AssistantTurnActivityDocument(
    string Id, string TurnId, string ExternalItemId, int Sequence, string Kind, string Status,
    string Summary, DateTime CreatedAtUtc);

public sealed record CodexApprovalDetails(
    string Reason,
    string Command,
    string WorkingDirectory,
    IReadOnlyList<string> Paths,
    string NetworkHost,
    string NetworkProtocol,
    IReadOnlyList<string> Permissions);

public sealed record AssistantTurnApprovalDocument(
    string Id,
    string TurnId,
    int Revision,
    string Kind,
    string Status,
    string? Decision,
    string Summary,
    CodexApprovalDetails Details,
    bool CanAccept,
    DateTime RequestedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? DecidedAtUtc,
    DateTime? DispatchedAtUtc,
    DateTime? ResolvedAtUtc);

public sealed record AssistantConversationDocument(
    AssistantConversationSummary Summary,
    string? ExternalThreadId,
    IReadOnlyList<AssistantTurnDocument> Turns,
    IReadOnlyList<AssistantMessageDocument> Messages,
    IReadOnlyList<AssistantTurnActivityDocument> Activities,
    IReadOnlyList<AssistantTurnApprovalDocument> Approvals);

public sealed record AssistantProviderStatus(
    bool Ready, string Provider, string Model, string Revision, string Profile,
    string ErrorCode, string ErrorMessage);

public sealed record AssistantConversationPage(
    IReadOnlyList<AssistantConversationSummary> Items,
    string? NextCursor);

public sealed record AssistantConversationCreate(
    string Provider, string Message, string IdempotencyKey);

public sealed record AssistantConversationTurnCreate(
    int ExpectedRevision, string Message, string IdempotencyKey);

public sealed record AssistantTurnBegin(
    string OperatorId, string Provider, string? ConversationId, int? ExpectedRevision,
    string Message, string IdempotencyKey, string RequestHash);

public sealed record AssistantTurnBeginResult(string ConversationId, string TurnId, bool Replay);

public sealed record AssistantTurnCompletion(
    string TurnId, string Status, string? Reply, string ErrorCode, string ErrorMessage,
    string ModelProvider, string Model, string ModelRevision, string ModelProfile,
    long ElapsedMilliseconds, int PromptTokens, int OutputTokens,
    string? ExternalStatus = null);

public sealed record CodexTurnBinding(
    string TurnId, string ExternalThreadId, string ExternalTurnId, string ExternalStatus);

public sealed record CodexTurnActivityAppend(
    string TurnId, string ExternalItemId, int Sequence, string Kind, string Status, string Summary);

public sealed record CodexApprovalAppend(
    string TurnId,
    string ExternalRequestId,
    string ExternalItemId,
    string? ExternalApprovalId,
    string Kind,
    string RequestFingerprint,
    string Summary,
    CodexApprovalDetails Details,
    bool CanAccept,
    DateTime ExpiresAtUtc);

public sealed record CodexApprovalDecisionRequest(
    string OperatorId,
    string ConversationId,
    string TurnId,
    string ApprovalId,
    int ExpectedRevision,
    string Decision);

public sealed record CodexApprovalDispatch(
    AssistantTurnApprovalDocument Approval,
    string ExternalRequestId,
    string Kind,
    string Decision);

public sealed class AssistantConversationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public interface IAssistantConversationStore
{
    Task<AssistantTurnBeginResult> BeginTurnAsync(AssistantTurnBegin request, CancellationToken cancellationToken = default);
    Task MarkRunningAsync(string turnId, CancellationToken cancellationToken = default);
    Task BindCodexTurnAsync(CodexTurnBinding binding, CancellationToken cancellationToken = default);
    Task<AssistantTurnActivityDocument> AppendCodexActivityAsync(
        CodexTurnActivityAppend activity, CancellationToken cancellationToken = default);
    Task<AssistantTurnApprovalDocument> AppendCodexApprovalAsync(
        CodexApprovalAppend approval, CancellationToken cancellationToken = default);
    Task<CodexApprovalDispatch> DecideCodexApprovalAsync(
        CodexApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task MarkCodexApprovalDispatchedAsync(
        string approvalId, CancellationToken cancellationToken = default);
    Task ResolveCodexApprovalAsync(
        string turnId, string externalRequestId, CancellationToken cancellationToken = default);
    Task<CodexApprovalDispatch?> ExpireCodexApprovalAsync(
        string approvalId, CancellationToken cancellationToken = default);
    Task CloseOpenCodexApprovalsAsync(
        string turnId, string status, CancellationToken cancellationToken = default);
    Task CompleteTurnAsync(AssistantTurnCompletion completion, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
    Task<AssistantConversationDocument?> GetAsync(string operatorId, string conversationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(string operatorId, string provider, DateTime? beforeUpdatedAtUtc, string? beforeId, int limit, CancellationToken cancellationToken = default);
}

public interface IAssistantConversationService
{
    Task<AssistantProviderStatus> GetLocalStatusAsync(CancellationToken cancellationToken = default);
    Task<AssistantConversationDocument> CreateAsync(string operatorId, AssistantConversationCreate request, CancellationToken cancellationToken = default);
    Task<AssistantConversationDocument> SendAsync(string operatorId, string conversationId, AssistantConversationTurnCreate request, CancellationToken cancellationToken = default);
    Task<AssistantConversationDocument?> GetAsync(string operatorId, string conversationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(string operatorId, string provider, DateTime? beforeUpdatedAtUtc, string? beforeId, int limit, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
}
