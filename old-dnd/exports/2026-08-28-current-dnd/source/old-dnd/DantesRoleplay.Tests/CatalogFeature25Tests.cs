using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 25 Slice 1 only adds immutable profile facts. It must not make a weapon held, usable,
/// mastered, or capable of changing an attack or damage result.
/// </summary>
public sealed class CatalogFeature25Tests : IDisposable
{
    private const string Profile = "dnd2024.weapon-profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-25-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_exact_static_property_and_mastery_facts()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        await AssertProfileAsync(contents, "weapon.dnd2024.dagger", ["finesse", "light", "thrown"], "nick", null, (20, 60), null);
        await AssertProfileAsync(contents, "weapon.dnd2024.shortbow", ["ammunition", "two-handed"], "vex", "arrow", null, null);
        await AssertProfileAsync(contents, "weapon.dnd2024.battleaxe", ["versatile"], "topple", null, null, (1, 10, "slashing"));
    }

    [Fact]
    public async Task Profile_schema_rejects_noncanonical_property_shapes_and_executable_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Profile).Schema);

        using var valid = ProfileData(Assert.Single(contents.Entities, entity => entity.Id == "weapon.dnd2024.dagger"));
        using var unordered = JsonDocument.Parse("""{"category":"simple","kind":"melee","attackAbilities":["str","dex"],"damage":{"count":1,"faces":4,"type":"piercing"},"propertyTags":["light","finesse","thrown"],"thrownRangeFeet":{"normal":20,"long":60},"mastery":"nick","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}""");
        using var missingThrownRange = JsonDocument.Parse("""{"category":"simple","kind":"melee","attackAbilities":["str","dex"],"damage":{"count":1,"faces":4,"type":"piercing"},"propertyTags":["finesse","light","thrown"],"mastery":"nick","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}""");
        using var executable = JsonDocument.Parse("""{"category":"simple","kind":"melee","attackAbilities":["str","dex"],"damage":{"count":1,"faces":4,"type":"piercing"},"propertyTags":["finesse","light","thrown"],"thrownRangeFeet":{"normal":20,"long":60},"mastery":"nick","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"},"masteryGranted":true}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.True(schema.Evaluate(unordered.RootElement).IsValid); // The schema permits an array; the writer owns canonical ordering.
        Assert.False(schema.Evaluate(missingThrownRange.RootElement).IsValid);
        Assert.False(schema.Evaluate(executable.RootElement).IsValid);
    }

    private static async Task AssertProfileAsync(CatalogContents contents, string id, string[] tags, string mastery, string? ammunition,
        (int normal, int @long)? thrown, (int count, int faces, string type)? versatile)
    {
        using var document = ProfileData(Assert.Single(contents.Entities, entity => entity.Id == id));
        var root = document.RootElement;
        Assert.Equal(tags, root.GetProperty("propertyTags").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(mastery, root.GetProperty("mastery").GetString());
        Assert.Equal(ammunition, root.TryGetProperty("ammunitionType", out var ammo) ? ammo.GetString() : null);
        if (thrown is null) Assert.False(root.TryGetProperty("thrownRangeFeet", out _));
        else
        {
            Assert.Equal(thrown.Value.normal, root.GetProperty("thrownRangeFeet").GetProperty("normal").GetInt32());
            Assert.Equal(thrown.Value.@long, root.GetProperty("thrownRangeFeet").GetProperty("long").GetInt32());
        }
        if (versatile is null) Assert.False(root.TryGetProperty("versatileDamage", out _));
        else
        {
            var damage = root.GetProperty("versatileDamage");
            Assert.Equal(versatile.Value.count, damage.GetProperty("count").GetInt32());
            Assert.Equal(versatile.Value.faces, damage.GetProperty("faces").GetInt32());
            Assert.Equal(versatile.Value.type, damage.GetProperty("type").GetString());
        }
    }

    private static JsonDocument ProfileData(EntityFile entity) => JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Profile).Data);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
