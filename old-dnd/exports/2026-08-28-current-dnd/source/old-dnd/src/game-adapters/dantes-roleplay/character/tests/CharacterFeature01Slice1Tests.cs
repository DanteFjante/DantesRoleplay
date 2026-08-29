using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CharacterFeature01Slice1Tests : IDisposable
{
    private const string Definition = "dnd2024.character.content-definition";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-feature-01-slice-1-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Ratified_content_definitions_import_with_immutable_source_identity()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);

        foreach (var expected in new[]
        {
            ("content.dnd2024.species.human.v1", "species", "human", "PDF page 86"),
            ("content.dnd2024.background.soldier.v1", "background", "soldier", "PDF page 83"),
            ("content.dnd2024.class.fighter.v1", "class", "fighter", "PDF pages 47–48")
        })
        {
            var entity = await world.GetEntityAsync(expected.Item1);
            Assert.NotNull(entity);
            using var data = JsonDocument.Parse(Assert.Single(entity!.Components, component => component.DefinitionId == Definition).Data);
            Assert.Equal(expected.Item2, data.RootElement.GetProperty("kind").GetString());
            Assert.Equal(expected.Item3, data.RootElement.GetProperty("contentKey").GetString());
            Assert.Equal(1, data.RootElement.GetProperty("contentVersion").GetInt32());
            Assert.Equal("active", data.RootElement.GetProperty("status").GetString());
            var source = data.RootElement.GetProperty("sourceRef");
            Assert.Equal("source.dnd2024.srd-5.2.1", source.GetProperty("sourceId").GetString());
            Assert.Contains(expected.Item4, source.GetProperty("locator").GetString());
        }

        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.character-content-definition.record"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.character-content-definition"));
    }

    [Fact]
    public async Task Content_definition_recorder_is_write_once_and_refuses_invalid_source_or_identity()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Fixture choice set", "fixture.character.choice-set.v1");
        await world.CreateEntityAsync("Imposter source", "fixture.character.imposter-source");
        await world.SetComponentAsync("fixture.character.imposter-source", "dnd2024.source", SourceData());
        var runner = Runner(db, world, mechanics);

        var recorded = await Run(runner, "administratively record character content definition", """{"kind":"choice-set","contentKey":"fighter-level-one","contentVersion":1,"status":"active","locator":"Classes > Fighter, PDF pages 47–48"}""",
            ("content", "fixture.character.choice-set.v1"), ("source", "source.dnd2024.srd-5.2.1"));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        await AssertDefinitionAsync(world, "fixture.character.choice-set.v1", "choice-set", "fighter-level-one", 1, "active");

        var duplicate = await Run(runner, "administratively record character content definition", """{"kind":"choice-set","contentKey":"fighter-level-one","contentVersion":2,"status":"archived","locator":"Classes > Fighter, PDF pages 47–48"}""",
            ("content", "fixture.character.choice-set.v1"), ("source", "source.dnd2024.srd-5.2.1"));
        Assert.False(duplicate.Ok);
        await AssertDefinitionAsync(world, "fixture.character.choice-set.v1", "choice-set", "fighter-level-one", 1, "active");

        await world.CreateEntityAsync("Rejected definition", "fixture.character.rejected.v1");
        var badSource = await Run(runner, "administratively record character content definition", """{"kind":"feature","contentKey":"second-wind","contentVersion":1,"status":"active","locator":"Classes > Fighter, PDF pages 47–48"}""",
            ("content", "fixture.character.rejected.v1"), ("source", "fixture.character.imposter-source"));
        Assert.False(badSource.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync("fixture.character.rejected.v1"))!.Components, component => component.DefinitionId == Definition);

        var invalidKey = await Run(runner, "administratively record character content definition", """{"kind":"feature","contentKey":"Bad Key","contentVersion":1,"status":"active","locator":"Classes > Fighter, PDF pages 47–48"}""",
            ("content", "fixture.character.rejected.v1"), ("source", "source.dnd2024.srd-5.2.1"));
        Assert.False(invalidKey.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync("fixture.character.rejected.v1"))!.Components, component => component.DefinitionId == Definition);
    }

    [Fact]
    public async Task Content_definition_schema_rejects_copied_rules_and_unversioned_identity()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schemaText = Assert.Single(contents.Components, component => component.Id == Definition).Schema;
        var schema = JsonSchema.FromText(schemaText);
        using var copiedRules = JsonDocument.Parse("""{"kind":"class","contentKey":"fighter","contentVersion":1,"status":"active","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Classes > Fighter, PDF pages 47–48"},"grants":["second-wind"]}""");
        using var badKey = JsonDocument.Parse("""{"kind":"class","contentKey":"Fighter","contentVersion":0,"status":"active","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Classes > Fighter, PDF pages 47–48"}}""");
        Assert.False(schema.Evaluate(copiedRules.RootElement).IsValid);
        Assert.False(schema.Evaluate(badKey.RootElement).IsValid);
    }

    private static async Task AssertDefinitionAsync(WorldStore world, string id, string kind, string key, int version, string status)
    {
        var entity = await world.GetEntityAsync(id);
        Assert.NotNull(entity);
        using var data = JsonDocument.Parse(Assert.Single(entity!.Components, component => component.DefinitionId == Definition).Data);
        Assert.Equal(kind, data.RootElement.GetProperty("kind").GetString());
        Assert.Equal(key, data.RootElement.GetProperty("contentKey").GetString());
        Assert.Equal(version, data.RootElement.GetProperty("contentVersion").GetInt32());
        Assert.Equal(status, data.RootElement.GetProperty("status").GetString());
    }

    private static string SourceData() => """{"system":"dnd2024","document":"System Reference Document","version":"5.2.1","publisher":"Wizards of the Coast LLC","canonicalUrl":"https://www.dndbeyond.com/srd","documentUrl":"https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf","publishedOn":"2025-05-01","license":{"id":"CC-BY-4.0","url":"https://creativecommons.org/licenses/by/4.0/legalcode","attribution":"This work includes material from the System Reference Document 5.2 (“SRD 5.2”) by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode."},"locatorFormat":"section heading plus PDF page(s) when stable"}""";
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 1, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
