using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature28Tests : IDisposable
{
    private const string Languages = "dnd2024.language-proficiencies";
    private const string Tools = "dnd2024.tool-proficiencies";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-28-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_records_and_guards_language_and_tool_proficiencies()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.languages-and-tools"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.language-proficiencies.record"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.tool-proficiencies.record"));

        const string subject = "fixture.catalog.f28.subject";
        await world.CreateEntityAsync("Language and tool fixture", subject);
        var runner = CreateRunner(db, world, mechanics);

        var languageRecorded = await runner.RunAsync(Request(
            "record languages", """{"languages":["giant","common"]}""", subject));
        Assert.True(languageRecorded.Ok, languageRecorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.language-proficiencies.record", languageRecorded.Mechanic?.Id);
        Assert.Single(languageRecorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, languageRecorded.Output.Effects[0].Type);
        await AssertMembershipAsync(world, subject, Languages, "languages",
            ["common", "giant"], "Character Creation > Step 2: Character Origin > Choose Languages");

        var toolsRecorded = await runner.RunAsync(Request(
            "record tool proficiencies", """{"tools":["dice-set","bagpipes"]}""", subject));
        Assert.True(toolsRecorded.Ok, toolsRecorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.tool-proficiencies.record", toolsRecorded.Mechanic?.Id);
        Assert.Single(toolsRecorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, toolsRecorded.Output.Effects[0].Type);
        await AssertMembershipAsync(world, subject, Tools, "tools",
            ["bagpipes", "dice-set"], "Equipment > Tools > Tool Proficiency");

        var corrected = await runner.RunAsync(Request(
            "correct language proficiencies", """{"languages":["undercommon","common-sign-language"]}""", subject));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Single(corrected.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, corrected.Output.Effects[0].Type);
        await AssertMembershipAsync(world, subject, Languages, "languages",
            ["common-sign-language", "undercommon"], "Character Creation > Step 2: Character Origin > Choose Languages");
        await AssertMembershipAsync(world, subject, Tools, "tools",
            ["bagpipes", "dice-set"], "Equipment > Tools > Tool Proficiency");

        var beforeLanguages = await ComponentAsync(world, subject, Languages);
        var beforeTools = await ComponentAsync(world, subject, Tools);
        foreach (var invalid in new[]
                 {
                     "null", "[]", "{}", """{"languages":null}""", """{"languages":["Common"]}""",
                     """{"languages":["common","common"]}""", """{"languages":["other"]}""",
                     """{"languages":["common"],"sourceRef":{}}""", """{"tools":["dice-set"]}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("set known languages", invalid, subject));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal(beforeLanguages, await ComponentAsync(world, subject, Languages));
            Assert.Equal(beforeTools, await ComponentAsync(world, subject, Tools));
        }

        foreach (var invalid in new[]
                 {
                     "null", "[]", "{}", """{"tools":null}""", """{"tools":["Dice Set"]}""",
                     """{"tools":["dice-set","dice-set"]}""", """{"tools":["other"]}""",
                     """{"tools":["dice-set"],"proficiencyBonus":2}""", """{"languages":["common"]}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("set tool proficiencies", invalid, subject));
            Assert.False(rejected.Ok, invalid);
            Assert.Equal(beforeLanguages, await ComponentAsync(world, subject, Languages));
            Assert.Equal(beforeTools, await ComponentAsync(world, subject, Tools));
        }

        const string empty = "fixture.catalog.f28.empty";
        await world.CreateEntityAsync("Known empty fixture", empty);
        var emptyRecorded = await runner.RunAsync(Request("record languages", """{"languages":[]}""", empty));
        Assert.True(emptyRecorded.Ok, emptyRecorded.Error?.Why);
        await AssertMembershipAsync(world, empty, Languages, "languages", [],
            "Character Creation > Step 2: Character Origin > Choose Languages");

        const string corrupt = "fixture.catalog.f28.corrupt";
        await world.CreateEntityAsync("Corrupt language fixture", corrupt);
        await world.SetComponentAsync(corrupt, Languages,
            """{"languages":["giant","common"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Step 2: Character Origin > Choose Languages"}}""");
        var corruptBefore = await ComponentAsync(world, corrupt, Languages);
        var corruptRejected = await runner.RunAsync(Request("correct language proficiencies",
            """{"languages":["common"]}""", corrupt));
        Assert.False(corruptRejected.Ok);
        Assert.Equal(corruptBefore, await ComponentAsync(world, corrupt, Languages));
    }

    private static ActionRequest Request(string intent, string input, string subject) => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
        Input = input,
        Seed = 1
    };

    private static async Task AssertMembershipAsync(
        WorldStore world, string entityId, string definitionId, string field, string[] expected, string locator)
    {
        using var document = JsonDocument.Parse(await ComponentAsync(world, entityId, definitionId));
        var root = document.RootElement;
        Assert.Equal(expected, root.GetProperty(field).EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(SourceId, root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(locator, root.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(2, root.EnumerateObject().Count());
    }

    private static async Task<string> ComponentAsync(WorldStore world, string entityId, string definitionId) =>
        Assert.Single((await world.GetEntityAsync(entityId))!.Components,
            component => component.DefinitionId == definitionId).Data;

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(
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
