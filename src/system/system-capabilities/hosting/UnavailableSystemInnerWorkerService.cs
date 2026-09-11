using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Truthful production boundary until focused workers are backed by durable execution.</summary>
public sealed class UnavailableSystemInnerWorkerService : ISystemInnerWorkerService
{
    public Task<InteractionInvocationResult> SubmitAsync(SystemInnerWorkerRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(
            cancellationToken.IsCancellationRequested
                ? InteractionInvocationResult.Cancelled("SYSTEM_INNER_WORKER_CANCELLED", "The inner worker request was cancelled.")
                : InteractionInvocationResult.Unavailable("SYSTEM_INNER_WORKER_UNAVAILABLE", "Focused inner workers are not available."));
}
