using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.TriggerScheduling;

public sealed class ObservationIngestionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ITriggerObservationRateLease : IAsyncDisposable
{
}

public interface ITriggerObservationRateLimiter
{
    ValueTask<ITriggerObservationRateLease?> TryAcquireAsync(
        string principalId,
        ApplicationIdentifier applicationId,
        string sourceId,
        int sourceRequestsPerMinute,
        CancellationToken cancellationToken = default);
}

public interface IObservationIngestionService
{
    Task<TriggerSchedulingWriteResult<StoredObservation>> SubmitAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        CancellationToken cancellationToken = default);
}
