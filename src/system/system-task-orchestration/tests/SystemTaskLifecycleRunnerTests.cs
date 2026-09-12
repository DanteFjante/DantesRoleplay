using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

// These execute the real lifecycle and SQLite store. Callbacks are test computation only;
// they do not establish acceptance of the separately owned JavaScript or standing-grant paths.
public sealed class SystemTaskLifecycleRunnerTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Computation_runs_after_claim_transaction_and_publishes_durable_readback()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        Assert.True(await Runner(fixture).RunOnceAsync("worker.1", async (lease, token) =>
        {
            // A separate connection can write while computation is running.
            var sibling = await fixture.CreateStore().EnqueueAsync(Request(fixture, "other.command"), cancellationToken: token);
            Assert.Equal(SystemTaskEnqueueDisposition.Created, sibling.Disposition);
            Assert.Equal(handle, lease.Request.Handle);
            return Completed();
        }));
        var readback = await fixture.CreateStore().ReadAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Completed, readback!.State);
        Assert.Equal("{\"answer\":42}", readback.ResultJson);
        Assert.Equal("computation.fixture", readback.CompletionEvidenceReference);
    }

    [Fact]
    public async Task Durable_wait_returns_worker_and_reopens_at_recorded_checkpoint()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        var checkpoint = new SystemTaskCheckpoint("step.2", "handler.2", "wake.2", "{\"saved\":true}");
        await Runner(fixture).RunOnceAsync("worker.1", (_, _) =>
            Task.FromResult<SystemTaskRunOutcome>(new SystemTaskRunOutcome.Waiting(checkpoint)));
        Assert.Equal(SystemTaskLifecycleState.Waiting, (await fixture.CreateStore().ReadAsync(handle))!.State);
        Assert.False(await Runner(fixture).RunOnceAsync("worker.2", (_, _) => throw new Exception("Waiting work must not execute.")));
        Assert.Equal(1, await fixture.CreateStore().WakeAsync(handle, "wake.2", "{\"ready\":true}"));
        Assert.Equal(0, await fixture.CreateStore().WakeAsync(handle, "wake.2", "{\"ready\":false}"));
        await Runner(fixture).RunOnceAsync("worker.3", (lease, _) =>
        {
            Assert.Equal(checkpoint, lease.Checkpoint);
            Assert.Equal("{\"ready\":true}", lease.WakeJson);
            Assert.Equal(handle.CommandId, lease.Attempt.StableCommandId);
            Assert.Equal(2, lease.Attempt.FencingCounter);
            return Task.FromResult<SystemTaskRunOutcome>(Completed());
        });
        Assert.Equal(SystemTaskLifecycleState.Completed, (await store.ReadAsync(handle))!.State);
    }

    [Fact]
    public async Task Successful_checkpoints_do_not_spend_the_consecutive_failure_limit()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        for (var step = 1; step <= 4; step++)
        {
            var checkpoint = new SystemTaskCheckpoint($"step.{step}", "handler", $"wake.{step}", "{}");
            Assert.True(await Runner(fixture).RunOnceAsync("worker", (_, _) =>
                Task.FromResult<SystemTaskRunOutcome>(new SystemTaskRunOutcome.Waiting(checkpoint))));
            Assert.Equal(1, await store.WakeAsync(handle, checkpoint.CorrelationId, "{}"));
        }
        await Runner(fixture).RunOnceAsync("worker", (_, _) => Task.FromResult<SystemTaskRunOutcome>(Completed()));
        var evidence = await store.InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Completed, evidence!.Snapshot.State);
        Assert.Equal(5, evidence.Attempts.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, evidence.Checkpoints.Select(value => value.Sequence));
        Assert.All(evidence.Checkpoints, value => Assert.Equal("woken", value.Status));
    }

    [Fact]
    public async Task Later_wait_cannot_reuse_consumed_correlation_or_accept_delayed_redelivery()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        var checkpoint = new SystemTaskCheckpoint("step.1", "handler", "same.correlation", "{\"state\":1}");
        await Runner(fixture).RunOnceAsync("worker", (_, _) =>
            Task.FromResult<SystemTaskRunOutcome>(new SystemTaskRunOutcome.Waiting(checkpoint)));
        await store.WakeAsync(handle, checkpoint.CorrelationId, "{\"reply\":1}");
        await Runner(fixture).RunOnceAsync("worker", (_, _) =>
            Task.FromResult<SystemTaskRunOutcome>(new SystemTaskRunOutcome.Waiting(
                new("step.2", "handler", "same.correlation", "{\"state\":2}"))));
        Assert.Equal(0, await store.WakeAsync(handle, checkpoint.CorrelationId, "{\"reply\":1}"));
        var evidence = await store.InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Failed, evidence!.Snapshot.State);
        Assert.Equal(checkpoint, evidence.Snapshot.Checkpoint);
        Assert.Single(evidence.Checkpoints);
    }

    [Fact]
    public async Task Durable_cancellation_wins_over_late_computation_completion()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<SystemTaskRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Runner(fixture).RunOnceAsync("worker.1", (_, _) => { started.SetResult(); return finish.Task; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.RequestCancellationAsync(handle, false);
        finish.SetResult(Completed());
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = await fixture.CreateStore().ReadAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Cancelled, snapshot!.State);
        Assert.True(snapshot.CancellationAcknowledged);
        Assert.Null(snapshot.ResultJson);
    }

    [Fact]
    public async Task Cancellation_committed_between_monitor_read_and_renewal_is_acknowledged()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        var clock = new RenewalInterleavingClock(fixture.TimeProvider, async () =>
        {
            Assert.False((await store.ReadAsync(handle))!.CancellationRequested);
            Assert.True(await store.RequestCancellationAsync(handle, false));
        });
        var runner = new SqliteSystemTaskLifecycleRunner(new(fixture.ConnectionString, clock),
            fixture.TimeProvider, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));
        var finish = new TaskCompletionSource<SystemTaskRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { Assert.True(await runner.RunOnceAsync("worker.1", (_, _) => finish.Task).WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { finish.TrySetResult(Completed()); }
        Assert.True(clock.Interleaved);
        var snapshot = await store.ReadAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Cancelled, snapshot!.State);
        Assert.True(snapshot.CancellationAcknowledged);
        Assert.Null(snapshot.ResultJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Renewal_failure_cannot_acknowledge_or_publish_for_a_replacement_lease(bool cancelReplacement)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        SystemTaskLease? replacement = null;
        var clock = new RenewalInterleavingClock(fixture.TimeProvider, async () =>
        {
            fixture.TimeProvider.Advance(TimeSpan.FromSeconds(31));
            replacement = await store.ClaimNextAsync("worker.replacement", TimeSpan.FromSeconds(30));
            Assert.NotNull(replacement);
            if (cancelReplacement) Assert.True(await store.RequestCancellationAsync(handle, false));
        });
        var runner = new SqliteSystemTaskLifecycleRunner(new(fixture.ConnectionString, clock),
            fixture.TimeProvider, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));
        var finish = new TaskCompletionSource<SystemTaskRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { Assert.True(await runner.RunOnceAsync("worker.old", (_, _) => finish.Task).WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { finish.TrySetResult(Completed()); }
        Assert.True(clock.Interleaved);
        var snapshot = await store.ReadAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Running, snapshot!.State);
        Assert.Equal(replacement!.Attempt.FencingCounter, snapshot.FencingCounter);
        Assert.Equal(cancelReplacement, snapshot.CancellationRequested);
        Assert.False(snapshot.CancellationAcknowledged);
        Assert.Null(snapshot.ResultJson);
        if (cancelReplacement) Assert.True(await store.AcknowledgeCancellationAsync(replacement));
        else Assert.True(await store.CompleteAsync(replacement, new("{}", "replacement.completed")));
    }

    // Only the store uses this clock: claim samples it once; renewal's sample occurs after
    // the monitor's read and before renewal's UPDATE. The interleaving uses the real SQLite API.
    private sealed class RenewalInterleavingClock(TimeProvider clock, Func<Task> betweenReadAndRenew) : TimeProvider
    {
        private int _samples;
        internal bool Interleaved { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref _samples) == 2)
            {
                Task.Run(betweenReadAndRenew).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                Interleaved = true;
            }
            return clock.GetUtcNow();
        }
    }

    [Fact]
    public async Task Worker_shutdown_abandons_lease_without_cancelling_task_and_restart_recovers()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        using var shutdown = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Runner(fixture).RunOnceAsync("worker.1", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Completed();
        }, shutdown.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shutdown.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False((await store.ReadAsync(handle))!.CancellationRequested);
        fixture.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        await Runner(fixture).RunOnceAsync("worker.2", (lease, _) =>
        {
            Assert.Equal(2, lease.Attempt.FencingCounter);
            Assert.Equal(handle.CommandId, lease.Attempt.StableCommandId);
            return Task.FromResult<SystemTaskRunOutcome>(Completed());
        });
        Assert.Equal(SystemTaskLifecycleState.Completed, (await store.ReadAsync(handle))!.State);
    }

    [Fact]
    public async Task Unknown_callback_exception_is_inspectable_and_never_automatically_retried()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture))).Handle!;
        await Runner(fixture).RunOnceAsync("worker.1", (_, _) => throw new InvalidOperationException("Untrusted error details."));
        var evidence = await fixture.CreateStore().InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, evidence!.Snapshot.State);
        Assert.Equal("SYSTEM_TASK_EXECUTION_UNKNOWN", evidence.Snapshot.ErrorCode);
        Assert.DoesNotContain("Untrusted", evidence.Snapshot.SafeMessage!);
        Assert.Single(evidence.Attempts);
        Assert.False(await Runner(fixture).RunOnceAsync("worker.2", (_, _) => throw new Exception("Must not retry.")));
    }

    [Fact]
    public async Task Elapsed_deadline_ends_running_work_without_waiting_for_another_worker()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, deadline: TimeSpan.FromSeconds(10)))).Handle!;
        await Runner(fixture).RunOnceAsync("worker.1", (_, _) =>
        {
            fixture.TimeProvider.Advance(TimeSpan.FromSeconds(11));
            return Task.FromResult<SystemTaskRunOutcome>(Completed());
        });
        Assert.Equal(SystemTaskLifecycleState.Failed, (await store.ReadAsync(handle))!.State);
    }

    private static SqliteSystemTaskLifecycleRunner Runner(SystemTaskLifecycleSchemaFixture fixture) =>
        new(fixture.CreateStore(), fixture.TimeProvider, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));

    private static SystemTaskRunOutcome.Completed Completed() => new(new("{\"answer\":42}", "computation.fixture"));

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command = "command.1", TimeSpan? deadline = null) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", command, "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().Add(deadline ?? TimeSpan.FromHours(1)).UtcDateTime)),
        new("procedure.fixture", 1, Hash), "{}");
}
