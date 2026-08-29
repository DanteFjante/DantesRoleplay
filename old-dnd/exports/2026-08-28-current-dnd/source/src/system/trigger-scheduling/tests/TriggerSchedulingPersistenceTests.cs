using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using DantesRoleplay.TriggerScheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class TriggerSchedulingPersistenceTests : IDisposable
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly TrustedPrincipalContext OtherPrincipal = PrivateOperatorPrincipal.Create("test", "other");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Immutable_structure_source_and_trigger_revisions_replay_or_conflict()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var structure = Structure(1);
        var source = Source(1, 1);
        var trigger = Trigger(1);

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, (await store.AppendStructureAsync(structure)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, (await store.AppendStructureAsync(structure)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict, (await store.AppendStructureAsync(Structure(1, "Changed description."))).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, (await store.AppendSourceAsync(source)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, (await store.AppendSourceAsync(source)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict, (await store.AppendSourceAsync(Source(1, 1, ObservationSourceStatus.Disabled))).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, (await store.AppendOneTimeTriggerAsync(trigger)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, (await store.AppendOneTimeTriggerAsync(trigger)).Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict, (await store.AppendOneTimeTriggerAsync(Trigger(1, TriggerMisfirePolicy.Skip))).Disposition);

        Assert.Single(db.TriggerObservationStructures);
        Assert.Single(db.TriggerObservationSources);
        Assert.Single(db.TriggerObservationSourceStructures);
        Assert.Single(db.OneTimeTriggers);
        Assert.Equal(1, db.TriggerObservationStructureCurrent.Single().CurrentVersion);
        Assert.Equal(1, db.TriggerObservationSourceCurrent.Single().CurrentVersion);
        Assert.Equal(Principal.PrincipalId, db.TriggerObservationSourcePrincipals.Single().PrincipalId);
        Assert.Equal(1, db.OneTimeTriggerCurrent.Single().CurrentVersion);
    }

    [Fact]
    public async Task Source_requires_an_existing_exact_same_application_structure_revision()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        var exception = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendSourceAsync(Source(1, 1)));

        Assert.Equal("TRIGGER_SCHEDULING_STRUCTURE_NOT_FOUND", exception.Code);
        Assert.Empty(db.TriggerObservationSources);
    }

    [Fact]
    public async Task Observation_evidence_is_idempotent_and_retains_exact_revisions()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var structureV1 = Structure(1);
        var sourceV1 = Source(1, 1);
        await store.AppendStructureAsync(structureV1);
        await store.AppendSourceAsync(sourceV1);
        var first = Submission(RequestId('1'), "arrival.1", "{\"transition\":\"entered\"}");

        var appended = await store.AppendObservationAsync(Principal, Application, first);
        var replay = await store.AppendObservationAsync(Principal, Application, first);

        var structureV2 = Structure(2, "Revised geofence transition.");
        var sourceV2 = Source(2, 2);
        await store.AppendStructureAsync(structureV2);
        await store.AppendSourceAsync(sourceV2);
        var stale = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendObservationAsync(Principal, Application, Submission(RequestId('2'), "arrival.2", "{\"transition\":\"left\"}")));
        var secondSubmission = Submission(RequestId('2'), "arrival.2", "{\"transition\":\"left\"}", 2);
        var second = await store.AppendObservationAsync(Principal, Application, secondSubmission);
        var occurrenceConflict = await store.AppendObservationAsync(Principal, Application,
            Submission(RequestId('3'), "arrival.2", "{\"transition\":\"entered\"}", 2));
        var requestConflict = await store.AppendObservationAsync(Principal, Application,
            Submission(RequestId('2'), "arrival.3", "{\"transition\":\"left\"}", 2));

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(appended.Value!.Id, replay.Value!.Id);
        Assert.Equal("TRIGGER_SCHEDULING_OBSERVATION_STALE", stale.Code);
        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, second.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict, occurrenceConflict.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Conflict, requestConflict.Disposition);
        Assert.Equal(2, db.TriggerObservations.Count());
        Assert.Equal([1, 2], db.TriggerObservations.OrderBy(row => row.SourceVersion).Select(row => row.SourceVersion).ToArray());
        Assert.Equal([1, 2], db.TriggerObservations.OrderBy(row => row.StructureVersion).Select(row => row.StructureVersion).ToArray());
        Assert.All(db.TriggerObservations, row => Assert.Equal(Principal.PrincipalId, row.PrincipalId));
    }

    [Fact]
    public async Task Store_rejects_a_principal_not_permitted_by_the_current_source_revision()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        await store.AppendStructureAsync(Structure(1));
        await store.AppendSourceAsync(Source(1, 1));

        var denied = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendObservationAsync(OtherPrincipal, Application,
                Submission(RequestId('9'), "arrival.9", "{}")));

        Assert.Equal("OBSERVATION_PRINCIPAL_FORBIDDEN", denied.Code);
        Assert.Empty(db.TriggerObservations);
    }

    [Fact]
    public async Task Fire_receipts_only_record_eligible_evaluations_and_replay_deterministically()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var trigger = Trigger(1);
        await store.AppendOneTimeTriggerAsync(trigger);

        var exception = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendFireReceiptAsync(trigger));
        clock.Set(trigger.DueAt);
        var appended = await store.AppendFireReceiptAsync(trigger);
        clock.Advance(TimeSpan.FromMinutes(1));
        var replay = await store.AppendFireReceiptAsync(trigger);

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(appended.Value!.Id, replay.Value!.Id);
        Assert.Equal("TRIGGER_FIRE_NOT_ELIGIBLE", exception.Code);
        Assert.Single(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task Failed_source_permission_insert_rolls_back_the_source_header()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        await store.AppendStructureAsync(Structure(1));
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER trigger_source_permission_failure BEFORE INSERT ON trigger_observation_source_structure BEGIN SELECT RAISE(ABORT, 'injected permission failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => store.AppendSourceAsync(Source(1, 1)));

        db.ChangeTracker.Clear();
        Assert.Empty(db.TriggerObservationSources);
        Assert.Empty(db.TriggerObservationSourceStructures);
    }

    [Fact]
    public async Task SQLite_rejects_forged_observation_values_that_bypass_the_store()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        var structure = Structure(1);
        var source = Source(1, 1);
        await store.AppendStructureAsync(structure);
        await store.AppendSourceAsync(source);
        await store.AppendStructureAsync(Structure(2));
        db.TriggerObservations.Add(new TriggerObservationRecord
        {
            Id = "observation.0123456789abcdef0123456789abcdef", ApplicationId = Application.Value,
            RequestId = RequestId('4'), SourceId = source.Id, SourceVersion = 1,
            SourceInstanceId = "android-primary", OccurrenceId = "arrival.4", StructureId = structure.Id,
            StructureVersion = 2, StructureHash = new string('A', 64), ObservedAtUtc = Now.UtcDateTime,
            ReceivedAtUtc = Now.UtcDateTime, DataJson = "{}", DataHash = new string('A', 64),
            RequestFingerprint = new string('A', 64)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(db.TriggerObservations);
    }

    [Fact]
    public async Task Store_revalidates_time_and_current_enabled_source_with_its_trusted_clock()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendStructureAsync(Structure(1));
        await store.AppendSourceAsync(Source(1, 1));
        var submission = Submission(RequestId('5'), "arrival.5", "{}");

        clock.Advance(TimeSpan.FromHours(2));
        var expired = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendObservationAsync(Principal, Application, submission));
        clock.Set(Now);
        await store.AppendSourceAsync(Source(2, 1, ObservationSourceStatus.Disabled));
        var disabled = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            store.AppendObservationAsync(Principal, Application, Submission(RequestId('6'), "arrival.6", "{}")));

        Assert.Equal("OBSERVATION_TIME_EXPIRED", expired.Code);
        Assert.Equal("OBSERVATION_SOURCE_DISABLED", disabled.Code);
        Assert.Empty(db.TriggerObservations);
    }

    [Fact]
    public async Task Superseded_trigger_revision_cannot_record_a_fire()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var clock = new FakeTriggerClock(Now.AddHours(1));
        var store = new SqliteTriggerSchedulingStore(db, clock);
        var first = Trigger(1);
        await store.AppendOneTimeTriggerAsync(first);
        await store.AppendOneTimeTriggerAsync(Trigger(2));

        var stale = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() => store.AppendFireReceiptAsync(first));

        Assert.Equal("TRIGGER_SCHEDULING_TRIGGER_STALE", stale.Code);
        Assert.Empty(db.TriggerFireReceipts);
    }

    [Fact]
    public async Task EF_rejects_updates_and_deletes_of_immutable_trigger_rows()
    {
        await using var db = _fixture.CreateContext();
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        await store.AppendStructureAsync(Structure(1));
        var row = db.TriggerObservationStructures.Single();
        row.Description = "Rewritten evidence.";

        var update = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.TriggerObservationStructures.Remove(db.TriggerObservationStructures.Single());
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Equal("TRIGGER_SCHEDULING_IMMUTABLE", update.Message);
        Assert.Equal("TRIGGER_SCHEDULING_IMMUTABLE", delete.Message);
        db.ChangeTracker.Clear();
        Assert.Single(db.TriggerObservationStructures);
    }

    [Fact]
    public async Task Hardening_migration_backfills_current_pointers_and_installs_SQLite_immutability_triggers()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260825104620_TriggerSchedulingPersistence");
        RegisterApplication(db);
        var structure1 = Structure(1);
        var structure2 = Structure(2);
        await InsertLegacyStructureAsync(db, structure1);
        await InsertLegacyStructureAsync(db, structure2);
        db.TriggerObservationSources.Add(SourceRow(Source(1, 1)));
        db.TriggerObservationSourceStructures.Add(new TriggerObservationSourceStructureRecord
        {
            ApplicationId = Application.Value, SourceId = "phone.dante", SourceVersion = 1,
            StructureId = structure1.Id, StructureVersion = 1
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_one_time_definition
                (ApplicationId, Id, Version, DueAtUtc, MisfirePolicy, Target, RecordedAtUtc)
            VALUES ('quest', 'trigger.session.soft-ending', 1, '2026-08-25 21:00:00',
                'fire-once', 'notification-only', '2026-08-25 20:00:00');
            """);
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        Assert.Equal(2, db.TriggerObservationStructureCurrent.Single().CurrentVersion);
        Assert.Equal(1, db.TriggerObservationSourceCurrent.Single().CurrentVersion);
        Assert.Equal(1, db.OneTimeTriggerCurrent.Single().CurrentVersion);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay,
            (await new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now))
                .AppendOneTimeTriggerAsync(Trigger(1))).Disposition);
        var triggerNames = await ScalarStringsAsync(db,
            "SELECT name FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'trigger_%_immutable_%' ORDER BY name");
        Assert.True(triggerNames.Count == 20, $"Expected 20 immutable triggers, found: {string.Join(", ", triggerNames)}");
        Assert.Empty(db.TriggerObservationSourcePrincipals);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_observation_structure SET Description = Description WHERE ApplicationId = 'quest'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM trigger_observation_structure WHERE ApplicationId = 'quest'"));
    }

    [Fact]
    public async Task Ingestion_migration_preserves_historical_unbound_evidence_and_requires_principal_on_new_rows()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260825111858_TriggerSchedulingObservationImmutability");
        RegisterApplication(db);
        var structure = Structure(1);
        await InsertLegacyStructureAsync(db, structure);
        db.TriggerObservationSources.Add(SourceRow(Source(1, 1)));
        db.TriggerObservationSourceStructures.Add(new TriggerObservationSourceStructureRecord
        {
            ApplicationId = Application.Value, SourceId = "phone.dante", SourceVersion = 1,
            StructureId = structure.Id, StructureVersion = 1
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_observation
                (Id, ApplicationId, RequestId, SourceId, SourceVersion, SourceInstanceId,
                 OccurrenceId, StructureId, StructureVersion, StructureHash, ObservedAtUtc,
                 ReceivedAtUtc, DataJson, DataHash, RequestFingerprint)
            VALUES
                ('observation.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'quest',
                 'observation-request.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'phone.dante', 1,
                 'android-primary', 'arrival.legacy', 'device.geofence.transition', 1,
                 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                 '2026-08-25 19:59:00', '2026-08-25 20:00:00', '{{}}',
                 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
            """);
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        Assert.Null(db.TriggerObservations.Single().PrincipalId);
        Assert.Empty(db.TriggerObservationSourcePrincipals);
        var required = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_observation
                (Id, ApplicationId, RequestId, SourceId, SourceVersion, SourceInstanceId,
                 OccurrenceId, StructureId, StructureVersion, StructureHash, ObservedAtUtc,
                 ReceivedAtUtc, DataJson, DataHash, RequestFingerprint, PrincipalId)
            VALUES
                ('observation.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'quest',
                 'observation-request.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'phone.dante', 1,
                 'android-primary', 'arrival.new', 'device.geofence.transition', 1,
                 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                 '2026-08-25 19:59:00', '2026-08-25 20:00:00', '{{}}',
                 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB', NULL);
            """));
        Assert.Contains("OBSERVATION_PRINCIPAL_REQUIRED", required.Message, StringComparison.Ordinal);
        Assert.Single(db.TriggerObservations);
    }

    [Fact]
    public async Task Concurrent_exact_observation_loser_rereads_the_winner_as_replay()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dantesroleplay-trigger-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var baseOptions = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connectionString).Options;
            await using (var setup = new DantesRoleplayDbContext(baseOptions))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                RegisterApplication(setup);
                var setupStore = new SqliteTriggerSchedulingStore(setup, new FakeTriggerClock(Now));
                await setupStore.AppendStructureAsync(Structure(1));
                await setupStore.AppendSourceAsync(Source(1, 1));
            }

            {
                var submission = Submission(RequestId('7'), "arrival.7", "{\"transition\":\"entered\"}");
                await using var firstDb = new DantesRoleplayDbContext(baseOptions);
                await using var secondDb = new DantesRoleplayDbContext(baseOptions);
                var firstStore = new SqliteTriggerSchedulingStore(firstDb, new FakeTriggerClock(Now));
                var secondStore = new SqliteTriggerSchedulingStore(secondDb, new FakeTriggerClock(Now));

                var results = await Task.WhenAll(
                    firstStore.AppendObservationAsync(Principal, Application, submission),
                    secondStore.AppendObservationAsync(Principal, Application, submission));

                Assert.Contains(results, result => result.Disposition == TriggerSchedulingWriteDisposition.Appended);
                Assert.Contains(results, result => result.Disposition == TriggerSchedulingWriteDisposition.Replay);
                Assert.Equal(results[0].Value!.Id, results[1].Value!.Id);
                await using var verify = new DantesRoleplayDbContext(baseOptions);
                Assert.Single(verify.TriggerObservations);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var target = databasePath + suffix;
                if (File.Exists(target)) File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task Concurrent_changed_observation_identity_returns_conflict_without_a_second_row()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dantesroleplay-trigger-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var baseOptions = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connectionString).Options;
            await using (var setup = new DantesRoleplayDbContext(baseOptions))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                RegisterApplication(setup);
                var setupStore = new SqliteTriggerSchedulingStore(setup, new FakeTriggerClock(Now));
                await setupStore.AppendStructureAsync(Structure(1));
                await setupStore.AppendSourceAsync(Source(1, 1));
            }

            {
                await using var firstDb = new DantesRoleplayDbContext(baseOptions);
                await using var secondDb = new DantesRoleplayDbContext(baseOptions);
                var firstStore = new SqliteTriggerSchedulingStore(firstDb, new FakeTriggerClock(Now));
                var secondStore = new SqliteTriggerSchedulingStore(secondDb, new FakeTriggerClock(Now));

                var results = await Task.WhenAll(
                    firstStore.AppendObservationAsync(Principal, Application,
                        Submission(RequestId('8'), "arrival.8", "{\"transition\":\"entered\"}")),
                    secondStore.AppendObservationAsync(Principal, Application,
                        Submission(RequestId('8'), "arrival.9", "{\"transition\":\"left\"}")));

                Assert.Contains(results, result => result.Disposition == TriggerSchedulingWriteDisposition.Appended);
                Assert.Contains(results, result => result.Disposition == TriggerSchedulingWriteDisposition.Conflict);
                await using var verify = new DantesRoleplayDbContext(baseOptions);
                Assert.Single(verify.TriggerObservations);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var target = databasePath + suffix;
                if (File.Exists(target)) File.Delete(target);
            }
        }
    }

    private static void RegisterApplication(DantesRoleplayDbContext db) => new SqliteApplicationRegistry(db).Register(
        new ApplicationRegistration(Application, "Quest", "A test application.", []));

    private static ObservationStructureDefinition Structure(int version, string description = "A device geofence transition.")
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"}}}";
        return ObservationStructureDefinition.Create(Application, "device.geofence.transition", version,
            SystemJsonSchemaProfile.Version2Id, schema, Hash(schema), description);
    }

    private static ObservationSourceDefinition Source(int version, int structureVersion, ObservationSourceStatus status = ObservationSourceStatus.Enabled) =>
        ObservationSourceDefinition.Create(Application, "phone.dante", version, status,
            [ObservationStructureReference.Create("device.geofence.transition", structureVersion)],
            [Principal.PrincipalId], TimeSpan.FromHours(1), 10);

    private static OneTimeTriggerDefinition Trigger(int version, TriggerMisfirePolicy policy = TriggerMisfirePolicy.FireOnce) =>
        OneTimeTriggerDefinition.Create(Application, "trigger.session.soft-ending", version, Now.AddHours(1), policy);

    private static ObservationSubmission Submission(string requestId, string occurrenceId, string data, int structureVersion = 1) =>
        ObservationSubmission.Create(requestId, ObservationSourceReference.Create("phone.dante", "android-primary", occurrenceId),
            ObservationStructureReference.Create("device.geofence.transition", structureVersion), Now.AddMinutes(-1), data);

    private static string RequestId(char suffix) => "observation-request.0123456789abcdef0123456789abcde" + suffix;
    private static string Hash(string value) => TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(value));

    private static TriggerObservationStructureRecord StructureRow(ObservationStructureDefinition value) => new()
    {
        ApplicationId = value.ApplicationId.Value, Id = value.Id, Version = value.Version,
        SchemaProfileId = value.SchemaProfileId, NormalizedSchema = value.NormalizedSchema,
        SchemaHash = value.SchemaHash, Description = value.Description, Status = "active", RecordedAtUtc = Now.UtcDateTime
    };

    private static Task<int> InsertLegacyStructureAsync(DantesRoleplayDbContext db,
        ObservationStructureDefinition value) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO trigger_observation_structure
            (ApplicationId, Id, Version, SchemaProfileId, NormalizedSchema, SchemaHash,
             Description, Status, RecordedAtUtc)
        VALUES ({value.ApplicationId.Value}, {value.Id}, {value.Version}, {value.SchemaProfileId},
            {value.NormalizedSchema}, {value.SchemaHash}, {value.Description}, 'active', {Now.UtcDateTime});
        """);

    private static TriggerObservationSourceRecord SourceRow(ObservationSourceDefinition value) => new()
    {
        ApplicationId = value.ApplicationId.Value, Id = value.Id, Version = value.Version,
        Status = "enabled", ReplayWindowSeconds = (int)value.ReplayWindow.TotalSeconds,
        RequestsPerMinute = value.RequestsPerMinute, RecordedAtUtc = Now.UtcDateTime
    };

    private static OneTimeTriggerRecord TriggerRow(OneTimeTriggerDefinition value) => new()
    {
        ApplicationId = value.ApplicationId.Value, Id = value.Id, Version = value.Version,
        DueAtUtc = value.DueAt.UtcDateTime, MisfirePolicy = "fire-once", Target = "notification-only",
        RecordedAtUtc = Now.UtcDateTime
    };

    private static async Task<long> ScalarLongAsync(DantesRoleplayDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<string>> ScalarStringsAsync(DantesRoleplayDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

}
