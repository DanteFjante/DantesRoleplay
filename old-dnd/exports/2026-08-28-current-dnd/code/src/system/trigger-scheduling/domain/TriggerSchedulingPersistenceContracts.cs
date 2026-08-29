using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.TriggerScheduling;

public enum TriggerSchedulingWriteDisposition
{
    Appended,
    Replay,
    Conflict
}

public sealed record TriggerSchedulingWriteResult<T>(TriggerSchedulingWriteDisposition Disposition, T? Value, string Code)
{
    public static TriggerSchedulingWriteResult<T> Appended(T value) => new(TriggerSchedulingWriteDisposition.Appended, value, "TRIGGER_SCHEDULING_APPENDED");
    public static TriggerSchedulingWriteResult<T> Replay(T value) => new(TriggerSchedulingWriteDisposition.Replay, value, "TRIGGER_SCHEDULING_REPLAY");
    public static TriggerSchedulingWriteResult<T> Conflict() => new(TriggerSchedulingWriteDisposition.Conflict, default, "TRIGGER_SCHEDULING_IDEMPOTENCY_CONFLICT");
}

public sealed record StoredObservationStructure(
    ApplicationIdentifier ApplicationId,
    string Id,
    int Version,
    string SchemaProfileId,
    string NormalizedSchema,
    string SchemaHash,
    string Description,
    ObservationStructureStatus Status,
    ObservationDataClassification DataClassification,
    DateTimeOffset RecordedAt);

public sealed record StoredObservationSource(
    ApplicationIdentifier ApplicationId,
    string Id,
    int Version,
    ObservationSourceStatus Status,
    IReadOnlyList<ObservationStructureReference> AllowedStructures,
    IReadOnlyList<string> AllowedPrincipalIds,
    TimeSpan ReplayWindow,
    int RequestsPerMinute,
    DateTimeOffset RecordedAt);

public sealed record StoredOneTimeTrigger(
    ApplicationIdentifier ApplicationId,
    string Id,
    int Version,
    DateTimeOffset DueAt,
    TriggerMisfirePolicy MisfirePolicy,
    TriggerFireTarget Target,
    TriggerLifecycle Lifecycle,
    TriggerNotificationTarget Notification,
    DateTimeOffset RecordedAt);

public sealed record StoredRecurringTrigger(
    ApplicationIdentifier ApplicationId,
    string Id,
    int Version,
    RecurringTriggerLifecycle Lifecycle,
    RecurrencePattern Pattern,
    TriggerMisfirePolicy MisfirePolicy,
    TriggerFireTarget Target,
    TriggerNotificationTarget Notification,
    DateTimeOffset RecordedAt);

public sealed record StoredObservation(
    string Id,
    ApplicationIdentifier ApplicationId,
    string RequestId,
    string SourceId,
    int SourceVersion,
    string SourceInstanceId,
    string OccurrenceId,
    string StructureId,
    int StructureVersion,
    string StructureHash,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    string DataJson,
    string DataHash,
    string RequestFingerprint,
    string? PrincipalId);

public sealed record StoredTriggerFireReceipt(
    string Id,
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    DateTimeOffset OccurrenceAt,
    OneTimeTriggerDisposition Disposition,
    DateTimeOffset RecordedAt);

public interface ITriggerSchedulingStore
{
    Task<TriggerSchedulingWriteResult<StoredObservationStructure>> AppendStructureAsync(
        ObservationStructureDefinition definition,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingWriteResult<StoredObservationSource>> AppendSourceAsync(
        ObservationSourceDefinition definition,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingWriteResult<StoredOneTimeTrigger>> AppendOneTimeTriggerAsync(
        OneTimeTriggerDefinition definition,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingWriteResult<StoredRecurringTrigger>> AppendRecurringTriggerAsync(
        RecurringTriggerDefinition definition,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingWriteResult<StoredObservation>> AppendObservationAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingWriteResult<StoredTriggerFireReceipt>> AppendFireReceiptAsync(
        OneTimeTriggerDefinition trigger,
        CancellationToken cancellationToken = default);
}

public sealed class TriggerObservationStructureRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string SchemaProfileId { get; set; }
    public required string NormalizedSchema { get; set; }
    public required string SchemaHash { get; set; }
    public required string Description { get; set; }
    public required string Status { get; set; }
    public string DataClassification { get; set; } = "general";
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class TriggerObservationSourceRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string Status { get; set; }
    public int ReplayWindowSeconds { get; set; }
    public int RequestsPerMinute { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public ICollection<TriggerObservationSourceStructureRecord> AllowedStructures { get; } = new List<TriggerObservationSourceStructureRecord>();
    public ICollection<TriggerObservationSourcePrincipalRecord> AllowedPrincipals { get; } = new List<TriggerObservationSourcePrincipalRecord>();
}

public sealed class TriggerObservationSourceCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class TriggerObservationSourceStructureRecord
{
    public required string ApplicationId { get; set; }
    public required string SourceId { get; set; }
    public int SourceVersion { get; set; }
    public required string StructureId { get; set; }
    public int StructureVersion { get; set; }
    public TriggerObservationSourceRecord? Source { get; set; }
}

public sealed class TriggerObservationSourcePrincipalRecord
{
    public required string ApplicationId { get; set; }
    public required string SourceId { get; set; }
    public int SourceVersion { get; set; }
    public required string PrincipalId { get; set; }
    public TriggerObservationSourceRecord? Source { get; set; }
}

public sealed class OneTimeTriggerRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public DateTime DueAtUtc { get; set; }
    public required string MisfirePolicy { get; set; }
    public required string Target { get; set; }
    public string Lifecycle { get; set; } = "active";
    public string NotificationTopic { get; set; } = "scheduled.reminder";
    public string NotificationSubject { get; set; } = "Scheduled reminder";
    public string NotificationBody { get; set; } = string.Empty;
    public string? NotificationStateSpaceId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public ICollection<OneTimeTriggerNotificationEntityRecord> NotificationEntities { get; } =
        new List<OneTimeTriggerNotificationEntityRecord>();
}

public sealed class OneTimeTriggerNotificationEntityRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public OneTimeTriggerRecord? Trigger { get; set; }
}

public sealed class TriggerObservationStructureCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class OneTimeTriggerCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class TriggerObservationRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string RequestId { get; set; }
    public required string SourceId { get; set; }
    public int SourceVersion { get; set; }
    public required string SourceInstanceId { get; set; }
    public required string OccurrenceId { get; set; }
    public required string StructureId { get; set; }
    public int StructureVersion { get; set; }
    public required string StructureHash { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public required string DataJson { get; set; }
    public required string DataHash { get; set; }
    public required string RequestFingerprint { get; set; }
    public string? PrincipalId { get; set; }
}

public sealed class TriggerFireReceiptRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
    public required string Disposition { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class TriggerFireWorkRecord
{
    public required string FireId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
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

public sealed class TriggerNotificationLinkRecord
{
    public required string FireId { get; set; }
    public required string NotificationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class RecurringTriggerRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string Lifecycle { get; set; }
    public required string Kind { get; set; }
    public int Interval { get; set; }
    public int LocalTimeSeconds { get; set; }
    public required string TimeZoneId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int WeekdaysMask { get; set; }
    public int? DayOfMonth { get; set; }
    public required string GapPolicy { get; set; }
    public required string OverlapPolicy { get; set; }
    public required string MisfirePolicy { get; set; }
    public required string Target { get; set; }
    public required string NotificationTopic { get; set; }
    public required string NotificationSubject { get; set; }
    public required string NotificationBody { get; set; }
    public string? NotificationStateSpaceId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public ICollection<RecurringTriggerNotificationEntityRecord> NotificationEntities { get; } =
        new List<RecurringTriggerNotificationEntityRecord>();
}

public sealed class RecurringTriggerNotificationEntityRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public RecurringTriggerRecord? Trigger { get; set; }
}

public sealed class RecurringTriggerCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class RecurringTriggerStateRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int CurrentVersion { get; set; }
    public DateTime? NextOccurrenceAtUtc { get; set; }
    public DateTime? LastOccurrenceAtUtc { get; set; }
    public string? LastDisposition { get; set; }
    public string? LastFailureKind { get; set; }
    public int Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class RecurringTriggerFireWorkRecord
{
    public required string FireId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
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

public sealed class RecurringTriggerFireReceiptRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
    public required string Disposition { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class RecurringTriggerNotificationLinkRecord
{
    public required string FireId { get; set; }
    public required string NotificationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public DateTime OccurrenceAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
