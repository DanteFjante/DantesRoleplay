using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.World;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

/// <summary>Exercises the generic application reaction bridge with a real catalog JavaScript mechanic.</summary>
public sealed class ApplicationEcsReactionRouterTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Production_registration_resolves_the_application_reaction_bridge_without_a_cycle()
    {
        using var provider = new ServiceCollection()
            .AddDantesRoleplayDataAccess(":memory:")
            // The complete host adds provider-specific AI services; resolving only the two
            // application paths here keeps this a focused cycle guard.
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationEcsReactionRouter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationEcsEffectApplier>());
    }

    [Fact]
    public async Task A_committed_object_submission_routes_only_the_exact_scoped_subscription_once()
    {
        await using var db = fixture.CreateContext();
        var (app, space, type, entities, edges, catalogs) = await SeedAsync(db);
        var source = new EventSourceContext(app.Value, space.StateSpaceId);
        await RegisterReactionSubscriptionsAsync(db, catalogs, source,
            new EventSourceContext(app.Value, "space.other"));
        var router = BuildRouter(db, app, space, catalogs, entities, edges);
        var setup = await BuildObjectWriterAsync(db, app, space, type, entities, edges, router);
        var before = await setup.ReadAsync();
        var request = setup.Request(before.SourceRevisionFingerprint, "object-reaction", "{\"value\":2}");
        var result = await setup.Writer.WriteAsync(request);
        var replay = await setup.Writer.WriteAsync(request);
        var noOp = await setup.Writer.WriteAsync(
            setup.Request(result.SourceRevisionFingerprint, "object-noop", "{}"));
        var stale = await Assert.ThrowsAsync<ApplicationObjectWriteException>(() => setup.Writer.WriteAsync(
            setup.Request(before.SourceRevisionFingerprint, "object-stale", "{\"value\":3}")));

        Assert.True(result.Applied);
        Assert.True(replay.Replayed);
        Assert.True(noOp.NoOp);
        Assert.Equal("OBJECT_WRITE_SOURCE_STALE", stale.Code);
        Assert.Equal(2, (await entities.GetComponentAsync(space.StateSpaceId, "actor", "fixture.stats"))!
            .Revision);
        var reacted = await entities.GetComponentAsync(space.StateSpaceId, "actor", "fixture.reacted");
        Assert.Equal("{\"handled\":true}", reacted!.ValueJson);
        var history = await setup.Ledger.FindAsync(rootOperationId: result.OperationId, limit: 20);
        Assert.Collection(history.OrderBy(value => value.Sequence),
            root =>
            {
                Assert.Equal("world.component.replaced", root.TypeId);
                Assert.Equal(source, root.Source);
                Assert.Equal(0, root.Depth);
            },
            child =>
            {
                Assert.Equal("world.component.added", child.TypeId);
                Assert.Equal(source, child.Source);
                Assert.Equal(1, child.Depth);
                Assert.Equal(history.Single(value => value.Depth == 0).Id, child.CausationId);
                Assert.Equal(result.OperationId, child.RootOperationId);
            });
        Assert.Equal(2, (await setup.Ledger.FindAsync(limit: 20)).Count);
    }

    [Fact]
    public async Task A_late_object_chain_failure_rolls_back_the_root_reaction_and_events()
    {
        await using var db = fixture.CreateContext();
        var (app, space, type, entities, edges, catalogs) = await SeedAsync(db);
        await RegisterReactionSubscriptionsAsync(db, catalogs,
            new EventSourceContext(app.Value, space.StateSpaceId),
            new EventSourceContext(app.Value, "space.other"));
        var setup = await BuildObjectWriterAsync(db, app, space, type, entities, edges,
            BuildRouter(db, app, space, catalogs, entities, edges), rejectAfterEvents: true);
        var before = await setup.ReadAsync();

        var failure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(() => setup.Writer.WriteAsync(
            setup.Request(before.SourceRevisionFingerprint, "object-rollback", "{\"value\":2}")));

        Assert.Equal("OBJECT_WRITE_REJECTED", failure.Code);
        Assert.Equal("{\"value\":1}", (await entities.GetComponentAsync(
            space.StateSpaceId, "actor", "fixture.stats"))!.ValueJson);
        Assert.Null(await entities.GetComponentAsync(space.StateSpaceId, "actor", "fixture.reacted"));
        Assert.Empty(await setup.Ledger.FindAsync(limit: 20));
    }

    [Fact]
    public async Task A_legacy_event_does_not_match_an_application_subscription()
    {
        await using var db = fixture.CreateContext();
        var (app, space, type, entities, edges, catalogs) = await SeedAsync(db);
        var source = new EventSourceContext(app.Value, space.StateSpaceId);
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "fixture.mechanic.legacy-target", Category = "test", Name = "Legacy target", Description = "Legacy target.",
            Matches = "legacy target", Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"world.component.replaced\"]}}",
            Source = "return { narration: 'legacy' };", Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db, catalogs).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.fixture.app-only", Category = "test", EventTypeId = "world.component.replaced",
            EventMechanicId = "fixture.mechanic.legacy-target", Mode = SubscriptionMode.Reaction,
            PayloadEqualsJson = "{\"definitionId\":\"fixture.stats\"}", Status = SubscriptionStatus.Active, Source = source
        });

        var router = new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db));
        var result = await router.RouteAsync([new EventDetail("legacy", "world.component.replaced", 1, "", "{\"definitionId\":\"fixture.stats\"}", DateTime.UtcNow, "corr", "", 0, 1, "root", ["actor"])], 1, new ChainBudget());

        Assert.Empty(result.Outcomes);
    }

    private static ApplicationEcsReactionRouter BuildRouter(
        DantesRoleplayDbContext db, ApplicationIdentifier app, StateSpaceView space,
        IPublicApplicationCatalogProvider catalogs, IEntityComponentStore entities, IStateSpaceEdgeStore edges)
    {
        var stateSpaces = new SqliteStateSpaceRegistry(db, new SqliteApplicationRegistry(db));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges);
        var evaluator = new ApplicationMechanicEvaluator(catalogs,
            new ApplicationMechanicProjectionResolver(db, stateSpaces), new JintMechanicEngine());
        return new(db, catalogs, stateSpaces, mapping, evaluator,
            new ApplicationEcsEffectBatchBuilder(types, entities, edges));
    }

    private static async Task RegisterReactionSubscriptionsAsync(
        DantesRoleplayDbContext db,
        IPublicApplicationCatalogProvider catalogs,
        EventSourceContext source,
        EventSourceContext foreignSource)
    {
        const string legacyRequirements =
            "{\"roles\":{\"subject\":{\"components\":[]}},\"event\":{\"mode\":\"reaction\",\"types\":[\"world.component.replaced\"]}}";
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "fixture.mechanic.app-reaction", Category = "test", Name = "Legacy wildcard canary",
            Description = "Legacy wildcard isolation canary.", Matches = "legacy wildcard canary",
            Requirements = legacyRequirements,
            Source = "return { effects: [{ type: 'component.add', entityId: ctx.roles.subject.id, definitionId: 'reacted', data: JSON.stringify({ legacy: true }) }] };",
            Status = MechanicStatus.Active
        });
        var subscriptions = new SubscriptionStore(db, catalogs);
        await subscriptions.WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.fixture.app-reaction", Category = "test",
            EventTypeId = "world.component.replaced", EventMechanicId = "fixture.mechanic.app-reaction",
            Mode = SubscriptionMode.Reaction, FixedRoleEntityIdsJson = "{\"subject\":\"actor\"}",
            PayloadEqualsJson = "{\"definitionId\":\"fixture.stats\"}", Status = SubscriptionStatus.Active,
            Source = source
        });
        await subscriptions.WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.fixture.app-reaction-two", Category = "test",
            EventTypeId = "world.component.replaced", EventMechanicId = "fixture.mechanic.app-reaction-two",
            Mode = SubscriptionMode.Reaction, FixedRoleEntityIdsJson = "{\"subject\":\"actor\"}",
            PayloadEqualsJson = "{\"definitionId\":\"fixture.stats\"}", Status = SubscriptionStatus.Active,
            Source = foreignSource
        });
        await subscriptions.WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.fixture.app-only", Category = "test",
            EventTypeId = "world.component.replaced", EventMechanicId = "fixture.mechanic.app-reaction",
            Mode = SubscriptionMode.Reaction, FixedRoleEntityIdsJson = "{\"subject\":\"actor\"}",
            PayloadEqualsJson = "{\"definitionId\":\"fixture.stats\"}", Status = SubscriptionStatus.Active
        });
        Assert.Equal(source, (await subscriptions.GetAsync("subscription.fixture.app-reaction"))!.Source);
        Assert.Equal(foreignSource, (await subscriptions.GetAsync("subscription.fixture.app-reaction-two"))!.Source);
        Assert.Null((await subscriptions.GetAsync("subscription.fixture.app-only"))!.Source);
    }

    private static async Task<ObjectWriterSetup> BuildObjectWriterAsync(
        DantesRoleplayDbContext db,
        ApplicationIdentifier app,
        StateSpaceView space,
        EcsComponentReference stats,
        SqliteEntityComponentStore entities,
        SqliteStateSpaceEdgeStore edges,
        ApplicationEcsReactionRouter router,
        bool rejectAfterEvents = false)
    {
        var schemas = new BoundedJsonSchemaValidator();
        var applications = new SqliteApplicationRegistry(db);
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var memberType = types.Define(new(app, "fixture.member-state",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"status\"],\"properties\":{\"status\":{\"type\":\"string\"}}}"));
        var member = new EcsComponentReference(memberType.QualifiedId, memberType.Version, memberType.SchemaHash);
        await entities.CreateEntityAsync(space.StateSpaceId, "member", "Member");
        await entities.AddComponentAsync(new(space.StateSpaceId, "member", member,
            "{\"status\":\"active\"}", 0));
        await edges.SetRelationshipAsync(space.StateSpaceId, "actor", "member", "fixture.member", "{}", 0);

        var dependencies = new ApplicationObjectDependencyIndexCache();
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications, dependencies);
        var projection = registry.Define(new(app, "fixture.object.members", ObjectOutputSchema,
            [new("stats", "subject", stats)], [],
            [new("stats", "/value", "/value")],
            new([new("subject", true), new("member", false)],
                [new("stats", true)],
                [new("members", "fixture.member", "subject", "member", "many", "/members",
                    [new("to", member)], [])], [],
                [new("members", "members", 20, 20, [new("/name", "asc")], "source-revision-bound")],
                new(2, 20, 16_384, 8), new(["dm"], ["dm"]),
                new(ObjectEditSchema, ["set"], [new("/value", ["set"])])), 1)).Reference;
        var source = new SqliteProjectionSourceSnapshotReader(db, stateSpaces, entities);
        var root = new ProjectionMaterializer(registry, entities, stateSpaces, schemas,
            new ProjectionPlanCache(), source);
        var materializer = new ProjectionCollectionMaterializer(registry, root, edges, entities,
            entities, schemas, new SqliteProjectionReadTransaction(db));
        var ledger = new EventLedger(db);
        var participants = new List<IApplicationEcsTransactionParticipant>
        {
            new ApplicationObjectChangeTransactionParticipant(db, stateSpaces, dependencies)
        };
        if (rejectAfterEvents) participants.Add(new RejectAfterEvents());
        var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, new OperationLog(db), edges,
            participants, eventSources: [new ApplicationStructuralEventTransactionParticipant(
                stateSpaces, ledger, new EventTypeStore(db), schemas)], reactionRouter: router);
        var writer = new ApplicationObjectWriteService(registry, materializer, entities, applier,
            new OperationLog(db), schemas);
        return new(app, space.StateSpaceId, projection, materializer, writer, ledger);
    }

    private static async Task<(ApplicationIdentifier App, StateSpaceView Space, EcsComponentReference Type,
        SqliteEntityComponentStore Entities, SqliteStateSpaceEdgeStore Edges, IPublicApplicationCatalogProvider Catalogs)> SeedAsync(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = applications.Register(new(app, "Fixture", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        var space = spaces.Create(new("space", revision, Hash("activation")));
        spaces.Create(new("space.other", revision, Hash("other activation")));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var stats = types.Define(new(app, "fixture.stats",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        types.Define(new(app, "fixture.reacted", "{}"));
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync(space.StateSpaceId, "actor", "Actor");
        await entities.AddComponentAsync(new EcsComponentWrite(space.StateSpaceId, "actor",
            new EcsComponentReference(stats.QualifiedId, stats.Version, stats.SchemaHash), "{\"value\":1}", 0));
        db.Entities.Add(new Entity { Id = "actor", Name = "Actor", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        foreach (var file in EventTypeSeeder.Load())
            await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest { Id = file.Id, Category = file.Category, Name = file.Name, Description = file.Description, PayloadSchema = file.Schema, Scope = file.Scope, Status = EventTypeStatus.Active });
        var requirements = "{\"roles\":{\"subject\":{\"components\":[\"stats\"],\"optionalComponents\":[\"reacted\"]}},\"event\":{\"mode\":\"reaction\",\"types\":[\"world.component.replaced\"]}}";
        var content = JsonSerializer.Serialize(new { requirements, source = "return { effects: [{ type: 'component.add', entityId: ctx.roles.subject.id, definitionId: 'reacted', data: JSON.stringify({ handled: true }) }] };" });
        var contentTwo = JsonSerializer.Serialize(new { requirements, source = "return { effects: [{ type: 'component.set', entityId: ctx.roles.subject.id, definitionId: 'reacted', data: JSON.stringify({ handled: true, second: true }) }] };" });
        var legacyContent = JsonSerializer.Serialize(new { requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"world.component.replaced\"]}}", source = "return { narration: 'legacy' };" });
        var record = new CatalogRecordDefinition(app.Value, "mechanic", "fixture.mechanic.app-reaction", "App reaction", "Fixture reaction.", [], [], "mechanics", "active", 1, content, Hash(content), "test", "mechanics/app-reaction.md");
        var recordTwo = new CatalogRecordDefinition(app.Value, "mechanic", "fixture.mechanic.app-reaction-two", "App reaction two", "Fixture reaction two.", [], [], "mechanics", "active", 1, contentTwo, Hash(contentTwo), "test", "mechanics/app-reaction-two.md");
        var legacyRecord = new CatalogRecordDefinition(app.Value, "mechanic", "fixture.mechanic.legacy-target", "Legacy target", "Fixture legacy target.", [], [], "mechanics", "active", 1, legacyContent, Hash(legacyContent), "test", "mechanics/legacy-target.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("catalog"), "catalog-lexical-v1", [new(app.Value, "Fixture", "Fixture catalog.")], [new(app.Value, "", "Fixture", "Fixture catalog.", CatalogDescriptionStatus.Authored), new(app.Value, "mechanics", "Mechanics", "Fixture mechanics.", CatalogDescriptionStatus.Authored)], [record, recordTwo, legacyRecord]);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator> { [app] = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(Encoding.UTF8.GetBytes("fixture-cursor-key-32-bytes-long"))) });
        return (app, space, new EcsComponentReference(stats.QualifiedId, stats.Version, stats.SchemaHash), entities, edges, catalogs);
    }

    private const string ObjectOutputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\",\"members\",\"totalCount\",\"complete\",\"nextCursor\"],\"properties\":{\"value\":{\"type\":\"integer\"},\"members\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"name\",\"status\"],\"properties\":{\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"status\":{\"type\":\"string\"}}}},\"totalCount\":{\"type\":\"integer\"},\"complete\":{\"type\":\"boolean\"},\"nextCursor\":{\"type\":[\"string\",\"null\"]}}}";
    private const string ObjectEditSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"integer\"}}}";

    private sealed record ObjectWriterSetup(
        ApplicationIdentifier Application,
        string StateSpaceId,
        ProjectionReference Projection,
        IProjectionCollectionMaterializer Materializer,
        IApplicationObjectWriteService Writer,
        IEventLedger Ledger)
    {
        private IReadOnlyDictionary<string, string> Roles { get; } =
            new Dictionary<string, string> { ["subject"] = "actor" };

        public Task<ProjectionCollectionMaterializationResult> ReadAsync() =>
            Materializer.MaterializeAsync(new(StateSpaceId, Projection, Roles, "members", "dm"));

        public ApplicationObjectWriteRequest Request(string expectedSource, string idempotencyKey,
            string submittedObject) => new(StateSpaceId, Application, Projection, Roles, "members", "dm",
            idempotencyKey, expectedSource, "{}", [])
            {
                SubmissionMode = "object",
                SubmittedObjectJson = submittedObject
            };
    }

    private sealed class RejectAfterEvents : IApplicationEcsTransactionParticipant
    {
        public Task StageAsync(ApplicationEcsEffectBatch batch,
            IReadOnlyList<ApplicationEcsEffectReceipt> receipts, string operationId,
            CancellationToken cancellationToken = default) =>
            throw new ApplicationEcsTransactionParticipantException("Reject after event and reaction staging.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
