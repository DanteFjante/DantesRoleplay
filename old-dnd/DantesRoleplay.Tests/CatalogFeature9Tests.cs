using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 9 Slice 1 imports the catalog-defined damage child into a fresh database. The child
/// provides reproducible damage evidence only; applying it to Hit Points is deliberately Slice 2.
/// </summary>
public sealed class CatalogFeature9Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-9-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_rolls_normal_and_critical_weapon_damage_without_effects()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.True(imported.ManifestUpdated);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.weapon-damage.roll"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.weapon-damage.roll"));

        const string subject = "fixture.catalog.f9.subject";
        await CreateSubjectAsync(world, subject, strength: 16, dexterity: 14);
        var runner = CreateRunner(db, world, mechanics);
        var before = await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger", "weapon.dnd2024.battleaxe");

        var normal = await runner.RunAsync(Request("roll confirmed dagger damage", subject, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 17));
        var critical = await runner.RunAsync(Request("roll confirmed damage for a critical hit", subject, "weapon.dnd2024.dagger", """{"ability":"str","critical":true}""", 17));
        Assert.True(normal.Ok, normal.Error?.Why);
        Assert.True(critical.Ok, critical.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-damage.roll", normal.Mechanic?.Id);
        Assert.Equal(0, normal.AppliedCount);
        Assert.Empty(normal.Output!.Effects);
        Assert.Empty(critical.Output!.Effects);
        using (var normalData = JsonDocument.Parse(normal.Output.Data))
        using (var criticalData = JsonDocument.Parse(critical.Output.Data))
        {
            var standard = normalData.RootElement;
            var doubled = criticalData.RootElement;
            Assert.Equal("weapon-damage", standard.GetProperty("test").GetString());
            Assert.Equal("piercing", standard.GetProperty("damageType").GetString());
            Assert.Equal(1, standard.GetProperty("baseDiceCount").GetInt32());
            Assert.Equal(4, standard.GetProperty("damageDieFaces").GetInt32());
            Assert.Equal(1, standard.GetProperty("damageDiceCount").GetInt32());
            Assert.Equal(2, doubled.GetProperty("damageDiceCount").GetInt32());
            Assert.Equal(3, standard.GetProperty("abilityModifier").GetInt32());
            Assert.Equal(standard.GetProperty("rolls")[0].GetInt32(), doubled.GetProperty("rolls")[0].GetInt32());
            Assert.Equal(standard.GetProperty("diceSubtotal").GetInt32() + 3, standard.GetProperty("damage").GetInt32());
            Assert.Equal(doubled.GetProperty("diceSubtotal").GetInt32() + 3, doubled.GetProperty("damage").GetInt32());
            Assert.True(doubled.GetProperty("critical").GetBoolean());
        }

        var battleaxe = await runner.RunAsync(Request("roll confirmed weapon damage", subject, "weapon.dnd2024.battleaxe", """{"ability":"str","critical":false}""", 17));
        Assert.True(battleaxe.Ok, battleaxe.Error?.Why);
        using (var battleaxeData = JsonDocument.Parse(battleaxe.Output!.Data))
        {
            Assert.Equal("slashing", battleaxeData.RootElement.GetProperty("damageType").GetString());
            Assert.Equal(8, battleaxeData.RootElement.GetProperty("damageDieFaces").GetInt32());
        }
        Assert.Equal(before, await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger", "weapon.dnd2024.battleaxe"));
    }

    [Fact]
    public async Task Imported_catalog_clamps_damage_and_rejects_noncanonical_confirmations()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        const string subject = "fixture.catalog.f9.low-strength";
        await CreateSubjectAsync(world, subject, strength: 1, dexterity: 10);
        var before = await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger", "weapon.dnd2024.shortbow");

        ActionRunResult? clamped = null;
        for (var seed = 1L; seed <= 100; seed++)
        {
            var candidate = await runner.RunAsync(Request("roll confirmed dagger damage", subject, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", seed));
            Assert.True(candidate.Ok, candidate.Error?.Why);
            using var data = JsonDocument.Parse(candidate.Output!.Data);
            if (data.RootElement.GetProperty("damage").GetInt32() == 0)
            {
                clamped = candidate;
                break;
            }
        }
        Assert.NotNull(clamped);
        using (var data = JsonDocument.Parse(clamped!.Output!.Data))
        {
            Assert.Equal(-5, data.RootElement.GetProperty("abilityModifier").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("damage").GetInt32());
        }

        var replay = await runner.RunAsync(Request("roll confirmed dagger damage", subject, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", clamped.Seed!.Value));
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(clamped.Output!.Data, replay.Output!.Data);

        foreach (var invalid in new[]
                 {
                     "{}",
                     "{\"ability\":\"str\"}",
                     "{\"ability\":\"wis\",\"critical\":false}",
                     "{\"ability\":\"str\",\"critical\":\"false\"}",
                     "{\"ability\":\"str\",\"critical\":false,\"damage\":99}",
                     "{\"ability\":\"str\",\"critical\":false,\"hit\":true}",
                     "{\"ability\":\"str\",\"critical\":false,\"target\":\"other\"}"
                 })
        {
            var rejected = await runner.RunAsync(Request("roll confirmed weapon damage", subject, "weapon.dnd2024.dagger", invalid, 1));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal(before, await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger", "weapon.dnd2024.shortbow"));
        }

        var forbiddenAbility = await runner.RunAsync(Request("roll confirmed shortbow damage", subject, "weapon.dnd2024.shortbow", """{"ability":"str","critical":false}""", 1));
        Assert.False(forbiddenAbility.Ok);

        const string corruptWeapon = "fixture.catalog.f9.corrupt-weapon";
        await world.CreateEntityAsync("Corrupt weapon", corruptWeapon);
        await world.SetComponentAsync(corruptWeapon, "dnd2024.weapon-profile", """{"category":"simple","kind":"melee","attackAbilities":["str"],"damage":{"count":51,"faces":4,"type":"piercing"},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}""");
        var corrupt = await runner.RunAsync(Request("roll confirmed weapon damage", subject, corruptWeapon, """{"ability":"str","critical":true}""", 1));
        Assert.False(corrupt.Ok);
        Assert.Equal(before[subject], (await SnapshotsAsync(world, subject))[subject]);
    }

    [Fact]
    public async Task Imported_catalog_composes_damage_and_atomically_updates_target_hit_points()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.weapon-damage.apply"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.weapon-damage.apply"));
        var runner = CreateRunner(db, world, mechanics);

        const string subject = "fixture.catalog.f9.apply.subject";
        const string target = "fixture.catalog.f9.apply.target";
        await CreateSubjectAsync(world, subject, strength: 16, dexterity: 10);
        await CreateTargetAsync(world, target, current: 12, maximum: 12);
        var protectedBefore = await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger");

        var applied = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, target, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 23));
        Assert.True(applied.Ok, applied.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-damage.apply", applied.Mechanic?.Id);
        Assert.Equal(1, applied.AppliedCount);
        var effect = Assert.Single(applied.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, effect.Type);
        Assert.Equal(target, effect.EntityId);
        Assert.Equal("dnd2024.hit-points", effect.DefinitionId);
        var child = Assert.Single(applied.Projection!.Children["damage"]);
        Assert.Equal("mechanic.dnd2024.weapon-damage.roll", child.MechanicId);
        Assert.Empty(child.Output.Effects);
        using (var childData = JsonDocument.Parse(child.Output.Data))
        using (var parentData = JsonDocument.Parse(applied.Output.Data))
        {
            Assert.Equal("weapon-damage-application", parentData.RootElement.GetProperty("test").GetString());
            Assert.Equal(childData.RootElement.GetProperty("damage").GetInt32(), parentData.RootElement.GetProperty("damage").GetInt32());
            Assert.Equal(12, parentData.RootElement.GetProperty("beforeCurrent").GetInt32());
            Assert.Equal(12 - childData.RootElement.GetProperty("damage").GetInt32(), parentData.RootElement.GetProperty("afterCurrent").GetInt32());
            Assert.Equal(12, parentData.RootElement.GetProperty("maximum").GetInt32());
        }
        await AssertHitPointsAsync(world, target, applied.Output.Data);
        Assert.Equal(protectedBefore, await SnapshotsAsync(world, subject, "weapon.dnd2024.dagger"));

        const string criticalTarget = "fixture.catalog.f9.apply.critical";
        await CreateTargetAsync(world, criticalTarget, current: 100, maximum: 100);
        var critical = await runner.RunAsync(ApplyRequest("apply confirmed critical weapon damage", subject, criticalTarget, "weapon.dnd2024.dagger", """{"ability":"str","critical":true}""", 23));
        Assert.True(critical.Ok, critical.Error?.Why);
        var criticalChild = Assert.Single(critical.Projection!.Children["damage"]);
        using (var childData = JsonDocument.Parse(criticalChild.Output.Data))
        using (var parentData = JsonDocument.Parse(critical.Output!.Data))
        {
            Assert.True(childData.RootElement.GetProperty("critical").GetBoolean());
            Assert.Equal(2, childData.RootElement.GetProperty("damageDiceCount").GetInt32());
            Assert.Equal(childData.RootElement.GetProperty("damage").GetInt32(), parentData.RootElement.GetProperty("damage").GetInt32());
        }

        const string overkillTarget = "fixture.catalog.f9.apply.overkill";
        await CreateTargetAsync(world, overkillTarget, current: 1, maximum: 12);
        var overkill = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, overkillTarget, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 23));
        Assert.True(overkill.Ok, overkill.Error?.Why);
        using (var overkillData = JsonDocument.Parse(overkill.Output!.Data))
        {
            Assert.Equal(0, overkillData.RootElement.GetProperty("afterCurrent").GetInt32());
        }
        await AssertHitPointsAsync(world, overkillTarget, overkill.Output.Data);

        const string replayTarget = "fixture.catalog.f9.apply.replay";
        await CreateTargetAsync(world, replayTarget, current: 20, maximum: 20);
        var replayFirst = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, replayTarget, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 41));
        Assert.True(replayFirst.Ok, replayFirst.Error?.Why);
        var firstState = (await world.GetEntityAsync(replayTarget))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data;
        await CreateOrResetHitPointsAsync(world, replayTarget, current: 20, maximum: 20);
        var replaySecond = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, replayTarget, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 41));
        Assert.True(replaySecond.Ok, replaySecond.Error?.Why);
        Assert.Equal(replayFirst.Output!.Data, replaySecond.Output!.Data);
        Assert.Equal(firstState, (await world.GetEntityAsync(replayTarget))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data);

        const string corruptTarget = "fixture.catalog.f9.apply.corrupt";
        await world.CreateEntityAsync("Corrupt damage target", corruptTarget);
        await world.SetComponentAsync(corruptTarget, "dnd2024.hit-points", """{"current":13,"maximum":12,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"wrong"}}""");
        var corruptBefore = (await world.GetEntityAsync(corruptTarget))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data;
        var corrupt = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, corruptTarget, "weapon.dnd2024.dagger", """{"ability":"str","critical":false}""", 1));
        Assert.False(corrupt.Ok);
        Assert.Equal(corruptBefore, (await world.GetEntityAsync(corruptTarget))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data);

        var invalidBefore = (await world.GetEntityAsync(target))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data;
        var invalid = await runner.RunAsync(ApplyRequest("apply confirmed weapon damage", subject, target, "weapon.dnd2024.dagger", """{"ability":"str","critical":false,"damage":99}""", 1));
        Assert.False(invalid.Ok);
        Assert.Equal(invalidBefore, (await world.GetEntityAsync(target))!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data);
    }

    private static async Task CreateSubjectAsync(WorldStore world, string id, int strength, int dexterity)
    {
        await world.CreateEntityAsync("Weapon damage subject", id);
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":{{strength}},"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
    }

    private static async Task CreateTargetAsync(WorldStore world, string id, int current, int maximum)
    {
        await world.CreateEntityAsync("Weapon damage target", id);
        await CreateOrResetHitPointsAsync(world, id, current, maximum);
    }

    private static Task CreateOrResetHitPointsAsync(WorldStore world, string id, int current, int maximum) =>
        world.SetComponentAsync(id, "dnd2024.hit-points", JsonSerializer.Serialize(new
        {
            current,
            maximum,
            sourceRef = new
            {
                sourceId = "source.dnd2024.srd-5.2.1",
                locator = "Playing the Game > Damage and Healing > Hit Points"
            }
        }));

    private static async Task AssertHitPointsAsync(WorldStore world, string target, string parentData)
    {
        using var result = JsonDocument.Parse(parentData);
        var entity = await world.GetEntityAsync(target);
        using var state = JsonDocument.Parse(entity!.Components.Single(component => component.DefinitionId == "dnd2024.hit-points").Data);
        Assert.Equal(result.RootElement.GetProperty("afterCurrent").GetInt32(), state.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(result.RootElement.GetProperty("maximum").GetInt32(), state.RootElement.GetProperty("maximum").GetInt32());
        Assert.Equal("source.dnd2024.srd-5.2.1", state.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Playing the Game > Damage and Healing > Hit Points", state.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
    }

    private static ActionRequest Request(string intent, string subject, string weapon, string input, long seed) => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["weapon"] = weapon },
        Input = input,
        Seed = seed
    };

    private static ActionRequest ApplyRequest(string intent, string subject, string target, string weapon, string input, long seed) => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["target"] = target, ["weapon"] = weapon },
        Input = input,
        Seed = seed
    };

    private static async Task<Dictionary<string, string>> SnapshotsAsync(WorldStore world, params string[] ids) =>
        (await Task.WhenAll(ids.Select(async id => (Id: id, Entity: await world.GetEntityAsync(id)))))
        .ToDictionary(item => item.Id, item => string.Join("\n", item.Entity!.Components.OrderBy(component => component.DefinitionId).Select(component => component.DefinitionId + "=" + component.Data)), StringComparer.Ordinal);

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, events: new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

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
