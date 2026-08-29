using System.Text.Json;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.SystemTasks;

public static class SystemTaskOperations
{
    public const string Resolve = "resolve";
    public const string Submit = "submit";
    public static bool IsKnown(string? value) => value is Resolve or Submit;
}

public static class SystemTaskStatuses
{
    public const string Planning = "planning";
    public const string Prepared = "prepared";
    public const string Completed = "completed";
    public const string NeedsInput = "needs-input";
    public const string Unknown = "unknown";
    public const string Unsupported = "unsupported";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";

    public static bool IsTerminal(string? value) => value is Prepared or Completed or NeedsInput or
        Unknown or Unsupported or Unavailable or Failed;
}

public static class SystemTaskExecutionStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Stale = "stale";
    public const string Unauthorized = "unauthorized";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed-out";
    public const string Indeterminate = "indeterminate";

    public static bool IsTerminal(string? value) => value is Succeeded or Partial or Failed or Stale or
        Unauthorized or Cancelled or TimedOut or Indeterminate;
}

public static class SystemTaskStepStatuses
{
    public const string Running = "running";
    public const string Read = "read";
    public const string Prepared = "prepared";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Stale = "stale";
    public const string Unauthorized = "unauthorized";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed-out";
    public const string Indeterminate = "indeterminate";
    public const string Skipped = "skipped";
}

public sealed record SystemTaskAgendaItem(string CapabilityId, JsonElement Input);

public sealed record SystemTaskPrepareRequest(
    string Operation,
    string Intent,
    IReadOnlyList<SystemTaskAgendaItem>? Agenda,
    string IdempotencyKey);

public sealed record SystemTaskConfirmationRequest(
    string PlanFingerprint,
    string IdempotencyKey);

public sealed record SystemTaskExecutionRequest(
    string ConfirmationId,
    string PlanFingerprint,
    string IdempotencyKey);

public sealed record SystemTaskRequestContext(
    TrustedPrincipalContext Principal,
    string Scope,
    string CorrelationId)
{
    public static SystemTaskRequestContext FromAuthorization(AuthorizationAuditEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var principal = evidence.Allowed &&
            TrustedPrincipalContext.IsValidPrincipalId(evidence.PrincipalReference) &&
            Bounded(evidence.AuthenticationMethod, 64)
                ? TrustedPrincipalContext.VerifiedPrincipal(
                    evidence.PrincipalReference, evidence.AuthenticationMethod)
                : TrustedPrincipalContext.Unauthenticated(
                    Bounded(evidence.ReasonCode, 80) ? evidence.ReasonCode : "PRIVATE_OPERATOR_UNAUTHENTICATED");
        return new(principal,
            Bounded(evidence.Scope, 80) ? evidence.Scope : "invalid",
            Bounded(evidence.CorrelationId, 128) ? evidence.CorrelationId : "invalid");
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}

public sealed record SystemTaskStepDocument(
    string StepId,
    int Ordinal,
    string CapabilityId,
    int CapabilityVersion,
    string DescriptorFingerprint,
    string Owner,
    string Mode,
    JsonElement Input,
    string InputFingerprint,
    string PreflightStatus,
    string PreconditionFingerprint,
    string SafeSummary,
    IReadOnlyList<string> AffectedReferences,
    IReadOnlyList<string> DeferredStepIds,
    JsonElement? Result,
    string ResultFingerprint);

public sealed record SystemTaskRoundDocument(
    int Ordinal,
    string Disposition,
    string Summary,
    string ContextFingerprint,
    string ResponseFingerprint,
    string ModelProvider,
    string Model,
    string ModelRevision,
    string ModelProfile,
    IReadOnlyList<string> Evidence,
    DateTime CreatedAtUtc);

public sealed record SystemTaskConfirmationDocument(
    string Id,
    string PlanFingerprint,
    DateTime ConfirmedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record SystemTaskExecutionStepDocument(
    string StepId,
    int Ordinal,
    string Status,
    string OperationId,
    JsonElement? Output,
    string OutputFingerprint,
    string ReadBackFingerprint,
    string ErrorCode,
    string ErrorMessage,
    DateTime? CompletedAtUtc);

public sealed record SystemTaskExecutionDocument(
    string Id,
    string ConfirmationId,
    string Status,
    string PlanFingerprint,
    string SafeSummary,
    string ErrorCode,
    string ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<SystemTaskExecutionStepDocument> Steps);

public sealed record SystemTaskSummary(
    string Id,
    string ConversationId,
    string Operation,
    string Intent,
    string Status,
    string SafeSummary,
    string PlanFingerprint,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record SystemTaskDocument(
    SystemTaskSummary Summary,
    string ContextProfile,
    string ContextFingerprint,
    IReadOnlyList<string> ContextSourceReferences,
    string ErrorCode,
    string ErrorMessage,
    IReadOnlyList<SystemTaskRoundDocument> Rounds,
    IReadOnlyList<SystemTaskStepDocument> Steps,
    IReadOnlyList<SystemTaskConfirmationDocument> Confirmations,
    IReadOnlyList<SystemTaskExecutionDocument> Executions);

public sealed record SystemTaskPage(
    IReadOnlyList<SystemTaskSummary> Items,
    string? NextCursor);

public sealed class SystemTaskException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record SystemTaskContextSnapshot(
    string Profile,
    string Json,
    string Fingerprint,
    IReadOnlyList<string> SourceReferences);

public interface ISystemTaskContextMaterializer
{
    Task<SystemTaskContextSnapshot> MaterializeAsync(
        string query,
        SystemTaskRequestContext context,
        CancellationToken cancellationToken = default);
}

public interface ISystemTaskService
{
    Task<SystemTaskDocument> PrepareAsync(
        SystemTaskRequestContext context,
        string conversationId,
        SystemTaskPrepareRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemTaskDocument?> GetAsync(
        SystemTaskRequestContext context,
        string taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemTaskSummary>> ListAsync(
        SystemTaskRequestContext context,
        string conversationId,
        DateTime? beforeCreatedAtUtc,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SystemTaskConfirmationDocument> ConfirmAsync(
        SystemTaskRequestContext context,
        string taskId,
        SystemTaskConfirmationRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemTaskExecutionDocument> ExecuteAsync(
        SystemTaskRequestContext context,
        string taskId,
        SystemTaskExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SystemTaskRecord
{
    public required string Id { get; set; }
    public required string PrincipalReference { get; set; }
    public required string ConversationId { get; set; }
    public required string Operation { get; set; }
    public required string Intent { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string Status { get; set; }
    public string SafeSummary { get; set; } = string.Empty;
    public string PlanFingerprint { get; set; } = string.Empty;
    public string ContextProfile { get; set; } = string.Empty;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string ContextSourceReferencesJson { get; set; } = "[]";
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ICollection<SystemTaskRoundRecord> Rounds { get; } = new List<SystemTaskRoundRecord>();
    public ICollection<SystemTaskStepRecord> Steps { get; } = new List<SystemTaskStepRecord>();
    public ICollection<SystemTaskConfirmationRecord> Confirmations { get; } = new List<SystemTaskConfirmationRecord>();
    public ICollection<SystemTaskExecutionRecord> Executions { get; } = new List<SystemTaskExecutionRecord>();
}

public sealed class SystemTaskRoundRecord
{
    public required string TaskId { get; set; }
    public int Ordinal { get; set; }
    public required string Disposition { get; set; }
    public required string Summary { get; set; }
    public required string ContextFingerprint { get; set; }
    public required string ResponseFingerprint { get; set; }
    public required string ModelProvider { get; set; }
    public required string Model { get; set; }
    public required string ModelRevision { get; set; }
    public required string ModelProfile { get; set; }
    public required string EvidenceJson { get; set; }
    public required string OutputJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public SystemTaskRecord? Task { get; set; }
}

public sealed class SystemTaskStepRecord
{
    public required string TaskId { get; set; }
    public int Ordinal { get; set; }
    public required string StepId { get; set; }
    public required string CapabilityId { get; set; }
    public int CapabilityVersion { get; set; }
    public required string DescriptorFingerprint { get; set; }
    public required string Owner { get; set; }
    public required string Mode { get; set; }
    public required string InputJson { get; set; }
    public required string InputFingerprint { get; set; }
    public required string PreflightStatus { get; set; }
    public required string PreconditionFingerprint { get; set; }
    public required string SafeSummary { get; set; }
    public required string AffectedReferencesJson { get; set; }
    public required string DeferredStepIdsJson { get; set; }
    public required string ResultJson { get; set; }
    public required string ResultFingerprint { get; set; }
    public SystemTaskRecord? Task { get; set; }
}

public sealed class SystemTaskConfirmationRecord
{
    public required string Id { get; set; }
    public required string TaskId { get; set; }
    public required string PrincipalReference { get; set; }
    public required string PlanFingerprint { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string AuthorizationEvidenceJson { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public SystemTaskRecord? Task { get; set; }
    public ICollection<SystemTaskExecutionRecord> Executions { get; } = new List<SystemTaskExecutionRecord>();
}

public sealed class SystemTaskExecutionRecord
{
    public required string Id { get; set; }
    public required string TaskId { get; set; }
    public required string ConfirmationId { get; set; }
    public required string PrincipalReference { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string PlanFingerprint { get; set; }
    public required string Status { get; set; }
    public required string SafeSummary { get; set; }
    public required string ErrorCode { get; set; }
    public required string ErrorMessage { get; set; }
    public required string AuthorizationEvidenceJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public SystemTaskRecord? Task { get; set; }
    public SystemTaskConfirmationRecord? Confirmation { get; set; }
    public ICollection<SystemTaskExecutionStepRecord> Steps { get; } = new List<SystemTaskExecutionStepRecord>();
}

public sealed class SystemTaskExecutionStepRecord
{
    public required string ExecutionId { get; set; }
    public int Ordinal { get; set; }
    public required string TaskStepId { get; set; }
    public required string Status { get; set; }
    public required string ExecutionEvidenceJson { get; set; }
    public required string OperationId { get; set; }
    public required string OutputJson { get; set; }
    public required string OutputFingerprint { get; set; }
    public required string ReadBackFingerprint { get; set; }
    public required string ErrorCode { get; set; }
    public required string ErrorMessage { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public SystemTaskExecutionRecord? Execution { get; set; }
}
