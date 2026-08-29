using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public sealed record StoredConditionalTrigger(
    ApplicationIdentifier ApplicationId,
    string Id,
    int Version,
    ConditionalTriggerLifecycle Lifecycle,
    ConditionalTriggerKind Kind,
    ConditionalTriggerActivation Activation,
    ConditionalTriggerRearm Rearm,
    string StateSpaceId,
    IReadOnlyList<ConditionalTriggerDependency> Dependencies,
    ConditionalTriggerAdapterReference Adapter,
    CanonicalObservationData AdapterConfiguration,
    TriggerNotificationTarget Notification,
    bool? CurrentTruth,
    bool Armed,
    DateTimeOffset RecordedAt);

public interface IConditionalTriggerStore
{
    Task<TriggerSchedulingWriteResult<StoredConditionalTrigger>> AppendAsync(
        ConditionalTriggerDefinition definition,
        CancellationToken cancellationToken = default);
}

public interface IConditionalTriggerWorker
{
    Task<TriggerWorkerBatchResult> RunBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default);
}

public enum ConditionalTriggerStatus { Active, Paused, Cancelled, Superseded }

public sealed record ConditionalTriggerStatusView(
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    ConditionalTriggerStatus Status,
    bool? CurrentTruth,
    bool Armed,
    int EvaluationRevision,
    string? LastOperationId,
    string? LastFiredOperationId,
    string? LastNotificationId,
    int CurrentAttemptCount,
    string? CurrentFailureKind);

public interface IConditionalTriggerStatusReader
{
    Task<ConditionalTriggerStatusView?> GetAsync(
        ApplicationIdentifier applicationId,
        string triggerId,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConditionalTriggerStatusView>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
