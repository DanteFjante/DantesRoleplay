using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>Durable submission boundary for host-selected procedure workers.</summary>
internal sealed class SystemInnerWorkerService(
    SystemInnerWorkerProcedureResolver resolver,
    SqliteSystemTaskDurableService durable) : ISystemInnerWorkerService
{
    internal Task<InteractionInvocationResult> GetAsync(InteractionInvocationHost host,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default) =>
        durable.GetAsync(host, handle, cancellationToken);

    internal Task<InteractionInvocationResult> CancelAsync(InteractionInvocationHost host,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default) =>
        durable.CancelAsync(host, handle, cancellationToken);

    public async Task<InteractionInvocationResult> SubmitAsync(SystemInnerWorkerRequest request,
        CancellationToken cancellationToken = default) =>
        await SubmitCoreAsync(request, allowEphemeralParent: false, cancellationToken);

    /// <summary>
    /// Trusted workflow-service admission. The non-durable parent command remains explicit
    /// causation while the focused worker owns a new durable root lifecycle and budget.
    /// </summary>
    internal async Task<InteractionInvocationResult> SubmitEphemeralRootAsync(
        SystemInnerWorkerRequest request,
        CancellationToken cancellationToken = default) =>
        await SubmitCoreAsync(request, allowEphemeralParent: true, cancellationToken);

    private async Task<InteractionInvocationResult> SubmitCoreAsync(SystemInnerWorkerRequest request,
        bool allowEphemeralParent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return InteractionInvocationResult.Cancelled("SYSTEM_INNER_WORKER_CANCELLED",
                "The focused worker submission was cancelled.");
        try
        {
            var preparation = await resolver.ResolveAsync(request, cancellationToken);
            return allowEphemeralParent
                ? await durable.SubmitEphemeralRootInnerWorkerAsync(preparation, cancellationToken)
                : await durable.SubmitInnerWorkerAsync(preparation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled("SYSTEM_INNER_WORKER_CANCELLED",
                "The focused worker submission was cancelled or its deadline elapsed.");
        }
        catch (InteractionTaskContextException error)
        {
            return InteractionInvocationResult.Unavailable(error.Code, error.Message);
        }
        catch (InteractionContractException error)
        {
            return error.Code.Contains("UNAVAILABLE", StringComparison.Ordinal)
                ? InteractionInvocationResult.Unavailable(error.Code, error.Message)
                : InteractionInvocationResult.Failed(error.Code, error.Message);
        }
    }
}
