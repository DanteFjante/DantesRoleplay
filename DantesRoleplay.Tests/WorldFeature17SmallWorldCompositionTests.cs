using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class WorldFeature17SmallWorldCompositionTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"world-feature-17-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Composer_returns_the_fixed_world_bundle_in_canonical_order_without_writing()
    {
        var setup = await ArrangeAsync();
        var before = await StateAsync(setup.Db);

        var result = await setup.Planner.ComposeAsync(Blueprint(), "world.c10.lantern-compact");

        Assert.True(result.Valid, result.Problems.FirstOrDefault()?.Reason);
        Assert.Equal("world.c10.lantern-compact.world", result.WorldRootId);
        Assert.Equal((14, 20, 4, 20), (result.Counts!.Entities, result.Counts.Components, result.Counts.Containment, result.Counts.Relationships));
        Assert.Equal(new[] { "world", "region", "location.gate", "location.market", "location.observatory", "faction", "actor.one", "actor.two", "knowledge.fact", "knowledge.rumour", "knowledge.secret", "knowledge.clue.one", "knowledge.clue.two", "knowledge.clue.three" }, result.LocalKeyMap.Select(item => item.LocalKey));
        Assert.Equal(58, result.Effects.Count);
        Assert.Equal(14, result.Effects.Take(14).Count(effect => effect.Type == EffectType.EntityCreate));
        Assert.Equal(20, result.Effects.Skip(14).Take(20).Count(effect => effect.Type == EffectType.ComponentAdd));
        Assert.Equal(4, result.Effects.Skip(34).Take(4).Count(effect => effect.Type == EffectType.ContainmentMove));
        Assert.Equal(20, result.Effects.Skip(38).Count(effect => effect.Type == EffectType.RelationshipCreate));
        Assert.Equal("world.c10.lantern-compact.location.gate", result.Effects[14 + 2].EntityId);
        Assert.Equal("game.core.world.location", result.Effects[14 + 2].DefinitionId);
        Assert.Equal("world.c10.lantern-compact.location.gate", result.Effects[38].EntityId);
        Assert.Equal("world.c10.lantern-compact.location.market", result.Effects[38].ToEntityId);
        Assert.Equal("game.core.world.location.connected-to", result.Effects[38].Kind);

        var virtualSecret = await result.World!.GetEntityAsync("world.c10.lantern-compact.knowledge.secret");
        Assert.NotNull(virtualSecret);
        Assert.Equal("gm", Property(virtualSecret!, "game.core.world.secret", "visibility"));
        Assert.Equal("secret", Property(virtualSecret, "game.core.world.knowledge.classification", "sensitivity"));
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    [Fact]
    public async Task Composer_is_deterministic_and_rejects_closed_invalid_input_without_writing()
    {
        var setup = await ArrangeAsync();
        var before = await StateAsync(setup.Db);
        var first = await setup.Planner.ComposeAsync(Blueprint(), "world.c10.lantern-compact");
        var second = await setup.Planner.ComposeAsync(Blueprint(), "world.c10.lantern-compact");

        Assert.True(first.Valid); Assert.True(second.Valid);
        Assert.Equal(JsonSerializer.Serialize(first.LocalKeyMap), JsonSerializer.Serialize(second.LocalKeyMap));
        Assert.Equal(JsonSerializer.Serialize(first.Effects), JsonSerializer.Serialize(second.Effects));

        var invalid = Blueprint() with { Fact = new("Ledger", "", "Archive", "state", "open") };
        var rejected = await setup.Planner.ComposeAsync(invalid, "world.c10.INVALID");
        Assert.False(rejected.Valid);
        Assert.Empty(rejected.Effects);
        Assert.Contains(rejected.Problems, problem => problem.Code == "WORLD_BLUEPRINT_INVALID" && problem.Path == "worldNamespace");
        Assert.Contains(rejected.Problems, problem => problem.Code == "WORLD_BLUEPRINT_REQUIRED" && problem.Path == "fact.summary");
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    [Fact]
    public async Task Composer_rejects_a_derived_id_collision_without_reserving_any_other_id()
    {
        var setup = await ArrangeAsync();
        await setup.World.CreateEntityAsync("Existing world", "world.c10.lantern-compact.world");
        var before = await StateAsync(setup.Db);

        var result = await setup.Planner.ComposeAsync(Blueprint(), "world.c10.lantern-compact");

        Assert.False(result.Valid);
        Assert.Empty(result.Effects);
        Assert.Equal("WORLD_ID_CONFLICT", Assert.Single(result.Problems).Code);
        Assert.NotNull(await setup.World.GetEntityAsync("world.c10.lantern-compact.world"));
        Assert.Null(await setup.World.GetEntityAsync("world.c10.lantern-compact.region"));
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var effects = new EffectApplier(db, world);
        return new(db, world, new SmallWorldCompositionPlanner(new StagedWorldComposer(effects, world)));
    }

    private static SmallWorldBlueprint Blueprint() => new(
        new("Lantern Compact", "A compact setting built for one campaign."),
        new("Old Ward", "The region around the sealed observatory."),
        new("North Gate", "The party arrives through the old northern gate."),
        new("Archive Market", "A market surrounding a disputed archive."),
        new("Sealed Observatory", "An observatory with a disputed signal."),
        new("The Lantern Compact", "A faction protecting the old records.", ["Protect the archive"], ["Negotiate quietly"], ["A sealed ledger"], "Keep the records from public misuse."),
        new("Mara Vell", "Mara wants the archive opened safely."),
        new("Oren Dale", "Oren wants the observatory secret preserved."),
        new("Old Toll Ledger", "The market archive holds the old toll ledger.", "Catalogued archive entry.", "state", "open"),
        new("Observatory Signal", "A light answers from the observatory after midnight.", "Market gossip.", "event", "discreet"),
        new("Oren's Correspondence", "Oren's family hid records implicating the old council.", "Private ledger annotation.", "relationship", "secret"),
        new("Ledger Seal", "A seal matches the market archive door.", "Inspection of the toll ledger.", "identity", "confidential"),
        new("Lantern Soot", "Fresh soot marks the observatory shutter.", "Soot beneath the shutter.", "state", "confidential"),
        new("Unsent Letter", "A letter asks Oren to keep a family promise.", "A folded letter.", "relationship", "secret"));

    private static string Property(EntitySnapshot entity, string definition, string property)
    {
        using var document = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == definition).Data);
        return document.RootElement.GetProperty(property).GetString()!;
    }
    private static async Task<string> StateAsync(DantesRoleplayDbContext db) => string.Join("|", await db.Entities.CountAsync(), await db.Components.CountAsync(), await db.Relationships.CountAsync(), await db.Events.CountAsync(), await db.Operations.CountAsync());
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, SmallWorldCompositionPlanner Planner);
}
