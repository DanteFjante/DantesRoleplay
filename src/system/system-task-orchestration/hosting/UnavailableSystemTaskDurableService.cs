using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemTasks;

/// <summary>Production seam until a durable store and fenced worker lifecycle are installed.</summary>
public sealed class UnavailableSystemTaskDurableService : ISystemTaskDurableService
{
    public Task<InteractionInvocationResult> SubmitAsync(SystemTaskDurableSubmissionRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(Unavailable(cancellationToken));

    public Task<InteractionInvocationResult> GetAsync(InteractionInvocationHost invocationHost, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default) => Task.FromResult(Unavailable(cancellationToken));

    public Task<InteractionInvocationResult> CancelAsync(InteractionInvocationHost invocationHost, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default) => Task.FromResult(Unavailable(cancellationToken));

    private static InteractionInvocationResult Unavailable(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED", "The durable task request was cancelled.")
            : InteractionInvocationResult.Unavailable("SYSTEM_TASK_DURABILITY_UNAVAILABLE", "Durable task execution is not available.");
}
