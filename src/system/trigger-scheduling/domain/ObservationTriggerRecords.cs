namespace DantesRoleplay.TriggerScheduling;

public sealed class ObservationTriggerRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string Lifecycle { get; set; }
    public required string SourceId { get; set; }
    public int SourceVersion { get; set; }
    public required string StructureId { get; set; }
    public int StructureVersion { get; set; }
    public required string StructureHash { get; set; }
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
    public ICollection<ObservationTriggerNotificationEntityRecord> NotificationEntities { get; } =
        new List<ObservationTriggerNotificationEntityRecord>();
}

public sealed class ObservationTriggerNotificationEntityRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public ObservationTriggerRecord? Trigger { get; set; }
}

public sealed class ObservationTriggerCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class ObservationTriggerMatchWorkRecord
{
    public required string FireId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ObservationId { get; set; }
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

public sealed class ObservationTriggerMatchReceiptRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ObservationId { get; set; }
    public required string Disposition { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class ObservationTriggerNotificationLinkRecord
{
    public required string FireId { get; set; }
    public required string NotificationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ObservationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
