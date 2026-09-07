namespace DantesRoleplay.TriggerScheduling;

internal sealed class ScheduledAiTaskWorkRecord
{
    public required string NotificationId { get; set; }
    public required string State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureKind { get; set; }
    public string? FailureMessage { get; set; }
    public long? QueueAgeMilliseconds { get; set; }
    public long? ProviderDurationMilliseconds { get; set; }
    public int Revision { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

internal sealed record ScheduledAiTaskLease(
    string NotificationId,
    int Attempt,
    string WorkerId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt,
    long QueueAgeMilliseconds,
    bool Recovered);

internal sealed record ScheduledAiTaskClaimBatch(
    int Discovered,
    IReadOnlyList<ScheduledAiTaskLease> Leases,
    IReadOnlyList<string> ExhaustedNotificationIds);

internal sealed record ScheduledAiTaskBatchResult(
    int Discovered,
    int Claimed,
    int Completed,
    int Retried,
    int Failed,
    int Recovered);

internal enum ScheduledAiTaskExecutionOutcome
{
    None,
    Completed,
    Retried,
    Failed
}
