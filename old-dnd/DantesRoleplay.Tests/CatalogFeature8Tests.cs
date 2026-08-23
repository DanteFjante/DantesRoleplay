using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 8 is an import-backed, effect-free attack resolver. It consumes the canonical facts
/// written by Features 7 and 24 and must leave every entity exactly as it found it.
/// </summary>
public sealed class CatalogFeature8Tests : IDisposable
{
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private const string LevelLocator = "Character Creation > Character Advancement";
    private const string ProficiencyLocator = "Equipment > Weapons > Weapon Proficiency";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-8-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_catalog_resolves_effect_free_weapon_attacks_from_canonical_state()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.True(imported.ManifestUpdated);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.weapon-attack"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.weapon-attack"));

        const string subject = "fixture.catalog.f8.subject";
        const string target = "fixture.catalog.f8.target";
        await CreateSubjectAsync(world, subject, level: 5, categories: ["simple"], strength: 12, dexterity: 16);
        await CreateTargetAsync(world, target, 14);
        var before = await SnapshotsAsync(world, subject, target, "weapon.dnd2024.dagger");
        var runner = CreateRunner(db, world, mechanics);

        var result = await runner.RunAsync(Request("attack target with dagger", subject, target, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 1));
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-attack", result.Mechanic?.Id);
        Assert.Equal(0, result.AppliedCount);
        Assert.Empty(result.Output!.Effects);
        using (var data = JsonDocument.Parse(result.Output.Data))
        {
            var root = data.RootElement;
            Assert.Equal("weapon-attack", root.GetProperty("test").GetString());
            Assert.Equal("simple", root.GetProperty("weaponCategory").GetString());
            Assert.Equal("dex", root.GetProperty("ability").GetString());
            Assert.True(root.GetProperty("proficient").GetBoolean());
            Assert.Equal(3, root.GetProperty("abilityModifier").GetInt32());
            Assert.Equal(3, root.GetProperty("proficiencyBonusDerived").GetInt32());
            Assert.Equal(3, root.GetProperty("proficiencyBonusApplied").GetInt32());
            Assert.Equal(root.GetProperty("roll").GetInt32() + 6, root.GetProperty("total").GetInt32());
            Assert.Equal("normal", root.GetProperty("rollMode").GetString());
            Assert.Equal(1, root.GetProperty("rolls").GetArrayLength());
            Assert.False(root.GetProperty("critical").GetBoolean());
        }
        Assert.Equal(before, await SnapshotsAsync(world, subject, target, "weapon.dnd2024.dagger"));

        // The authoritative final AC is the comparison target: equality hits, one higher misses,
        // and one lower still hits. The repeated seed keeps the selected die identical.
        using (var primary = JsonDocument.Parse(result.Output.Data))
        {
            var total = primary.RootElement.GetProperty("total").GetInt32();
            const string equalAc = "fixture.catalog.f8.ac.equal";
            const string aboveAc = "fixture.catalog.f8.ac.above";
            const string belowAc = "fixture.catalog.f8.ac.below";
            await CreateTargetAsync(world, equalAc, total);
            await CreateTargetAsync(world, aboveAc, total + 1);
            await CreateTargetAsync(world, belowAc, total - 1);
            var equal = await runner.RunAsync(Request("weapon attack", subject, equalAc, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 1));
            var above = await runner.RunAsync(Request("weapon attack", subject, aboveAc, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 1));
            var below = await runner.RunAsync(Request("weapon attack", subject, belowAc, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 1));
            Assert.True(equal.Ok, equal.Error?.Why);
            Assert.True(above.Ok, above.Error?.Why);
            Assert.True(below.Ok, below.Error?.Why);
            using var equalData = JsonDocument.Parse(equal.Output!.Data);
            using var aboveData = JsonDocument.Parse(above.Output!.Data);
            using var belowData = JsonDocument.Parse(below.Output!.Data);
            Assert.True(equalData.RootElement.GetProperty("hit").GetBoolean());
            Assert.False(aboveData.RootElement.GetProperty("hit").GetBoolean());
            Assert.True(belowData.RootElement.GetProperty("hit").GetBoolean());
        }

        // The proficiency band is derived solely from level; no supplied bonus can influence it.
        foreach (var (level, bonus) in new[] { (4, 2), (5, 3), (16, 5), (17, 6) })
        {
            var bandSubject = $"fixture.catalog.f8.level.{level}";
            await CreateSubjectAsync(world, bandSubject, level, ["simple"]);
            var band = await runner.RunAsync(Request("weapon attack", bandSubject, target, "weapon.dnd2024.dagger", """{"ability":"str"}""", 2));
            Assert.True(band.Ok, band.Error?.Why);
            using var data = JsonDocument.Parse(band.Output!.Data);
            Assert.Equal(bonus, data.RootElement.GetProperty("proficiencyBonusDerived").GetInt32());
            Assert.Equal(bonus, data.RootElement.GetProperty("proficiencyBonusApplied").GetInt32());
        }

        const string nonProficient = "fixture.catalog.f8.nonproficient";
        await CreateSubjectAsync(world, nonProficient, level: 5, categories: [], strength: 12, dexterity: 16);
        var proficient = await runner.RunAsync(Request("weapon attack", subject, target, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 3));
        var withoutProficiency = await runner.RunAsync(Request("weapon attack", nonProficient, target, "weapon.dnd2024.dagger", """{"ability":"dex"}""", 3));
        Assert.True(proficient.Ok, proficient.Error?.Why);
        Assert.True(withoutProficiency.Ok, withoutProficiency.Error?.Why);
        using (var withData = JsonDocument.Parse(proficient.Output!.Data))
        using (var withoutData = JsonDocument.Parse(withoutProficiency.Output!.Data))
        {
            Assert.Equal(3, withData.RootElement.GetProperty("total").GetInt32() - withoutData.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(0, withoutData.RootElement.GetProperty("proficiencyBonusApplied").GetInt32());
            Assert.False(withoutData.RootElement.GetProperty("proficient").GetBoolean());
        }

        var daggerStrength = await runner.RunAsync(Request("weapon attack", subject, target, "weapon.dnd2024.dagger", """{"ability":"str"}""", 4));
        Assert.True(daggerStrength.Ok, daggerStrength.Error?.Why);
        var shortbowStrength = await runner.RunAsync(Request("weapon attack", subject, target, "weapon.dnd2024.shortbow", """{"ability":"str"}""", 4));
        var battleaxeDexterity = await runner.RunAsync(Request("weapon attack", subject, target, "weapon.dnd2024.battleaxe", """{"ability":"dex"}""", 4));
        Assert.False(shortbowStrength.Ok);
        Assert.False(battleaxeDexterity.Ok);
    }

    [Fact]
    public async Task Imported_catalog_enforces_d20_precedence_modes_replay_and_closed_inputs()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        const string subject = "fixture.catalog.f8.modes.subject";
        const string lowAc = "fixture.catalog.f8.modes.low";
        const string highAc = "fixture.catalog.f8.modes.high";
        await CreateSubjectAsync(world, subject, level: 1, categories: ["simple"]);
        await CreateTargetAsync(world, lowAc, 1);
        await CreateTargetAsync(world, highAc, 999);

        var naturalOne = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", """{"ability":"str"}""", 35));
        var naturalTwenty = await runner.RunAsync(Request("weapon attack", subject, highAc, "weapon.dnd2024.dagger", """{"ability":"str"}""", 36));
        Assert.True(naturalOne.Ok, naturalOne.Error?.Why);
        Assert.True(naturalTwenty.Ok, naturalTwenty.Error?.Why);
        using (var oneData = JsonDocument.Parse(naturalOne.Output!.Data))
        using (var twentyData = JsonDocument.Parse(naturalTwenty.Output!.Data))
        {
            Assert.Equal(1, oneData.RootElement.GetProperty("roll").GetInt32());
            Assert.False(oneData.RootElement.GetProperty("hit").GetBoolean());
            Assert.Equal("natural-1", oneData.RootElement.GetProperty("hitReason").GetString());
            Assert.Equal(20, twentyData.RootElement.GetProperty("roll").GetInt32());
            Assert.True(twentyData.RootElement.GetProperty("hit").GetBoolean());
            Assert.True(twentyData.RootElement.GetProperty("critical").GetBoolean());
            Assert.Equal("natural-20", twentyData.RootElement.GetProperty("hitReason").GetString());
        }

        var advantageInput = """{"ability":"str","rollCircumstances":[{"kind":"advantage","source":"help"}]}""";
        var disadvantageInput = """{"ability":"str","rollCircumstances":[{"kind":"disadvantage","source":"poisoned"}]}""";
        var cancelledInput = """{"ability":"str","rollCircumstances":[{"kind":"advantage","source":"help"},{"kind":"disadvantage","source":"poisoned"}]}""";
        var advantage = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", advantageInput, 9));
        var disadvantage = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", disadvantageInput, 9));
        var cancelled = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", cancelledInput, 9));
        Assert.True(advantage.Ok, advantage.Error?.Why);
        Assert.True(disadvantage.Ok, disadvantage.Error?.Why);
        Assert.True(cancelled.Ok, cancelled.Error?.Why);
        using (var advantageData = JsonDocument.Parse(advantage.Output!.Data))
        using (var disadvantageData = JsonDocument.Parse(disadvantage.Output!.Data))
        using (var cancelledData = JsonDocument.Parse(cancelled.Output!.Data))
        {
            Assert.Equal("advantage", advantageData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(2, advantageData.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(Math.Max(
                advantageData.RootElement.GetProperty("rolls")[0].GetInt32(),
                advantageData.RootElement.GetProperty("rolls")[1].GetInt32()),
                advantageData.RootElement.GetProperty("roll").GetInt32());
            Assert.Equal("disadvantage", disadvantageData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(2, disadvantageData.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(Math.Min(
                disadvantageData.RootElement.GetProperty("rolls")[0].GetInt32(),
                disadvantageData.RootElement.GetProperty("rolls")[1].GetInt32()),
                disadvantageData.RootElement.GetProperty("roll").GetInt32());
            Assert.Equal("normal", cancelledData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(1, cancelledData.RootElement.GetProperty("rolls").GetArrayLength());
        }

        var replay = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", advantageInput, 9));
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(advantage.Output!.Data, replay.Output!.Data);

        var before = await SnapshotsAsync(world, subject, lowAc, "weapon.dnd2024.dagger");
        foreach (var invalid in new[]
                 {
                     "{}",
                     "{\"ability\":\"wis\"}",
                     "{\"ability\":\"str\",\"total\":99}",
                     "{\"ability\":\"str\",\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"},{\"kind\":\"advantage\",\"source\":\"help\"}]}"
                 })
        {
            var rejected = await runner.RunAsync(Request("weapon attack", subject, lowAc, "weapon.dnd2024.dagger", invalid, 1));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal(before, await SnapshotsAsync(world, subject, lowAc, "weapon.dnd2024.dagger"));
        }

        const string corruptTarget = "fixture.catalog.f8.modes.corrupt";
        await world.CreateEntityAsync("Corrupt target", corruptTarget);
        await world.SetComponentAsync(corruptTarget, "dnd2024.armor-class", """{"value":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"wrong"}}""");
        var corrupt = await runner.RunAsync(Request("weapon attack", subject, corruptTarget, "weapon.dnd2024.dagger", """{"ability":"str"}""", 1));
        Assert.False(corrupt.Ok);
    }

    private static async Task CreateSubjectAsync(WorldStore world, string id, int level, string[] categories, int strength = 10, int dexterity = 10)
    {
        await world.CreateEntityAsync("Weapon attack subject", id);
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":{{strength}},"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
        await world.SetComponentAsync(id, "dnd2024.character-level", JsonSerializer.Serialize(new
        {
            level,
            sourceRef = new { sourceId = SourceId, locator = LevelLocator }
        }));
        await world.SetComponentAsync(id, "dnd2024.weapon-proficiencies", JsonSerializer.Serialize(new
        {
            categories,
            sourceRef = new { sourceId = SourceId, locator = ProficiencyLocator }
        }));
    }

    private static async Task CreateTargetAsync(WorldStore world, string id, int armorClass)
    {
        await world.CreateEntityAsync("Weapon attack target", id);
        var supportedArmorClass = Math.Clamp(armorClass, 5, 20);
        var dexterity = Math.Max(1, 10 + ((supportedArmorClass - 10) * 2));
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":10,"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
    }

    private static ActionRequest Request(string intent, string subject, string target, string weapon, string input, long seed) => new()
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
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
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
