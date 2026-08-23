using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature33Slice2Tests : IDisposable
{
    private const string World = "world.feature-01.fixture", Policy = "content.dnd2024.rest-policy.standard.v1", Episode = "dnd2024.rest-episode", Membership = "dnd2024.rest.world";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"feature-33-slice-2-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, recursive: true); }

    [Fact]
    public async Task Fresh_import_registers_the_closed_episode_contract_and_scoped_fanout()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);

        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.rest.begin")); Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.rest.clock-reconcile"));
        var subscription = Assert.IsType<SubscriptionDetail>(await new SubscriptionStore(db).GetAsync("subscription.dnd2024.rest.clock-reconcile"));
        Assert.Equal("game.core.world.clock.advanced", subscription.EventTypeId); Assert.Equal(World, subscription.Scope); Assert.Equal("{\"policy\":\"content.dnd2024.rest-policy.standard.v1\"}", subscription.FixedRoleEntityIdsJson);
        Assert.Equal("{\"componentId\":\"dnd2024.rest-episode\",\"direction\":\"scope-to-candidate\",\"relationshipKind\":\"dnd2024.rest.world\",\"role\":\"creature\"}", subscription.FanoutSelectorJson);

        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Episode).Schema);
        using var shortEpisode = JsonDocument.Parse("""{"policyEntityId":"content.dnd2024.rest-policy.standard.v1","kind":"short","worldId":"world.one","startedAtMinute":0,"requiredMinutes":60,"status":"active","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary > Short Rest, PDF page 186"}}""");
        using var wrongDuration = JsonDocument.Parse(shortEpisode.RootElement.GetRawText().Replace("\"requiredMinutes\":60", "\"requiredMinutes\":61", StringComparison.Ordinal));
        using var elapsedCopy = JsonDocument.Parse(shortEpisode.RootElement.GetRawText()[..^1] + ",\"elapsedMinutes\":60}");
        Assert.True(schema.Evaluate(shortEpisode.RootElement).IsValid); Assert.False(schema.Evaluate(wrongDuration.RootElement).IsValid); Assert.False(schema.Evaluate(elapsedCopy.RootElement).IsValid);
    }

    [Fact]
    public async Task Short_rest_derives_its_start_and_becomes_ready_once_without_recovery()
    {
        await using var db = await ImportedAsync(); var world = new WorldStore(db); const string creature = "fixture.f33.short";
        await CreatureAsync(world, creature, "Short Rest Fixture", 7);
        var runner = ReactiveRunner(db, world); var begin = await BeginAsync(runner, creature, "short");

        Assert.True(begin.Ok, begin.Error?.Why); Assert.Equal(2, begin.AppliedCount); AssertEpisode((await world.GetEntityAsync(creature))!, "short", 0, 60, "active"); AssertMembership(await world.GetRelationshipsAsync(World), creature);
        var hp = Component((await world.GetEntityAsync(creature))!, "dnd2024.hit-points");

        var below = await AdvanceAsync(runner, 59); Assert.True(below.Ok, below.Error?.Why); AssertEpisode((await world.GetEntityAsync(creature))!, "short", 0, 60, "active"); Assert.Equal(hp, Component((await world.GetEntityAsync(creature))!, "dnd2024.hit-points"));
        var exact = await AdvanceAsync(runner, 1); Assert.True(exact.Ok, exact.Error?.Why); AssertEpisode((await world.GetEntityAsync(creature))!, "short", 0, 60, "ready"); Assert.Equal(hp, Component((await world.GetEntityAsync(creature))!, "dnd2024.hit-points"));
        var replay = await AdvanceAsync(runner, 1); Assert.True(replay.Ok, replay.Error?.Why); AssertEpisode((await world.GetEntityAsync(creature))!, "short", 0, 60, "ready");
        Assert.Equal(3, await db.EventExecutions.AsNoTracking().CountAsync(execution => execution.SubscriptionId == "subscription.dnd2024.rest.clock-reconcile"));
    }

    [Fact]
    public async Task Begin_rejects_zero_hp_duplicate_and_wrong_policy_without_an_episode()
    {
        await using var db = await ImportedAsync(); var world = new WorldStore(db); const string creature = "fixture.f33.denied";
        await CreatureAsync(world, creature, "Denied Rest Fixture", 0); var runner = ReactiveRunner(db, world);

        var zero = await BeginAsync(runner, creature, "long"); Assert.False(zero.Ok); Assert.DoesNotContain((await world.GetEntityAsync(creature))!.Components, component => component.DefinitionId == Episode); Assert.DoesNotContain(await world.GetRelationshipsAsync(World), link => link.Kind == Membership && link.ToEntityId == creature);
        await world.SetComponentAsync(creature, "dnd2024.hit-points", HitPoints(1));
        var wrongPolicy = await runner.RunAsync(new ActionRequest { Intent = "begin short rest", RoleEntityIds = new Dictionary<string, string> { ["creature"] = creature, ["world"] = World, ["policy"] = World }, Input = "{\"kind\":\"short\"}", Seed = 3302 });
        Assert.False(wrongPolicy.Ok); Assert.DoesNotContain((await world.GetEntityAsync(creature))!.Components, component => component.DefinitionId == Episode);
        var first = await BeginAsync(runner, creature, "long"); Assert.True(first.Ok, first.Error?.Why); var duplicate = await BeginAsync(runner, creature, "long"); Assert.False(duplicate.Ok); AssertEpisode((await world.GetEntityAsync(creature))!, "long", 0, 480, "active");
    }

    [Fact]
    public async Task Scoped_fanout_is_ordered_and_a_corrupt_selected_episode_rolls_back_the_clock()
    {
        await using var db = await ImportedAsync(); var world = new WorldStore(db); var runner = ReactiveRunner(db, world);
        await CreatureAsync(world, "fixture.f33.zed", "Zed", 5); await CreatureAsync(world, "fixture.f33.aye", "Aye", 5);
        Assert.True((await BeginAsync(runner, "fixture.f33.zed", "short")).Ok); Assert.True((await BeginAsync(runner, "fixture.f33.aye", "short")).Ok);

        var advanced = await AdvanceAsync(runner, 60); Assert.True(advanced.Ok, advanced.Error?.Why); AssertEpisode((await world.GetEntityAsync("fixture.f33.aye"))!, "short", 0, 60, "ready"); AssertEpisode((await world.GetEntityAsync("fixture.f33.zed"))!, "short", 0, 60, "ready");
        var names = (await db.EventExecutions.AsNoTracking().Where(execution => execution.SubscriptionId == "subscription.dnd2024.rest.clock-reconcile").OrderBy(execution => execution.Ordinal).ToListAsync()).Select(execution => execution.Narration).ToArray();
        Assert.Equal(new[] { "Aye has completed the required rest duration.", "Zed has completed the required rest duration." }, names);

        await CreatureAsync(world, "fixture.f33.corrupt", "Corrupt", 5); Assert.True((await BeginAsync(runner, "fixture.f33.corrupt", "short")).Ok);
        await world.SetComponentAsync("fixture.f33.corrupt", Episode, "{}"); var before = Component((await world.GetEntityAsync(World))!, "game.core.world.clock");
        var failed = await AdvanceAsync(runner, 1); Assert.False(failed.Ok); Assert.Equal(before, Component((await world.GetEntityAsync(World))!, "game.core.world.clock")); Assert.Equal("{}", Component((await world.GetEntityAsync("fixture.f33.corrupt"))!, Episode));
    }

    private async Task<DantesRoleplayDbContext> ImportedAsync()
    {
        Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions()); Assert.False(imported.Aborted); return db;
    }
    private static async Task CreatureAsync(WorldStore world, string id, string name, long current) { await world.CreateEntityAsync(name, id); await world.SetComponentAsync(id, "dnd2024.hit-points", HitPoints(current)); }
    private static ActionRunner ReactiveRunner(DantesRoleplayDbContext db, WorldStore world) => new(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), world), new EventLedger(db), new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), world)), new OperationLog(db), new MechanicComposer(new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine()));
    private static Task<ActionRunResult> BeginAsync(ActionRunner runner, string creature, string kind) => runner.RunAsync(new ActionRequest { Intent = $"begin {kind} rest", RoleEntityIds = new Dictionary<string, string> { ["creature"] = creature, ["world"] = World, ["policy"] = Policy }, Input = $"{{\"kind\":\"{kind}\"}}", Seed = 3301 });
    private static Task<ActionRunResult> AdvanceAsync(ActionRunner runner, int minutes) => runner.RunAsync(new ActionRequest { Intent = "advance world time", RoleEntityIds = new Dictionary<string, string> { ["world"] = World }, Input = $"{{\"minutes\":{minutes}}}", Seed = 3303 });
    private static string HitPoints(long current) => $"{{\"current\":{current},\"maximum\":12,\"sourceRef\":{{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Hit Points\"}}}}";
    private static string Component(EntitySnapshot entity, string id) => entity.Components.Single(component => component.DefinitionId == id).Data;
    private static void AssertMembership(IReadOnlyList<RelationshipView> links, string creature) => Assert.Contains(links, link => link.FromEntityId == World && link.ToEntityId == creature && link.Kind == Membership && link.Data == "{}");
    private static void AssertEpisode(EntitySnapshot entity, string kind, long started, long required, string status) { using var document = JsonDocument.Parse(Component(entity, Episode)); var root = document.RootElement; Assert.Equal(Policy, root.GetProperty("policyEntityId").GetString()); Assert.Equal(kind, root.GetProperty("kind").GetString()); Assert.Equal(World, root.GetProperty("worldId").GetString()); Assert.Equal(started, root.GetProperty("startedAtMinute").GetInt64()); Assert.Equal(required, root.GetProperty("requiredMinutes").GetInt64()); Assert.Equal(status, root.GetProperty("status").GetString()); }
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
}
