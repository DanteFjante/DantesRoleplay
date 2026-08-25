namespace DantesRoleplay.CodexBridge;

public static class CodexBridgeVersions
{
    public const string CurrentPinnedVersion = "0.149.1";
}

public static class CodexBridgeModels
{
    public const string Luna = "gpt-5.6-luna";
}

public sealed record CodexBridgeOptions(
    string ExecutablePath,
    string RepositoryRoot,
    string PinnedVersion = CodexBridgeVersions.CurrentPinnedVersion,
    int MaximumConcurrentTurns = 2,
    int MaximumLineBytes = 256 * 1024,
    TimeSpan? InitializationTimeout = null,
    TimeSpan? TurnTimeout = null,
    TimeSpan? ApprovalTimeout = null,
    string Model = CodexBridgeModels.Luna)
{
    public TimeSpan EffectiveInitializationTimeout => InitializationTimeout ?? TimeSpan.FromSeconds(10);
    public TimeSpan EffectiveTurnTimeout => TurnTimeout ?? TimeSpan.FromMinutes(10);
    public TimeSpan EffectiveApprovalTimeout => ApprovalTimeout ?? TimeSpan.FromMinutes(2);
}

public sealed record CodexBridgeStatus(
    bool Ready,
    string Provider,
    string PinnedVersion,
    string ObservedVersion,
    string RepositoryRoot,
    string Sandbox,
    bool NetworkAccess,
    string ErrorCode,
    string ErrorMessage,
    string Model = CodexBridgeModels.Luna);

public sealed record CodexTurnStartResult(
    string ExternalThreadId,
    string ExternalTurnId,
    string Model,
    string ModelProvider,
    string Status);

public sealed record CodexProtocolActivity(
    string ExternalItemId,
    string Kind,
    string Status,
    string Summary);

public sealed record CodexProtocolApprovalRequest(
    string ExternalRequestId,
    string ExternalItemId,
    string ExternalApprovalId,
    string Kind,
    string RequestFingerprint,
    string Summary,
    DantesRoleplay.Assistants.CodexApprovalDetails Details,
    bool CanAccept);

public sealed record CodexProtocolEvent(
    string Type,
    string Delta = "",
    string Reply = "",
    CodexProtocolActivity? Activity = null,
    CodexProtocolApprovalRequest? Approval = null,
    string ExternalRequestId = "",
    string Status = "",
    string ErrorCode = "",
    string ErrorMessage = "");

public interface ICodexAppServerSession : IAsyncDisposable
{
    Task<CodexTurnStartResult> StartTurnAsync(
        string? externalThreadId, string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
    Task RespondApprovalAsync(
        string externalRequestId, string decision, CancellationToken cancellationToken = default);
    Task InterruptAsync(CancellationToken cancellationToken = default);
}

public interface ICodexAppServerFactory
{
    Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ICodexAppServerSession> CreateAsync(CancellationToken cancellationToken = default);
}

public sealed record CodexConversationEvent(
    string Type,
    DantesRoleplay.Assistants.AssistantConversationDocument? Conversation = null,
    string Delta = "",
    DantesRoleplay.Assistants.AssistantTurnActivityDocument? Activity = null,
    DantesRoleplay.Assistants.AssistantTurnApprovalDocument? Approval = null);

public sealed record CodexCancelResult(bool Accepted, string ConversationId, string TurnId);

public sealed record CodexApprovalDecisionInput(int ExpectedRevision, string Decision);

public sealed record CodexApprovalResult(
    DantesRoleplay.Assistants.AssistantTurnApprovalDocument Approval,
    DantesRoleplay.Assistants.AssistantConversationDocument Conversation);

public interface ICodexConversationService
{
    Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<CodexConversationEvent> CreateAsync(
        string operatorId,
        DantesRoleplay.Assistants.AssistantConversationCreate request,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<CodexConversationEvent> SendAsync(
        string operatorId,
        string conversationId,
        DantesRoleplay.Assistants.AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default);
    Task<CodexCancelResult> CancelAsync(
        string operatorId, string conversationId, string turnId,
        CancellationToken cancellationToken = default);
    Task<CodexApprovalResult> ApproveAsync(
        string operatorId, string conversationId, string turnId, string approvalId,
        CodexApprovalDecisionInput request, CancellationToken cancellationToken = default);
}

public sealed class CodexBridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
