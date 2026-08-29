using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Notifications;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class TriggerSchedulingNotificationTests : IDisposable
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly DateTimeOffset ElevenPm = new(2026, 8, 25, 23, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Production_composition_enables_only_the_notification_participant_and_status_reader()
    {
        using var services = new ServiceCollection()
            .AddDantesRoleplayDataAccess("Data Source=:memory:")
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var participant = scope.ServiceProvider.GetRequiredService<ITriggerFireTransactionParticipant>();

        Assert.IsType<TriggerNotificationTransactionParticipant>(participant);
        Assert.True(participant.IsAvailable);
        Assert.IsType<SqliteTriggerScheduleStatusReader>(
            scope.ServiceProvider.GetRequiredService<ITriggerScheduleStatusReader>());
    }

    [Fact]
    public async Task Eleven_pm_reminder_commits_exact_content_links_receipt_and_status_once()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(ElevenPm.AddMinutes(-1));
        RegisterApplication(db);
        await RegisterStateSpaceAsync(db, "session.table");
        var target = TriggerNotificationTarget.Create(
            "session.soft-ending", "It is getting late", "Softly end this session now.",
            "space.quest", ["session.table"]);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendOneTimeTriggerAsync(Trigger(1, ElevenPm, notification: target));
        var statuses = new SqliteTriggerScheduleStatusReader(db, clock);

        Assert.Equal(TriggerScheduleStatus.Scheduled,
            (await statuses.GetAsync(Application, "session.soft-ending"))!.Status);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TriggerScheduleStatus.Due,
            (await statuses.GetAsync(Application, "session.soft-ending"))!.Status);

        var first = await Worker(db, clock).RunBatchAsync("worker.notification");
        var second = await Worker(db, clock).RunBatchAsync("worker.notification");

        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Examined);
        var notification = Assert.Single(await new NotificationStore(db).FindAsync());
        Assert.Equal("session.soft-ending", notification.Topic);
        Assert.Equal("It is getting late", notification.Subject);
        Assert.Equal("Softly end this session now.", notification.Body);
        Assert.Equal(["session.table"], notification.EntityIds);
        var link = Assert.Single(db.TriggerNotificationLinks.AsNoTracking());
        Assert.Equal(notification.Id, link.NotificationId);
        Assert.Equal("session.soft-ending", link.TriggerId);
        Assert.Single(db.TriggerFireReceipts);
        Assert.Equal("completed", Assert.Single(db.TriggerFireWork).State);
        var completed = await statuses.GetAsync(Application, "session.soft-ending");
        Assert.Equal(TriggerScheduleStatus.Completed, completed!.Status);
        Assert.Equal(notification.Id, completed.NotificationId);
        Assert.Empty(db.Events);
        Assert.Empty(db.Operations);
        Assert.Single(db.Set<ApplicationEcsEntityRecord>());

        var read = await new NotificationStore(db).SetStateAsync(notification.Id, NotificationState.Read);
        Assert.True(read.Ok);
        Assert.Equal(NotificationState.Read, read.Notification!.State);
    }

    [Fact]
    public async Task Reschedule_supersedes_old_revision_and_cancelled_current_never_fires()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(ElevenPm.AddHours(-1));
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var target = TriggerNotificationTarget.Create("session.soft-ending", "End soon");
        await store.AppendOneTimeTriggerAsync(Trigger(1, ElevenPm, notification: target));
        await store.AppendOneTimeTriggerAsync(Trigger(2, ElevenPm.AddHours(1), notification: target));
        var statuses = new SqliteTriggerScheduleStatusReader(db, clock);

        Assert.Equal(TriggerScheduleStatus.Superseded,
            (await statuses.GetAsync(Application, "session.soft-ending", 1))!.Status);
        Assert.Equal(TriggerScheduleStatus.Scheduled,
            (await statuses.GetAsync(Application, "session.soft-ending"))!.Status);

        await store.AppendOneTimeTriggerAsync(Trigger(3, ElevenPm.AddHours(1),
            TriggerLifecycle.Cancelled, target));
        clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(TriggerScheduleStatus.Superseded,
            (await statuses.GetAsync(Application, "session.soft-ending", 2))!.Status);
        Assert.Equal(TriggerScheduleStatus.Cancelled,
            (await statuses.GetAsync(Application, "session.soft-ending"))!.Status);
        Assert.Equal(0, (await Worker(db, clock).RunBatchAsync("worker.cancelled")).Examined);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.TriggerFireReceipts);
        Assert.Empty(db.TriggerFireWork);
    }

    [Fact]
    public async Task Missed_schedule_projects_missed_without_creating_a_notification()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(ElevenPm);
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(
            Trigger(1, ElevenPm.AddSeconds(-1), policy: TriggerMisfirePolicy.Skip));

        Assert.Equal(1, (await Worker(db, clock).RunBatchAsync("worker.missed")).Missed);

        Assert.Equal(TriggerScheduleStatus.Missed,
            (await new SqliteTriggerScheduleStatusReader(db, clock)
                .GetAsync(Application, "session.soft-ending"))!.Status);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.TriggerNotificationLinks);
        Assert.Equal("missed", Assert.Single(db.TriggerFireReceipts).Disposition);
    }

    [Fact]
    public async Task Missing_or_wrong_scope_entity_link_fails_without_partial_delivery()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(ElevenPm);
        RegisterApplication(db);
        var target = TriggerNotificationTarget.Create(
            "session.soft-ending", "End soon", stateSpaceId: "space.missing",
            entityIds: ["session.table"]);
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(
            Trigger(1, ElevenPm, notification: target));

        var result = await Worker(db, clock).RunBatchAsync("worker.bad-link");

        Assert.Equal(1, result.Failed);
        Assert.Equal("permanent-handler", Assert.Single(db.TriggerFireWork).FailureKind);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.TriggerNotificationLinks);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Injected_link_failure_rolls_back_notification_and_receipt_then_retry_completes_once()
    {
        await using var db = fixture.CreateContext();
        var clock = new FakeTriggerClock(ElevenPm);
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(Trigger(1, ElevenPm));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER injected_trigger_notification_failure
            BEFORE INSERT ON trigger_notification_link
            BEGIN SELECT RAISE(ABORT, 'database is locked'); END;
            """);

        Assert.Equal(1, (await Worker(db, clock).RunBatchAsync("worker.rollback")).Retried);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.TriggerNotificationLinks);
        Assert.Empty(db.TriggerFireReceipts);
        Assert.Equal("retry", Assert.Single(db.TriggerFireWork).State);

        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER injected_trigger_notification_failure");
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, (await Worker(db, clock).RunBatchAsync("worker.rollback")).Completed);
        Assert.Single(db.Notifications);
        Assert.Single(db.TriggerNotificationLinks);
        Assert.Single(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Two_context_workers_leave_one_notification_link_and_receipt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dantes-trigger-notification-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=5").Options;
        try
        {
            await using (var setup = new DantesRoleplayDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                RegisterApplication(setup);
                var clock = new FakeTriggerClock(ElevenPm);
                await new SqliteTriggerSchedulingStore(setup, clock)
                    .AppendOneTimeTriggerAsync(Trigger(1, ElevenPm));
            }
            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var sharedClock = new FakeTriggerClock(ElevenPm);
            var blocking = new BlockingParticipant(
                new TriggerNotificationTransactionParticipant(firstDb, sharedClock));
            var firstTask = new SqliteOneTimeTriggerWorker(firstDb, sharedClock,
                new SqliteTriggerSchedulingStore(firstDb, sharedClock), blocking)
                .RunBatchAsync("worker.first");
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = await Worker(secondDb, sharedClock).RunBatchAsync("worker.second");
            blocking.Release.SetResult();
            var first = await firstTask;

            Assert.Equal(0, second.Claimed);
            Assert.Equal(1, first.Completed);
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Single(verify.Notifications);
            Assert.Single(verify.TriggerNotificationLinks);
            Assert.Single(verify.TriggerFireReceipts);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Migrated_database_rejects_content_provenance_and_target_tampering()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var clock = new FakeTriggerClock(ElevenPm);
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock).AppendOneTimeTriggerAsync(Trigger(1, ElevenPm));
        Assert.Equal(1, (await Worker(db, clock).RunBatchAsync("worker.migrated")).Completed);
        var notification = Assert.Single(db.Notifications.AsNoTracking());
        var fireId = Assert.Single(db.TriggerNotificationLinks.AsNoTracking()).FireId;

        var trackedNotification = db.Notifications.Single(value => value.Id == notification.Id);
        trackedNotification.Subject = "rewritten";
        Assert.Equal("NOTIFICATION_CONTENT_IMMUTABLE",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
        db.ChangeTracker.Clear();
        var trackedLink = db.TriggerNotificationLinks.Single(value => value.FireId == fireId);
        trackedLink.CreatedAtUtc = trackedLink.CreatedAtUtc.AddSeconds(1);
        Assert.Equal("TRIGGER_SCHEDULING_IMMUTABLE",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
        db.ChangeTracker.Clear();

        var content = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE notification SET Subject = 'rewritten' WHERE Id = {0}", notification.Id));
        var provenance = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM trigger_notification_link WHERE FireId = {0}", fireId));
        var target = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_one_time_definition SET NotificationSubject = 'rewritten' WHERE ApplicationId = 'quest'"));
        var invalidTarget = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_one_time_definition
                (ApplicationId, Id, Version, DueAtUtc, MisfirePolicy, Target, Lifecycle,
                 NotificationTopic, NotificationSubject, NotificationBody, RecordedAtUtc)
            VALUES ('quest', 'session.invalid', 1, '2026-08-26 23:00:00', 'fire-once',
                'notification-only', 'forged', 'scheduled.reminder', 'Invalid', '',
                '2026-08-25 23:00:00');
            """));

        Assert.Contains("NOTIFICATION_CONTENT_IMMUTABLE", content.Message, StringComparison.Ordinal);
        Assert.Contains("TRIGGER_SCHEDULING_IMMUTABLE", provenance.Message, StringComparison.Ordinal);
        Assert.Contains("TRIGGER_SCHEDULING_IMMUTABLE", target.Message, StringComparison.Ordinal);
        Assert.Contains("TRIGGER_NOTIFICATION_TARGET_INVALID", invalidTarget.Message, StringComparison.Ordinal);
        Assert.True((await new NotificationStore(db).SetStateAsync(notification.Id, NotificationState.Read)).Ok);
    }

    [Fact]
    public void Notification_target_rejects_ambiguous_or_unbounded_content_and_cancel_is_not_due()
    {
        Assert.Throws<TriggerSchedulingContractException>(() =>
            TriggerNotificationTarget.Create("topic", "subject", stateSpaceId: "space.quest"));
        Assert.Throws<TriggerSchedulingContractException>(() =>
            TriggerNotificationTarget.Create("topic", "subject", entityIds: ["entity"]));
        Assert.Throws<TriggerSchedulingContractException>(() =>
            TriggerNotificationTarget.Create("topic", new string('s', 401)));
        var cancelled = Trigger(1, ElevenPm, TriggerLifecycle.Cancelled);
        Assert.Equal(OneTimeTriggerDisposition.Pending,
            OneTimeTriggerEvaluator.Evaluate(cancelled, new FakeTriggerClock(ElevenPm)).Disposition);
    }

    private static SqliteOneTimeTriggerWorker Worker(DantesRoleplayDbContext db, ITriggerClock clock) =>
        new(db, clock, new SqliteTriggerSchedulingStore(db, clock),
            new TriggerNotificationTransactionParticipant(db, clock));

    private static OneTimeTriggerDefinition Trigger(
        int version,
        DateTimeOffset dueAt,
        TriggerLifecycle lifecycle = TriggerLifecycle.Active,
        TriggerNotificationTarget? notification = null,
        TriggerMisfirePolicy policy = TriggerMisfirePolicy.FireOnce) =>
        OneTimeTriggerDefinition.Create(Application, "session.soft-ending", version, dueAt, policy,
            TriggerFireTarget.NotificationOnly, lifecycle, notification);

    private static void RegisterApplication(DantesRoleplayDbContext db) =>
        new SqliteApplicationRegistry(db).Register(new ApplicationRegistration(
            Application, "Quest", "Scheduled notification tests.", []));

    private static async Task RegisterStateSpaceAsync(DantesRoleplayDbContext db, string entityId)
    {
        db.Set<ApplicationStateSpaceRecord>().Add(new ApplicationStateSpaceRecord
        {
            Id = "space.quest", ApplicationId = Application.Value, ApplicationRevision = 1,
            ManifestFingerprint = new string('A', 64), BindingRevision = 1,
            CreatedAtUtc = ElevenPm.AddHours(-1).UtcDateTime
        });
        db.Set<ApplicationEcsEntityRecord>().Add(new ApplicationEcsEntityRecord
        {
            StateSpaceId = "space.quest", Id = entityId, Name = "Session", Revision = 1,
            CreatedAtUtc = ElevenPm.AddHours(-1).UtcDateTime
        });
        await db.SaveChangesAsync();
    }

    private sealed class BlockingParticipant(ITriggerFireTransactionParticipant inner)
        : ITriggerFireTransactionParticipant
    {
        public bool IsAvailable => true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TriggerFireAttemptResult> StageAsync(
            TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.StageAsync(lease, cancellationToken);
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
