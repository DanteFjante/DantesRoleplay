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
/// Feature 22 Slice 1 is diagnostic-only: it shares the established D20/condition convention
/// without inventing an unarmed weapon, reach, Action expenditure, or Hit Point application.
/// </summary>
public sealed class CatalogFeature22Tests : IDisposable
{
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private const string LevelLocator = "Character Creation > Character Advancement";
    private const string ConditionsLocator = "Rules Glossary";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-22-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_resolves_closed_effect_free_unarmed_strike_damage()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.unarmed-strike.damage"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.unarmed-strike.damage"));

        const string subject = "fixture.catalog.f22.subject";
        const string target = "fixture.catalog.f22.target";
        await CreateSubjectAsync(world, subject, level: 5, strength: 16);
        await CreateTargetAsync(world, target, armorClass: 14);
        var runner = CreateRunner(db, world, mechanics);
        var before = await SnapshotsAsync(world, subject, target);

        var result = await RunAsync(runner, "unarmed strike damage", subject, target, "{}", seed: 1);
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("mechanic.dnd2024.unarmed-strike.damage", result.Mechanic?.Id);
        Assert.Equal(0, result.AppliedCount);
        Assert.Empty(result.Output!.Effects);
        using (var data = JsonDocument.Parse(result.Output.Data))
        {
            var root = data.RootElement;
            Assert.Equal("unarmed-strike-damage", root.GetProperty("test").GetString());
            Assert.Equal("str", root.GetProperty("ability").GetString());
            Assert.Equal(3, root.GetProperty("strengthModifier").GetInt32());
            Assert.Equal(3, root.GetProperty("proficiencyBonusDerived").GetInt32());
            Assert.Equal(3, root.GetProperty("proficiencyBonusApplied").GetInt32());
            Assert.Equal(root.GetProperty("roll").GetInt32() + 6, root.GetProperty("total").GetInt32());
            Assert.Equal("bludgeoning", root.GetProperty("damageType").GetString());
            Assert.Equal(4, root.GetProperty("damageOnHit").GetInt32());
            Assert.Equal(root.GetProperty("hit").GetBoolean() ? 4 : 0, root.GetProperty("potentialDamage").GetInt32());
        }
        Assert.Equal(before, await SnapshotsAsync(world, subject, target));

        foreach (var (level, bonus) in new[] { (1, 2), (5, 3), (9, 4), (13, 5), (17, 6) })
        {
            var bandSubject = $"fixture.catalog.f22.band.{level}";
            await CreateSubjectAsync(world, bandSubject, level, strength: 10);
            var band = await RunAsync(runner, "unarmed strike damage", bandSubject, target, "{}", seed: 2);
            Assert.True(band.Ok, band.Error?.Why);
            using var data = JsonDocument.Parse(band.Output!.Data);
            Assert.Equal(bonus, data.RootElement.GetProperty("proficiencyBonusDerived").GetInt32());
            Assert.Equal(1, data.RootElement.GetProperty("damageOnHit").GetInt32());
        }

        const string lowAc = "fixture.catalog.f22.low-ac";
        const string highAc = "fixture.catalog.f22.high-ac";
        await CreateTargetAsync(world, lowAc, 1);
        await CreateTargetAsync(world, highAc, 999);
        var naturalOne = await RunAsync(runner, "unarmed strike damage", subject, lowAc, "{}", seed: 35);
        var naturalTwenty = await RunAsync(runner, "unarmed strike damage", subject, highAc, "{}", seed: 36);
        Assert.True(naturalOne.Ok, naturalOne.Error?.Why);
        Assert.True(naturalTwenty.Ok, naturalTwenty.Error?.Why);
        using (var one = JsonDocument.Parse(naturalOne.Output!.Data))
        using (var twenty = JsonDocument.Parse(naturalTwenty.Output!.Data))
        {
            Assert.Equal(1, one.RootElement.GetProperty("roll").GetInt32());
            Assert.False(one.RootElement.GetProperty("hit").GetBoolean());
            Assert.Equal(0, one.RootElement.GetProperty("potentialDamage").GetInt32());
            Assert.Equal("natural-1", one.RootElement.GetProperty("hitReason").GetString());
            Assert.Equal(20, twenty.RootElement.GetProperty("roll").GetInt32());
            Assert.True(twenty.RootElement.GetProperty("hit").GetBoolean());
            Assert.True(twenty.RootElement.GetProperty("critical").GetBoolean());
            Assert.Equal(4, twenty.RootElement.GetProperty("damageOnHit").GetInt32());
            Assert.Equal(4, twenty.RootElement.GetProperty("potentialDamage").GetInt32());
        }

        var advantageInput = """{"rollCircumstances":[{"kind":"advantage","source":"help"}]}""";
        var disadvantageInput = """{"rollCircumstances":[{"kind":"disadvantage","source":"mud"}]}""";
        var cancelledInput = """{"rollCircumstances":[{"kind":"advantage","source":"help"},{"kind":"disadvantage","source":"mud"}]}""";
        var advantage = await RunAsync(runner, "unarmed strike damage", subject, lowAc, advantageInput, seed: 9);
        var disadvantage = await RunAsync(runner, "unarmed strike damage", subject, lowAc, disadvantageInput, seed: 9);
        var cancelled = await RunAsync(runner, "unarmed strike damage", subject, lowAc, cancelledInput, seed: 9);
        Assert.True(advantage.Ok, advantage.Error?.Why);
        Assert.True(disadvantage.Ok, disadvantage.Error?.Why);
        Assert.True(cancelled.Ok, cancelled.Error?.Why);
        using (var advantageData = JsonDocument.Parse(advantage.Output!.Data))
        using (var disadvantageData = JsonDocument.Parse(disadvantage.Output!.Data))
        using (var cancelledData = JsonDocument.Parse(cancelled.Output!.Data))
        {
            Assert.Equal("advantage", advantageData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("disadvantage", disadvantageData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("normal", cancelledData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(2, advantageData.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(2, disadvantageData.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(1, cancelledData.RootElement.GetProperty("rolls").GetArrayLength());
        }
        var replay = await RunAsync(runner, "unarmed strike damage", subject, lowAc, advantageInput, seed: 9);
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(advantage.Output!.Data, replay.Output!.Data);

        await world.SetComponentAsync(subject, "dnd2024.conditions", Conditions("poisoned"));
        var conditioned = await RunAsync(runner, "unarmed strike damage", subject, lowAc, "{}", seed: 9);
        Assert.True(conditioned.Ok, conditioned.Error?.Why);
        using (var data = JsonDocument.Parse(conditioned.Output!.Data))
        {
            Assert.True(data.RootElement.GetProperty("attackerConditionsKnown").GetBoolean());
            Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:poisoned", data.RootElement.GetProperty("attackerDerivedCircumstances")[0].GetProperty("source").GetString());
        }

        var protectedBefore = await SnapshotsAsync(world, subject, target);
        foreach (var invalid in new[]
                 {
                     "[]", "{\"strengthModifier\":3}", "{\"roll\":20}", "{\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"condition:forged\"}]}",
                     "{\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"},{\"kind\":\"advantage\",\"source\":\"help\"}]}", "{\"rollCircumstances\":{}}"
                 })
        {
            var rejected = await RunAsync(runner, "unarmed strike damage", subject, target, invalid, seed: 1);
            Assert.False(rejected.Ok, invalid);
            Assert.Equal(protectedBefore, await SnapshotsAsync(world, subject, target));
        }

        const string corruptTarget = "fixture.catalog.f22.corrupt-target";
        await world.CreateEntityAsync("Corrupt unarmed target", corruptTarget);
        await world.SetComponentAsync(corruptTarget, "dnd2024.armor-class", """{"value":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"wrong"}}""");
        Assert.False((await RunAsync(runner, "unarmed strike damage", subject, corruptTarget, "{}", seed: 1)).Ok);
    }

    private static async Task CreateSubjectAsync(WorldStore world, string id, int level, int strength)
    {
        await world.CreateEntityAsync("Unarmed strike subject", id);
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":{{strength}},"dex":10,"con":10,"int":10,"wis":10,"cha":10}""");
        await world.SetComponentAsync(id, "dnd2024.character-level", JsonSerializer.Serialize(new
        {
            level,
            sourceRef = new { sourceId = SourceId, locator = LevelLocator }
        }));
    }

    private static async Task CreateTargetAsync(WorldStore world, string id, int armorClass)
    {
        await world.CreateEntityAsync("Unarmed strike target", id);
        var supportedArmorClass = Math.Clamp(armorClass, 5, 20);
        var dexterity = Math.Max(1, 10 + ((supportedArmorClass - 10) * 2));
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":10,"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
    }

    private static string Conditions(string condition) => JsonSerializer.Serialize(new
    {
        entries = new[] { new { condition } },
        sourceRef = new { sourceId = SourceId, locator = ConditionsLocator }
    });

    private static ActionRequest Request(string intent, string subject, string target, string input, long seed) => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["target"] = target },
        Input = input,
        Seed = seed
    };

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string target, string input, long seed) =>
        runner.RunAsync(Request(intent, subject, target, input, seed));

    private static async Task<Dictionary<string, string>> SnapshotsAsync(WorldStore world, params string[] ids) =>
        (await Task.WhenAll(ids.Select(async id => (Id: id, Entity: await world.GetEntityAsync(id)))))
        .ToDictionary(item => item.Id, item => string.Join("\n", item.Entity!.Components.OrderBy(component => component.DefinitionId)
            .Select(component => component.DefinitionId + "=" + component.Data)), StringComparer.Ordinal);

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
