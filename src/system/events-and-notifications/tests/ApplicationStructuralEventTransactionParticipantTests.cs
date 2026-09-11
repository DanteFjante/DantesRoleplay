using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Events.Tests;

public sealed class ApplicationStructuralEventTransactionParticipantTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    [Fact]
    public async Task Component_receipts_write_one_sourced_structural_event_per_committed_change()
    {
        var setup = await SetupAsync();
        var batch = setup.Batch("11111111111111111111111111111111", new[]
        {
            Set(setup.First, "{\"value\":2}", 1),
            new ApplicationEcsEffect
            {
                Type = ApplicationEcsEffectType.ComponentMerge,
                EntityId = "entity.two",
                ComponentType = setup.Second,
                DataJson = "{\"extra\":true}",
                ExpectedRevision = 1
            }
        });

        var first = await setup.Applier.ApplyAsync(batch);
        var replay = await setup.Applier.ApplyAsync(batch);
        var events = await setup.Ledger.FindAsync(rootOperationId: "11111111111111111111111111111111");

        Assert.True(first.Applied);
        Assert.True(replay.Replayed);
        Assert.Equal(new[] { "world.component.replaced", "world.component.merged" },
            events.Select(value => value.TypeId));
        Assert.All(events, value => Assert.Equal(new EventSourceContext("event-fixture", "event-space"), value.Source));
        Assert.All(events, value => Assert.Equal("11111111111111111111111111111111", value.RootOperationId));

        var replaced = await setup.Ledger.GetAsync(events[0].Id);
        Assert.NotNull(replaced?.ComponentSnapshot);
        Assert.Equal("entity.one", replaced.ComponentSnapshot.EntityId);
        Assert.Equal(setup.First.QualifiedTypeId, replaced.ComponentSnapshot.QualifiedTypeId);
        Assert.Equal(1, replaced.ComponentSnapshot.BeforeRevision);
        Assert.Equal(2, replaced.ComponentSnapshot.AfterRevision);
        Assert.Equal("{\"value\":1}", replaced.ComponentSnapshot.BeforeJson);
        Assert.Equal("{\"value\":2}", replaced.ComponentSnapshot.AfterJson);
        using var payload = JsonDocument.Parse(replaced.PayloadJson);
        Assert.Equal(0, payload.RootElement.GetProperty("effectIndex").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("after").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Noop_and_late_rollback_leave_no_sourced_event_or_snapshot()
    {
        var setup = await SetupAsync();
        var noOp = await setup.Applier.ApplyAsync(setup.Batch("22222222222222222222222222222222", []));
        var failing = setup.WithLateFailure();
        var rejected = await failing.ApplyAsync(setup.Batch("33333333333333333333333333333333",
            [Set(setup.First, "{\"value\":2}", 1)]));

        Assert.True(noOp.Applied);
        Assert.False(rejected.Applied);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: "22222222222222222222222222222222"));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: "33333333333333333333333333333333"));
        Assert.Equal(0, await setup.Db.Set<EventComponentSnapshot>().CountAsync());
        var current = await setup.Entities.GetComponentAsync("event-space", "entity.one", setup.First.QualifiedTypeId);
        Assert.Equal("{\"value\":1}", current!.ValueJson);
        Assert.Equal(1, current.Revision);
    }

    [Fact]
    public async Task Legacy_ledger_rows_remain_unsourced_and_malformed_source_is_rejected()
    {
        var setup = await SetupAsync();
        var legacy = await setup.Ledger.WriteAcceptedAsync(
            [new ProposedEvent("world.component.added", "{\"effectIndex\":0,\"entityId\":\"entity.one\",\"definitionId\":\"fixture.component\",\"before\":null,\"after\":{\"value\":1}}",
                ["entity.one"], "", 0)], "legacy-operation");

        Assert.Null((await setup.Ledger.GetAsync(legacy.Single().Id))!.Source);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Ledger.WriteAcceptedAsync(
            [new ProposedEvent("world.component.added", "{}", [], "", 0)], "invalid-source",
            source: new EventSourceContext("", "event-space")));
    }

    [Fact]
    public async Task Trusted_reaction_clock_uses_immutable_emission_provenance_when_it_has_no_request_identity()
    {
        var setup = await SetupAsync();
        await setup.EventTypes.WriteAsync(new()
        {
            Id = "fixture.clock.advanced", Category = "fixture", Name = "Clock", Scope = "",
            Status = EventTypeStatus.Active,
            PayloadSchema = """{"type":"object","required":["idempotencyKey"],"properties":{"idempotencyKey":{"type":"string","minLength":64,"maxLength":64,"pattern":"^[0-9A-F]{64}$"}}}"""
        });
        var batch = new ApplicationEcsEffectBatch
        {
            StateSpaceId = "event-space", MechanicId = "fixture.clock", MechanicVersion = 1,
            Seed = 1, ProjectionJson = "{}", Effects = [new()
            {
                Type = ApplicationEcsEffectType.ClockAdvance, EntityId = "entity.one",
                ComponentType = setup.First, DataJson = "{\"value\":2}", ExpectedRevision = 1,
                CalendarId = "fixture-calendar", PreviousMinute = 10, DeltaMinutes = 1,
                ResultingMinute = 11, PreviousClockRevision = 1, ResultingClockRevision = 2,
                EventTypeId = "fixture.clock.advanced", SubjectEntityId = "entity.one", ActivityId = "fixture-activity"
            }]
        };
        Assert.Contains(ApplicationEcsEffectValidation.Validate(batch), value => value.Code == "CLOCK_AUDIT_REQUIRED");
        Assert.Empty(ApplicationEcsEffectValidation.Validate(batch, trustedReaction: true));

        var clock = new ApplicationClockEventTransactionParticipant(
            setup.EventTypes, setup.Ledger, setup.Spaces, setup.Schemas);
        var emitted = await clock.StageEventsAsync(batch,
            [new(0, ApplicationEcsEffectType.ClockAdvance, "entity.one", setup.First.QualifiedTypeId, 2,
                ComponentTypeVersion: 1, BatchEffectIndex: 0)],
            new("44444444444444444444444444444444", 1, "parent-event", "event-reaction:fixture"));

        var detail = Assert.Single(emitted);
        using var payload = JsonDocument.Parse(detail.PayloadJson);
        Assert.Matches("^[0-9A-F]{64}$", payload.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.NotEqual("", detail.CausationId);
        Assert.Equal(1, detail.Depth);
    }

    [Fact]
    public async Task Source_context_migration_retains_legacy_rows_enforces_pairs_and_rolls_back_on_a_disposable_database()
    {
        var path = Path.Combine(Path.GetTempPath(), "event-source-migration-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260907210000_ProjectionRegistryGeneration");

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO event (Id, TypeId, TypeVersion, Timestamp, CorrelationId, CausationId,
                    RootOperationId, Scope, PayloadJson, Depth, ProducerExecutionId, Sequence)
                VALUES ('legacy-event', 'world.component.replaced', 1, CURRENT_TIMESTAMP, 'legacy-event', '',
                    'legacy-root', '', '{{}}', 0, '', 1);
                INSERT INTO subscription (Id, Category, CurrentVersion, Scope, Status, CreatedAt, UpdatedAt)
                VALUES ('legacy-subscription', 'test', 1, '', 'Active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                INSERT INTO subscription_version (SubscriptionId, Version, EventTypeId, EventMechanicId, Mode,
                    "Order", FixedRoleEntityIdsJson, RoleFromEventPayloadJson, FanoutSelectorJson,
                    TrackedEntityIdsJson, PayloadEqualsJson, MaxExecutionsPerChain, ChangeNote, CreatedBy,
                    SourceHash, CreatedAt)
                VALUES ('legacy-subscription', 1, 'world.component.replaced', 'fixture.mechanic', 'Reaction',
                    0, '{{}}', '{{}}', '{{}}', '[]', '{{}}', 1, '', 'test', '', CURRENT_TIMESTAMP);
                """);

            await migrator.MigrateAsync("20260911000100_ApplicationEventSourceContext");
            Assert.Equal(1L, await db.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS Value FROM event
                WHERE Id = 'legacy-event' AND ApplicationId IS NULL AND StateSpaceId IS NULL
                """).SingleAsync());
            Assert.Equal(1L, await db.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS Value FROM subscription_version
                WHERE SubscriptionId = 'legacy-subscription' AND ApplicationId IS NULL AND StateSpaceId IS NULL
                """).SingleAsync());
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE event SET ApplicationId = 'fixture' WHERE Id = 'legacy-event';"));
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE event SET ApplicationId = 'fixture', StateSpaceId = 'space' WHERE Id = 'legacy-event';");
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE subscription_version SET ApplicationId = 'fixture' WHERE SubscriptionId = 'legacy-subscription';"));
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE subscription_version SET ApplicationId = 'fixture', StateSpaceId = 'space' WHERE SubscriptionId = 'legacy-subscription';");

            await migrator.MigrateAsync("20260907210000_ProjectionRegistryGeneration");
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
            await using var columns = connection.CreateCommand();
            columns.CommandText = "SELECT count(*) FROM pragma_table_info('event') WHERE name = 'ApplicationId';";
            Assert.Equal(0L, Convert.ToInt64(await columns.ExecuteScalarAsync()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public void Dispose() => fixture.Dispose();

    private async Task<Setup> SetupAsync()
    {
        var db = fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("event-fixture");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(application, "Event fixture", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("event-space", revision, new string('A', 64)));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var first = types.Define(new(application, "event-fixture.fixture.component",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var second = types.Define(new(application, "event-fixture.fixture.other",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"},\"extra\":{\"type\":\"boolean\"}}}"));
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync("event-space", "entity.one", "One");
        await entities.CreateEntityAsync("event-space", "entity.two", "Two");
        await entities.AddComponentAsync(new("event-space", "entity.one", Ref(first), "{\"value\":1}", 0));
        await entities.AddComponentAsync(new("event-space", "entity.two", Ref(second), "{\"value\":1}", 0));
        var events = new EventTypeStore(db);
        foreach (var (id, schema) in StructuralSchemas())
            await events.WriteAsync(new() { Id = id, Category = "fixture", Name = id, Scope = "",
                Status = EventTypeStatus.Active, PayloadSchema = schema });
        var ledger = new EventLedger(db);
        var participant = new ApplicationStructuralEventTransactionParticipant(spaces, ledger, events, schemas);
        return new Setup(db, entities, ledger, new ApplicationEcsEffectApplier(db, entities, spaces,
            new OperationLog(db), eventSources: [participant]), Ref(first), Ref(second), participant, spaces, events, schemas);
    }

    private static IReadOnlyList<(string Id, string Schema)> StructuralSchemas() =>
    [
        ("world.component.added", "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"effectIndex\",\"entityId\",\"definitionId\",\"before\",\"after\"],\"properties\":{\"effectIndex\":{\"type\":\"integer\"},\"entityId\":{\"type\":\"string\"},\"definitionId\":{\"type\":\"string\"},\"before\":{},\"after\":{}}}"),
        ("world.component.replaced", "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"effectIndex\",\"entityId\",\"definitionId\",\"before\",\"after\"],\"properties\":{\"effectIndex\":{\"type\":\"integer\"},\"entityId\":{\"type\":\"string\"},\"definitionId\":{\"type\":\"string\"},\"before\":{},\"after\":{}}}"),
        ("world.component.merged", "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"effectIndex\",\"entityId\",\"definitionId\",\"patch\",\"before\",\"after\"],\"properties\":{\"effectIndex\":{\"type\":\"integer\"},\"entityId\":{\"type\":\"string\"},\"definitionId\":{\"type\":\"string\"},\"patch\":{},\"before\":{},\"after\":{}}}")
    ];

    private static ApplicationEcsEffect Set(EcsComponentReference type, string value, int revision) => new()
    {
        Type = ApplicationEcsEffectType.ComponentSet,
        EntityId = "entity.one",
        ComponentType = type,
        DataJson = value,
        ExpectedRevision = revision
    };

    private static EcsComponentReference Ref(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private sealed record Setup(
        DantesRoleplayDbContext Db,
        SqliteEntityComponentStore Entities,
        EventLedger Ledger,
        ApplicationEcsEffectApplier Applier,
        EcsComponentReference First,
        EcsComponentReference Second,
        ApplicationStructuralEventTransactionParticipant Participant,
        SqliteStateSpaceRegistry Spaces,
        EventTypeStore EventTypes,
        BoundedJsonSchemaValidator Schemas)
    {
        public ApplicationEcsEffectBatch Batch(string operationId, IReadOnlyList<ApplicationEcsEffect> effects) => new()
        {
            StateSpaceId = "event-space",
            Effects = effects,
            ExecutionIdentity = new ApplicationEcsExecutionIdentity(operationId, new string('B', 64))
        };

        public ApplicationEcsEffectApplier WithLateFailure() => new(Db, Entities, Spaces, new OperationLog(Db),
            transactionParticipants: [new RejectAfterEvents()], eventSources: [Participant]);
    }

    private sealed class RejectAfterEvents : IApplicationEcsTransactionParticipant
    {
        public Task StageAsync(ApplicationEcsEffectBatch batch, IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
            string operationId, CancellationToken cancellationToken = default) =>
            throw new ApplicationEcsTransactionParticipantException("Fixture rejection after event staging.");
    }
}
