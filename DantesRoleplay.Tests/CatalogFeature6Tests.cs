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
/// Feature 6 Slice 1 is a catalog-authored final Armor Class record. This gate imports a copy of
/// those files into a fresh database, then exercises creation, correction, rejection and routing
/// through the normal action path.
/// </summary>
public sealed class CatalogFeature6Tests : IDisposable
{
    private const string ArmorClass = "dnd2024.armor-class";
    private const string HitPoints = "dnd2024.hit-points";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-6-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();

        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_catalog_records_corrects_and_guards_authoritative_armor_class()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.True(imported.ManifestUpdated);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.armor-class.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.armor-class"));

        await world.CreateEntityAsync("Armor Class fixture", "fixture.catalog.f6.subject");
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request("record armor class", """{"mode":"record","value":14}"""));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.armor-class.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        using (var recordData = JsonDocument.Parse(recorded.Output.Data))
        {
            Assert.Equal(14, recordData.RootElement.GetProperty("value").GetInt32());
            Assert.Equal("record", recordData.RootElement.GetProperty("mode").GetString());
            Assert.Equal(JsonValueKind.Null, recordData.RootElement.GetProperty("previousValue").ValueKind);
        }
        await AssertArmorClassAsync(world, 14);

        var duplicate = await runner.RunAsync(Request("record armor class", """{"mode":"record","value":15}"""));
        Assert.False(duplicate.Ok);
        Assert.Equal("MECHANIC_FAILED", duplicate.Error?.Code);
        await AssertArmorClassAsync(world, 14);

        var corrected = await runner.RunAsync(Request("correct armor class", """{"mode":"correct","value":17}"""));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Single(corrected.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, corrected.Output.Effects[0].Type);
        using (var correctionData = JsonDocument.Parse(corrected.Output.Data))
        {
            Assert.Equal(14, correctionData.RootElement.GetProperty("previousValue").GetInt32());
        }
        await AssertArmorClassAsync(world, 17);

        // The stored value deliberately admits the full positive safe-integer range; the same
        // writer must handle its useful low, ordinary, and highest boundary values without a
        // special path or a formula.
        foreach (var (id, value) in new[]
                 {
                     ("fixture.catalog.f6.low", 1L),
                     ("fixture.catalog.f6.ordinary", 10L),
                     ("fixture.catalog.f6.maximum", 9007199254740991L)
                 })
        {
            await world.CreateEntityAsync("Armor Class boundary fixture", id);
            var boundary = await runner.RunAsync(Request(
                "record armor class",
                $$"""{"mode":"record","value":{{value}}}""",
                id));
            Assert.True(boundary.Ok, boundary.Error?.Why);
            await AssertArmorClassAsync(world, value, id);
        }

        foreach (var invalid in new[]
                 {
                     "{}",
                     "{\"mode\":\"record\",\"value\":0}",
                     "{\"mode\":\"record\",\"value\":1.5}",
                     "{\"mode\":\"other\",\"value\":14}",
                     "{\"mode\":\"correct\",\"value\":14,\"sourceRef\":{}}",
                     "{\"mode\":\"correct\",\"value\":9007199254740992}"
                 })
        {
            var rejected = await runner.RunAsync(Request("set armor class", invalid));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal("MECHANIC_FAILED", rejected.Error?.Code);
            await AssertArmorClassAsync(world, 17);
        }

        await world.CreateEntityAsync("Absent Armor Class fixture", "fixture.catalog.f6.absent");
        var absentCorrection = await runner.RunAsync(Request(
            "correct armor class",
            """{"mode":"correct","value":12}""",
            "fixture.catalog.f6.absent"));
        Assert.False(absentCorrection.Ok);
        Assert.DoesNotContain(
            (await world.GetEntityAsync("fixture.catalog.f6.absent"))!.Components,
            component => component.DefinitionId == ArmorClass);

        // A valid writer must not turn a malformed pre-existing record into a plausible-looking
        // value. Its repair needs an explicitly governed migration, not a normal correction.
        const string corruptId = "fixture.catalog.f6.corrupt";
        const string corruptData = "{\"value\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"wrong\"}}";
        await world.CreateEntityAsync("Corrupt Armor Class fixture", corruptId);
        await world.SetComponentAsync(corruptId, ArmorClass, corruptData);
        var corruptCorrection = await runner.RunAsync(Request(
            "correct armor class",
            """{"mode":"correct","value":12}""",
            corruptId));
        Assert.False(corruptCorrection.Ok);
        var corrupt = await world.GetEntityAsync(corruptId);
        Assert.Equal(corruptData, Assert.Single(corrupt!.Components, component => component.DefinitionId == ArmorClass).Data);
    }

    [Fact]
    public async Task Imported_catalog_records_corrects_and_guards_authoritative_hit_points()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.hit-points.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.hit-points"));

        const string subject = "fixture.catalog.f6.hp.subject";
        await world.CreateEntityAsync("Hit Point fixture", subject);
        await world.SetComponentAsync(
            subject,
            ArmorClass,
            """{"value":14,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}""");
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request(
            "record hit points",
            """{"mode":"record","current":12,"maximum":20}""",
            subject));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.hit-points.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        using (var recordData = JsonDocument.Parse(recorded.Output.Data))
        {
            Assert.Equal(12, recordData.RootElement.GetProperty("current").GetInt32());
            Assert.Equal(20, recordData.RootElement.GetProperty("maximum").GetInt32());
            Assert.Equal(JsonValueKind.Null, recordData.RootElement.GetProperty("previous").ValueKind);
        }
        await AssertHitPointsAsync(world, 12, 20, subject);
        await AssertArmorClassAsync(world, 14, subject);

        var duplicate = await runner.RunAsync(Request(
            "record hit points",
            """{"mode":"record","current":13,"maximum":20}""",
            subject));
        Assert.False(duplicate.Ok);
        Assert.Equal("MECHANIC_FAILED", duplicate.Error?.Code);
        await AssertHitPointsAsync(world, 12, 20, subject);

        var corrected = await runner.RunAsync(Request(
            "correct hit points",
            """{"mode":"correct","current":4,"maximum":22}""",
            subject));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Single(corrected.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, corrected.Output.Effects[0].Type);
        using (var correctionData = JsonDocument.Parse(corrected.Output.Data))
        {
            Assert.Equal(12, correctionData.RootElement.GetProperty("previous").GetProperty("current").GetInt32());
            Assert.Equal(20, correctionData.RootElement.GetProperty("previous").GetProperty("maximum").GetInt32());
        }
        await AssertHitPointsAsync(world, 4, 22, subject);
        await AssertArmorClassAsync(world, 14, subject);

        foreach (var (id, current, maximum) in new[]
                 {
                     ("fixture.catalog.f6.hp.zero", 0L, 1L),
                     ("fixture.catalog.f6.hp.one", 1L, 1L),
                     ("fixture.catalog.f6.hp.full", 30L, 30L),
                     ("fixture.catalog.f6.hp.maximum", 9007199254740991L, 9007199254740991L)
                 })
        {
            await world.CreateEntityAsync("Hit Point boundary fixture", id);
            var boundary = await runner.RunAsync(Request(
                "record hit points",
                $$"""{"mode":"record","current":{{current}},"maximum":{{maximum}}}""",
                id));
            Assert.True(boundary.Ok, boundary.Error?.Why);
            await AssertHitPointsAsync(world, current, maximum, id);
        }

        foreach (var invalid in new[]
                 {
                     "{}",
                     "{\"mode\":\"record\",\"current\":-1,\"maximum\":1}",
                     "{\"mode\":\"record\",\"current\":1.5,\"maximum\":2}",
                     "{\"mode\":\"record\",\"current\":1,\"maximum\":0}",
                     "{\"mode\":\"record\",\"current\":3,\"maximum\":2}",
                     "{\"mode\":\"other\",\"current\":1,\"maximum\":2}",
                     "{\"mode\":\"correct\",\"current\":1,\"maximum\":2,\"damage\":1}",
                     "{\"mode\":\"correct\",\"current\":1,\"maximum\":9007199254740992}"
                 })
        {
            var rejected = await runner.RunAsync(Request("set hit points", invalid, subject));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal("MECHANIC_FAILED", rejected.Error?.Code);
            await AssertHitPointsAsync(world, 4, 22, subject);
            await AssertArmorClassAsync(world, 14, subject);
        }

        const string absentId = "fixture.catalog.f6.hp.absent";
        await world.CreateEntityAsync("Absent Hit Point fixture", absentId);
        var absentCorrection = await runner.RunAsync(Request(
            "correct hit points",
            """{"mode":"correct","current":1,"maximum":1}""",
            absentId));
        Assert.False(absentCorrection.Ok);
        Assert.DoesNotContain(
            (await world.GetEntityAsync(absentId))!.Components,
            component => component.DefinitionId == HitPoints);

        const string corruptId = "fixture.catalog.f6.hp.corrupt";
        const string corruptData = "{\"current\":3,\"maximum\":2,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"wrong\"}}";
        await world.CreateEntityAsync("Corrupt Hit Point fixture", corruptId);
        await world.SetComponentAsync(corruptId, HitPoints, corruptData);
        var corruptCorrection = await runner.RunAsync(Request(
            "correct hit points",
            """{"mode":"correct","current":1,"maximum":2}""",
            corruptId));
        Assert.False(corruptCorrection.Ok);
        var corrupt = await world.GetEntityAsync(corruptId);
        Assert.Equal(corruptData, Assert.Single(corrupt!.Components, component => component.DefinitionId == HitPoints).Data);
    }

    private static ActionRequest Request(string intent, string input, string subject = "fixture.catalog.f6.subject") => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
        Input = input,
        Seed = 1
    };

    private static async Task AssertArmorClassAsync(WorldStore world, long expected, string entityId = "fixture.catalog.f6.subject")
    {
        var entity = await world.GetEntityAsync(entityId);
        var component = Assert.Single(entity!.Components, component => component.DefinitionId == ArmorClass);
        using var document = JsonDocument.Parse(component.Data);
        Assert.Equal(expected, document.RootElement.GetProperty("value").GetInt64());
        Assert.Equal("source.dnd2024.srd-5.2.1", document.RootElement
            .GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(
            "Playing the Game > D20 Tests > Attack Rolls > Armor Class",
            document.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
    }

    private static async Task AssertHitPointsAsync(WorldStore world, long current, long maximum, string entityId)
    {
        var entity = await world.GetEntityAsync(entityId);
        var component = Assert.Single(entity!.Components, component => component.DefinitionId == HitPoints);
        using var document = JsonDocument.Parse(component.Data);
        Assert.Equal(current, document.RootElement.GetProperty("current").GetInt64());
        Assert.Equal(maximum, document.RootElement.GetProperty("maximum").GetInt64());
        Assert.Equal("source.dnd2024.srd-5.2.1", document.RootElement
            .GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(
            "Playing the Game > Damage and Healing > Hit Points",
            document.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());
    }

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore mechanics) =>
        new(
            db,
            mechanics,
            new ProjectionResolver(db),
            new JintMechanicEngine(),
            new EffectApplier(db, world),
            new OperationLog(db),
            new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var catalog = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(catalog))
            {
                return Path.GetDirectoryName(catalog)!;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
