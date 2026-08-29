using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public enum TriggerScheduleStatus
{
    Scheduled,
    Due,
    Completed,
    Cancelled,
    Missed,
    Superseded
}

public sealed record TriggerScheduleStatusView(
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    DateTimeOffset DueAt,
    TriggerScheduleStatus Status,
    string? FireId,
    string? NotificationId,
    int AttemptCount,
    string? FailureKind);

public interface ITriggerScheduleStatusReader
{
    Task<TriggerScheduleStatusView?> GetAsync(
        ApplicationIdentifier applicationId,
        string triggerId,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TriggerScheduleStatusView>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
