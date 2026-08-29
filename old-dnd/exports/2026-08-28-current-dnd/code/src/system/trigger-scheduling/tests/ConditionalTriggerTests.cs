using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ConditionalTriggerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 18, 0, 0, TimeSpan.Zero);
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Closed_scalar_adapter_rejects_paths_unknown_fields_and_wrong_clock_policy()
    {
        var adapter = new ClosedScalarConditionalTriggerAdapter();
        var dependency = ConditionalTriggerDependency.Create("world",
            new EcsComponentReference("quest.clock", 1, new string('A', 64)));
        var path = Definition([dependency], "{\"property\":\"nested.value\",\"operator\":\"gte\",\"value\":10}");
        var unknown = Definition([dependency], "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":10,\"path\":\"x\"}");
        var wrongComparison = Definition([dependency], "{\"property\":\"minute\",\"operator\":\"eq\",\"value\":10}");

        Assert.Equal("CLOSED_SCALAR_PROPERTY", Assert.Throws<TriggerSchedulingContractException>(() => adapter.Validate(path)).Code);
        Assert.Equal("CLOSED_SCALAR_CONFIG_FIELD", Assert.Throws<TriggerSchedulingContractException>(() => adapter.Validate(unknown)).Code);
        Assert.Equal("WORLD_CLOCK_TRIGGER_COMPARISON", Assert.Throws<TriggerSchedulingContractException>(() => adapter.Validate(wrongComparison)).Code);
    }

    [Fact]
    public async Task World_clock_threshold_fires_once_only_after_exact_crossing()
    {
        var setup = await SetupAsync("{\"minute\":5,\"calendar\":\"main\"}");
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"guardValue\":\"main\",\"value\":10,\"operator\":\"gte\",\"guardProperty\":\"calendar\",\"property\":\"minute\"}"));

        var crossing = await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":10,\"calendar\":\"main\"}", 1, '1'));
        var stillTrue = await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":11,\"calendar\":\"main\"}", 2, '2'));

        Assert.True(crossing.Applied);
        Assert.True(stillTrue.Applied);
        var work = Assert.Single(await setup.Db.ConditionalTriggerFireWork.AsNoTracking().ToListAsync());
        Assert.Equal(crossing.OperationId, work.ChangeOperationId);
        var state = await setup.Db.ConditionalTriggerState.AsNoTracking().SingleAsync();
        Assert.True(state.CurrentTruth);
        Assert.False(state.Armed);
        Assert.Equal(2, state.EvaluationRevision);
    }

    [Fact]
    public async Task Already_past_threshold_and_unrelated_component_do_not_fire_or_scan()
    {
        var setup = await SetupAsync("{\"minute\":20,\"calendar\":\"main\"}");
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":10}"));

        var unrelated = await setup.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = "quest-space",
            ExecutionIdentity = Identity('3'),
            Effects = [new ApplicationEcsEffect
            {
                Type = ApplicationEcsEffectType.ComponentSet, EntityId = "world",
                ComponentType = setup.OtherType, DataJson = "{\"value\":2}", ExpectedRevision = 1
            }]
        });

        Assert.True(unrelated.Applied);
        Assert.Empty(setup.Db.ConditionalTriggerFireWork);
        var state = setup.Db.ConditionalTriggerState.AsNoTracking().Single();
        Assert.True(state.CurrentTruth);
        Assert.False(state.Armed);
        Assert.Equal(0, state.EvaluationRevision);
    }

    [Fact]
    public async Task State_condition_rearms_on_false_and_each_batch_evaluates_once()
    {
        var setup = await SetupAsync("{\"minute\":0,\"calendar\":\"main\"}");
        var stateDefinition = ConditionalTriggerDefinition.Create(App, "trigger.state.ready", 1,
            ConditionalTriggerLifecycle.Active, ConditionalTriggerKind.StateCondition,
            ConditionalTriggerActivation.Level, ConditionalTriggerRearm.OnFalse, "quest-space",
            [ConditionalTriggerDependency.Create("world", setup.Type)],
            ConditionalTriggerAdapterReference.Create(ClosedScalarConditionalTriggerAdapter.StableId, 1),
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}",
            TriggerFireTarget.NotificationOnly, Notification());
        await setup.Store.AppendAsync(stateDefinition);

        await setup.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = "quest-space", ExecutionIdentity = Identity('4'),
            Effects =
            [
                Set(setup.Type, "{\"minute\":1,\"calendar\":\"main\"}", 1),
                Set(setup.Type, "{\"minute\":2,\"calendar\":\"main\"}", 2)
            ]
        });
        await setup.Applier.ApplyAsync(Batch(setup.Type, "{\"minute\":0,\"calendar\":\"main\"}", 3, '5'));
        await setup.Applier.ApplyAsync(Batch(setup.Type, "{\"minute\":1,\"calendar\":\"main\"}", 4, '6'));

        Assert.Equal(2, await setup.Db.ConditionalTriggerFireWork.CountAsync());
        var state = await setup.Db.ConditionalTriggerState.AsNoTracking().SingleAsync();
        Assert.Equal(3, state.EvaluationRevision);
        Assert.False(state.Armed);
    }

    [Fact]
    public async Task Store_rejects_stale_component_contract_and_cross_application_state_space()
    {
        var setup = await SetupAsync("{\"minute\":0,\"calendar\":\"main\"}");
        var stale = Definition([ConditionalTriggerDependency.Create("world",
            setup.Type with { SchemaHash = new string('B', 64) })],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}");
        var staleFailure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.Store.AppendAsync(stale));
        var applications = new SqliteApplicationRegistry(setup.Db);
        var other = ApplicationIdentifier.Parse("other");
        var revision = applications.Register(new(other, "Other", "", []));
        new SqliteStateSpaceRegistry(setup.Db, applications).Create(new("other-space", revision, Manifest));
        var crossScope = ConditionalTriggerDefinition.Create(App, "trigger.cross.scope", 1,
            ConditionalTriggerLifecycle.Active, ConditionalTriggerKind.StateCondition,
            ConditionalTriggerActivation.RisingEdge, ConditionalTriggerRearm.OnFalse, "other-space",
            [ConditionalTriggerDependency.Create("world", setup.Type)],
            ConditionalTriggerAdapterReference.Create(ClosedScalarConditionalTriggerAdapter.StableId, 1),
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}",
            TriggerFireTarget.NotificationOnly, Notification());
        var scopeFailure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            setup.Store.AppendAsync(crossScope));

        Assert.Equal("CONDITIONAL_DEPENDENCY_CONTRACT", staleFailure.Code);
        Assert.Equal("CONDITIONAL_STATE_SPACE_SCOPE", scopeFailure.Code);
        Assert.Empty(setup.Db.ConditionalTriggers);
    }

    [Fact]
    public async Task Adapter_failure_rolls_back_the_originating_ecs_change_and_work()
    {
        var throwing = new ThrowsAfterBaselineAdapter();
        var setup = await SetupAsync("{\"minute\":0,\"calendar\":\"main\"}", throwing);
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}", throwing.Id));

        var result = await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":1,\"calendar\":\"main\"}", 1, '7'));

        Assert.False(result.Applied);
        Assert.Equal("EFFECT_REJECTED", Assert.Single(result.Problems).Code);
        Assert.Equal("{\"minute\":0,\"calendar\":\"main\"}",
            (await setup.Components.GetComponentAsync("quest-space", "world", setup.Type.QualifiedTypeId))!.ValueJson);
        Assert.Empty(setup.Db.ConditionalTriggerFireWork);
        Assert.Equal(0, setup.Db.ConditionalTriggerState.AsNoTracking().Single().EvaluationRevision);
    }

    [Fact]
    public async Task Conditional_worker_commits_one_notification_receipt_link_and_status()
    {
        var setup = await SetupAsync("{\"minute\":0,\"calendar\":\"main\"}");
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}"));
        await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":1,\"calendar\":\"main\"}", 1, '8'));
        var worker = new SqliteConditionalTriggerWorker(setup.Db, setup.Clock,
            new TriggerNotificationTransactionParticipant(setup.Db, setup.Clock));

        var first = await worker.RunBatchAsync("conditional.test");
        var replay = await worker.RunBatchAsync("conditional.test");
        var status = await new SqliteConditionalTriggerStatusReader(setup.Db)
            .GetAsync(App, "trigger.world.threshold");

        Assert.Equal(1, first.Completed);
        Assert.Equal(0, replay.Completed);
        Assert.Single(setup.Db.Notifications);
        Assert.Single(setup.Db.ConditionalTriggerFireReceipts);
        Assert.Single(setup.Db.ConditionalTriggerNotificationLinks);
        Assert.NotNull(status!.LastNotificationId);
        Assert.Equal(0, status.CurrentAttemptCount);
    }

    [Fact]
    public async Task Conditional_retry_preserves_one_fire_identity_and_completes_after_backoff()
    {
        var setup = await SetupAsync("{\"minute\":0,\"calendar\":\"main\"}");
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}"));
        await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":1,\"calendar\":\"main\"}", 1, 'b'));
        var participant = new SequenceParticipant(
            TriggerFireAttemptResult.Transient(), TriggerFireAttemptResult.Succeeded());
        var worker = new SqliteConditionalTriggerWorker(setup.Db, setup.Clock, participant);

        var first = await worker.RunBatchAsync("conditional.retry");
        setup.Clock.Advance(SqliteConditionalTriggerWorker.FirstRetryDelay);
        var second = await worker.RunBatchAsync("conditional.retry");

        Assert.Equal(1, first.Retried);
        Assert.Equal(1, second.Completed);
        var work = setup.Db.ConditionalTriggerFireWork.Single();
        Assert.Equal("completed", work.State);
        Assert.Equal(2, work.AttemptCount);
        Assert.Equal(work.FireId, Assert.Single(setup.Db.ConditionalTriggerFireReceipts).Id);
    }

    [Fact]
    public async Task Migrated_database_enforces_provenance_and_immutable_evidence()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var setup = await SetupAsync(db, "{\"minute\":0,\"calendar\":\"main\"}");
        await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
            "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}"));
        Assert.True((await setup.Applier.ApplyAsync(Batch(setup.Type,
            "{\"minute\":1,\"calendar\":\"main\"}", 1, '9'))).Applied);
        var worker = new SqliteConditionalTriggerWorker(db, setup.Clock,
            new TriggerNotificationTransactionParticipant(db, setup.Clock));

        Assert.Equal(1, (await worker.RunBatchAsync("conditional.migrated")).Completed);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_conditional_definition SET NotificationBody = 'rewritten'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE trigger_conditional_state SET EvaluationRevision = EvaluationRevision + 2"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_conditional_fire_work
                (FireId, ApplicationId, TriggerId, TriggerVersion, ChangeOperationId, State,
                 AttemptCount, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('trigger-fire.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'quest',
                'trigger.world.threshold', 1, 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'ready',
                0, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            """));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_conditional_fire_receipt
                (Id, ApplicationId, TriggerId, TriggerVersion, ChangeOperationId, Disposition, RecordedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'quest',
                'trigger.world.threshold', 1, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'due', CURRENT_TIMESTAMP)
            """));
        Assert.Single(db.ConditionalTriggerFireReceipts);
        Assert.Single(db.ConditionalTriggerNotificationLinks);
    }

    [Fact]
    public async Task Two_contexts_commit_only_one_delivery_for_the_same_state_change()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dantes-conditional-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10").Options;
        try
        {
            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                var setup = await SetupAsync(db, "{\"minute\":0,\"calendar\":\"main\"}");
                await setup.Store.AppendAsync(Definition([ConditionalTriggerDependency.Create("world", setup.Type)],
                    "{\"property\":\"minute\",\"operator\":\"gte\",\"value\":1}"));
                Assert.True((await setup.Applier.ApplyAsync(Batch(setup.Type,
                    "{\"minute\":1,\"calendar\":\"main\"}", 1, 'a'))).Applied);
            }
            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var clock = new FakeTriggerClock(Now);
            var results = await Task.WhenAll(
                new SqliteConditionalTriggerWorker(firstDb, clock,
                    new TriggerNotificationTransactionParticipant(firstDb, clock))
                    .RunBatchAsync("conditional.first"),
                new SqliteConditionalTriggerWorker(secondDb, clock,
                    new TriggerNotificationTransactionParticipant(secondDb, clock))
                    .RunBatchAsync("conditional.second"));

            Assert.Equal(1, results.Sum(value => value.Completed));
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Single(verify.ConditionalTriggerFireReceipts);
            Assert.Single(verify.ConditionalTriggerNotificationLinks);
            Assert.Single(verify.Notifications);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private async Task<Setup> SetupAsync(string initialJson, params IConditionalTriggerAdapter[] replacementAdapters)
        => await SetupAsync(fixture.CreateContext(), initialJson, replacementAdapters);

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db, string initialJson,
        params IConditionalTriggerAdapter[] replacementAdapters)
    {
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(App, "Quest", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("quest-space", revision, Manifest));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var clockType = types.Define(new(App, "quest.clock",
            "{\"type\":\"object\",\"required\":[\"minute\",\"calendar\"],\"properties\":{\"minute\":{\"type\":\"integer\"},\"calendar\":{\"type\":\"string\"}}}"));
        var other = types.Define(new(App, "quest.other",
            "{\"type\":\"object\",\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var type = new EcsComponentReference(clockType.QualifiedId, clockType.Version, clockType.SchemaHash);
        var otherType = new EcsComponentReference(other.QualifiedId, other.Version, other.SchemaHash);
        var components = new SqliteEntityComponentStore(db, types, schemas);
        await components.CreateEntityAsync("quest-space", "world", "World");
        await components.AddComponentAsync(new("quest-space", "world", type, initialJson, 0));
        await components.AddComponentAsync(new("quest-space", "world", otherType, "{\"value\":1}", 0));
        var adapters = replacementAdapters.Length == 0
            ? [new ClosedScalarConditionalTriggerAdapter() as IConditionalTriggerAdapter]
            : replacementAdapters;
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteConditionalTriggerStore(db, spaces, types, components, adapters, clock);
        var participant = new ConditionalTriggerEcsTransactionParticipant(db, store, clock);
        var applier = new ApplicationEcsEffectApplier(db, components, spaces, new OperationLog(db), null, [participant]);
        return new(db, type, otherType, components, store, applier, clock);
    }

    private static ConditionalTriggerDefinition Definition(
        IReadOnlyList<ConditionalTriggerDependency> dependencies, string config,
        string adapterId = ClosedScalarConditionalTriggerAdapter.StableId) =>
        ConditionalTriggerDefinition.Create(App, "trigger.world.threshold", 1,
            ConditionalTriggerLifecycle.Active, ConditionalTriggerKind.WorldClockThreshold,
            ConditionalTriggerActivation.RisingEdge, ConditionalTriggerRearm.Manual,
            "quest-space", dependencies, ConditionalTriggerAdapterReference.Create(adapterId, 1), config,
            TriggerFireTarget.NotificationOnly, Notification());

    private static TriggerNotificationTarget Notification() =>
        TriggerNotificationTarget.Create("world.threshold", "World threshold", "The threshold was reached.",
            "quest-space", ["world"]);

    private static ApplicationEcsEffectBatch Batch(EcsComponentReference type, string json, int revision, char id) =>
        new() { StateSpaceId = "quest-space", ExecutionIdentity = Identity(id), Effects = [Set(type, json, revision)] };
    private static ApplicationEcsEffect Set(EcsComponentReference type, string json, int revision) =>
        new() { Type = ApplicationEcsEffectType.ComponentSet, EntityId = "world", ComponentType = type,
            DataJson = json, ExpectedRevision = revision };
    private static ApplicationEcsExecutionIdentity Identity(char value) =>
        new(new string(value, 32), new string(char.ToUpperInvariant(value), 64));
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("quest");

    public void Dispose() => fixture.Dispose();

    private sealed record Setup(DantesRoleplayDbContext Db, EcsComponentReference Type,
        EcsComponentReference OtherType, SqliteEntityComponentStore Components,
        SqliteConditionalTriggerStore Store, ApplicationEcsEffectApplier Applier, FakeTriggerClock Clock);

    private sealed class ThrowsAfterBaselineAdapter : IConditionalTriggerAdapter
    {
        private int evaluations;
        public string Id => "system.trigger.test-throw";
        public int Version => 1;
        public void Validate(ConditionalTriggerDefinition definition) { }
        public bool Evaluate(ConditionalTriggerDefinition definition,
            IReadOnlyList<ConditionalTriggerDependencySnapshot> dependencies)
        {
            if (++evaluations > 1) throw new TriggerSchedulingContractException("TEST_ADAPTER_FAILURE", "Injected failure.");
            return false;
        }
    }

    private sealed class SequenceParticipant(params TriggerFireAttemptResult[] results)
        : ITriggerFireTransactionParticipant
    {
        private int index;
        public bool IsAvailable => true;
        public Task<TriggerFireAttemptResult> StageAsync(TriggerFireLease lease,
            CancellationToken cancellationToken = default) => Task.FromResult(
            results[Math.Min(index++, results.Length - 1)]);
    }
}
