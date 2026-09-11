using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleCancellationRecoveryTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PendingCode = "SYSTEM_TASK_HOST_CALL_PENDING";
    private const string PendingMessage = "A host call has an uncertain commit and must be reconciled before recovery.";

    [Fact]
    public async Task Cancellation_acknowledgement_with_pending_host_call_preserves_indeterminate_recovery()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.cancel-pending"))).Handle!;
        var lease = (await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30)))!;
        var call = await store.BeginHostCallAsync(lease, "operation.cancel-pending", "{\"effect\":1}");

        Assert.True(await store.RequestCancellationAsync(handle, propagate: false));
        Assert.True(await store.AcknowledgeCancellationAsync(lease));

        var evidence = await store.InspectEvidenceAsync(handle);
        AssertPendingCancellation(evidence!, "operation.cancel-pending", call.RequestFingerprint);
    }

    [Fact]
    public async Task Runner_late_outcome_after_pending_call_and_cancellation_remains_indeterminate()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.runner-cancel-pending"))).Handle!;
        var started = new TaskCompletionSource<SystemTaskHostCallJournalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<SystemTaskRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new SqliteSystemTaskLifecycleRunner(store, fixture.TimeProvider,
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));

        var running = runner.RunOnceAsync("worker.1", async (lease, cancellationToken) =>
        {
            var call = await store.BeginHostCallAsync(lease, "operation.runner-cancel-pending",
                "{\"effect\":1}", cancellationToken);
            started.SetResult(call);
            return await finish.Task;
        });
        var journal = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SystemTaskHostCallDisposition.NewPending, journal.Disposition);
        Assert.True(await store.RequestCancellationAsync(handle, propagate: false));
        finish.SetResult(new SystemTaskRunOutcome.Completed(
            new SystemTaskTerminalOutcome("{\"late\":true}", "evidence.late")));
        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(5)));

        var evidence = await fixture.CreateStore().InspectEvidenceAsync(handle);
        AssertPendingCancellation(evidence!, "operation.runner-cancel-pending", journal.RequestFingerprint);
        Assert.Null(evidence!.Snapshot.ResultJson);
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Permanent")]
    [InlineData("Indeterminate")]
    public async Task Executor_failure_with_pending_host_call_uses_reconciliation_identity_and_message(
        string classification)
    {
        var kind = Enum.Parse<SystemTaskFailureKind>(classification);
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.failure-" + kind.ToString().ToLowerInvariant()))).Handle!;
        var lease = (await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30)))!;
        var call = await store.BeginHostCallAsync(lease, "operation.failure-pending", "{\"effect\":1}");

        Assert.True(await store.FailAsync(lease, kind, "UNTRUSTED_EXECUTOR_CODE", "Untrusted executor detail."));

        var evidence = await fixture.CreateStore().InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, evidence!.Snapshot.State);
        Assert.Equal(PendingCode, evidence.Snapshot.ErrorCode);
        Assert.Equal(PendingMessage, evidence.Snapshot.SafeMessage);
        var pending = Assert.Single(evidence.HostCalls);
        Assert.Equal("operation.failure-pending", pending.OperationId);
        Assert.Equal(call.RequestFingerprint, pending.RequestFingerprint);
        Assert.Equal("pending", pending.State);
        Assert.DoesNotContain("UNTRUSTED", evidence.Snapshot.ErrorCode!, StringComparison.Ordinal);
        Assert.DoesNotContain("Untrusted", evidence.Snapshot.SafeMessage!, StringComparison.Ordinal);
    }

    private static void AssertPendingCancellation(SystemTaskLifecycleEvidence evidence,
        string operationId, string requestFingerprint)
    {
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, evidence.Snapshot.State);
        Assert.True(evidence.Snapshot.CancellationRequested);
        // Match expired-lease recovery: reconciliation takes precedence over cancellation.
        Assert.False(evidence.Snapshot.CancellationAcknowledged);
        Assert.Equal(PendingCode, evidence.Snapshot.ErrorCode);
        Assert.Equal(PendingMessage, evidence.Snapshot.SafeMessage);
        var pending = Assert.Single(evidence.HostCalls);
        Assert.Equal(operationId, pending.OperationId);
        Assert.Equal(requestFingerprint, pending.RequestFingerprint);
        Assert.Equal("pending", pending.State);
        Assert.Null(pending.CompletionJson);
    }

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", command, "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime)),
        new SystemTaskSelectedDefinition("procedure.fixture", 1, Hash), "{}");
}
