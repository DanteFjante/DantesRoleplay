using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public enum RecurringTriggerStatus
{
    Scheduled,
    Due,
    Paused,
    Cancelled,
    Completed,
    Superseded
}

public sealed record RecurringTriggerStatusView(
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    RecurringTriggerStatus Status,
    DateTimeOffset? NextOccurrenceAt,
    DateTimeOffset? LastOccurrenceAt,
    string? LastDisposition,
    string? LastFailureKind,
    string? LastNotificationId,
    int CurrentAttemptCount,
    string? CurrentFailureKind);

public interface IRecurringTriggerStatusReader
{
    Task<RecurringTriggerStatusView?> GetAsync(
        ApplicationIdentifier applicationId,
        string triggerId,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurringTriggerStatusView>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
