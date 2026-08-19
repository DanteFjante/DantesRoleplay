using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 10 Slice 1 provides static catalog-owned fixtures only. Replay and all runtime changes
/// remain the separately reviewed Slice 2 responsibility.
/// </summary>
public sealed class CatalogFeature10Tests : IDisposable
{
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-10-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_contains_the_static_feature_10_training_baseline()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        var encounter = await world.GetEntityAsync(Encounter);
        var hero = await world.GetEntityAsync(Hero);
        var target = await world.GetEntityAsync(Target);
        Assert.NotNull(encounter);
        Assert.NotNull(hero);
        Assert.NotNull(target);
        Assert.DoesNotContain(encounter!.Components, component => component.DefinitionId == "dnd2024.encounter-initiative-order");
        Assert.Collection(encounter.Contains!.OrderBy(entity => entity.ContainedId, StringComparer.Ordinal),
            entity => AssertContainment(entity, Hero),
            entity => AssertContainment(entity, Target));
        Assert.Equal(Encounter, hero!.ContainerId);
        Assert.Equal("participant", hero.ContainerSlot);
        Assert.Equal(Encounter, target!.ContainerId);
        Assert.Equal("participant", target.ContainerSlot);

        AssertComponent(hero, "dnd2024.abilities", """{"str":12,"dex":16,"con":14,"int":10,"wis":13,"cha":8}""");
        AssertComponent(hero, "dnd2024.character-level", """{"level":5,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Character Advancement"}}""");
        AssertComponent(hero, "dnd2024.saving-throw-proficiencies", """{"abilities":["con","wis"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Proficiency > Saving Throw Proficiencies"}}""");
        AssertComponent(hero, "dnd2024.skill-proficiencies", """{"skills":["perception","stealth"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Proficiency > Skill Proficiencies and Skills"}}""");
        AssertComponent(hero, "dnd2024.weapon-proficiencies", """{"categories":["simple"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons > Weapon Proficiency"}}""");
        AssertVitalState(hero, armorClass: 14, currentHitPoints: 20, maximumHitPoints: 20);
        AssertComponent(target, "dnd2024.abilities", """{"str":10,"dex":10,"con":12,"int":8,"wis":10,"cha":8}""");
        AssertVitalState(target, armorClass: 12, currentHitPoints: 12, maximumHitPoints: 12);
    }

    [Fact]
    public async Task Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        var first = await RunSessionAsync(_catalogCopy);
        var second = await RunSessionAsync(_catalogCopy);

        AssertTranscriptEqual(first, second);
        AssertExpectedDeltas(first);
    }

    private static async Task<SessionTranscript> RunSessionAsync(string catalog)
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(catalog, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var before = await SessionStateAsync(world);
        var runner = CreateRunner(db, world, mechanics);
        var check = await RunAsync(runner, new ActionRequest
        {
            Intent = "perception check",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = """{"ability":"wis","skill":"perception","dc":12}""",
            Seed = 10
        }, "mechanic.dnd2024.check.ability");
        var save = await RunAsync(runner, new ActionRequest
        {
            Intent = "constitution saving throw",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = """{"ability":"con","dc":12}""",
            Seed = 11
        }, "mechanic.dnd2024.saving-throw");
        Assert.Equal(0, check.AppliedCount);
        Assert.Equal(0, save.AppliedCount);
        var initiative = await RunAsync(runner, new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        }, "mechanic.dnd2024.encounter-initiative-order");
        Assert.Equal(1, initiative.AppliedCount);
        using (var initiativeData = JsonDocument.Parse(initiative.Output!.Data))
            Assert.Equal(0, initiativeData.RootElement.GetProperty("tiedCounts").GetInt32());
        var attack = await RunAsync(runner, new ActionRequest
        {
            Intent = "attack target with dagger",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero, ["target"] = Target, ["weapon"] = "weapon.dnd2024.dagger" },
            Input = """{"ability":"dex"}""",
            Seed = 36
        }, "mechanic.dnd2024.weapon-attack");
        Assert.Equal(0, attack.AppliedCount);
        using var attackData = JsonDocument.Parse(attack.Output!.Data);
        Assert.True(attackData.RootElement.GetProperty("hit").GetBoolean());
        Assert.True(attackData.RootElement.GetProperty("critical").GetBoolean());
        var damage = await RunAsync(runner, new ActionRequest
        {
            Intent = "apply critical weapon damage",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero, ["target"] = Target, ["weapon"] = "weapon.dnd2024.dagger" },
            Input = JsonSerializer.Serialize(new { ability = "dex", critical = attackData.RootElement.GetProperty("critical").GetBoolean() }),
            Seed = 23
        }, "mechanic.dnd2024.weapon-damage.apply");
        Assert.Equal(1, damage.AppliedCount);

        return new SessionTranscript(
            Snapshot(check),
            Snapshot(save),
            Snapshot(initiative),
            Snapshot(attack),
            Snapshot(damage),
            before,
            await SessionStateAsync(world));
    }

    private static async Task<ActionRunResult> RunAsync(ActionRunner runner, ActionRequest request, string mechanicId)
    {
        var result = await runner.RunAsync(request);
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal(mechanicId, result.Mechanic?.Id);
        return result;
    }

    private static ActionTranscript Snapshot(ActionRunResult result) => new(
        result.Mechanic!.Id,
        result.Output!.Data,
        JsonSerializer.Serialize(result.Output.Effects),
        result.AppliedCount);

    private static async Task<SessionState> SessionStateAsync(WorldStore world) => new(
        await ComponentMapAsync(world, Encounter),
        await ComponentMapAsync(world, Hero),
        await ComponentMapAsync(world, Target),
        await ComponentMapAsync(world, "weapon.dnd2024.dagger"));

    private static async Task<Dictionary<string, string>> ComponentMapAsync(WorldStore world, string entityId) =>
        (await world.GetEntityAsync(entityId))!.Components.ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);

    private static void AssertTranscriptEqual(SessionTranscript first, SessionTranscript second)
    {
        AssertActionEqual(first.Check, second.Check);
        AssertActionEqual(first.Save, second.Save);
        AssertActionEqual(first.Initiative, second.Initiative);
        AssertActionEqual(first.Attack, second.Attack);
        AssertActionEqual(first.Damage, second.Damage);
        AssertComponentMapsEqual(first.After.Encounter, second.After.Encounter);
        AssertComponentMapsEqual(first.After.Hero, second.After.Hero);
        AssertComponentMapsEqual(first.After.Target, second.After.Target);
        AssertComponentMapsEqual(first.After.Dagger, second.After.Dagger);
    }

    private static void AssertActionEqual(ActionTranscript first, ActionTranscript second)
    {
        Assert.Equal(first.MechanicId, second.MechanicId);
        Assert.Equal(first.AppliedCount, second.AppliedCount);
        AssertJsonEqual(first.Data, second.Data);
        AssertJsonEqual(first.Effects, second.Effects);
    }

    private static void AssertExpectedDeltas(SessionTranscript session)
    {
        Assert.Empty(session.Before.Encounter);
        Assert.Equal(new[] { "dnd2024.encounter-initiative-order" }, session.After.Encounter.Keys.OrderBy(id => id, StringComparer.Ordinal));
        AssertComponentMapsEqual(session.Before.Hero, session.After.Hero);
        AssertComponentMapsEqual(session.Before.Dagger, session.After.Dagger);
        Assert.Equal(session.Before.Target.Keys.OrderBy(id => id, StringComparer.Ordinal), session.After.Target.Keys.OrderBy(id => id, StringComparer.Ordinal));
        AssertJsonEqual(session.Before.Target["dnd2024.abilities"], session.After.Target["dnd2024.abilities"]);
        AssertJsonEqual(session.Before.Target["dnd2024.armor-class"], session.After.Target["dnd2024.armor-class"]);

        using var before = JsonDocument.Parse(session.Before.Target["dnd2024.hit-points"]);
        using var after = JsonDocument.Parse(session.After.Target["dnd2024.hit-points"]);
        Assert.True(after.RootElement.GetProperty("current").GetInt32() < before.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(before.RootElement.GetProperty("maximum").GetInt32(), after.RootElement.GetProperty("maximum").GetInt32());
        Assert.True(JsonElement.DeepEquals(before.RootElement.GetProperty("sourceRef"), after.RootElement.GetProperty("sourceRef")));
    }

    private static void AssertComponentMapsEqual(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        Assert.Equal(first.Keys.OrderBy(id => id, StringComparer.Ordinal), second.Keys.OrderBy(id => id, StringComparer.Ordinal));
        foreach (var definitionId in first.Keys)
            AssertJsonEqual(first[definitionId], second[definitionId]);
    }

    private static void AssertJsonEqual(string first, string second)
    {
        using var firstDocument = JsonDocument.Parse(first);
        using var secondDocument = JsonDocument.Parse(second);
        Assert.True(JsonElement.DeepEquals(firstDocument.RootElement, secondDocument.RootElement));
    }

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private sealed record ActionTranscript(string MechanicId, string Data, string Effects, int AppliedCount);
    private sealed record SessionState(
        Dictionary<string, string> Encounter,
        Dictionary<string, string> Hero,
        Dictionary<string, string> Target,
        Dictionary<string, string> Dagger);
    private sealed record SessionTranscript(
        ActionTranscript Check,
        ActionTranscript Save,
        ActionTranscript Initiative,
        ActionTranscript Attack,
        ActionTranscript Damage,
        SessionState Before,
        SessionState After);

    private static void AssertContainment(ContainmentView containment, string id)
    {
        Assert.Equal(id, containment.ContainedId);
        Assert.Equal("participant", containment.Slot);
    }

    private static void AssertVitalState(EntitySnapshot entity, int armorClass, int currentHitPoints, int maximumHitPoints)
    {
        AssertComponent(entity, "dnd2024.armor-class", JsonSerializer.Serialize(new
        {
            value = armorClass,
            sourceRef = new { sourceId = SourceId, locator = "Playing the Game > D20 Tests > Attack Rolls > Armor Class" }
        }));
        AssertComponent(entity, "dnd2024.hit-points", JsonSerializer.Serialize(new
        {
            current = currentHitPoints,
            maximum = maximumHitPoints,
            sourceRef = new { sourceId = SourceId, locator = "Playing the Game > Damage and Healing > Hit Points" }
        }));
    }

    private static void AssertComponent(EntitySnapshot entity, string definitionId, string expected)
    {
        var actual = Assert.Single(entity.Components, component => component.DefinitionId == definitionId).Data;
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement), definitionId);
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var catalog = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(catalog)) return Path.GetDirectoryName(catalog)!;
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
