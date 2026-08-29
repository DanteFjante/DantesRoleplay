namespace DantesRoleplay.TriggerScheduling;

public sealed class ConditionalTriggerRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string Lifecycle { get; set; }
    public required string Kind { get; set; }
    public required string Activation { get; set; }
    public required string Rearm { get; set; }
    public required string StateSpaceId { get; set; }
    public required string AdapterId { get; set; }
    public int AdapterVersion { get; set; }
    public required string AdapterConfigurationJson { get; set; }
    public required string AdapterConfigurationHash { get; set; }
    public required string Target { get; set; }
    public required string NotificationTopic { get; set; }
    public required string NotificationSubject { get; set; }
    public required string NotificationBody { get; set; }
    public string? NotificationStateSpaceId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public ICollection<ConditionalTriggerDependencyRecord> Dependencies { get; } =
        new List<ConditionalTriggerDependencyRecord>();
    public ICollection<ConditionalTriggerNotificationEntityRecord> NotificationEntities { get; } =
        new List<ConditionalTriggerNotificationEntityRecord>();
}

public sealed class ConditionalTriggerDependencyRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public required string QualifiedTypeId { get; set; }
    public int TypeVersion { get; set; }
    public required string SchemaHash { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerNotificationEntityRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class ConditionalTriggerStateRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int CurrentVersion { get; set; }
    public bool? CurrentTruth { get; set; }
    public bool Armed { get; set; }
    public int EvaluationRevision { get; set; }
    public string? LastOperationId { get; set; }
    public string? LastFiredOperationId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ConditionalTriggerFireWorkRecord
{
    public required string FireId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public required string State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureKind { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ConditionalTriggerFireReceiptRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public required string Disposition { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class ConditionalTriggerNotificationLinkRecord
{
    public required string FireId { get; set; }
    public required string NotificationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
