namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed class SystemTaskRootBudgetRecord
{
    public string RootTaskId { get; set; } = null!;
    public int MaximumOperations { get; set; }
    public int ConsumedOperations { get; set; }
}

internal sealed class SystemTaskLifecycleRecord
{
    public string TaskId { get; set; } = null!;
    public string CommandId { get; set; } = null!;
    public string PayloadFingerprint { get; set; } = null!;
    public string? ParentTaskId { get; set; }
    public string? ParentCommandId { get; set; }
    public string RootTaskId { get; set; } = null!;
    public int ParentDepth { get; set; }
    public int PropagateCancellation { get; set; }
    public string State { get; set; } = null!;
    public string PrincipalReference { get; set; } = null!;
    public string AuthenticationMethod { get; set; } = null!;
    public string ApplicationId { get; set; } = null!;
    public int ApplicationRevision { get; set; }
    public string ApplicationFingerprint { get; set; } = null!;
    public int? ActivationRevision { get; set; }
    public string? ActivationFingerprint { get; set; }
    public int? ActivationApplicationRevision { get; set; }
    public string? ActivationApplicationFingerprint { get; set; }
    public string BaseApplicationsJson { get; set; } = null!;
    public string StateSpaceId { get; set; } = null!;
    public string GrantReference { get; set; } = null!;
    public string StateRevision { get; set; } = null!;
    public string ExecutionProfile { get; set; } = null!;
    public int AdmittedOperations { get; set; }
    public string DeadlineUtc { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public int DefinitionVersion { get; set; }
    public string DefinitionFingerprint { get; set; } = null!;
    public string InputJson { get; set; } = null!;
    public string? CheckpointName { get; set; }
    public string? CompletionHandler { get; set; }
    public string? CorrelationId { get; set; }
    public string? CheckpointStateJson { get; set; }
    public string? WakeJson { get; set; }
    public int AttemptCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsumedOperations { get; set; }
    public long FencingCounter { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public string? LeaseExpiresAtUtc { get; set; }
    public string? NextAttemptAtUtc { get; set; }
    public int CancelRequested { get; set; }
    public int CancelAcknowledged { get; set; }
    public string? ResultJson { get; set; }
    public string? CompletionEvidenceReference { get; set; }
    public string? EvidenceJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? SafeMessage { get; set; }
    public string CreatedAtUtc { get; set; } = null!;
    public string UpdatedAtUtc { get; set; } = null!;
    public string? CompletedAtUtc { get; set; }
}

internal sealed class SystemTaskDependencyRecord
{
    public string TaskId { get; set; } = null!;
    public string DependencyTaskId { get; set; } = null!;
    public string DependencyCommandId { get; set; } = null!;
}

internal sealed class SystemTaskAttemptRecord
{
    public string TaskId { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public int Ordinal { get; set; }
    public long FencingCounter { get; set; }
    public string LeaseToken { get; set; } = null!;
    public string State { get; set; } = null!;
    public string? FailureCode { get; set; }
    public string? SafeMessage { get; set; }
    public string StartedAtUtc { get; set; } = null!;
    public string? CompletedAtUtc { get; set; }
}

internal sealed class SystemTaskCheckpointRecord
{
    public string TaskId { get; set; } = null!;
    public int Sequence { get; set; }
    public string CheckpointName { get; set; } = null!;
    public string CompletionHandler { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public string StateJson { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? WakeJson { get; set; }
    public string CreatedAtUtc { get; set; } = null!;
    public string? WokenAtUtc { get; set; }
}

internal sealed class SystemTaskHostCallRecord
{
    public string TaskId { get; set; } = null!;
    public string OperationId { get; set; } = null!;
    public string RequestFingerprint { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? CompletionJson { get; set; }
    public string AttemptId { get; set; } = null!;
    public long FencingCounter { get; set; }
    public string StartedAtUtc { get; set; } = null!;
    public string? CompletedAtUtc { get; set; }
}
