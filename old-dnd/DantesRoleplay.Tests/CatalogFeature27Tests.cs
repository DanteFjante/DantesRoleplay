using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature27Tests : IDisposable
{
    private const string Fighter = "content.dnd2024.class.fighter.v1";
    private const string Progression = "dnd2024.class-progression";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-27-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_source_backed_Fighter_progression_and_feature_identities()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.class-progression"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.class-progression.read"));
        var fighter = await world.GetEntityAsync(Fighter);
        Assert.NotNull(fighter);
        using var progression = JsonDocument.Parse(Component(fighter, Progression));
        Assert.Equal(10, progression.RootElement.GetProperty("hitDieSides").GetInt32());
        Assert.Equal(6, progression.RootElement.GetProperty("fixedHitPointGainBeforeConstitution").GetInt32());
        Assert.Equal(2, progression.RootElement.GetProperty("levels").GetArrayLength());
        AssertFeatureIdentity(await world.GetEntityAsync("content.dnd2024.feature.fighter.fighting-style.v1"), "fighter-fighting-style");
        AssertFeatureIdentity(await world.GetEntityAsync("content.dnd2024.feature.fighter.second-wind.v1"), "fighter-second-wind");
        AssertFeatureIdentity(await world.GetEntityAsync("content.dnd2024.feature.fighter.weapon-mastery.v1"), "fighter-weapon-mastery");
        AssertFeatureIdentity(await world.GetEntityAsync("content.dnd2024.feature.fighter.action-surge.v1"), "fighter-action-surge");
        AssertFeatureIdentity(await world.GetEntityAsync("content.dnd2024.feature.fighter.tactical-mind.v1"), "fighter-tactical-mind");
    }

    [Fact]
    public async Task Reader_reports_exact_declared_level_entitlements_without_effects()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        var before = Component(await world.GetEntityAsync(Fighter), Progression);

        var levelOne = await runner.RunAsync(Request("inspect fighter class level", 1));
        Assert.True(levelOne.Ok, levelOne.Error?.Why);
        Assert.Empty(levelOne.Output!.Effects);
        using (var document = JsonDocument.Parse(levelOne.Output.Data))
        {
            var data = document.RootElement;
            Assert.Equal("supported", data.GetProperty("status").GetString());
            Assert.Equal(1, data.GetProperty("requestedClassLevel").GetInt32());
            Assert.Equal(10, data.GetProperty("hitDieSides").GetInt32());
            Assert.Equal(6, data.GetProperty("fixedHitPointGainBeforeConstitution").GetInt32());
            Assert.Equal(
                ["content.dnd2024.feature.fighter.fighting-style.v1", "content.dnd2024.feature.fighter.second-wind.v1", "content.dnd2024.feature.fighter.weapon-mastery.v1"],
                data.GetProperty("featureEntitlements").EnumerateArray().Select(x => x.GetProperty("definitionId").GetString()));
        }

        var levelTwo = await runner.RunAsync(Request("read class progression", 2));
        Assert.True(levelTwo.Ok, levelTwo.Error?.Why);
        Assert.Empty(levelTwo.Output!.Effects);
        using (var document = JsonDocument.Parse(levelTwo.Output.Data))
        {
            var data = document.RootElement;
            Assert.Equal("supported", data.GetProperty("status").GetString());
            var features = data.GetProperty("featureEntitlements").EnumerateArray().ToArray();
            Assert.Equal(["content.dnd2024.feature.fighter.action-surge.v1", "content.dnd2024.feature.fighter.tactical-mind.v1"], features.Select(x => x.GetProperty("definitionId").GetString()));
            Assert.All(features, feature => Assert.Equal("unimplemented", feature.GetProperty("behaviorStatus").GetString()));
        }

        var unsupported = await runner.RunAsync(Request("read class progression", 3));
        Assert.True(unsupported.Ok, unsupported.Error?.Why);
        Assert.Empty(unsupported.Output!.Effects);
        using var unsupportedData = JsonDocument.Parse(unsupported.Output.Data);
        Assert.Equal("unsupported-level", unsupportedData.RootElement.GetProperty("status").GetString());
        Assert.Equal("unsupported-level", unsupportedData.RootElement.GetProperty("problem").GetString());
        Assert.Equal(before, Component(await world.GetEntityAsync(Fighter), Progression));
    }

    [Fact]
    public async Task Reader_diagnoses_invalid_or_mismatched_declaration_and_rejects_closed_input()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        var original = Component(await world.GetEntityAsync(Fighter), Progression);

        var extra = await runner.RunAsync(new ActionRequest
        {
            Intent = "read class progression", Input = """{"classLevel":2,"effects":[]}""", Seed = 27,
            RoleEntityIds = new Dictionary<string, string> { ["class"] = Fighter }
        });
        Assert.False(extra.Ok);
        Assert.Equal(original, Component(await world.GetEntityAsync(Fighter), Progression));

        await world.SetComponentAsync(Fighter, Progression, """{"hitDieSides":10,"fixedHitPointGainBeforeConstitution":6,"levels":[{"classLevel":2,"featureDefinitionIds":["content.dnd2024.feature.fighter.tactical-mind.v1","content.dnd2024.feature.fighter.action-surge.v1"],"choiceSetDefinitionIds":[]}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Classes > Fighter, PDF pages 47–48"}}""");
        var invalid = await runner.RunAsync(Request("read class progression", 2));
        Assert.True(invalid.Ok, invalid.Error?.Why);
        Assert.Empty(invalid.Output!.Effects);
        using (var document = JsonDocument.Parse(invalid.Output.Data))
        {
            Assert.Equal("unknown", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("invalid-progression", document.RootElement.GetProperty("problem").GetString());
        }

        await world.SetComponentAsync(Fighter, Progression, original);
        await world.SetComponentAsync(Fighter, "dnd2024.character.content-definition", """{"kind":"class","contentKey":"fighter","contentVersion":1,"status":"active","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Classes > Fighter, PDF pages 49–50"}}""");
        var mismatch = await runner.RunAsync(Request("read class progression", 2));
        Assert.True(mismatch.Ok, mismatch.Error?.Why);
        Assert.Empty(mismatch.Output!.Effects);
        using var mismatchData = JsonDocument.Parse(mismatch.Output.Data);
        Assert.Equal("unknown", mismatchData.RootElement.GetProperty("status").GetString());
        Assert.Equal("source-mismatch", mismatchData.RootElement.GetProperty("problem").GetString());
    }

    private async Task<(WorldStore World, MechanicStore Mechanics, DantesRoleplayDbContext Db)> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return (world, mechanics, db);
    }

    private static ActionRequest Request(string intent, int classLevel) => new()
    {
        Intent = intent, Input = JsonSerializer.Serialize(new { classLevel }), Seed = 27,
        RoleEntityIds = new Dictionary<string, string> { ["class"] = Fighter }
    };

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static void AssertFeatureIdentity(EntitySnapshot? feature, string key)
    {
        using var document = JsonDocument.Parse(Component(feature, "dnd2024.character.content-definition"));
        var data = document.RootElement;
        Assert.Equal("feature", data.GetProperty("kind").GetString());
        Assert.Equal(key, data.GetProperty("contentKey").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());
        Assert.Equal("Classes > Fighter, PDF pages 47–48", data.GetProperty("sourceRef").GetProperty("locator").GetString());
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
