namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed class SystemTaskAiCeilingRecord
{
    public string TaskId { get; set; } = null!;
    public string TaskPurpose { get; set; } = "procedure-workflow";
    public string EnrollmentFingerprint { get; set; } = null!;
    public string ProfileId { get; set; } = null!;
    public int ProfileVersion { get; set; }
    public string ProfileFingerprint { get; set; } = null!;
    public string GrantReference { get; set; } = null!;
    public string GrantRevision { get; set; } = null!;
    public string GrantFingerprint { get; set; } = null!;
    public string? DefinitionId { get; set; }
    public int? DefinitionVersion { get; set; }
    public string? DefinitionFingerprint { get; set; }
    public string OutputSchemaFingerprint { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public int MaximumProviderTokens { get; set; }
    public int MaximumToolCalls { get; set; }
    public int MaximumConcurrentProviderRequests { get; set; }
    public string DeadlineUtc { get; set; } = null!;
    public string CreatedAtUtc { get; set; } = null!;
}

internal sealed class SystemTaskAiReservationRecord
{
    public string RecordReference { get; set; } = null!;
    public string TaskId { get; set; } = null!;
    public string ReservationId { get; set; } = null!;
    public string RequestFingerprint { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public long FencingCounter { get; set; }
    public string LeaseToken { get; set; } = null!;
    public string LeaseExpiresAtUtc { get; set; } = null!;
    public string DeadlineUtc { get; set; } = null!;
    public int RequestedProviderTokens { get; set; }
    public int ReservedProviderTokens { get; set; }
    public int ReservedToolCalls { get; set; }
    public string Mode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long ChargedProviderTokens { get; set; }
    public long ChargedToolCalls { get; set; }
    public int? SettledEvidenceSequence { get; set; }
    public string CreatedAtUtc { get; set; } = null!;
    public string UpdatedAtUtc { get; set; } = null!;
}

internal sealed class SystemTaskAiReservationAncestorRecord
{
    public string RecordReference { get; set; } = null!;
    public string AncestorTaskId { get; set; } = null!;
}

internal sealed class SystemTaskAiDispatchEvidenceRecord
{
    public string RecordReference { get; set; } = null!;
    public int Sequence { get; set; }
    public string EventReference { get; set; } = null!;
    public string PayloadFingerprint { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string DispatchKind { get; set; } = null!;
    public string RequestFingerprint { get; set; } = null!;
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string ProfileFingerprint { get; set; } = null!;
    public string SchemaFingerprint { get; set; } = null!;
    public string? RequestJson { get; set; }
    public string? ResponseFingerprint { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? TotalTokens { get; set; }
    public long ObservedToolCalls { get; set; }
    public int IsComplete { get; set; }
    public string? CompletionKind { get; set; }
    public string ObservedAtUtc { get; set; } = null!;
}
