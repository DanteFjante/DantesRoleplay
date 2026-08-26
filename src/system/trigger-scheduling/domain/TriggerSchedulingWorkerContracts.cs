using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public enum TriggerFireWorkStatus
{
    Ready,
    Leased,
    Retry,
    Completed,
    Missed,
    Failed
}

public enum TriggerFireFailureKind
{
    HandlerUnavailable,
    TransientDatabase,
    PermanentHandler,
    StaleTrigger,
    AttemptsExhausted
}

public enum TriggerFireAttemptDisposition
{
    Succeeded,
    TransientFailure,
    PermanentFailure
}

public enum TriggerScheduleKind { OneTime, Recurring, Conditional, Observation }

public sealed record TriggerFireLease(
    string FireId,
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    DateTimeOffset OccurrenceAt,
    TriggerMisfirePolicy MisfirePolicy,
    TriggerFireTarget Target,
    int Attempt,
    string WorkerId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt)
{
    public TriggerScheduleKind ScheduleKind { get; init; } = TriggerScheduleKind.OneTime;
    public string? ChangeOperationId { get; init; }
    public string? ObservationId { get; init; }
}

public sealed record TriggerFireAttemptResult(
    TriggerFireAttemptDisposition Disposition,
    TriggerFireFailureKind? FailureKind = null)
{
    public static TriggerFireAttemptResult Succeeded() =>
        new(TriggerFireAttemptDisposition.Succeeded);

    public static TriggerFireAttemptResult Transient(
        TriggerFireFailureKind kind = TriggerFireFailureKind.HandlerUnavailable) =>
        kind is TriggerFireFailureKind.HandlerUnavailable or TriggerFireFailureKind.TransientDatabase
            ? new(TriggerFireAttemptDisposition.TransientFailure, kind)
            : throw new ArgumentOutOfRangeException(nameof(kind));

    public static TriggerFireAttemptResult Permanent(
        TriggerFireFailureKind kind = TriggerFireFailureKind.PermanentHandler) =>
        kind is TriggerFireFailureKind.PermanentHandler or TriggerFireFailureKind.StaleTrigger
            ? new(TriggerFireAttemptDisposition.PermanentFailure, kind)
            : throw new ArgumentOutOfRangeException(nameof(kind));
}

/// <summary>
/// Stages one target result inside the worker-owned database transaction. Implementations must use
/// the scoped kernel DbContext, must not commit independently, and must stage nothing on failure.
/// </summary>
public interface ITriggerFireTransactionParticipant
{
    bool IsAvailable { get; }

    Task<TriggerFireAttemptResult> StageAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken = default);
}

public sealed record TriggerWorkerBatchResult(
    int Examined,
    int Claimed,
    int Completed,
    int Missed,
    int Retried,
    int Failed);

public interface IOneTimeTriggerWorker
{
    Task<TriggerWorkerBatchResult> RunBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default);
}

public interface IRecurringTriggerWorker
{
    Task<TriggerWorkerBatchResult> RunBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default);
}
