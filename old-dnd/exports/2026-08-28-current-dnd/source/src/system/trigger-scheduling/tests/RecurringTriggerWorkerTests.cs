using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class RecurringTriggerWorkerTests : IDisposable
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Exact_due_commits_notification_receipt_provenance_and_next_atomically()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));

        var result = await Worker(db, clock).RunBatchAsync("worker.recurring.success");

        Assert.Equal(1, result.Completed);
        Assert.Single(db.Notifications);
        Assert.Single(db.RecurringTriggerNotificationLinks);
        Assert.Equal("due", Assert.Single(db.RecurringTriggerFireReceipts).Disposition);
        Assert.Equal("completed", Assert.Single(db.RecurringTriggerFireWork).State);
        var state = Assert.Single(db.RecurringTriggerState);
        Assert.Equal(Now.AddDays(1).UtcDateTime, state.NextOccurrenceAtUtc);
        Assert.Equal(Now.UtcDateTime, state.LastOccurrenceAtUtc);
        Assert.Equal("due", state.LastDisposition);
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Forward_jump_collapses_elapsed_occurrences_to_one_latest_fire()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now.AddDays(-5).AddHours(-2));
        await RegisterAsync(db, clock, Trigger(1));
        clock.Set(Now);

        var result = await Worker(db, clock).RunBatchAsync("worker.recurring.collapse");

        Assert.Equal(1, result.Completed);
        var receipt = Assert.Single(db.RecurringTriggerFireReceipts);
        Assert.Equal(Now.UtcDateTime, receipt.OccurrenceAtUtc);
        Assert.Single(db.RecurringTriggerFireWork);
        Assert.Equal(Now.AddDays(1).UtcDateTime, db.RecurringTriggerState.Single().NextOccurrenceAtUtc);
    }

    [Fact]
    public async Task Skip_and_expired_fire_once_record_one_collapsed_miss()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now.AddDays(-5).AddHours(-2));
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendRecurringTriggerAsync(Trigger(1, "trigger.recurring.skip", TriggerMisfirePolicy.Skip));
        await store.AppendRecurringTriggerAsync(RecurringTriggerDefinition.Create(Application,
            "trigger.recurring.expired", 1, RecurrencePattern.Daily(1, new TimeOnly(19, 0),
                "Etc/UTC", endDate: new DateOnly(2026, 8, 20)),
            misfirePolicy: TriggerMisfirePolicy.FireOnce));
        clock.Set(Now.AddMinutes(1));

        var result = await Worker(db, clock).RunBatchAsync("worker.recurring.miss");

        Assert.Equal(2, result.Missed);
        Assert.Equal(2, db.RecurringTriggerFireReceipts.Count(value => value.Disposition == "missed"));
        Assert.Equal(2, db.RecurringTriggerFireWork.Count());
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Pause_cancel_and_resume_are_versioned_and_skip_inactive_time()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now.AddMinutes(-1));
        await RegisterAsync(db, clock, Trigger(1));
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendRecurringTriggerAsync(Trigger(2, lifecycle: RecurringTriggerLifecycle.Paused));
        clock.Set(Now.AddDays(2));
        Assert.Equal(0, (await Worker(db, clock).RunBatchAsync("worker.recurring.paused")).Examined);
        await store.AppendRecurringTriggerAsync(Trigger(3, lifecycle: RecurringTriggerLifecycle.Active));

        var resumed = db.RecurringTriggerState.Single();
        Assert.Equal(Now.AddDays(2).UtcDateTime, resumed.NextOccurrenceAtUtc);
        Assert.Empty(db.RecurringTriggerFireWork);
        await store.AppendRecurringTriggerAsync(Trigger(4, lifecycle: RecurringTriggerLifecycle.Cancelled));
        Assert.Null(db.RecurringTriggerState.Single().NextOccurrenceAtUtc);
        Assert.Equal(0, (await Worker(db, clock).RunBatchAsync("worker.recurring.cancelled")).Examined);
    }

    [Fact]
    public async Task Retry_keeps_occurrence_identity_and_uses_five_then_thirty_seconds()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));
        var participant = new SequenceParticipant(TriggerFireAttemptResult.Transient(),
            TriggerFireAttemptResult.Transient(), TriggerFireAttemptResult.Succeeded());
        var worker = new SqliteRecurringTriggerWorker(db, clock, participant);

        Assert.Equal(1, (await worker.RunBatchAsync("worker.recurring.retry")).Retried);
        var fireId = db.RecurringTriggerFireWork.Single().FireId;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, (await worker.RunBatchAsync("worker.recurring.retry")).Retried);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, (await worker.RunBatchAsync("worker.recurring.retry")).Completed);

        Assert.Equal(fireId, db.RecurringTriggerFireWork.Single().FireId);
        Assert.Equal(3, db.RecurringTriggerFireWork.Single().AttemptCount);
        Assert.Single(db.RecurringTriggerFireReceipts);
    }

    [Fact]
    public async Task Permanent_failure_advances_calendar_and_projects_failure_evidence()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));
        var worker = new SqliteRecurringTriggerWorker(db, clock,
            new SequenceParticipant(TriggerFireAttemptResult.Permanent()));

        Assert.Equal(1, (await worker.RunBatchAsync("worker.recurring.failure")).Failed);

        var state = db.RecurringTriggerState.Single();
        Assert.Equal(Now.AddDays(1).UtcDateTime, state.NextOccurrenceAtUtc);
        Assert.Equal("permanent-handler", state.LastFailureKind);
        Assert.Empty(db.RecurringTriggerFireReceipts);
        var status = await new SqliteRecurringTriggerStatusReader(db, clock)
            .GetAsync(Application, "trigger.recurring.session");
        Assert.Equal("permanent-handler", status!.LastFailureKind);
        Assert.Equal(RecurringTriggerStatus.Scheduled, status.Status);
    }

    [Fact]
    public async Task Database_failure_rolls_back_notification_receipt_and_state_advance()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER recurring_notification_injected_failure
            BEFORE INSERT ON notification
            BEGIN SELECT RAISE(ABORT, 'injected notification failure'); END;
            """);

        var result = await Worker(db, clock).RunBatchAsync("worker.recurring.rollback");

        Assert.Equal(1, result.Retried);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.RecurringTriggerFireReceipts);
        Assert.Equal(Now.UtcDateTime, db.RecurringTriggerState.Single().NextOccurrenceAtUtc);
        Assert.Equal("retry", db.RecurringTriggerFireWork.Single().State);
    }

    [Fact]
    public async Task Migrated_database_rejects_definition_receipt_work_and_state_tampering()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));
        await Worker(db, clock).RunBatchAsync("worker.recurring.security");
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_recurring_definition SET Interval = 2"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM trigger_recurring_fire_receipt"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM trigger_recurring_fire_work"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_recurring_state SET NextOccurrenceAtUtc = NULL, Revision = Revision + 1"));
    }

    [Fact]
    public async Task Store_replays_exact_revision_rejects_conflict_and_requires_newer_version()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var original = Trigger(1);

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended,
            (await store.AppendRecurringTriggerAsync(original)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay,
            (await store.AppendRecurringTriggerAsync(original)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict,
            (await store.AppendRecurringTriggerAsync(Trigger(1, policy: TriggerMisfirePolicy.Skip))).Disposition);
        await store.AppendRecurringTriggerAsync(Trigger(3,
            lifecycle: RecurringTriggerLifecycle.Paused));
        var error = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendRecurringTriggerAsync(Trigger(2)));

        Assert.Equal("TRIGGER_SCHEDULING_RECURRING_TRIGGER_REVISION_STALE", error.Code);
        Assert.Equal(2, db.RecurringTriggers.Count());
        Assert.Single(db.RecurringTriggerState);
    }

    [Fact]
    public async Task Inclusive_end_completes_after_its_final_occurrence()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        RegisterApplication(db);
        var trigger = RecurringTriggerDefinition.Create(Application, "trigger.recurring.final", 1,
            RecurrencePattern.Daily(1, new TimeOnly(20, 0), "Etc/UTC",
                endDate: new DateOnly(2026, 8, 25)));
        await new SqliteTriggerSchedulingStore(db, clock).AppendRecurringTriggerAsync(trigger);

        Assert.Equal(1, (await Worker(db, clock).RunBatchAsync("worker.recurring.final")).Completed);
        var status = await new SqliteRecurringTriggerStatusReader(db, clock)
            .GetAsync(Application, trigger.Id);

        Assert.Null(db.RecurringTriggerState.Single().NextOccurrenceAtUtc);
        Assert.Equal(RecurringTriggerStatus.Completed, status!.Status);
    }

    [Fact]
    public async Task Two_contexts_commit_only_one_result_for_the_same_occurrence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dantes-recurring-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10").Options;
        try
        {
            await using (var setup = new DantesRoleplayDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await RegisterAsync(setup, new FakeTriggerClock(Now), Trigger(1));
            }
            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var clock = new FakeTriggerClock(Now);

            var results = await Task.WhenAll(
                Worker(firstDb, clock).RunBatchAsync("worker.recurring.first"),
                Worker(secondDb, clock).RunBatchAsync("worker.recurring.second"));

            Assert.Equal(1, results.Sum(value => value.Completed));
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Single(verify.RecurringTriggerFireReceipts);
            Assert.Single(verify.RecurringTriggerNotificationLinks);
            Assert.Single(verify.Notifications);
            Assert.Equal(Now.AddDays(1).UtcDateTime,
                verify.RecurringTriggerState.Single().NextOccurrenceAtUtc);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Superseding_a_retry_closes_old_work_without_advancing_new_state()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(Now);
        await RegisterAsync(db, clock, Trigger(1));
        await new SqliteRecurringTriggerWorker(db, clock,
            new SequenceParticipant(TriggerFireAttemptResult.Transient()))
            .RunBatchAsync("worker.recurring.old");
        await new SqliteTriggerSchedulingStore(db, clock).AppendRecurringTriggerAsync(
            Trigger(2, lifecycle: RecurringTriggerLifecycle.Paused));

        await Worker(db, clock).RunBatchAsync("worker.recurring.cleanup");

        Assert.Equal("stale-trigger", db.RecurringTriggerFireWork.Single().FailureKind);
        var state = db.RecurringTriggerState.Single();
        Assert.Equal(2, state.CurrentVersion);
        Assert.Null(state.NextOccurrenceAtUtc);
        Assert.Null(state.LastOccurrenceAtUtc);
    }

    private static SqliteRecurringTriggerWorker Worker(DantesRoleplayDbContext db, ITriggerClock clock) =>
        new(db, clock, new TriggerNotificationTransactionParticipant(db, clock));

    private static async Task RegisterAsync(DantesRoleplayDbContext db, ITriggerClock clock,
        RecurringTriggerDefinition trigger)
    {
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock).AppendRecurringTriggerAsync(trigger);
    }

    private static void RegisterApplication(DantesRoleplayDbContext db)
    {
        new SqliteApplicationRegistry(db).Register(new ApplicationRegistration(
            Application, "Quest", "Recurring worker tests.", []));
    }

    private static RecurringTriggerDefinition Trigger(int version,
        string id = "trigger.recurring.session",
        TriggerMisfirePolicy policy = TriggerMisfirePolicy.FireOnce,
        TimeOnly? localTime = null,
        RecurringTriggerLifecycle lifecycle = RecurringTriggerLifecycle.Active) =>
        RecurringTriggerDefinition.Create(Application, id, version,
            RecurrencePattern.Daily(1, localTime ?? new TimeOnly(20, 0), "Etc/UTC"), lifecycle,
            policy, notification: TriggerNotificationTarget.Create("session.reminder",
                "Session reminder", "Time to wrap up."));

    private sealed class SequenceParticipant(params TriggerFireAttemptResult[] results)
        : ITriggerFireTransactionParticipant
    {
        private readonly Queue<TriggerFireAttemptResult> results = new(results);
        public bool IsAvailable => true;
        public Task<TriggerFireAttemptResult> StageAsync(TriggerFireLease lease,
            CancellationToken cancellationToken = default) => Task.FromResult(results.Dequeue());
    }
}
