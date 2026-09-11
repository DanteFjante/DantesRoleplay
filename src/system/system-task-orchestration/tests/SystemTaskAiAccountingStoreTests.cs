using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskAiAccountingStoreTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Enqueue_and_ai_enrollment_are_one_caller_owned_atomic_unit()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        SystemTaskDurableHandle? staged = null;
        await fixture.InTransactionAsync(async (connection, transaction) =>
        {
            var profile = fixture.Profile("command.rollback", new(1_000, 2));
            var enqueue = await fixture.Store.StageEnqueueAsync(fixture.Submission(profile), false,
                connection, transaction);
            staged = enqueue.Handle;
            var enrollment = await fixture.Store.StageEnrollAiBudgetAsync(staged!, profile, connection, transaction);
            Assert.True(enrollment.Accepted);
            return 0;
        }, commit: false);

        Assert.Null(await fixture.Store.ReadAsync(staged!));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_ceiling"));
    }

    [Fact]
    public async Task Validation_subject_cannot_enroll_or_mutate_a_workflow_task_account()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 1);
        var handle = await fixture.EnqueueAsync("command.validation-subject", budget, enroll: false);
        var profile = fixture.ValidationProfile(handle.CommandId);

        var result = await fixture.EnrollAsync(handle, profile);

        Assert.False(result.Accepted);
        Assert.Equal("INNER_AI_SUBJECT_UNSUPPORTED", result.Code);
        Assert.Equal(16, profile.Worker.InvocationHost.Budget.RemainingOperations);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_ceiling"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_reservation"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_dispatch_evidence"));
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id=$task",
            ("$task", handle.TaskId)));
        Assert.Equal(SystemTaskLifecycleState.Queued, (await fixture.Store.ReadAsync(handle))!.State);
    }

    [Fact]
    public async Task Child_enrollment_requires_every_ancestor_ceiling_and_cannot_widen_it()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var parent = await fixture.EnqueueAsync("command.parent", new(100, 1), enroll: false);
        var missing = await fixture.TryEnqueueAndEnrollAsync("command.child.missing", new(100, 1), parent.CommandId);
        Assert.False(missing.Result.Accepted);
        Assert.Equal("INNER_AI_ANCESTOR_NOT_ENROLLED", missing.Result.Code);
        Assert.Null(await fixture.Store.ReadAsync(missing.Handle));

        await fixture.EnrollAsync(parent, fixture.Profile(parent.CommandId, new(100, 1)));
        var widened = await Assert.ThrowsAsync<InteractionContractException>(() =>
            fixture.TryEnqueueAndEnrollAsync("command.child.wide", new(101, 1), parent.CommandId));
        Assert.Equal("INNER_AI_BUDGET_EXPANDED", widened.Code);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE command_id = 'command.child.wide'"));

        var deadlineBudget = new SystemInnerWorkerAiBudget(100, 1);
        var taskDeadline = fixture.TimeProvider.GetUtcNow().AddMinutes(30);
        var ceilingDeadline = fixture.TimeProvider.GetUtcNow().AddMinutes(10);
        var childDeadline = fixture.TimeProvider.GetUtcNow().AddMinutes(11);
        var deadlineParent = await fixture.EnqueueAsync("command.deadline.parent", deadlineBudget,
            enroll: false, deadline: taskDeadline);
        var deadlineEnrollment = await fixture.EnrollAsync(deadlineParent,
            fixture.Profile(deadlineParent.CommandId, deadlineBudget, deadline: ceilingDeadline));
        Assert.True(deadlineEnrollment.Accepted);

        var deadlineError = await fixture.TryEnqueueAndEnrollAsync(
            "command.deadline.child", deadlineBudget, deadlineParent.CommandId, childDeadline);
        Assert.False(deadlineError.Result.Accepted);
        Assert.Equal("INNER_AI_DEADLINE_EXPANDED", deadlineError.Result.Code);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE command_id = 'command.deadline.child'"));

        var parentLease = (await fixture.ClaimAsync(2))[deadlineParent.CommandId];
        var reservationError = await fixture.StageReserveAsync(fixture.Reservation(deadlineParent,
            parentLease, deadlineBudget, "reservation.deadline.wide", 1, 0, deadline: childDeadline));
        Assert.False(reservationError.Accepted);
        Assert.Equal("INNER_AI_DEADLINE_EXPANDED", reservationError.Code);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_reservation WHERE task_id=$task",
            ("$task", deadlineParent.TaskId)));
    }

    [Fact]
    public async Task Sibling_provider_holds_share_root_tokens_and_concurrency_while_tools_use_separate_slots()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2,
            SystemInnerWorkerTokenBudgetMode.MeasuredStop, maxConcurrentProviderRequests: 2);
        var root = await fixture.EnqueueAsync("command.root", budget);
        var first = await fixture.EnqueueAsync("command.child.1", budget, parentCommand: root.CommandId);
        var second = await fixture.EnqueueAsync("command.child.2", budget, parentCommand: root.CommandId);
        var third = await fixture.EnqueueAsync("command.child.3", budget, parentCommand: root.CommandId);
        var leases = await fixture.ClaimAsync(4);

        var firstHold = await fixture.ReserveAsync(first, leases[first.CommandId], budget, "provider.1", 400, 0);
        var secondHold = await fixture.ReserveAsync(second, leases[second.CommandId], budget, "provider.2", 400, 0);
        var thirdHold = await Assert.ThrowsAsync<InteractionContractException>(() =>
            fixture.ReserveAsync(third, leases[third.CommandId], budget, "provider.3", 1, 0));
        Assert.Equal(400, firstHold.Reservation!.ProviderTokens);
        Assert.Equal(400, secondHold.Reservation!.ProviderTokens);
        Assert.Equal("INNER_AI_PROVIDER_IN_FLIGHT", thirdHold.Code);
        Assert.Equal(800L, await fixture.ScalarAsync("""
            SELECT SUM(reservation.reserved_provider_tokens)
            FROM system_task_ai_reservation AS reservation
            JOIN system_task_ai_reservation_ancestor AS ancestor
              ON ancestor.record_reference = reservation.record_reference
            WHERE ancestor.ancestor_task_id = $root AND reservation.status = 'reserved'
            """, ("$root", root.TaskId)));

        var firstTool = await fixture.ReserveAsync(root, leases[root.CommandId], budget, "tool.1", 0, 1);
        var secondTool = await fixture.ReserveAsync(first, leases[first.CommandId], budget, "tool.2", 0, 1);
        var thirdTool = await fixture.ReserveAsync(second, leases[second.CommandId], budget, "tool.3", 0, 1);
        Assert.True(firstTool.Accepted);
        Assert.True(secondTool.Accepted);
        Assert.False(thirdTool.Accepted);
        Assert.Equal("INNER_AI_TOOL_BUDGET_EXHAUSTED", thirdTool.Code);

        await fixture.RecordProviderDispatchAsync(firstHold.Reservation.RecordReference,
            leases[first.CommandId], budget);
        var firstUsage = await fixture.RecordProviderOutcomeAsync(firstHold.Reservation.RecordReference,
            leases[first.CommandId], fixture.ProviderOutcome(400, 0, 400, complete: true));
        Assert.True(firstUsage.Accepted);
        var finalHold = await fixture.ReserveAsync(third, leases[third.CommandId], budget,
            "provider.final", 200, 0);
        Assert.True(finalHold.Accepted);
        var toolBlockedByHeldTokens = await fixture.ReserveAsync(second, leases[second.CommandId], budget,
            "tool.blocked-by-tokens", 0, 1);
        Assert.False(toolBlockedByHeldTokens.Accepted);
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", toolBlockedByHeldTokens.Code);
    }

    [Fact]
    public async Task Reservation_replay_is_inert_while_conflict_stale_fence_and_missing_hard_cap_bound_fail_closed()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var measured = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.replay", measured);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var request = fixture.Reservation(handle, lease, measured, "reservation.same", 100, 0);

        var first = await fixture.StageReserveAsync(request);
        var debit = await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id = $task", ("$task", handle.TaskId));
        var replay = await fixture.StageReserveAsync(request);
        var afterReplay = await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id = $task", ("$task", handle.TaskId));
        Assert.True(first.Accepted);
        Assert.True(replay.Accepted);
        Assert.Equal("INNER_AI_RESERVATION_EXISTING", replay.Code);
        Assert.Equal(debit, afterReplay);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_reservation"));

        var changed = await fixture.StageReserveAsync(fixture.Reservation(handle, lease, measured,
            "reservation.same", 101, 0));
        Assert.False(changed.Accepted);
        Assert.Equal("INNER_AI_RESERVATION_CONFLICT", changed.Code);
        var staleAttempt = new SystemTaskAttemptIdentity(lease.Attempt.StableCommandId, lease.Attempt.AttemptId,
            lease.Attempt.LeaseToken, lease.Attempt.FencingCounter + 1, lease.Attempt.LeaseExpiresAtUtc);
        var stale = await fixture.StageReserveAsync(new(fixture.Host(handle.CommandId), handle, staleAttempt,
            "reservation.stale", measured, 100, 0));
        Assert.False(stale.Accepted);
        Assert.Equal("INNER_AI_LEASE_STALE", stale.Code);

        var hard = new SystemInnerWorkerAiBudget(100, 1, SystemInnerWorkerTokenBudgetMode.HardCap);
        var hardHandle = await fixture.EnqueueAsync("command.hard", hard);
        var hardLease = (await fixture.ClaimAsync(1))[hardHandle.CommandId];
        var unbounded = await fixture.StageReserveAsync(fixture.Reservation(hardHandle, hardLease, hard,
            "reservation.unbounded", 50, 0));
        Assert.False(unbounded.Accepted);
        Assert.Equal("INNER_AI_PROVIDER_BOUND_UNAVAILABLE", unbounded.Code);
    }

    [Fact]
    public async Task Narrow_reservation_deadline_blocks_new_dispatch_but_preserves_replay_and_late_accounting()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 1);
        var taskDeadline = fixture.TimeProvider.GetUtcNow().AddMinutes(30);
        var reservationDeadline = fixture.TimeProvider.GetUtcNow().AddMinutes(2);
        var profile = fixture.Profile("command.deadline", budget, deadline: taskDeadline);
        var handle = await fixture.EnqueueAsync("command.deadline", budget, deadline: taskDeadline);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var expiresBeforeDispatch = fixture.Reservation(handle, lease, budget, "reservation.expires", 100, 0,
            deadline: reservationDeadline);
        var dispatched = fixture.Reservation(handle, lease, budget, "reservation.dispatched", 100, 0,
            deadline: reservationDeadline);
        var first = await fixture.StageReserveAsync(expiresBeforeDispatch);
        var second = await fixture.StageReserveAsync(dispatched);
        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.True((await fixture.RecordProviderDispatchAsync(second.Reservation!.RecordReference,
            lease, budget, profile)).Accepted);
        fixture.TimeProvider.Advance(TimeSpan.FromMinutes(3));

        var expired = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageRecordAiProviderDispatchAsync(first.Reservation!.RecordReference,
                lease.Attempt, profile, fixture.ProviderCall("too late"), connection, transaction));
        var replay = await fixture.StageReserveAsync(expiresBeforeDispatch);
        Assert.Null(await fixture.Store.ClaimNextAsync("worker.normalize-expired-ai", TimeSpan.FromMinutes(10)));
        var late = await fixture.RecordProviderOutcomeAsync(second.Reservation.RecordReference, lease,
            fixture.ProviderOutcome(20, 10, 30, complete: true));

        Assert.False(expired.Accepted);
        Assert.Equal("INNER_AI_DEADLINE_EXPIRED", expired.Code);
        Assert.True(replay.Accepted);
        Assert.Equal("INNER_AI_RESERVATION_EXISTING", replay.Code);
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT COUNT(*) FROM system_task_ai_reservation
            WHERE record_reference=$reference AND status='unknown'
                AND charged_provider_tokens=reserved_provider_tokens
            """, ("$reference", first.Reservation!.RecordReference)));
        Assert.True(late.Accepted);
        Assert.Equal(30, late.Usage!.ChargedProviderTokens);
        Assert.Equal(0L, await fixture.ScalarAsync("""
            SELECT COUNT(*) FROM system_task_ai_dispatch_evidence
            WHERE record_reference=$reference
            """, ("$reference", first.Reservation!.RecordReference)));
    }

    [Fact]
    public async Task Unknown_observation_retains_the_full_hold_and_blocks_new_root_dispatch()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.unknown", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.unknown", 400, 0);
        var reference = reservation.Reservation!.RecordReference;
        Assert.True((await fixture.RecordProviderDispatchAsync(reference, lease, budget)).Accepted);

        var unknown = await fixture.RecordProviderOutcomeAsync(reference, lease,
            new(AiDispatchCompletionKind.Threw, null, "PROVIDER_FAILED"));
        var blocked = await fixture.ReserveAsync(handle, lease, budget, "reservation.blocked", 1, 0);

        Assert.True(unknown.Accepted);
        Assert.True(unknown.Usage!.RequiresReconciliation);
        Assert.Equal(400, unknown.Usage.ChargedProviderTokens);
        Assert.False(blocked.Accepted);
        Assert.Equal("INNER_AI_RECONCILIATION_REQUIRED", blocked.Code);
        Assert.False(await fixture.Store.CompleteAsync(lease, new("{}", "evidence.blocked")));
        Assert.Equal(SystemTaskLifecycleState.Indeterminate, (await fixture.Store.ReadAsync(handle))!.State);
    }

    [Fact]
    public async Task Partial_then_credible_known_usage_releases_unused_hold_but_lower_or_conflicting_evidence_does_not()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.known", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.known", 100, 0);
        var reference = reservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(reference, lease, budget);

        var partial = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(20, 10, 30, complete: false));
        var lower = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(19, 10, 29, complete: true));
        Assert.True(partial.Usage!.RequiresReconciliation);
        Assert.False(lower.Accepted);
        Assert.Equal("INNER_AI_USAGE_CONFLICT", lower.Code);
        Assert.Equal(100L, await fixture.ScalarAsync(
            "SELECT charged_provider_tokens FROM system_task_ai_reservation WHERE record_reference=$reference",
            ("$reference", reference)));

        var known = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(30, 10, 40, complete: true));
        var conflictingKnown = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(31, 10, 41, complete: true));
        Assert.True(known.Accepted);
        Assert.True(known.Usage!.UsageKnown);
        Assert.Equal(40, known.Usage.ChargedProviderTokens);
        Assert.Equal(60, known.Usage.ReleasedProviderTokens);
        Assert.False(conflictingKnown.Accepted);
        Assert.Equal("INNER_AI_USAGE_CONFLICT", conflictingKnown.Code);
        Assert.Equal(40L, await fixture.ScalarAsync(
            "SELECT charged_provider_tokens FROM system_task_ai_reservation WHERE record_reference=$reference",
            ("$reference", reference)));
    }

    [Fact]
    public async Task Caller_report_without_persisted_observation_cannot_settle_and_complete_overrun_is_unclamped()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var first = await fixture.EnqueueAsync("command.report", budget);
        var second = await fixture.EnqueueAsync("command.overrun", budget);
        var leases = await fixture.ClaimAsync(2);
        var reportReservation = await fixture.ReserveAsync(first, leases[first.CommandId], budget,
            "reservation.report", 100, 0);
        var reportReference = reportReservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(reportReference, leases[first.CommandId], budget);

        var unsupported = await fixture.ReconcileAsync(first,
            new(reportReference, leases[first.CommandId].Attempt, 10, 10, 0, 20, true));
        Assert.True(unsupported.Accepted);
        Assert.Equal("INNER_AI_USAGE_EVIDENCE_UNAVAILABLE", unsupported.Code);
        Assert.True(unsupported.Usage!.RequiresReconciliation);
        Assert.Equal(100, unsupported.Usage.ChargedProviderTokens);

        var overrunReservation = await fixture.ReserveAsync(second, leases[second.CommandId], budget,
            "reservation.overrun", 100, 0);
        var overrunReference = overrunReservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(overrunReference, leases[second.CommandId], budget);
        var overrun = await fixture.RecordProviderOutcomeAsync(overrunReference, leases[second.CommandId],
            fixture.ProviderOutcome(100, 50, 150, complete: true));
        var followup = await fixture.ReserveAsync(second, leases[second.CommandId], budget,
            "reservation.after-overrun", 1, 0);
        Assert.True(overrun.Accepted);
        Assert.Equal(150, overrun.Usage!.ChargedProviderTokens);
        Assert.True(overrun.Usage.ExceededReservation);
        Assert.False(overrun.Usage.RequiresReconciliation);
        Assert.True(followup.Accepted);
    }

    [Fact]
    public async Task Late_usage_settles_original_reservation_without_reviving_stale_lease()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.late", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.late", 100, 0);
        await fixture.RecordProviderDispatchAsync(reservation.Reservation!.RecordReference, lease, budget);
        fixture.TimeProvider.Advance(TimeSpan.FromMinutes(11));

        var late = await fixture.RecordProviderOutcomeAsync(reservation.Reservation.RecordReference, lease,
            fixture.ProviderOutcome(20, 10, 30, complete: true));

        Assert.True(late.Accepted);
        Assert.Equal(30, late.Usage!.ChargedProviderTokens);
        Assert.False(await fixture.Store.CompleteAsync(lease, new("{}", "evidence.stale")));
    }

    [Fact]
    public async Task Trusted_not_started_observation_releases_hold()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.not-started", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.not-started", 100, 0);
        await fixture.RecordProviderDispatchAsync(reservation.Reservation!.RecordReference, lease, budget);

        var result = await fixture.RecordProviderOutcomeAsync(reservation.Reservation.RecordReference,
            lease, new(AiDispatchCompletionKind.NotStarted, null));

        Assert.True(result.Accepted);
        Assert.True(result.Usage!.UsageKnown);
        Assert.Equal(0, result.Usage.ChargedProviderTokens);
        Assert.Equal(100, result.Usage.ReleasedProviderTokens);
    }

    [Fact]
    public async Task Known_overrun_does_not_block_terminal_wait_status_or_cancellation_paths()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var completed = await fixture.EnqueueAsync("command.lifecycle.complete", budget);
        var waiting = await fixture.EnqueueAsync("command.lifecycle.wait", budget);
        var cancelled = await fixture.EnqueueAsync("command.lifecycle.cancel", budget);
        var leases = await fixture.ClaimAsync(3);

        foreach (var handle in new[] { completed, waiting, cancelled })
        {
            var lease = leases[handle.CommandId];
            var reservation = await fixture.ReserveAsync(handle, lease, budget,
                "reservation." + handle.CommandId, 100, 0);
            await fixture.RecordProviderDispatchAsync(reservation.Reservation!.RecordReference, lease, budget);
            var known = await fixture.RecordProviderOutcomeAsync(reservation.Reservation.RecordReference, lease,
                fixture.ProviderOutcome(100, 50, 150, complete: true));
            Assert.True(known.Accepted);
            Assert.False(known.Usage!.RequiresReconciliation);
            Assert.Equal(SystemTaskLifecycleState.Running, (await fixture.Store.ReadAsync(handle))!.State);
        }

        Assert.True(await fixture.Store.CompleteAsync(leases[completed.CommandId],
            new("{\"done\":true}", "evidence.complete")));
        Assert.True(await fixture.Store.SaveWaitingAsync(leases[waiting.CommandId],
            new("checkpoint.one", "handler.one", "correlation.one", "{}")));
        Assert.Equal(SystemTaskLifecycleState.Waiting, (await fixture.Store.ReadAsync(waiting))!.State);
        Assert.True(await fixture.Store.RequestCancellationAsync(cancelled, propagate: true));
        Assert.True(await fixture.Store.AcknowledgeCancellationAsync(leases[cancelled.CommandId]));
        Assert.Equal(SystemTaskLifecycleState.Cancelled, (await fixture.Store.ReadAsync(cancelled))!.State);
    }

    [Fact]
    public async Task Settled_ancestor_provider_and_tool_usage_exhausts_each_shared_budget_without_reopening_holds()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(100, 1);
        var root = await fixture.EnqueueAsync("command.exhaust.root", budget);
        var first = await fixture.EnqueueAsync("command.exhaust.first", budget, parentCommand: root.CommandId);
        var second = await fixture.EnqueueAsync("command.exhaust.second", budget, parentCommand: root.CommandId);
        var leases = await fixture.ClaimAsync(3);

        var tool = await fixture.ReserveAsync(first, leases[first.CommandId], budget,
            "reservation.exhaust.tool", 0, 1);
        await fixture.RecordToolDispatchAsync(tool.Reservation!.RecordReference, leases[first.CommandId], budget);
        var toolUsage = await fixture.RecordToolOutcomeAsync(tool.Reservation.RecordReference,
            leases[first.CommandId], new(AiDispatchCompletionKind.Threw, null, "TOOL_FAILED"));
        Assert.True(toolUsage.Accepted);
        Assert.Equal(1, toolUsage.Usage!.ChargedToolCalls);
        var toolExhausted = await fixture.ReserveAsync(second, leases[second.CommandId], budget,
            "reservation.exhaust.tool.next", 0, 1);
        Assert.False(toolExhausted.Accepted);
        Assert.Equal("INNER_AI_TOOL_BUDGET_EXHAUSTED", toolExhausted.Code);

        var provider = await fixture.ReserveAsync(first, leases[first.CommandId], budget,
            "reservation.exhaust.provider", 100, 0);
        await fixture.RecordProviderDispatchAsync(provider.Reservation!.RecordReference, leases[first.CommandId], budget);
        var providerUsage = await fixture.RecordProviderOutcomeAsync(provider.Reservation.RecordReference,
            leases[first.CommandId], fixture.ProviderOutcome(100, 50, 150, complete: true));
        Assert.True(providerUsage.Accepted);
        Assert.False(providerUsage.Usage!.RequiresReconciliation);
        var providerExhausted = await Assert.ThrowsAsync<InteractionContractException>(() => fixture.ReserveAsync(
            second, leases[second.CommandId], budget, "reservation.exhaust.provider.next", 1, 0));
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", providerExhausted.Code);
        var toolStoppedByTokens = await fixture.ReserveAsync(second, leases[second.CommandId], budget,
            "reservation.exhaust.tool.after-tokens", 0, 1);
        Assert.False(toolStoppedByTokens.Accepted);
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", toolStoppedByTokens.Code);
    }

    [Theory]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET total_tokens=35 WHERE record_reference=$reference AND sequence=1", "INNER_AI_USAGE_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET is_complete=0 WHERE record_reference=$reference AND sequence=1", "INNER_AI_USAGE_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET provider_id='provider.changed' WHERE record_reference=$reference AND sequence=0", "INNER_AI_DISPATCH_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET model_id='model.changed' WHERE record_reference=$reference AND sequence=0", "INNER_AI_DISPATCH_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET profile_fingerprint='BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB' WHERE record_reference=$reference AND sequence=0", "INNER_AI_DISPATCH_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET payload_fingerprint='BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB' WHERE record_reference=$reference AND sequence=0", "INNER_AI_DISPATCH_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET payload_fingerprint='BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB' WHERE record_reference=$reference AND sequence=1", "INNER_AI_USAGE_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET response_fingerprint='BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB' WHERE record_reference=$reference AND sequence=1", "INNER_AI_USAGE_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET event_reference='ai-dispatch.changed' WHERE record_reference=$reference AND sequence=0", "INNER_AI_DISPATCH_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_dispatch_evidence SET event_reference='ai-observation.changed' WHERE record_reference=$reference AND sequence=1", "INNER_AI_USAGE_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_reservation SET charged_provider_tokens=31 WHERE record_reference=$reference", "INNER_AI_SETTLEMENT_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_reservation SET status='exceeded' WHERE record_reference=$reference", "INNER_AI_SETTLEMENT_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_reservation SET settled_evidence_sequence=2 WHERE record_reference=$reference", "INNER_AI_SETTLEMENT_EVIDENCE_INVALID")]
    [InlineData("UPDATE system_task_ai_reservation SET fencing_counter=fencing_counter+1 WHERE record_reference=$reference", "INNER_AI_ATTEMPT_MISMATCH")]
    public async Task Corrupted_evidence_or_settlement_cache_rejects_reconcile_admission_and_completion_without_mutation(
        string corruptionSql, string expectedCode)
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.integrity", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.integrity", 100, 0);
        var reference = reservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(reference, lease, budget);
        var settled = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(20, 10, 40, complete: true));
        Assert.True(settled.Accepted);
        Assert.Equal(40, settled.Usage!.ChargedProviderTokens);

        await fixture.ExecuteAsync(corruptionSql, ("$reference", reference));
        var retainedAfterCorruption = await fixture.ReservationStateAsync(reference);
        var report = new SystemInnerWorkerAiUsageReport(reference, lease.Attempt, 20, 10, 0, 40, true);

        await AssertIntegrityFailureAsync(expectedCode, () => fixture.ReconcileAsync(handle, report));
        await AssertIntegrityFailureAsync(expectedCode, () => fixture.ReserveAsync(
            handle, lease, budget, "reservation.after-corruption", 1, 0));
        await AssertIntegrityFailureAsync(expectedCode, () => fixture.Store.CompleteAsync(
            lease, new("{}", "evidence.integrity")));

        Assert.Equal(retainedAfterCorruption, await fixture.ReservationStateAsync(reference));
        Assert.Equal(2L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference",
            ("$reference", reference)));
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_reservation WHERE reservation_id='reservation.after-corruption'"));
        Assert.Equal(SystemTaskLifecycleState.Running, (await fixture.Store.ReadAsync(handle))!.State);
    }

    [Fact]
    public async Task Forged_complete_flag_cannot_turn_uncertain_usage_into_a_released_hold()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 2);
        var handle = await fixture.EnqueueAsync("command.integrity.unknown", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget,
            "reservation.integrity.unknown", 100, 0);
        var reference = reservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(reference, lease, budget);
        var uncertain = await fixture.RecordProviderOutcomeAsync(reference, lease,
            fixture.ProviderOutcome(20, 10, 40, complete: false));
        Assert.True(uncertain.Accepted);
        Assert.True(uncertain.Usage!.RequiresReconciliation);
        Assert.Equal(100, uncertain.Usage.ChargedProviderTokens);

        await fixture.ExecuteAsync("""
            UPDATE system_task_ai_dispatch_evidence SET is_complete=1
            WHERE record_reference=$reference AND sequence=1
            """, ("$reference", reference));
        var retained = await fixture.ReservationStateAsync(reference);
        var report = new SystemInnerWorkerAiUsageReport(reference, lease.Attempt, 20, 10, 0, 40, true);

        await AssertIntegrityFailureAsync("INNER_AI_USAGE_EVIDENCE_INVALID", () =>
            fixture.ReconcileAsync(handle, report));
        await AssertIntegrityFailureAsync("INNER_AI_USAGE_EVIDENCE_INVALID", () => fixture.ReserveAsync(
            handle, lease, budget, "reservation.integrity.forged-release", 1, 0));
        await AssertIntegrityFailureAsync("INNER_AI_USAGE_EVIDENCE_INVALID", () =>
            fixture.Store.CompleteAsync(lease, new("{}", "evidence.integrity.unknown")));

        Assert.Equal(retained, await fixture.ReservationStateAsync(reference));
        Assert.Equal(100L, await fixture.ScalarAsync(
            "SELECT charged_provider_tokens FROM system_task_ai_reservation WHERE record_reference=$reference",
            ("$reference", reference)));
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_reservation WHERE reservation_id='reservation.integrity.forged-release'"));
    }

    [Fact]
    public async Task Missing_root_membership_cannot_hide_a_sibling_charge()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(100, 2);
        var root = await fixture.EnqueueAsync("command.membership.root", budget);
        var charged = await fixture.EnqueueAsync("command.membership.charged", budget, root.CommandId);
        var sibling = await fixture.EnqueueAsync("command.membership.sibling", budget, root.CommandId);
        var leases = await fixture.ClaimAsync(3);
        var reservation = await fixture.ReserveAsync(charged, leases[charged.CommandId], budget,
            "reservation.membership", 80, 0);
        var reference = reservation.Reservation!.RecordReference;
        await fixture.RecordProviderDispatchAsync(reference, leases[charged.CommandId], budget);
        var usage = await fixture.RecordProviderOutcomeAsync(reference, leases[charged.CommandId],
            fixture.ProviderOutcome(40, 40, 80, complete: true));
        Assert.True(usage.Accepted);

        await fixture.ExecuteAsync("""
            DELETE FROM system_task_ai_reservation_ancestor
            WHERE record_reference=$reference AND ancestor_task_id=$root
            """, ("$reference", reference), ("$root", root.TaskId));
        var retainedAfterCorruption = await fixture.ReservationStateAsync(reference);

        await AssertIntegrityFailureAsync("INNER_AI_ANCESTOR_MEMBERSHIP_INVALID", () => fixture.ReserveAsync(
            sibling, leases[sibling.CommandId], budget, "reservation.membership.hidden", 1, 0));
        await AssertIntegrityFailureAsync("INNER_AI_ANCESTOR_MEMBERSHIP_INVALID", () =>
            fixture.Store.CompleteAsync(leases[charged.CommandId], new("{}", "evidence.membership")));

        Assert.Equal(retainedAfterCorruption, await fixture.ReservationStateAsync(reference));
        Assert.Equal(80L, await fixture.ScalarAsync(
            "SELECT charged_provider_tokens FROM system_task_ai_reservation WHERE record_reference=$reference",
            ("$reference", reference)));
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_reservation WHERE reservation_id='reservation.membership.hidden'"));
    }

    [Fact]
    public async Task Oversize_provider_request_is_rejected_without_truncation_or_dispatch_evidence()
    {
        await using var fixture = await AccountingFixture.CreateAsync();
        var budget = new SystemInnerWorkerAiBudget(1_000, 1);
        var handle = await fixture.EnqueueAsync("command.large", budget);
        var lease = (await fixture.ClaimAsync(1))[handle.CommandId];
        var reservation = await fixture.ReserveAsync(handle, lease, budget, "reservation.large", 100, 0);
        var profile = fixture.Profile(handle.CommandId, budget);
        var call = fixture.ProviderCall(new string('\u00e9', 40_000));

        var error = await Assert.ThrowsAsync<InteractionContractException>(() => fixture.InTransactionAsync(
            (connection, transaction) => fixture.Store.StageRecordAiProviderDispatchAsync(
                reservation.Reservation!.RecordReference, lease.Attempt, profile, call, connection, transaction)));

        Assert.Equal("JSON_TOO_LARGE", error.Code);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference",
            ("$reference", reservation.Reservation!.RecordReference)));
    }

    private static async Task AssertIntegrityFailureAsync(string expectedCode, Func<Task> action)
    {
        var failure = await Assert.ThrowsAsync<SystemTaskException>(action);
        Assert.Equal(expectedCode, failure.Code);
    }

    private sealed class AccountingFixture : IAsyncDisposable
    {
        private const string Schema = "{\"type\":\"object\"}";
        private readonly SystemTaskAiAccountingSchemaFixture schema;

        private AccountingFixture(SystemTaskAiAccountingSchemaFixture schema, MutableTimeProvider timeProvider)
        {
            this.schema = schema;
            TimeProvider = timeProvider;
            Store = new(schema.ConnectionString, timeProvider);
        }

        internal MutableTimeProvider TimeProvider { get; }
        internal SqliteSystemTaskLifecycleStore Store { get; }

        internal static async Task<AccountingFixture> CreateAsync() => new(
            await SystemTaskAiAccountingSchemaFixture.CreateAsync(),
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)));

        internal InteractionInvocationHost Host(string command, string? parentCommand = null,
            DateTimeOffset? deadline = null) => new(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", command, "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16,
                (deadline ?? TimeProvider.GetUtcNow().AddHours(1)).UtcDateTime), parentCommand);

        internal SystemInnerWorkerResolvedProfile Profile(string command, SystemInnerWorkerAiBudget budget,
            string? parentCommand = null, DateTimeOffset? deadline = null)
        {
            var worker = new SystemInnerWorkerRequest(Host(command, parentCommand, deadline),
                new("fixture-app.jobs.procedure-one", 1, Hash), "{\"work\":true}", Schema);
            return new(worker, new("fixture-app.jobs.worker-profile", 1, Hash),
                new("fixture-app.jobs.worker-profile", "Worker", "Host identity", "Host instructions."),
                HashOf(Schema), [], [], new("manual.packet.1", Hash),
                new("authority.1", "grant-revision.1", Hash), budget);
        }

        internal SystemInnerWorkerResolvedProfile ValidationProfile(string command)
        {
            var host = InteractionInvocationHost.ForApplication(
                TrustedPrincipalContext.VerifiedPrincipal(
                    "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "fixture"),
                new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
                "grant.1", command, InteractionExecutionProfile.ReadOnly,
                new InteractionInvocationBudget(16, TimeProvider.GetUtcNow().AddHours(1).UtcDateTime));
            var subject = new SystemInnerWorkerSubject.ApplicationCandidateValidation(new(
                host.ApplicationRevision.ApplicationId, new string('a', 32), 1, Hash));
            var worker = new SystemInnerWorkerRequest(subject, host, "{\"work\":true}", Schema);
            return new(worker, SystemInnerWorkerCandidateReviewer.ProfileVersion,
                SystemInnerWorkerCandidateReviewer.Profile, HashOf(Schema), [], [],
                new("manual.packet.1", Hash), new("authority.validation", "grant-revision.1", Hash),
                new SystemInnerWorkerAiBudget(toolCalls: 0),
                new("authority.read", "grant-revision.1", Hash));
        }

        internal SystemTaskDurableSubmissionRequest Submission(SystemInnerWorkerResolvedProfile profile)
        {
            var workflow = Assert.IsType<SystemInnerWorkerSubject.ProcedureWorkflow>(profile.Worker.Subject);
            return new(profile.Worker.InvocationHost, workflow.ProcedureVersion, profile.Worker.InputJson,
                dependencyHandles: profile.Worker.DependencyHandles);
        }

        internal async Task<SystemTaskDurableHandle> EnqueueAsync(string command, SystemInnerWorkerAiBudget budget,
            string? parentCommand = null, bool enroll = true, DateTimeOffset? deadline = null)
        {
            var profile = Profile(command, budget, parentCommand, deadline);
            return await InTransactionAsync(async (connection, transaction) =>
            {
                var enqueue = await Store.StageEnqueueAsync(Submission(profile), false, connection, transaction);
                Assert.NotNull(enqueue.Handle);
                if (enroll)
                {
                    var enrollment = await Store.StageEnrollAiBudgetAsync(enqueue.Handle!, profile, connection, transaction);
                    Assert.True(enrollment.Accepted, enrollment.Code);
                }
                return enqueue.Handle!;
            });
        }

        internal async Task<(SystemTaskDurableHandle Handle, SystemTaskAiAccountingResult Result)> TryEnqueueAndEnrollAsync(
            string command, SystemInnerWorkerAiBudget budget, string parentCommand, DateTimeOffset? deadline = null)
        {
            var profile = Profile(command, budget, parentCommand, deadline);
            return await InTransactionAsync(async (connection, transaction) =>
            {
                var enqueue = await Store.StageEnqueueAsync(Submission(profile), false, connection, transaction);
                var result = await Store.StageEnrollAiBudgetAsync(enqueue.Handle!, profile, connection, transaction);
                return (enqueue.Handle!, result);
            }, commit: false);
        }

        internal Task<SystemTaskAiAccountingResult> EnrollAsync(SystemTaskDurableHandle handle,
            SystemInnerWorkerResolvedProfile profile) => InTransactionAsync((connection, transaction) =>
            Store.StageEnrollAiBudgetAsync(handle, profile, connection, transaction));

        internal async Task<IReadOnlyDictionary<string, SystemTaskLease>> ClaimAsync(int count)
        {
            var values = new Dictionary<string, SystemTaskLease>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var lease = await Store.ClaimNextAsync("worker." + index, TimeSpan.FromMinutes(10));
                Assert.NotNull(lease);
                values.Add(lease!.Request.Handle.CommandId, lease);
            }
            return values;
        }

        internal SystemInnerWorkerAiReservationRequest Reservation(SystemTaskDurableHandle handle,
            SystemTaskLease lease, SystemInnerWorkerAiBudget budget, string id, int tokens, int tools,
            SystemInnerWorkerAiProviderTokenBound? bound = null, DateTimeOffset? deadline = null) =>
            new(Host(handle.CommandId, deadline: deadline), handle, lease.Attempt, id, budget, tokens, tools, bound);

        internal Task<SystemTaskAiAccountingResult> ReserveAsync(SystemTaskDurableHandle handle,
            SystemTaskLease lease, SystemInnerWorkerAiBudget budget, string id, int tokens, int tools,
            SystemInnerWorkerAiProviderTokenBound? bound = null) =>
            StageReserveAsync(Reservation(handle, lease, budget, id, tokens, tools, bound));

        internal Task<SystemTaskAiAccountingResult> StageReserveAsync(SystemInnerWorkerAiReservationRequest request) =>
            InTransactionAsync((connection, transaction) =>
                Store.StageReserveAiBudgetAsync(request, connection, transaction));

        internal Task<SystemTaskAiAccountingResult> RecordProviderDispatchAsync(string reference,
            SystemTaskLease lease, SystemInnerWorkerAiBudget budget, SystemInnerWorkerResolvedProfile? profile = null) =>
            InTransactionAsync((connection, transaction) =>
                Store.StageRecordAiProviderDispatchAsync(reference, lease.Attempt,
                    profile ?? Profile(lease.Request.Handle.CommandId, budget), ProviderCall("request"), connection, transaction));

        internal Task<SystemTaskAiAccountingResult> RecordProviderOutcomeAsync(string reference,
            SystemTaskLease lease, AiProviderCallObservation outcome) => InTransactionAsync((connection, transaction) =>
                Store.StageRecordAiProviderOutcomeAsync(reference, lease.Attempt, outcome, connection, transaction));

        internal Task<SystemTaskAiAccountingResult> RecordToolDispatchAsync(string reference,
            SystemTaskLease lease, SystemInnerWorkerAiBudget budget) => InTransactionAsync((connection, transaction) =>
                Store.StageRecordAiToolDispatchAsync(reference, lease.Attempt,
                    Profile(lease.Request.Handle.CommandId, budget), new(1,
                        new("read_value", "Fixture tool", Schema),
                        new("call.1", "read_value", JsonDocument.Parse("{}").RootElement.Clone(), AiRequestKind.Task)),
                    connection, transaction));

        internal Task<SystemTaskAiAccountingResult> RecordToolOutcomeAsync(string reference,
            SystemTaskLease lease, AiToolDispatchObservation outcome) => InTransactionAsync((connection, transaction) =>
                Store.StageRecordAiToolOutcomeAsync(reference, lease.Attempt, outcome, connection, transaction));

        internal Task<SystemTaskAiAccountingResult> ReconcileAsync(SystemTaskDurableHandle handle,
            SystemInnerWorkerAiUsageReport report) => InTransactionAsync((connection, transaction) =>
                Store.StageReconcileAiUsageAsync(Host(handle.CommandId), report, connection, transaction));

        internal AiProviderCallDescriptor ProviderCall(string content) => new("provider.1", 0,
            new("model.1", [new(AiMessageRole.User, content)], AiRequestKind.Task,
                AiReasoningEffort.None, Schema, [], null, 128));

        internal AiProviderCallObservation ProviderOutcome(long input, long output, long total, bool complete) =>
            new(AiDispatchCompletionKind.Returned,
                new(true, null, "ok", "{}", [], Usage: new(input, output, total, complete)));

        internal async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<string> ReservationStateAsync(string reference)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT status,charged_provider_tokens,charged_tool_calls,settled_evidence_sequence,
                    fencing_counter,updated_at_utc
                FROM system_task_ai_reservation WHERE record_reference=$reference
                """;
            command.Parameters.AddWithValue("$reference", reference);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index =>
                reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        internal async Task<T> InTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action,
            bool commit = true)
        {
            await using var connection = await schema.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await action(connection, transaction);
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
            return result;
        }

        public ValueTask DisposeAsync() => schema.DisposeAsync();

        private static string HashOf(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    }
}
