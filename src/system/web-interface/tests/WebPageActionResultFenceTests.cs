using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Tests;

public sealed class WebPageActionResultFenceTests
{
    private const string Operation = "0123456789abcdef0123456789abcdef";
    private static readonly string Hash = new('A', 64);

    [Fact]
    public async Task Post_dispatch_failures_preserve_commit_pending_and_recovery_evidence()
    {
        var committed = InteractionInvocationResult.Committed(new(Operation, Hash, []));
        var pending = InteractionInvocationResult.Pending(
            new SystemTaskDurableHandle("task.1", "command.1"), [new(Operation, Hash, [])]);
        var recovering = InteractionInvocationResult.Failed("ACTION_OUTCOME_UNKNOWN", "Reconcile this operation.",
            recoveryIdentity: new ApplicationEcsExecutionIdentity(Operation, Hash));

        var afterStoreFailure = await WebPageActionResultFence.RecheckAsync(committed,
            _ => throw new WebPageStoreException("WEB_PAGE_SELECTION_STALE", "The page changed."));
        var afterCancellation = await WebPageActionResultFence.RecheckAsync(pending,
            _ => throw new OperationCanceledException());
        var afterRecoveryFailure = await WebPageActionResultFence.RecheckAsync(recovering,
            _ => throw new WebPageStoreException("WEB_PAGE_SELECTION_STALE", "The page changed."));

        Assert.Same(committed, afterStoreFailure);
        Assert.Same(pending, afterCancellation);
        Assert.Same(recovering, afterRecoveryFailure);
        Assert.Equal(Operation, afterStoreFailure.Receipt!.OperationId);
        Assert.Equal("task.1", afterCancellation.TaskHandle!.TaskId);
        Assert.Equal(Operation, afterRecoveryFailure.RecoveryIdentity!.OperationId);

        var unreceipted = InteractionInvocationResult.CompletedComputation("{\"value\":1}", "fixture.result");
        var suppressed = await WebPageActionResultFence.RecheckAsync(unreceipted,
            _ => throw new OperationCanceledException());
        Assert.Equal(InteractionInvocationResultTag.Cancelled, suppressed.Tag);
        Assert.Null(suppressed.DataJson);
    }
}
