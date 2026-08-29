using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ObservationTriggerTests : IDisposable
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("quest");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Closed_scalar_matcher_is_exact_and_rejects_paths_operators_and_complex_values()
    {
        var matcher = new ClosedScalarsObservationMatchAdapter();
        var definition = Definition(new string('A', 64),
            "{\"matches\":[{\"property\":\"transition\",\"value\":\"entered\"},{\"property\":\"count\",\"value\":2}]}");
        matcher.Validate(definition);

        Assert.True(matcher.Evaluate(definition, Input(definition,
            "{\"count\":2.0,\"transition\":\"entered\",\"ignored\":true}")));
        Assert.False(matcher.Evaluate(definition, Input(definition,
            "{\"count\":\"2\",\"transition\":\"entered\"}")));
        Assert.False(matcher.Evaluate(definition, Input(definition,
            "{\"count\":2,\"transition\":\"left\"}")));
        Assert.Throws<TriggerSchedulingContractException>(() => matcher.Validate(
            Definition(new string('A', 64), "{\"matches\":[{\"property\":\"device.transition\",\"value\":\"entered\"}]}")));
        Assert.Throws<TriggerSchedulingContractException>(() => matcher.Validate(
            Definition(new string('A', 64), "{\"matches\":[{\"property\":\"transition\",\"operator\":\"eq\",\"value\":\"entered\"}]}")));
        Assert.Throws<TriggerSchedulingContractException>(() => matcher.Validate(
            Definition(new string('A', 64), "{\"matches\":[{\"property\":\"transition\",\"value\":{\"nested\":true}}]}")));
    }

    [Fact]
    public async Task Accepted_observation_stages_once_and_non_match_records_no_notification()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        var submission = Submission('1', "arrival.1", "{\"transition\":\"left\"}");

        var appended = await setup.ObservationStore.AppendObservationAsync(Principal, App, submission);
        var replay = await setup.ObservationStore.AppendObservationAsync(Principal, App, submission);
        var result = await setup.Worker.RunBatchAsync("observation.false");
        var status = await new SqliteObservationTriggerStatusReader(setup.Db)
            .GetAsync(App, "trigger.arrival.entered");

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(1, result.Completed);
        Assert.Single(setup.Db.ObservationTriggerMatchWork);
        Assert.Equal("not-matched", Assert.Single(setup.Db.ObservationTriggerMatchReceipts).Disposition);
        Assert.Empty(setup.Db.Notifications);
        Assert.Empty(setup.Db.ObservationTriggerNotificationLinks);
        Assert.Equal("not-matched", status!.LastDisposition);
        Assert.Equal(appended.Value!.Id, status.LastObservationId);
    }

    [Fact]
    public async Task Exact_match_atomically_creates_notification_receipt_and_provenance()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        var observation = await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('2', "arrival.2", "{\"transition\":\"entered\"}"));

        var first = await setup.Worker.RunBatchAsync("observation.match");
        var second = await setup.Worker.RunBatchAsync("observation.match");

        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Completed);
        Assert.Equal("completed", setup.Db.ObservationTriggerMatchWork.AsNoTracking().Single().State);
        Assert.Equal("matched", Assert.Single(setup.Db.ObservationTriggerMatchReceipts).Disposition);
        var link = Assert.Single(setup.Db.ObservationTriggerNotificationLinks);
        Assert.Equal(observation.Value!.Id, link.ObservationId);
        Assert.Equal(link.NotificationId, Assert.Single(setup.Db.Notifications).Id);
        Assert.Empty(setup.Db.Events);
    }

    [Fact]
    public async Task Superseded_structure_prevents_delivery_without_discarding_observation()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('3', "arrival.3", "{\"transition\":\"entered\"}"));
        await setup.ObservationStore.AppendStructureAsync(Structure(2));

        var result = await setup.Worker.RunBatchAsync("observation.stale");
        var status = await new SqliteObservationTriggerStatusReader(setup.Db)
            .GetAsync(App, "trigger.arrival.entered");

        Assert.Equal(1, result.Failed);
        Assert.Single(setup.Db.TriggerObservations);
        Assert.Equal("stale-trigger", setup.Db.ObservationTriggerMatchWork.Single().FailureKind);
        Assert.Empty(setup.Db.ObservationTriggerMatchReceipts);
        Assert.Empty(setup.Db.Notifications);
        Assert.Equal(ObservationTriggerStatus.StaleStructure, status!.Status);
    }

    [Fact]
    public async Task Superseded_or_disabled_source_prevents_delivery()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('a', "arrival.a", "{\"transition\":\"entered\"}"));
        await setup.ObservationStore.AppendSourceAsync(Source(2, 1, ObservationSourceStatus.Disabled));

        var result = await setup.Worker.RunBatchAsync("observation.stale-source");
        var status = await new SqliteObservationTriggerStatusReader(setup.Db)
            .GetAsync(App, "trigger.arrival.entered");

        Assert.Equal(1, result.Failed);
        Assert.Empty(setup.Db.Notifications);
        Assert.Equal(ObservationTriggerStatus.StaleSource, status!.Status);
    }

    [Fact]
    public async Task Transient_matcher_failure_retries_and_preserves_accepted_evidence()
    {
        var adapter = new TransientAdapter();
        var setup = await SetupAsync(fixture.CreateContext(), adapter);
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash,
            "{\"matches\":[{\"property\":\"transition\",\"value\":\"entered\"}]}", adapter.Id));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('4', "arrival.4", "{\"transition\":\"entered\"}"));

        var result = await setup.Worker.RunBatchAsync("observation.retry");

        Assert.Equal(1, result.Retried);
        Assert.Single(setup.Db.TriggerObservations);
        var work = setup.Db.ObservationTriggerMatchWork.Single();
        Assert.Equal("retry", work.State);
        Assert.Equal("handler-unavailable", work.FailureKind);
        Assert.Empty(setup.Db.ObservationTriggerMatchReceipts);
        Assert.Empty(setup.Db.Notifications);
    }

    [Fact]
    public async Task Store_requires_current_exact_source_structure_and_reviewed_adapter()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        var unavailable = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash, adapterId: "system.unreviewed.matcher")));
        await setup.ObservationStore.AppendStructureAsync(Structure(2));
        var stale = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash)));

        Assert.Equal("OBSERVATION_MATCH_ADAPTER_UNAVAILABLE", unavailable.Code);
        Assert.Equal("OBSERVATION_TRIGGER_STRUCTURE_STALE", stale.Code);
        Assert.Empty(setup.Db.ObservationTriggers);
    }

    [Fact]
    public async Task Notification_scope_cannot_cross_application_boundary()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        var applications = new SqliteApplicationRegistry(setup.Db);
        var other = ApplicationIdentifier.Parse("other");
        var revision = applications.Register(new(other, "Other", "", []));
        new SqliteStateSpaceRegistry(setup.Db, applications)
            .Create(new("other-space", revision, new string('A', 64)));

        var failure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash,
                notification: TriggerNotificationTarget.Create("arrival", "Arrived", "Body",
                    "other-space", ["outside"]))));

        Assert.Equal("OBSERVATION_TRIGGER_NOTIFICATION_SCOPE", failure.Code);
        Assert.Empty(setup.Db.ObservationTriggers);
    }

    [Fact]
    public async Task Duplicate_adapter_registration_and_permanent_failure_are_closed_safely()
    {
        var duplicate = await SetupAsync(fixture.CreateContext(),
            new ClosedScalarsObservationMatchAdapter(), new ClosedScalarsObservationMatchAdapter());
        var ambiguous = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            duplicate.MatchStore.AppendAsync(Definition(duplicate.Structure.SchemaHash)));
        Assert.Equal("OBSERVATION_MATCH_ADAPTER_UNAVAILABLE", ambiguous.Code);

        using var isolated = new SqliteFixture();
        var adapter = new PermanentAdapter();
        var setup = await SetupAsync(isolated.CreateContext(), adapter);
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash,
            adapterId: adapter.Id));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('6', "arrival.6", "{\"transition\":\"entered\"}"));

        var result = await setup.Worker.RunBatchAsync("observation.permanent");

        Assert.Equal(1, result.Failed);
        Assert.Single(setup.Db.TriggerObservations);
        Assert.Equal("permanent-handler",
            setup.Db.ObservationTriggerMatchWork.AsNoTracking().Single().FailureKind);
        Assert.Empty(setup.Db.ObservationTriggerMatchReceipts);
        Assert.Empty(setup.Db.Notifications);
    }

    [Fact]
    public async Task Candidate_bound_rolls_back_observation_and_all_work()
    {
        var setup = await SetupAsync(fixture.CreateContext());
        for (var index = 0; index <= ObservationTriggerAppendParticipant.MaximumCandidates; index++)
            await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash,
                triggerId: $"trigger.arrival.t{index:D2}"));

        var failure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.ObservationStore.AppendObservationAsync(Principal, App,
                Submission('7', "arrival.7", "{\"transition\":\"entered\"}")));

        Assert.Equal("OBSERVATION_TRIGGER_CANDIDATE_LIMIT", failure.Code);
        Assert.Empty(setup.Db.TriggerObservations);
        Assert.Empty(setup.Db.ObservationTriggerMatchWork);
    }

    [Fact]
    public async Task Injected_commit_failure_retries_without_partial_notification()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var setup = await SetupAsync(db);
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('8', "arrival.8", "{\"transition\":\"entered\"}"));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER observation_match_injected_failure
            BEFORE INSERT ON trigger_observation_match_receipt
            BEGIN SELECT RAISE(ABORT, 'injected receipt failure'); END;
            """);

        var first = await setup.Worker.RunBatchAsync("observation.injected");
        Assert.Equal(1, first.Retried);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.ObservationTriggerMatchReceipts);
        Assert.Empty(db.ObservationTriggerNotificationLinks);
        Assert.Single(db.TriggerObservations);

        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER observation_match_injected_failure");
        setup.Clock.Advance(SqliteObservationTriggerWorker.FirstRetryDelay);
        var second = await setup.Worker.RunBatchAsync("observation.injected");
        Assert.Equal(1, second.Completed);
        Assert.Single(db.Notifications);
        Assert.Single(db.ObservationTriggerMatchReceipts);
        Assert.Single(db.ObservationTriggerNotificationLinks);
    }

    [Fact]
    public async Task Two_workers_deliver_at_most_once_for_one_observation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dantes-observation-match-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10").Options;
        try
        {
            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                var setup = await SetupAsync(db);
                await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
                await setup.ObservationStore.AppendObservationAsync(Principal, App,
                    Submission('9', "arrival.9", "{\"transition\":\"entered\"}"));
            }
            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var clock = new FakeTriggerClock(Now);
            var results = await Task.WhenAll(
                Worker(firstDb, clock).RunBatchAsync("observation.first"),
                Worker(secondDb, clock).RunBatchAsync("observation.second"));

            Assert.Equal(1, results.Sum(value => value.Completed));
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Single(verify.Notifications);
            Assert.Single(verify.ObservationTriggerMatchReceipts);
            Assert.Single(verify.ObservationTriggerNotificationLinks);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Migrated_database_rejects_forged_and_mutated_observation_match_evidence()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var setup = await SetupAsync(db);
        await setup.MatchStore.AppendAsync(Definition(setup.Structure.SchemaHash));
        await setup.ObservationStore.AppendObservationAsync(Principal, App,
            Submission('5', "arrival.5", "{\"transition\":\"entered\"}"));
        Assert.Equal(1, (await setup.Worker.RunBatchAsync("observation.migrated")).Completed);

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_observation_match_definition SET NotificationBody = 'rewritten'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_observation_match_work SET Revision = Revision + 2"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_observation_match_receipt
                (Id, ApplicationId, TriggerId, TriggerVersion, ObservationId, Disposition, RecordedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'quest',
                'trigger.arrival.entered', 1, 'observation.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                'matched', CURRENT_TIMESTAMP)
            """));
        Assert.Single(db.ObservationTriggerMatchReceipts);
        Assert.Single(db.ObservationTriggerNotificationLinks);
    }

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db,
        params IObservationMatchAdapter[] replacements)
    {
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(App, "Quest", "", []));
        var schemas = new BoundedJsonSchemaValidator();
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var components = new SqliteEntityComponentStore(db, types, schemas);
        var clock = new FakeTriggerClock(Now);
        var adapters = replacements.Length == 0
            ? [new ClosedScalarsObservationMatchAdapter() as IObservationMatchAdapter]
            : replacements;
        var matchStore = new SqliteObservationTriggerStore(db, spaces, components, adapters, clock);
        var participant = new ObservationTriggerAppendParticipant(db, clock);
        var observationStore = new SqliteTriggerSchedulingStore(db, clock, [participant]);
        var structure = Structure(1);
        await observationStore.AppendStructureAsync(structure);
        await observationStore.AppendSourceAsync(Source(1, 1));
        var worker = new SqliteObservationTriggerWorker(db, clock, matchStore,
            new TriggerNotificationTransactionParticipant(db, clock));
        return new(db, structure, observationStore, matchStore, worker, clock);
    }

    private static SqliteObservationTriggerWorker Worker(DantesRoleplayDbContext db, FakeTriggerClock clock)
    {
        var applications = new SqliteApplicationRegistry(db);
        var schemas = new BoundedJsonSchemaValidator();
        var matchStore = new SqliteObservationTriggerStore(db,
            new SqliteStateSpaceRegistry(db, applications),
            new SqliteEntityComponentStore(db, new SqliteComponentTypeRegistry(db, schemas), schemas),
            [new ClosedScalarsObservationMatchAdapter()], clock);
        return new SqliteObservationTriggerWorker(db, clock, matchStore,
            new TriggerNotificationTransactionParticipant(db, clock));
    }

    private static ObservationStructureDefinition Structure(int version)
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"},\"count\":{\"type\":\"number\"}}}";
        return ObservationStructureDefinition.Create(App, "device.geofence.transition", version,
            SystemJsonSchemaProfile.Version2Id, schema,
            TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(schema)), "Geofence transition.");
    }

    private static ObservationSourceDefinition Source(int version, int structureVersion,
        ObservationSourceStatus status = ObservationSourceStatus.Enabled) =>
        ObservationSourceDefinition.Create(App, "phone.dante", version, status,
            [ObservationStructureReference.Create("device.geofence.transition", structureVersion)],
            [Principal.PrincipalId], TimeSpan.FromHours(1), 10);

    private static ObservationTriggerDefinition Definition(string structureHash,
        string config = "{\"matches\":[{\"property\":\"transition\",\"value\":\"entered\"}]}",
        string adapterId = ClosedScalarsObservationMatchAdapter.StableId,
        string triggerId = "trigger.arrival.entered",
        TriggerNotificationTarget? notification = null) =>
        ObservationTriggerDefinition.Create(App, triggerId, 1,
            ObservationTriggerLifecycle.Active, "phone.dante", 1, "device.geofence.transition", 1,
            structureHash, ObservationMatchAdapterReference.Create(adapterId, 1), config,
            TriggerFireTarget.NotificationOnly, notification ??
            TriggerNotificationTarget.Create("arrival", "Arrived", "The device entered the area."));

    private static ObservationSubmission Submission(char suffix, string occurrence, string data) =>
        ObservationSubmission.Create("observation-request.0123456789abcdef0123456789abcde" + suffix,
            ObservationSourceReference.Create("phone.dante", "android-primary", occurrence),
            ObservationStructureReference.Create("device.geofence.transition", 1), Now.AddMinutes(-1), data);

    private static ObservationMatchInput Input(ObservationTriggerDefinition definition, string data) =>
        new(definition.ApplicationId, "observation.0123456789abcdef0123456789abcdef",
            definition.SourceId, definition.SourceVersion, definition.StructureId,
            definition.StructureVersion, definition.StructureHash,
            ObservationDataCanonicalizer.ParseObject(data));

    public void Dispose() => fixture.Dispose();

    private sealed record Setup(DantesRoleplayDbContext Db, ObservationStructureDefinition Structure,
        SqliteTriggerSchedulingStore ObservationStore, SqliteObservationTriggerStore MatchStore,
        SqliteObservationTriggerWorker Worker, FakeTriggerClock Clock);

    private sealed class TransientAdapter : IObservationMatchAdapter
    {
        public string Id => "system.trigger.test-transient-observation";
        public int Version => 1;
        public void Validate(ObservationTriggerDefinition definition) { }
        public bool Evaluate(ObservationTriggerDefinition definition, ObservationMatchInput observation) =>
            throw new TriggerSchedulingTransientException();
    }

    private sealed class PermanentAdapter : IObservationMatchAdapter
    {
        public string Id => "system.trigger.test-permanent-observation";
        public int Version => 1;
        public void Validate(ObservationTriggerDefinition definition) { }
        public bool Evaluate(ObservationTriggerDefinition definition, ObservationMatchInput observation) =>
            throw new InvalidOperationException("Injected permanent matcher failure.");
    }
}
