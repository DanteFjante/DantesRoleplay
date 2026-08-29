using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class TriggerSchedulingWorkerTests : IDisposable
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Unavailable_default_participant_ignores_due_targets_but_finalizes_misses()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendOneTimeTriggerAsync(Trigger("trigger.session.end", Now));
        await store.AppendOneTimeTriggerAsync(Trigger("trigger.session.skipped", Now.AddSeconds(-1),
            TriggerMisfirePolicy.Skip));
        var worker = Worker(db, clock, new UnavailableParticipant());

        var result = await worker.RunBatchAsync("worker.default");

        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Missed);
        Assert.Equal("missed", Assert.Single(db.TriggerFireWork).State);
        Assert.Equal("trigger.session.skipped", Assert.Single(db.TriggerFireReceipts).TriggerId);
        Assert.DoesNotContain(db.TriggerFireWork, value => value.TriggerId == "trigger.session.end");
    }

    [Fact]
    public async Task Due_success_stages_participant_receipt_and_terminal_work_atomically()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        var participant = new RecordingParticipant(db, clock);

        var result = await Worker(db, clock, participant).RunBatchAsync("worker.success");

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Completed);
        Assert.Equal(1, participant.Calls);
        var work = Assert.Single(db.TriggerFireWork);
        Assert.Equal("completed", work.State);
        Assert.Equal(1, work.AttemptCount);
        Assert.Null(work.LeaseToken);
        Assert.Equal("due", Assert.Single(db.TriggerFireReceipts).Disposition);
        Assert.Single(db.Operations.Where(value => value.Tool == "trigger-test"));
    }

    [Fact]
    public async Task Transient_attempts_run_immediately_then_after_five_and_thirty_seconds()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        var participant = new SequenceParticipant(
            TriggerFireAttemptResult.Transient(),
            TriggerFireAttemptResult.Transient(TriggerFireFailureKind.TransientDatabase),
            TriggerFireAttemptResult.Transient());
        var worker = Worker(db, clock, participant);

        Assert.Equal(1, (await worker.RunBatchAsync("worker.retry")).Retried);
        Assert.Equal(Now.AddSeconds(5).UtcDateTime, db.TriggerFireWork.Single().NextAttemptAtUtc);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, (await worker.RunBatchAsync("worker.retry")).Examined);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await worker.RunBatchAsync("worker.retry")).Retried);
        Assert.Equal(Now.AddSeconds(35).UtcDateTime, db.TriggerFireWork.Single().NextAttemptAtUtc);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, (await worker.RunBatchAsync("worker.retry")).Examined);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, (await worker.RunBatchAsync("worker.retry")).Failed);
        var exhausted = db.TriggerFireWork.Single();
        Assert.Equal("failed", exhausted.State);
        Assert.Equal(3, exhausted.AttemptCount);
        Assert.Equal("attempts-exhausted", exhausted.FailureKind);
        Assert.Equal(3, participant.Calls);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Permanent_failure_is_terminal_after_one_attempt()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        var participant = new SequenceParticipant(TriggerFireAttemptResult.Permanent());

        var result = await Worker(db, clock, participant).RunBatchAsync("worker.permanent");

        Assert.Equal(1, result.Failed);
        var work = Assert.Single(db.TriggerFireWork);
        Assert.Equal("failed", work.State);
        Assert.Equal(1, work.AttemptCount);
        Assert.Equal("permanent-handler", work.FailureKind);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Skip_and_expired_fire_once_miss_while_bounded_fire_once_catches_up()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendOneTimeTriggerAsync(Trigger("trigger.skip", Now.AddSeconds(-1), TriggerMisfirePolicy.Skip));
        await store.AppendOneTimeTriggerAsync(Trigger("trigger.expired", Now.AddHours(-24).AddSeconds(-1)));
        await store.AppendOneTimeTriggerAsync(Trigger("trigger.catch-up", Now.AddHours(-23)));

        var result = await Worker(db, clock, new RecordingParticipant(db, clock))
            .RunBatchAsync("worker.misfire");

        Assert.Equal(2, result.Missed);
        Assert.Equal(1, result.Completed);
        Assert.Equal(3, db.TriggerFireWork.Count());
        Assert.Equal(2, db.TriggerFireWork.Count(value => value.State == "missed"));
        Assert.Equal(1, db.TriggerFireWork.Count(value => value.State == "completed"));
        Assert.Equal(2, db.TriggerFireReceipts.Count(value => value.Disposition == "missed"));
        Assert.Equal(1, db.TriggerFireReceipts.Count(value => value.Disposition == "due"));
    }

    [Fact]
    public async Task Participant_staging_rolls_back_when_the_lease_expires_then_restart_reclaims()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        var expiring = new RecordingParticipant(db, clock, advanceDuringStage: TimeSpan.FromSeconds(61));

        var expired = await Worker(db, clock, expiring).RunBatchAsync("worker.expiring");

        Assert.Equal(1, expired.Claimed);
        Assert.Equal(0, expired.Completed);
        Assert.Empty(db.Operations.Where(value => value.Tool == "trigger-test"));
        Assert.Empty(db.TriggerFireReceipts);
        Assert.Equal("leased", db.TriggerFireWork.Single().State);

        var recovered = await Worker(db, clock, new RecordingParticipant(db, clock))
            .RunBatchAsync("worker.restarted");

        Assert.Equal(1, recovered.Completed);
        Assert.Equal(2, db.TriggerFireWork.Single().AttemptCount);
        Assert.Single(db.TriggerFireReceipts);
        Assert.Single(db.Operations.Where(value => value.Tool == "trigger-test"));
    }

    [Fact]
    public async Task Two_workers_cannot_claim_the_same_unexpired_occurrence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dantes-trigger-worker-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False;Default Timeout=5";
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connectionString).Options;
            await using (var setup = new DantesRoleplayDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var setupClock = new FakeTriggerClock(Now);
                await RegisterAsync(setup, setupClock, Trigger("trigger.session.end", Now));
            }
            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var clock = new FakeTriggerClock(Now);
            var blocking = new BlockingParticipant();
            var firstTask = Worker(firstDb, clock, blocking).RunBatchAsync("worker.first");
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = await Worker(secondDb, clock, new SequenceParticipant(TriggerFireAttemptResult.Succeeded()))
                .RunBatchAsync("worker.second");
            blocking.Release.SetResult();
            var first = await firstTask;

            Assert.Equal(0, second.Claimed);
            Assert.Equal(1, first.Completed);
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Single(verify.TriggerFireReceipts);
            Assert.Equal("completed", verify.TriggerFireWork.Single().State);
            Assert.Equal(1, verify.TriggerFireWork.Single().AttemptCount);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Expired_third_lease_becomes_terminal_without_another_participant_call()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        var trigger = Trigger("trigger.session.end", Now);
        await RegisterAsync(db, clock, trigger);
        var fireId = TriggerSchedulingFingerprint.Fire(trigger);
        db.TriggerFireWork.Add(new TriggerFireWorkRecord
        {
            FireId = fireId, ApplicationId = Application.Value, TriggerId = trigger.Id,
            TriggerVersion = 1, OccurrenceAtUtc = Now.UtcDateTime, State = "leased", AttemptCount = 3,
            LeaseOwner = "dead.worker", LeaseToken = new string('a', 32),
            LeaseExpiresAtUtc = Now.AddSeconds(-1).UtcDateTime, Revision = 3,
            CreatedAtUtc = Now.AddMinutes(-3).UtcDateTime, UpdatedAtUtc = Now.AddSeconds(-1).UtcDateTime
        });
        await db.SaveChangesAsync();
        var participant = new SequenceParticipant(TriggerFireAttemptResult.Succeeded());

        await Worker(db, clock, participant).RunBatchAsync("worker.recovery");

        var work = db.TriggerFireWork.Single();
        Assert.Equal("failed", work.State);
        Assert.Equal("attempts-exhausted", work.FailureKind);
        Assert.Equal(0, participant.Calls);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Invalid_worker_identity_fails_before_operational_state_change()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));

        var error = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            Worker(db, clock, new SequenceParticipant(TriggerFireAttemptResult.Succeeded()))
                .RunBatchAsync("../../worker"));

        Assert.Equal("TRIGGER_WORKER_ID", error.Code);
        Assert.Empty(db.TriggerFireWork);
    }

    [Fact]
    public async Task Preexisting_immutable_fire_receipt_blocks_target_execution_and_work_claim()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        var trigger = Trigger("trigger.session.end", Now);
        await RegisterAsync(db, clock, trigger);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendFireReceiptAsync(trigger);
        var participant = new SequenceParticipant(TriggerFireAttemptResult.Succeeded());

        var result = await Worker(db, clock, participant).RunBatchAsync("worker.replay");

        Assert.Equal(0, result.Examined);
        Assert.Equal(0, participant.Calls);
        Assert.Single(db.TriggerFireReceipts);
        Assert.Empty(db.TriggerFireWork);
    }

    [Fact]
    public async Task Superseding_a_retrying_trigger_closes_old_work_without_a_success_receipt()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        await Worker(db, clock, new SequenceParticipant(TriggerFireAttemptResult.Transient()))
            .RunBatchAsync("worker.first");
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(
            OneTimeTriggerDefinition.Create(Application, "trigger.session.end", 2, Now,
                TriggerMisfirePolicy.FireOnce));

        await Worker(db, clock, new UnavailableParticipant()).RunBatchAsync("worker.cleanup");

        var old = db.TriggerFireWork.AsNoTracking().Single();
        Assert.Equal("failed", old.State);
        Assert.Equal("stale-trigger", old.FailureKind);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Terminal_receipt_survives_clock_rollback_and_forward_replay()
    {
        await using var db = fixture.CreateContext();
        var clock = new RewindableClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));
        var participant = new SequenceParticipant(TriggerFireAttemptResult.Succeeded());
        var worker = Worker(db, clock, participant);

        Assert.Equal(1, (await worker.RunBatchAsync("worker.clock")).Completed);
        clock.Set(Now.AddHours(-1));
        Assert.Equal(0, (await worker.RunBatchAsync("worker.clock")).Examined);
        clock.Set(Now.AddHours(1));
        Assert.Equal(0, (await worker.RunBatchAsync("worker.clock")).Examined);

        Assert.Equal(1, participant.Calls);
        Assert.Single(db.TriggerFireReceipts);
        Assert.Equal("completed", db.TriggerFireWork.AsNoTracking().Single().State);
    }

    [Fact]
    public async Task Migrated_database_rejects_terminal_rewrite_and_operational_delete()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var clock = new FakeTriggerClock(Now);
        var trigger = Trigger("trigger.session.end", Now);
        await RegisterAsync(db, clock, trigger);
        var fireId = TriggerSchedulingFingerprint.Fire(trigger);
        db.TriggerFireWork.Add(new TriggerFireWorkRecord
        {
            FireId = fireId, ApplicationId = Application.Value, TriggerId = trigger.Id,
            TriggerVersion = 1, OccurrenceAtUtc = Now.UtcDateTime, State = "ready",
            Revision = 0, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        await db.SaveChangesAsync();

        var transition = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_fire_work SET State = 'completed', Revision = Revision + 1 WHERE FireId = {0}", fireId));
        var deletion = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM trigger_fire_work WHERE FireId = {0}", fireId));

        Assert.Contains("TRIGGER_FIRE_WORK_TRANSITION_DENIED", transition.Message, StringComparison.Ordinal);
        Assert.Contains("TRIGGER_FIRE_WORK_DELETE_DENIED", deletion.Message, StringComparison.Ordinal);
        Assert.Equal("ready", db.TriggerFireWork.AsNoTracking().Single().State);
    }

    [Fact]
    public async Task Each_poll_is_bounded_to_eight_ordered_occurrences()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        for (var index = 0; index < 10; index++)
            await store.AppendOneTimeTriggerAsync(Trigger($"trigger.batch.item-{index}", Now));
        var participant = new SequenceParticipant(
            Enumerable.Repeat(TriggerFireAttemptResult.Permanent(), 10).ToArray());
        var worker = Worker(db, clock, participant);

        var first = await worker.RunBatchAsync("worker.batch");
        var second = await worker.RunBatchAsync("worker.batch");

        Assert.Equal(SqliteOneTimeTriggerWorker.MaximumBatchSize, first.Examined);
        Assert.Equal(2, second.Examined);
        Assert.Equal(10, db.TriggerFireWork.Count());
        Assert.Equal(10, participant.Calls);
    }

    [Fact]
    public async Task Cancellation_rolls_back_participant_staging_and_leaves_only_a_recoverable_lease()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger("trigger.session.end", Now));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Worker(db, clock, new CancellingParticipant(db, clock))
                .RunBatchAsync("worker.cancelled"));

        Assert.Empty(db.Operations.Where(value => value.Tool == "trigger-test"));
        Assert.Empty(db.TriggerFireReceipts);
        var work = db.TriggerFireWork.AsNoTracking().Single();
        Assert.Equal("leased", work.State);
        Assert.Equal(1, work.AttemptCount);
        Assert.Equal(Now.AddSeconds(60).UtcDateTime, work.LeaseExpiresAtUtc);
    }

    private static SqliteOneTimeTriggerWorker Worker(
        DantesRoleplayDbContext db,
        ITriggerClock clock,
        ITriggerFireTransactionParticipant participant) =>
        new(db, clock, new SqliteTriggerSchedulingStore(db, clock), participant);

    private static async Task RegisterAsync(
        DantesRoleplayDbContext db,
        ITriggerClock clock,
        OneTimeTriggerDefinition trigger)
    {
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(trigger);
    }

    private static void RegisterApplication(DantesRoleplayDbContext db) =>
        new SqliteApplicationRegistry(db).Register(new ApplicationRegistration(
            Application, "Quest", "Worker tests.", []));

    private static OneTimeTriggerDefinition Trigger(
        string id,
        DateTimeOffset dueAt,
        TriggerMisfirePolicy policy = TriggerMisfirePolicy.FireOnce) =>
        OneTimeTriggerDefinition.Create(Application, id, 1, dueAt, policy);

    private sealed class UnavailableParticipant : ITriggerFireTransactionParticipant
    {
        public bool IsAvailable => false;
        public Task<TriggerFireAttemptResult> StageAsync(TriggerFireLease lease, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class SequenceParticipant(params TriggerFireAttemptResult[] results)
        : ITriggerFireTransactionParticipant
    {
        private readonly Queue<TriggerFireAttemptResult> results = new(results);
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public Task<TriggerFireAttemptResult> StageAsync(
            TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(results.Count == 0
                ? TriggerFireAttemptResult.Succeeded()
                : results.Dequeue());
        }
    }

    private sealed class RecordingParticipant(
        DantesRoleplayDbContext db,
        FakeTriggerClock clock,
        TimeSpan? advanceDuringStage = null) : ITriggerFireTransactionParticipant
    {
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public async Task<TriggerFireAttemptResult> StageAsync(
            TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            db.Operations.Add(new Operation
            {
                Id = Guid.NewGuid().ToString("N"), Timestamp = clock.UtcNow.UtcDateTime,
                Tool = "trigger-test", Summary = "Staged trigger participant evidence.", Success = true
            });
            await db.SaveChangesAsync(cancellationToken);
            if (advanceDuringStage is { } advance) clock.Advance(advance);
            return TriggerFireAttemptResult.Succeeded();
        }
    }

    private sealed class BlockingParticipant : ITriggerFireTransactionParticipant
    {
        public bool IsAvailable => true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TriggerFireAttemptResult> StageAsync(
            TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return TriggerFireAttemptResult.Succeeded();
        }
    }

    private sealed class CancellingParticipant(
        DantesRoleplayDbContext db,
        FakeTriggerClock clock) : ITriggerFireTransactionParticipant
    {
        public bool IsAvailable => true;

        public async Task<TriggerFireAttemptResult> StageAsync(
            TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            db.Operations.Add(new Operation
            {
                Id = Guid.NewGuid().ToString("N"), Timestamp = clock.UtcNow.UtcDateTime,
                Tool = "trigger-test", Summary = "This staged row must roll back.", Success = true
            });
            await db.SaveChangesAsync(cancellationToken);
            throw new OperationCanceledException("Injected cancellation.");
        }
    }

    private sealed class RewindableClock(DateTimeOffset now) : ITriggerClock
    {
        private DateTimeOffset now = now;
        public DateTimeOffset UtcNow => now;
        public void Set(DateTimeOffset value) => now = value;
    }
}
