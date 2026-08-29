using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature24Slice2Tests : IDisposable
{
    private const string Training = "dnd2024.armor-training";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private const string Locator = "Rules Glossary > Armor Class and Armor Training";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-24-slice-2-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_catalog_records_reads_and_guards_closed_armor_training()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.armor-training.write"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.armor-training.read"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.armor-training"));

        const string subject = "fixture.catalog.f24.s2.subject";
        await world.CreateEntityAsync("Armor-training fixture", subject);
        await world.SetComponentAsync(subject, "dnd2024.armor-class", """{"value":14,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}""");
        var protectedBefore = await ProtectedStateAsync(world, subject);
        var runner = CreateRunner(db, world, mechanics);

        var absent = await runner.RunAsync(Request("read armor training diagnostics", "{}"));
        Assert.True(absent.Ok, absent.Error?.Why);
        Assert.Equal("mechanic.dnd2024.armor-training.read", absent.Mechanic?.Id);
        Assert.Equal(0, absent.AppliedCount);
        Assert.Empty(absent.Output!.Effects);
        AssertRead(absent.Output.Data, present: false, valid: false, "absent", null);

        var recorded = await runner.RunAsync(Request("record armor training", """{"mode":"record","categories":["light","medium","heavy","shield"]}"""));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.armor-training.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        await AssertTrainingAsync(world, subject, ["light", "medium", "heavy", "shield"]);
        Assert.Equal(protectedBefore, await ProtectedStateAsync(world, subject));

        var read = await runner.RunAsync(Request("inspect armor training", "{}"));
        Assert.True(read.Ok, read.Error?.Why);
        Assert.Equal(0, read.AppliedCount);
        Assert.Empty(read.Output!.Effects);
        AssertRead(read.Output.Data, present: true, valid: true, null, ["light", "medium", "heavy", "shield"]);
        Assert.Equal(protectedBefore, await ProtectedStateAsync(world, subject));

        var corrected = await runner.RunAsync(Request("correct armor training", """{"mode":"correct","categories":[]}"""));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        await AssertTrainingAsync(world, subject, []);

        foreach (var invalid in new[]
                 {
                     "null",
                     "[]",
                     "{}",
                     "{\"mode\":\"record\",\"categories\":[\"Light\"]}",
                     "{\"mode\":\"record\",\"categories\":[\"other\"]}",
                     "{\"mode\":\"record\",\"categories\":[\"shield\",\"heavy\"]}",
                     "{\"mode\":\"record\",\"categories\":[\"light\",\"light\"]}",
                     "{\"mode\":\"correct\",\"categories\":[],\"sourceRef\":{}}",
                     "{\"mode\":\"correct\",\"categories\":[],\"class\":\"fighter\"}",
                     "{\"mode\":\"correct\",\"categories\":[],\"armor\":\"item.dnd2024.chain-mail.v1\"}",
                     "{\"mode\":\"correct\",\"categories\":[],\"armorClass\":16}",
                     "{\"mode\":\"correct\",\"categories\":[],\"effects\":[]}"
                 })
        {
            var rejected = await runner.RunAsync(Request("set armor training", invalid));
            Assert.False(rejected.Ok, invalid);
            await AssertTrainingAsync(world, subject, []);
            Assert.Equal(protectedBefore, await ProtectedStateAsync(world, subject));
        }

        const string absentCorrectionId = "fixture.catalog.f24.s2.absent-correction";
        await world.CreateEntityAsync("Absent armor training", absentCorrectionId);
        var absentCorrection = await runner.RunAsync(Request("correct armor training", """{"mode":"correct","categories":[]}""", absentCorrectionId));
        Assert.False(absentCorrection.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(absentCorrectionId))!.Components, component => component.DefinitionId == Training);

        const string corruptId = "fixture.catalog.f24.s2.corrupt";
        await world.CreateEntityAsync("Corrupt armor training", corruptId);
        await world.SetComponentAsync(corruptId, Training, """{"categories":["heavy","light"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary > Armor Class and Armor Training"}}""");
        var corruptRead = await runner.RunAsync(Request("read armor training diagnostics", "{}", corruptId));
        Assert.True(corruptRead.Ok, corruptRead.Error?.Why);
        Assert.Empty(corruptRead.Output!.Effects);
        AssertRead(corruptRead.Output.Data, present: true, valid: false, "invalid", null);
        var corruptCorrection = await runner.RunAsync(Request("correct armor training", """{"mode":"correct","categories":["light"]}""", corruptId));
        Assert.False(corruptCorrection.Ok);

        const string replayId = "fixture.catalog.f24.s2.replay";
        await world.CreateEntityAsync("Replay armor training", replayId);
        var replay = await runner.RunAsync(Request("record armor training", """{"mode":"record","categories":["medium","shield"]}""", replayId));
        Assert.True(replay.Ok, replay.Error?.Why);
        var replayed = await runner.RunAsync(Request("record armor training", """{"mode":"record","categories":["medium","shield"]}""", replayId));
        Assert.False(replayed.Ok);
        await AssertTrainingAsync(world, replayId, ["medium", "shield"]);
    }

    private static ActionRequest Request(string intent, string input, string subject = "fixture.catalog.f24.s2.subject") => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
        Input = input,
        Seed = 1
    };

    private static void AssertRead(string data, bool present, bool valid, string? problem, string[]? categories)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        Assert.Equal("armor-training-read", root.GetProperty("test").GetString());
        Assert.Equal(present, root.GetProperty("present").GetBoolean());
        Assert.Equal(valid, root.GetProperty("valid").GetBoolean());
        if (problem is null)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("problem").ValueKind);
        }
        else
        {
            Assert.Equal(problem, root.GetProperty("problem").GetString());
        }

        if (categories is null)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("categories").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceRef").ValueKind);
        }
        else
        {
            Assert.Equal(categories, root.GetProperty("categories").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.Equal(SourceId, root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            Assert.Equal(Locator, root.GetProperty("sourceRef").GetProperty("locator").GetString());
        }
    }

    private static async Task AssertTrainingAsync(WorldStore world, string entityId, string[] expected)
    {
        var component = Assert.Single((await world.GetEntityAsync(entityId))!.Components, component => component.DefinitionId == Training);
        using var document = JsonDocument.Parse(component.Data);
        Assert.Equal(expected, document.RootElement.GetProperty("categories").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(SourceId, document.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(Locator, document.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
    }

    private static async Task<Dictionary<string, string>> ProtectedStateAsync(WorldStore world, string entityId) =>
        (await world.GetEntityAsync(entityId))!.Components
        .Where(component => component.DefinitionId == "dnd2024.armor-class")
        .ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(
        db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
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
