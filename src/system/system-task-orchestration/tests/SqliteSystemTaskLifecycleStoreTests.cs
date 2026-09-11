using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

// Journal JSON in these tests is inert fixture data, not proof of a committed action.
public sealed class SqliteSystemTaskLifecycleStoreTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Concurrent_admission_and_claim_create_one_job_and_one_live_attempt()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var submissions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            fixture.CreateStore().EnqueueAsync(Request(fixture, "command.concurrent")))));
        Assert.Single(submissions, value => value.Disposition == SystemTaskEnqueueDisposition.Created);
        Assert.Equal(7, submissions.Count(value => value.Disposition == SystemTaskEnqueueDisposition.Existing));
        Assert.Single(submissions.Select(value => value.Handle).Distinct());
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            fixture.CreateStore().ClaimNextAsync($"worker.{index}", TimeSpan.FromMinutes(1)))));
        var lease = Assert.Single(claims, value => value is not null)!;
        Assert.Equal(1, lease.Attempt.FencingCounter);
        var evidence = await fixture.CreateStore().InspectEvidenceAsync(lease.Request.Handle);
        Assert.Single(evidence!.Attempts);
    }

    [Fact]
    public async Task Crash_with_pending_host_call_preserves_recovery_identity_and_cannot_execute_again()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.uncertain"))).Handle!;
        var lease = (await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30)))!;
        var pending = await store.BeginHostCallAsync(lease, "operation.original", "{\"action\":1}");
        fixture.TimeProvider.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(await fixture.CreateStore().ClaimNextAsync("worker.2", TimeSpan.FromSeconds(30)));
        Assert.False(await store.CompleteHostCallAsync(lease, pending.OperationId, pending.RequestFingerprint, "{}"));
        var evidence = await fixture.CreateStore().InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, evidence!.Snapshot.State);
        var call = Assert.Single(evidence.HostCalls);
        Assert.Equal(pending.OperationId, call.OperationId);
        Assert.Equal(pending.RequestFingerprint, call.RequestFingerprint);
        Assert.Equal(lease.Attempt.AttemptId, call.AttemptId);
        Assert.Equal("pending", call.State);
        Assert.Null(call.CompletionJson);
    }

    [Fact]
    public async Task Third_consecutive_transient_failure_exhausts_retry_policy()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.exhausted"))).Handle!;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var lease = (await fixture.CreateStore().ClaimNextAsync("worker", TimeSpan.FromSeconds(30)))!;
            Assert.Equal(attempt, lease.AttemptOrdinal);
            Assert.True(await store.FailAsync(lease, SystemTaskFailureKind.Transient, "TEMPORARY", "Temporary failure."));
            fixture.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.Null(await store.ClaimNextAsync("worker", TimeSpan.FromSeconds(30)));
        var evidence = await store.InspectEvidenceAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Failed, evidence!.Snapshot.State);
        Assert.Equal(3, evidence.Attempts.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_running_cancellation_or_deadline_is_normalized_without_new_execution(bool cancelled)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.expired"))).Handle!;
        var lease = (await store.ClaimNextAsync("worker", TimeSpan.FromSeconds(30)))!;
        if (cancelled) await store.RequestCancellationAsync(handle, false);
        fixture.TimeProvider.Advance(TimeSpan.FromHours(2));
        Assert.Null(await fixture.CreateStore().ClaimNextAsync("replacement", TimeSpan.FromSeconds(30)));
        var snapshot = await store.ReadAsync(handle);
        Assert.Equal(cancelled ? SystemTaskLifecycleState.Cancelled : SystemTaskLifecycleState.Failed, snapshot!.State);
        Assert.False(await store.CompleteAsync(lease, Outcome(1)));
    }

    [Fact]
    public async Task Root_budget_is_shared_across_sibling_claims()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture, "command.root", maximumOperations: 2))).Handle!;
        var rootLease = (await store.ClaimNextAsync("root.worker", TimeSpan.FromSeconds(30)))!;
        await store.EnqueueAsync(Request(fixture, "command.child1", parentCommand: parent.CommandId, maximumOperations: 2));
        var childLease = (await store.ClaimNextAsync("child.worker", TimeSpan.FromSeconds(30)))!;
        var sibling = (await store.EnqueueAsync(Request(fixture, "command.child2", parentCommand: parent.CommandId, maximumOperations: 2))).Handle!;
        Assert.Null(await store.ClaimNextAsync("sibling.worker", TimeSpan.FromSeconds(30)));
        Assert.Equal("SYSTEM_TASK_BUDGET_EXHAUSTED", (await store.ReadAsync(sibling))!.ErrorCode);
        Assert.True(await store.CompleteAsync(childLease, Outcome(1)));
        Assert.True(await store.CompleteAsync(rootLease, Outcome(2)));
    }

    [Fact]
    public async Task Immutable_existing_only_graph_rejects_missing_and_ancestral_dependencies_and_excess_fanout()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var root = (await store.EnqueueAsync(Request(fixture, "command.root"))).Handle!;
        var missing = await store.EnqueueAsync(Request(fixture, "command.missing", dependencies: [new("task.missing", "command.absent")]));
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, missing.Disposition);
        var cycle = await store.EnqueueAsync(Request(fixture, "command.cycle", parentCommand: root.CommandId, dependencies: [root]));
        Assert.Equal("SYSTEM_TASK_DEPENDENCY_ANCESTOR", cycle.Code);
        for (var child = 0; child < 16; child++)
            Assert.Equal(SystemTaskEnqueueDisposition.Created,
                (await store.EnqueueAsync(Request(fixture, $"command.child{child}", parentCommand: root.CommandId))).Disposition);
        Assert.Equal("SYSTEM_TASK_FANOUT_LIMIT",
            (await store.EnqueueAsync(Request(fixture, "command.excess", parentCommand: root.CommandId))).Code);
        Assert.Null((await store.EnqueueAsync(Request(fixture, "command.excess", parentCommand: root.CommandId))).Handle);
    }

    [Fact]
    public async Task Enqueue_is_transactionally_stable_across_reopen_and_conflicts_on_changed_payload()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var created = await fixture.CreateStore().EnqueueAsync(Request(fixture, "command.enqueue", "{\"b\":2,\"a\":1}"));
        var replay = await fixture.CreateStore().EnqueueAsync(Request(fixture, "command.enqueue", "{\"a\":1,\"b\":2}"));
        var conflict = await fixture.CreateStore().EnqueueAsync(Request(fixture, "command.enqueue", "{\"a\":9}"));

        Assert.Equal(SystemTaskEnqueueDisposition.Created, created.Disposition);
        Assert.Equal(SystemTaskEnqueueDisposition.Existing, replay.Disposition);
        Assert.Equal(created.Handle, replay.Handle);
        Assert.Equal(SystemTaskEnqueueDisposition.Conflict, conflict.Disposition);
        Assert.Equal("{\"a\":1,\"b\":2}", (await fixture.CreateStore().ReadAsync(created.Handle!))!.Request.InputJson);
    }

    [Fact]
    public async Task Expired_lease_recovers_with_higher_fence_and_rejects_stale_completion()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.recover"))).Handle!;
        var first = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(10));
        fixture.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        var second = await fixture.CreateStore().ClaimNextAsync("worker.2", TimeSpan.FromSeconds(10));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, second!.AttemptOrdinal);
        Assert.True(second.Attempt.FencingCounter > first!.Attempt.FencingCounter);
        Assert.False(await store.CompleteAsync(first, Outcome(1)));
        Assert.True(await fixture.CreateStore().CompleteAsync(second, Outcome(2)));
        Assert.Equal("{\"value\":2}", (await store.ReadAsync(handle))!.ResultJson);
    }

    [Fact]
    public async Task Waiting_checkpoint_and_correlated_wake_survive_reopen_and_are_single_use()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.wait"))).Handle!;
        var lease = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30));
        var checkpoint = new SystemTaskCheckpoint("step.2", "resume.2", "correlation.2", "{\"saved\":true}");

        Assert.True(await store.SaveWaitingAsync(lease!, checkpoint));
        Assert.Equal(1, await fixture.CreateStore().WakeAsync(handle, "correlation.2", "{\"ready\":true}"));
        Assert.Equal(0, await fixture.CreateStore().WakeAsync(handle, "correlation.2", "{\"ready\":false}"));
        var resumed = await fixture.CreateStore().ClaimNextAsync("worker.2", TimeSpan.FromSeconds(30));
        Assert.Equal(checkpoint, resumed!.Checkpoint);
        Assert.Equal("{\"ready\":true}", resumed.WakeJson);
    }

    [Fact]
    public async Task Existing_dependency_blocks_child_until_successful_terminal_state()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var dependency = (await store.EnqueueAsync(Request(fixture, "command.dependency"))).Handle!;
        var child = (await store.EnqueueAsync(Request(fixture, "command.child", dependencies: [dependency]))).Handle!;

        var first = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30));
        Assert.Equal(dependency, first!.Request.Handle);
        Assert.True(await store.CompleteAsync(first, Outcome(1)));
        var next = await fixture.CreateStore().ClaimNextAsync("worker.2", TimeSpan.FromSeconds(30));
        Assert.Equal(child, next!.Request.Handle);
    }

    [Fact]
    public async Task Declared_cancellation_propagation_is_durable_for_unstarted_descendants()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture, "command.parent"))).Handle!;
        var child = (await store.EnqueueAsync(Request(fixture, "command.child", parentCommand: parent.CommandId),
            propagateCancellation: true)).Handle!;

        Assert.True(await store.RequestCancellationAsync(parent, propagate: true));
        Assert.Equal(SystemTaskLifecycleState.Cancelled, (await store.ReadAsync(parent))!.State);
        Assert.Equal(SystemTaskLifecycleState.Cancelled, (await fixture.CreateStore().ReadAsync(child))!.State);
    }

    [Fact]
    public async Task Only_explicit_transient_failure_retries_within_the_bound()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.retry"))).Handle!;
        var first = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30));
        Assert.True(await store.FailAsync(first!, SystemTaskFailureKind.Transient, "TEMPORARY", "Try later."));
        Assert.Null(await store.ClaimNextAsync("worker.2", TimeSpan.FromSeconds(30)));
        fixture.TimeProvider.Advance(TimeSpan.FromSeconds(6));
        var retry = await fixture.CreateStore().ClaimNextAsync("worker.2", TimeSpan.FromSeconds(30));
        Assert.True(await store.FailAsync(retry!, SystemTaskFailureKind.Permanent, "PERMANENT", "Do not retry."));
        Assert.Equal(SystemTaskLifecycleState.Failed, (await store.ReadAsync(handle))!.State);
        Assert.Null(await store.ClaimNextAsync("worker.3", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Pending_host_call_prevents_success_and_becomes_indeterminate_for_reconciliation()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.pending"))).Handle!;
        var lease = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30));
        var journal = await store.BeginHostCallAsync(lease!, "operation.1", "{\"effect\":1}");

        Assert.Equal(SystemTaskHostCallDisposition.NewPending, journal.Disposition);
        Assert.False(await store.CompleteAsync(lease!, Outcome(1)));
        var snapshot = await store.ReadAsync(handle);
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, snapshot!.State);
        Assert.Equal("SYSTEM_TASK_HOST_CALL_PENDING", snapshot.ErrorCode);
    }

    [Fact]
    public async Task Completed_host_call_is_replayed_from_journal_and_allows_terminal_result()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.journal"))).Handle!;
        var lease = await store.ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30));
        var started = await store.BeginHostCallAsync(lease!, "operation.1", "{\"effect\":1}");
        Assert.True(await store.CompleteHostCallAsync(lease!, "operation.1", started.RequestFingerprint, "{\"receipt\":1}"));
        var replay = await fixture.CreateStore().BeginHostCallAsync(lease!, "operation.1", "{\"effect\":1}");

        Assert.Equal(SystemTaskHostCallDisposition.Completed, replay.Disposition);
        Assert.Equal("{\"receipt\":1}", replay.CompletionJson);
        Assert.True(await store.CompleteAsync(lease!, Outcome(1)));
        Assert.Equal(SystemTaskLifecycleState.Completed, (await store.ReadAsync(handle))!.State);
    }

    private static SystemTaskTerminalOutcome Outcome(int value) =>
        new($"{{\"value\":{value}}}", "evidence.fixture", ["bounded evidence"]);

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command, string input = "{}", string? parentCommand = null,
        IReadOnlyList<SystemTaskDurableHandle>? dependencies = null, int maximumOperations = 16) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash,
                [ApplicationIdentifier.Parse("base-app")]),
            "state.1", "grant.1", command, "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(maximumOperations, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime), parentCommand),
        new("procedure.fixture", 1, Hash), input, dependencyHandles: dependencies);
}
