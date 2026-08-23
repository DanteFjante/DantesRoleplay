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

public sealed class CatalogFeature36Tests : IDisposable
{
    private const string Experience = "dnd2024.character-experience";
    private const string Level = "dnd2024.character-level";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-36-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_records_and_corrects_only_closed_source_backed_experience()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.character-experience"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.character-experience.write"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.character-experience.read"));
        const string subject = "fixture.catalog.f36.writer";
        await world.CreateEntityAsync("Experience subject", subject);
        var runner = Runner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request("record character experience", subject, ExperienceInput("record", 0)));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        AssertExperience(Component(await world.GetEntityAsync(subject), Experience), 0);

        var corrected = await runner.RunAsync(Request("correct character experience", subject, ExperienceInput("correct", 300)));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertExperience(Component(await world.GetEntityAsync(subject), Experience), 300);
        var before = Component(await world.GetEntityAsync(subject), Experience);

        foreach (var input in new[]
                 {
                     ExperienceInput("record", 300),
                     ExperienceInput("correct", -1),
                     """{"mode":"correct","total":1.5}""",
                     """{"mode":"correct","total":300,"campaignId":"campaign.untrusted"}""",
                     """{"mode":"correct","total":300,"sourceRef":{}}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("correct character experience", subject, input));
            Assert.False(rejected.Ok, input);
            Assert.Equal(before, Component(await world.GetEntityAsync(subject), Experience));
        }

        const string absent = "fixture.catalog.f36.absent";
        await world.CreateEntityAsync("Absent experience", absent);
        Assert.False((await runner.RunAsync(Request("correct character experience", absent, ExperienceInput("correct", 0)))).Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(absent))!.Components, component => component.DefinitionId == Experience);

        await world.SetComponentAsync(subject, Experience, """{"total":300,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"wrong"}}""");
        Assert.False((await runner.RunAsync(Request("correct character experience", subject, ExperienceInput("correct", 301)))).Ok);
    }

    [Fact]
    public async Task Reader_derives_only_exact_next_level_eligibility_and_never_writes()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        var cases = new (int Level, long Total, string Status, int? NextLevel, int? NextThreshold)[]
        {
            (1, 299, "below-next-threshold", 2, 300), (1, 300, "eligible-for-next-level", 2, 300), (1, 301, "eligible-for-next-level", 2, 300),
            (4, 6499, "below-next-threshold", 5, 6500), (4, 6500, "eligible-for-next-level", 5, 6500), (4, 6501, "eligible-for-next-level", 5, 6500),
            (5, 13999, "below-next-threshold", 6, 14000), (5, 14000, "eligible-for-next-level", 6, 14000), (5, 14001, "eligible-for-next-level", 6, 14000),
            (19, 354999, "below-next-threshold", 20, 355000), (19, 355000, "eligible-for-next-level", 20, 355000), (19, 355001, "eligible-for-next-level", 20, 355000),
            (20, 355000, "at-level-cap", null, null), (20, 355001, "at-level-cap", null, null), (20, 9007199254740991, "at-level-cap", null, null)
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var test = cases[index];
            var subject = $"fixture.catalog.f36.eligibility.{index}";
            await world.CreateEntityAsync("Eligibility subject", subject);
            Assert.True((await runner.RunAsync(Request("record character level", subject, JsonSerializer.Serialize(new { level = test.Level })))).Ok);
            Assert.True((await runner.RunAsync(Request("record character experience", subject, ExperienceInput("record", test.Total)))).Ok);
            var beforeExperience = Component(await world.GetEntityAsync(subject), Experience);
            var beforeLevel = Component(await world.GetEntityAsync(subject), Level);

            var read = await runner.RunAsync(Request("read character experience eligibility", subject, "{}"));
            Assert.True(read.Ok, read.Error?.Why);
            Assert.Empty(read.Output!.Effects);
            using var document = JsonDocument.Parse(read.Output.Data);
            var result = document.RootElement;
            Assert.Equal("character-experience-read", result.GetProperty("test").GetString());
            Assert.Equal(test.Status, result.GetProperty("status").GetString());
            Assert.Equal(test.NextLevel, NullableInt(result, "nextLevel"));
            Assert.Equal(test.NextThreshold, NullableInt(result, "nextThreshold"));
            Assert.True(result.GetProperty("experience").GetProperty("valid").GetBoolean());
            Assert.Equal(test.Total, result.GetProperty("experience").GetProperty("total").GetInt64());
            Assert.True(result.GetProperty("characterLevel").GetProperty("valid").GetBoolean());
            Assert.Equal(test.Level, result.GetProperty("characterLevel").GetProperty("totalLevel").GetInt32());
            Assert.Equal(beforeExperience, Component(await world.GetEntityAsync(subject), Experience));
            Assert.Equal(beforeLevel, Component(await world.GetEntityAsync(subject), Level));
        }
    }

    [Fact]
    public async Task Reader_treats_missing_or_invalid_state_as_unknown_without_defaulting()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        const string subject = "fixture.catalog.f36.diagnostics";
        await world.CreateEntityAsync("Diagnostic subject", subject);
        var runner = Runner(db, world, mechanics);

        var absent = await runner.RunAsync(Request("inspect character experience", subject, "{}"));
        Assert.True(absent.Ok, absent.Error?.Why);
        Assert.Empty(absent.Output!.Effects);
        using (var document = JsonDocument.Parse(absent.Output.Data))
        {
            var result = document.RootElement;
            Assert.Equal("unknown", result.GetProperty("status").GetString());
            Assert.Equal("absent", result.GetProperty("experience").GetProperty("problem").GetString());
            Assert.Equal("absent", result.GetProperty("characterLevel").GetProperty("problem").GetString());
        }

        await world.SetComponentAsync(subject, Experience, """{"total":"not-a-number"}""");
        await world.SetComponentAsync(subject, Level, """{"level":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"wrong"}}""");
        var invalid = await runner.RunAsync(Request("read character experience eligibility", subject, "{}"));
        Assert.True(invalid.Ok, invalid.Error?.Why);
        Assert.Empty(invalid.Output!.Effects);
        using var invalidDocument = JsonDocument.Parse(invalid.Output.Data);
        var invalidResult = invalidDocument.RootElement;
        Assert.Equal("unknown", invalidResult.GetProperty("status").GetString());
        Assert.Equal("invalid", invalidResult.GetProperty("experience").GetProperty("problem").GetString());
        Assert.Equal("invalid", invalidResult.GetProperty("characterLevel").GetProperty("problem").GetString());
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

    private static ActionRequest Request(string intent, string subject, string input) => new()
    {
        Intent = intent, Input = input, Seed = 36, RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject }
    };

    private static string ExperienceInput(string mode, long total) => JsonSerializer.Serialize(new { mode, total });

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static int? NullableInt(JsonElement value, string property) =>
        value.GetProperty(property).ValueKind == JsonValueKind.Null ? null : value.GetProperty(property).GetInt32();

    private static void AssertExperience(string data, long total)
    {
        using var document = JsonDocument.Parse(data);
        var experience = document.RootElement;
        Assert.Equal(2, experience.EnumerateObject().Count());
        Assert.Equal(total, experience.GetProperty("total").GetInt64());
        Assert.Equal("source.dnd2024.srd-5.2.1", experience.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Character Creation > Level Advancement", experience.GetProperty("sourceRef").GetProperty("locator").GetString());
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
