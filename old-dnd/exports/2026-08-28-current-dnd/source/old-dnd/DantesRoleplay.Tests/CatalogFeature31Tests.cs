using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 31 Slice 1 provides source-cited spell identity only. It cannot create a spell list,
/// resource, casting statistic, casting operation, or spell effect.
/// </summary>
public sealed class CatalogFeature31Tests : IDisposable
{
    private const string Identity = "dnd2024.spell-identity";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-31-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_three_source_cited_effect_free_spell_identities()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var expected = new Dictionary<string, SpellExpectation>(StringComparer.Ordinal)
        {
            ["fire-bolt"] = new(0, 132),
            ["cure-wounds"] = new(1, 121),
            ["dancing-lights"] = new(0, 121)
        };

        var identities = contents.Entities.Where(entity => entity.Components.Any(component => component.DefinitionId == Identity)).ToArray();
        Assert.Equal(3, identities.Length);
        foreach (var entity in identities)
        {
            using var data = IdentityData(entity);
            var root = data.RootElement;
            var key = root.GetProperty("spellKey").GetString()!;
            var expectation = expected[key];
            Assert.Equal($"content.dnd2024.spell.{key}.v1", entity.Id);
            Assert.Equal(1, root.GetProperty("spellVersion").GetInt32());
            Assert.Equal(expectation.Level, root.GetProperty("spellLevel").GetInt32());
            Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            Assert.EndsWith($"PDF page {expectation.Page}", root.GetProperty("sourceRef").GetProperty("locator").GetString());
        }

        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("spell", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Identity_schema_rejects_wrong_level_version_source_and_resolution_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Identity).Schema);
        using var valid = IdentityData(Assert.Single(contents.Entities, entity => entity.Id == "content.dnd2024.spell.fire-bolt.v1"));
        using var wrongLevel = JsonDocument.Parse("""{"spellKey":"fire-bolt","spellVersion":1,"spellLevel":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Fire Bolt, PDF page 132"}}""");
        using var wrongVersion = JsonDocument.Parse("""{"spellKey":"cure-wounds","spellVersion":0,"spellLevel":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Cure Wounds, PDF page 121"}}""");
        using var wrongSource = JsonDocument.Parse("""{"spellKey":"cure-wounds","spellVersion":1,"spellLevel":1,"sourceRef":{"sourceId":"source.unapproved","locator":"Spells > Cure Wounds, PDF page 121"}}""");
        using var encodedEffect = JsonDocument.Parse("""{"spellKey":"cure-wounds","spellVersion":1,"spellLevel":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Cure Wounds, PDF page 121"},"healing":8}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongLevel.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongVersion.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongSource.RootElement).IsValid);
        Assert.False(schema.Evaluate(encodedEffect.RootElement).IsValid);
    }

    private static JsonDocument IdentityData(EntityFile entity) => JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Identity).Data);
    private sealed record SpellExpectation(int Level, int Page);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
