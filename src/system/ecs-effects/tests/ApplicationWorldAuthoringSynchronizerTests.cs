using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.EcsEffects.Tests;

public sealed class ApplicationWorldAuthoringSynchronizerTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private static readonly ApplicationWorldAuthoringContext Context =
        new("Author reviewed world records.", ["procedure.system.use"]);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Dry_run_commit_and_replay_use_one_atomic_manifest_path()
    {
        var setup = await SetupAsync();
        var request = NewManifest("0123456789abcdef0123456789abcdef");

        var dryRun = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: true);

        Assert.True(dryRun.Accepted);
        Assert.True(dryRun.DryRun);
        Assert.Equal(7, dryRun.AppliedEffectCount);
        Assert.Null(await setup.Store.GetEntityAsync("world-space", "location.fixture.harbor"));

        var committed = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: false);
        var replayed = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: false);

        Assert.True(committed.Accepted);
        Assert.False(committed.Replayed);
        Assert.True(replayed.Accepted);
        Assert.True(replayed.Replayed);
        Assert.Equal(committed.OperationId, replayed.OperationId);
        Assert.Equal("world.fixture", (await setup.Edges.GetContainmentAsync(
            "world-space", "location.fixture.harbor"))!.ContainerEntityId);
        Assert.Equal("location.fixture.harbor", (await setup.Edges.GetContainmentAsync(
            "world-space", "fact.fixture.harbor-bells"))!.ContainerEntityId);
        Assert.Equal("{}", (await setup.Edges.GetRelationshipAsync(
            "world-space", "fact.fixture.harbor-bells", "location.fixture.harbor",
            "fixture-world.knowledge.about"))!.DataJson);
        Assert.Equal("{\"value\":2}", (await setup.Store.GetComponentAsync(
            "world-space", "fact.fixture.harbor-bells", setup.Type.QualifiedTypeId))!.ValueJson);
    }

    [Fact]
    public async Task Exact_existing_revision_produces_a_complete_component_replacement()
    {
        var setup = await SetupAsync();
        await setup.Store.CreateEntityAsync("world-space", "location.fixture.old-road", "Old Road");
        await setup.Edges.MoveContainmentAsync("world-space", "location.fixture.old-road", "world.fixture", "location", 0);
        await setup.Store.AddComponentAsync(new(
            "world-space", "location.fixture.old-road", setup.Type, "{\"value\":1}", 0));
        var request = new ApplicationWorldAuthoringRequest(
            "1123456789abcdef0123456789abcdef",
            "fixture-world",
            "world-space",
            "world.fixture",
            [new("location.fixture.old-road", "Old Road", 1,
                [new(setup.Type.QualifiedTypeId, 1, "{\"value\":3}")], null)],
            []);

        var result = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: false);

        Assert.True(result.Accepted);
        var component = await setup.Store.GetComponentAsync(
            "world-space", "location.fixture.old-road", setup.Type.QualifiedTypeId);
        Assert.Equal(2, component!.Revision);
        Assert.Equal("{\"value\":3}", component.ValueJson);
    }

    [Fact]
    public async Task Schema_failure_after_entity_creation_rolls_back_the_whole_manifest()
    {
        var setup = await SetupAsync();
        var request = new ApplicationWorldAuthoringRequest(
            "2123456789abcdef0123456789abcdef",
            "fixture-world",
            "world-space",
            "world.fixture",
            [new("location.fixture.invalid", "Invalid", 0,
                [new(setup.Type.QualifiedTypeId, 0, "{\"value\":\"wrong\"}")],
                new("world.fixture", "location", 0))],
            []);

        var result = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: false);

        Assert.False(result.Accepted);
        Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
        Assert.Null(await setup.Store.GetEntityAsync("world-space", "location.fixture.invalid"));
        Assert.Null(await setup.Edges.GetContainmentAsync("world-space", "location.fixture.invalid"));
    }

    [Fact]
    public async Task Existing_endpoint_outside_the_selected_root_is_rejected_before_effects()
    {
        var setup = await SetupAsync();
        await setup.Store.CreateEntityAsync("world-space", "outside.fixture", "Outside");
        var request = new ApplicationWorldAuthoringRequest(
            "3123456789abcdef0123456789abcdef",
            "fixture-world",
            "world-space",
            "world.fixture",
            [new("location.fixture.inside", "Inside", 0, [], new("world.fixture", "location", 0))],
            [new("location.fixture.inside", "outside.fixture", "fixture-world.knowledge.about", 0, "{}")]);

        var result = await setup.Synchronizer.SynchronizeAsync(request, Context, dryRun: false);

        Assert.False(result.Accepted);
        Assert.Equal("WORLD_SCOPE_INVALID", result.ErrorCode);
        Assert.Null(await setup.Store.GetEntityAsync("world-space", "location.fixture.inside"));
    }

    [Fact]
    public async Task Request_token_conflict_cannot_rebind_the_commit_operation()
    {
        var setup = await SetupAsync();
        var token = "4123456789abcdef0123456789abcdef";
        var first = NewSingleEntityManifest(token, "location.fixture.first", "First");
        var conflicting = NewSingleEntityManifest(token, "location.fixture.second", "Second");

        Assert.True((await setup.Synchronizer.SynchronizeAsync(first, Context, dryRun: false)).Accepted);
        var result = await setup.Synchronizer.SynchronizeAsync(conflicting, Context, dryRun: false);

        Assert.False(result.Accepted);
        Assert.Equal("OPERATION_ID_CONFLICT", result.ErrorCode);
        Assert.Null(await setup.Store.GetEntityAsync("world-space", "location.fixture.second"));
    }

    [Fact]
    public async Task Stale_ancestry_between_resolution_and_apply_rejects_every_authored_effect()
    {
        var setup = await SetupAsync();
        await setup.Store.CreateEntityAsync("world-space", "location.fixture.anchor", "Anchor");
        await setup.Edges.MoveContainmentAsync("world-space", "location.fixture.anchor", "world.fixture", "location", 0);
        await setup.Store.AddComponentAsync(new(
            "world-space", "location.fixture.anchor", setup.Type, "{\"value\":1}", 0));
        var mutating = new MutatingApplier(setup.Applier, setup.Edges);
        var synchronizer = new ApplicationWorldAuthoringSynchronizer(
            setup.StateSpaces, setup.Types, setup.Store, setup.Edges, mutating, setup.Operations);
        var request = new ApplicationWorldAuthoringRequest(
            "5123456789abcdef0123456789abcdef",
            "fixture-world",
            "world-space",
            "world.fixture",
            [new("location.fixture.anchor", "Anchor", 1,
                [new(setup.Type.QualifiedTypeId, 1, "{\"value\":2}")], null)],
            []);

        var result = await synchronizer.SynchronizeAsync(request, Context, dryRun: false);

        Assert.False(result.Accepted);
        Assert.Equal("REVISION_STALE", result.ErrorCode);
        Assert.Equal("{\"value\":1}", (await setup.Store.GetComponentAsync(
            "world-space", "location.fixture.anchor", setup.Type.QualifiedTypeId))!.ValueJson);
    }

    private async Task<SetupResult> SetupAsync()
    {
        var db = _fixture.CreateContext();
        var applicationId = ApplicationIdentifier.Parse("fixture-world");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(applicationId, "Fixture World", "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("world-space", revision, Manifest));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var registered = types.Define(new(applicationId, "fixture-world.record",
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var type = new EcsComponentReference(registered.QualifiedId, registered.Version, registered.SchemaHash);
        var store = new SqliteEntityComponentStore(db, types, schemas);
        var edgeStore = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        await store.CreateEntityAsync("world-space", "world.fixture", "Fixture World");
        var operations = new OperationLog(db);
        var applier = new ApplicationEcsEffectApplier(db, store, stateSpaces, operations, edgeStore);
        return new(store, types, type, stateSpaces, edgeStore, applier, operations,
            new ApplicationWorldAuthoringSynchronizer(stateSpaces, types, store, edgeStore, applier, operations));
    }

    private static ApplicationWorldAuthoringRequest NewManifest(string token) => new(
        token,
        "fixture-world",
        "world-space",
        "world.fixture",
        [
            new("location.fixture.harbor", "Harbor", 0,
                [new("fixture-world.record", 0, "{\"value\":1}")],
                new("world.fixture", "location", 0)),
            new("fact.fixture.harbor-bells", "Harbor Bells", 0,
                [new("fixture-world.record", 0, "{\"value\":2}")],
                new("location.fixture.harbor", "knowledge", 0))
        ],
        [new("fact.fixture.harbor-bells", "location.fixture.harbor", "fixture-world.knowledge.about", 0, "{}")] );

    private static ApplicationWorldAuthoringRequest NewSingleEntityManifest(
        string token,
        string entityId,
        string name) => new(
            token,
            "fixture-world",
            "world-space",
            "world.fixture",
            [new(entityId, name, 0, [], new("world.fixture", "location", 0))],
            []);

    public void Dispose() => _fixture.Dispose();

    private sealed record SetupResult(
        SqliteEntityComponentStore Store,
        SqliteComponentTypeRegistry Types,
        EcsComponentReference Type,
        SqliteStateSpaceRegistry StateSpaces,
        SqliteStateSpaceEdgeStore Edges,
        ApplicationEcsEffectApplier Applier,
        OperationLog Operations,
        ApplicationWorldAuthoringSynchronizer Synchronizer);

    private sealed class MutatingApplier(
        IApplicationEcsEffectApplier inner,
        IStateSpaceEdgeStore edges) : IApplicationEcsEffectApplier
    {
        public async Task<ApplicationEcsEffectResult> ApplyAsync(
            ApplicationEcsEffectBatch batch,
            bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            var current = await edges.GetContainmentAsync(
                batch.StateSpaceId, "location.fixture.anchor", cancellationToken);
            await edges.MoveContainmentAsync(batch.StateSpaceId, "location.fixture.anchor",
                "world.fixture", "changed", current!.Revision, cancellationToken);
            return await inner.ApplyAsync(batch, dryRun, cancellationToken);
        }
    }
}
