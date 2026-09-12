using DantesRoleplay.Interactions;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.ApplicationActivation;

namespace DantesRoleplay.SystemTasks.Persistence;

internal static class SystemTaskLifecycleLimits
{
    internal const int MaximumQueuedTasks = 1_024;
    internal const int MaximumAttemptsPerTask = 3;
    internal const int MaximumRootOperations = 16;
    internal const int MaximumChildrenPerTask = 16;
    internal const int MaximumDescendantsPerRoot = 64;
    internal const int MaximumParentDepth = 16;
    internal const int MaximumEvidenceItems = InteractionContractLimits.EvidenceItems;
}

internal enum SystemTaskLifecycleState
{
    Queued,
    Running,
    Waiting,
    Retry,
    Completed,
    Failed,
    Cancelled,
    Indeterminate
}

internal enum SystemTaskEnqueueDisposition { Created, Existing, Conflict, Rejected }

internal enum SystemTaskPurpose { ProcedureWorkflow, ApplicationValidation }

internal static class SystemTaskPurposeNames
{
    internal static string Get(SystemTaskPurpose value) => value switch
    {
        SystemTaskPurpose.ProcedureWorkflow => "procedure-workflow",
        SystemTaskPurpose.ApplicationValidation => "application-validation",
        _ => throw new InvalidDataException("The stored task purpose is invalid.")
    };

    internal static SystemTaskPurpose Parse(string value) => value switch
    {
        "procedure-workflow" => SystemTaskPurpose.ProcedureWorkflow,
        "application-validation" => SystemTaskPurpose.ApplicationValidation,
        _ => throw new InvalidDataException("The stored task purpose is invalid.")
    };
}

internal sealed record SystemTaskEnqueueResult(
    SystemTaskEnqueueDisposition Disposition,
    SystemTaskDurableHandle? Handle,
    string Code,
    string SafeMessage);

/// <summary>
/// Inert retained invocation metadata. This is deliberately not an InteractionInvocationHost and
/// cannot be used as authority without a fresh host-side authorization decision.
/// </summary>
internal sealed record SystemTaskStoredInvocation(
    string PrincipalReference,
    string AuthenticationMethod,
    string ApplicationId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string BaseApplicationsJson,
    string? StateSpaceId,
    string GrantReference,
    string? StateRevision,
    InteractionExecutionProfile Profile,
    string CommandId,
    string? ParentCommandId,
    int AdmittedOperations,
    DateTime DeadlineUtc);

internal sealed record SystemTaskStoredRequest(
    SystemTaskDurableHandle Handle,
    SystemTaskStoredInvocation Invocation,
    SystemTaskSelectedDefinition? SelectedDefinition,
    string InputJson,
    IReadOnlyList<SystemTaskDurableHandle> Dependencies,
    bool PropagateCancellation,
    StandingGrantActivationOrigin? ActivationOrigin = null,
    ApplicationCandidateReference? Candidate = null,
    SystemTaskValidationCausation? Causation = null,
    SystemTaskPurpose Purpose = SystemTaskPurpose.ProcedureWorkflow)
{
    internal SystemTaskSelectedDefinition WorkflowDefinition =>
        Purpose == SystemTaskPurpose.ProcedureWorkflow && SelectedDefinition is not null
            ? SelectedDefinition
            : throw new InvalidDataException("The retained task is not a procedure workflow.");
}

internal sealed record SystemTaskValidationCausation(string OperationId, string CausalCommandId);

internal sealed record SystemTaskLease(
    SystemTaskStoredRequest Request,
    SystemTaskAttemptIdentity Attempt,
    int AttemptOrdinal,
    SystemTaskCheckpoint? Checkpoint,
    string? WakeJson,
    bool CancellationRequested);

internal enum SystemTaskFailureKind { Transient, Permanent, Indeterminate }

internal sealed record SystemTaskTerminalOutcome(
    string DataJson,
    string CompletionEvidenceReference,
    IReadOnlyList<string>? Evidence = null);

internal sealed record SystemTaskLifecycleSnapshot(
    SystemTaskStoredRequest Request,
    SystemTaskLifecycleState State,
    SystemTaskCheckpoint? Checkpoint,
    string? WakeJson,
    int AttemptCount,
    long FencingCounter,
    bool CancellationRequested,
    bool CancellationAcknowledged,
    string? ResultJson,
    string? CompletionEvidenceReference,
    string? ErrorCode,
    string? SafeMessage,
    IReadOnlyList<string> Evidence,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

internal enum SystemTaskHostCallDisposition { NewPending, ExistingPending, BlockedByPending, Completed, Conflict, BudgetExhausted }

internal sealed record SystemTaskHostCallJournalResult(
    SystemTaskHostCallDisposition Disposition,
    string OperationId,
    string RequestFingerprint,
    string? CompletionJson);
