using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public sealed record StoredObservationTrigger(
    ApplicationIdentifier ApplicationId, string Id, int Version, ObservationTriggerLifecycle Lifecycle,
    string SourceId, int SourceVersion, string StructureId, int StructureVersion, string StructureHash,
    ObservationMatchAdapterReference Adapter, CanonicalObservationData AdapterConfiguration,
    TriggerNotificationTarget Notification, DateTimeOffset RecordedAt);

public interface IObservationTriggerStore
{
    Task<TriggerSchedulingWriteResult<StoredObservationTrigger>> AppendAsync(
        ObservationTriggerDefinition definition, CancellationToken cancellationToken = default);
}

public interface IObservationAppendTransactionParticipant
{
    Task StageAsync(StoredObservation observation, CancellationToken cancellationToken = default);
}

public interface IObservationTriggerWorker
{
    Task<TriggerWorkerBatchResult> RunBatchAsync(string workerId,
        CancellationToken cancellationToken = default);
}

public enum ObservationTriggerStatus { Active, Paused, Cancelled, StaleSource, StaleStructure, Superseded }
public sealed record ObservationTriggerStatusView(
    ApplicationIdentifier ApplicationId, string TriggerId, int TriggerVersion,
    ObservationTriggerStatus Status, string? LastObservationId, string? LastDisposition,
    string? LastNotificationId, int CurrentAttemptCount, string? CurrentFailureKind);

public interface IObservationTriggerStatusReader
{
    Task<ObservationTriggerStatusView?> GetAsync(ApplicationIdentifier applicationId, string triggerId,
        int? version = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ObservationTriggerStatusView>> ListAsync(ApplicationIdentifier applicationId,
        int limit = 50, CancellationToken cancellationToken = default);
}
