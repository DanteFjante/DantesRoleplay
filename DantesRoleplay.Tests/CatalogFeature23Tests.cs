using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Tests : IDisposable
{
    private const string Definition = "dnd2024.item-definition";
    private const string Source = "source.dnd2024.srd-5.2.1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_source_cited_immutable_item_definitions()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);

        await ImportAsync();

        Assert.Contains(contents.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.item-definition");
        var component = Assert.Single(contents.Components, component => component.Id == Definition);
        Assert.False(string.IsNullOrWhiteSpace(component.Schema));

        var definitions = contents.Entities
            .Where(entity => entity.Id.StartsWith("item.dnd2024.", StringComparison.Ordinal)
                || entity.Id.StartsWith("currency.dnd2024.", StringComparison.Ordinal))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .ToList();

        var originalSeedIds = new[]
        {
            "currency.dnd2024.copper-piece.v1",
            "currency.dnd2024.electrum-piece.v1",
            "currency.dnd2024.gold-piece.v1",
            "currency.dnd2024.platinum-piece.v1",
            "currency.dnd2024.silver-piece.v1",
            "item.dnd2024.backpack.v1",
            "item.dnd2024.dagger.v1",
            "item.dnd2024.hempen-rope-50-foot.v1",
            "item.dnd2024.pouch.v1",
            "item.dnd2024.quiver.v1"
        };
        foreach (var id in originalSeedIds)
            Assert.Contains(definitions, entity => entity.Id == id);

        foreach (var definition in definitions)
            AssertDefinition(definition);

        using var backpack = DefinitionData(definitions.Single(entity => entity.Id == "item.dnd2024.backpack.v1"));
        AssertRational(backpack, "massPounds", 5, 1);
        AssertCapacity(definitions.Single(entity => entity.Id == "item.dnd2024.backpack.v1"), "weightPounds", 30, 1);
        AssertCapacity(definitions.Single(entity => entity.Id == "item.dnd2024.backpack.v1"), "volumeCubicFeet", 1, 1);
        AssertCapacity(definitions.Single(entity => entity.Id == "item.dnd2024.pouch.v1"), "weightPounds", 6, 1);
        AssertCapacity(definitions.Single(entity => entity.Id == "item.dnd2024.pouch.v1"), "volumeCubicFeet", 1, 5);

        using var quiver = DefinitionData(definitions.Single(entity => entity.Id == "item.dnd2024.quiver.v1"));
        Assert.Equal(20, quiver.RootElement.GetProperty("capacity").GetProperty("itemCount").GetInt32());
        Assert.Equal("ammunition", quiver.RootElement.GetProperty("capacity").GetProperty("permittedItemKinds")[0].GetString());

        using var rope = DefinitionData(definitions.Single(entity => entity.Id == "item.dnd2024.hempen-rope-50-foot.v1"));
        AssertRational(rope, "massPounds", 5, 1);
        AssertRational(rope, "lengthFeet", 50, 1);

        using var dagger = DefinitionData(definitions.Single(entity => entity.Id == "item.dnd2024.dagger.v1"));
        Assert.Equal("weapon", dagger.RootElement.GetProperty("kind").GetString());
        Assert.Equal("weapon.dnd2024.dagger", dagger.RootElement.GetProperty("weaponProfileId").GetString());

        foreach (var coin in definitions.Where(entity => entity.Id.StartsWith("currency.", StringComparison.Ordinal)))
        {
            using var data = DefinitionData(coin);
            Assert.Equal("fungible", data.RootElement.GetProperty("stackPolicy").GetString());
            AssertRational(data, "massPounds", 1, 50);
            Assert.Equal(50, data.RootElement.GetProperty("currency").GetProperty("coinsPerPound").GetInt32());
        }
    }

    [Fact]
    public async Task Definition_schema_rejects_missing_weapon_profile_and_invalid_currency()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schemaText = Assert.Single(contents.Components, component => component.Id == Definition).Schema;
        var schema = JsonSchema.FromText(schemaText);

        using var missingWeaponProfile = JsonDocument.Parse("""{"definitionVersion":1,"kind":"weapon","stackPolicy":"separate","massPounds":{"numerator":1,"denominator":1},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}""");
        using var invalidCurrency = JsonDocument.Parse("""{"definitionVersion":1,"kind":"currency","stackPolicy":"separate","massPounds":{"numerator":1,"denominator":50},"currency":{"denomination":"gp","copperValue":100,"coinsPerPound":50},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Currency"}}""");

        Assert.False(schema.Evaluate(missingWeaponProfile.RootElement).IsValid);
        Assert.False(schema.Evaluate(invalidCurrency.RootElement).IsValid);
    }

    private async Task ImportAsync()
    {
        await using var db = _fixture.CreateContext();
        var result = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(result.Aborted);
    }

    private static void AssertDefinition(EntityFile entity)
    {
        using var data = DefinitionData(entity);
        var state = data.RootElement;
        Assert.Equal(1, state.GetProperty("definitionVersion").GetInt32());
        Assert.Contains(state.GetProperty("kind").GetString(), new[] { "adventuring-gear", "weapon", "armor", "shield", "currency" });
        Assert.Contains(state.GetProperty("stackPolicy").GetString(), new[] { "separate", "fungible" });
        AssertSource(state.GetProperty("sourceRef"));
    }

    private static JsonDocument DefinitionData(EntityFile entity) =>
        JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Definition).Data);

    private static void AssertCapacity(EntityFile entity, string field, long numerator, long denominator)
    {
        using var data = DefinitionData(entity);
        AssertRational(data.RootElement.GetProperty("capacity").GetProperty(field), numerator, denominator);
    }

    private static void AssertRational(JsonDocument data, string field, long numerator, long denominator) =>
        AssertRational(data.RootElement.GetProperty(field), numerator, denominator);

    private static void AssertRational(JsonElement value, long numerator, long denominator)
    {
        Assert.Equal(numerator, value.GetProperty("numerator").GetInt64());
        Assert.Equal(denominator, value.GetProperty("denominator").GetInt64());
    }

    private static void AssertSource(JsonElement sourceRef)
    {
        Assert.Equal(Source, sourceRef.GetProperty("sourceId").GetString());
        Assert.StartsWith("Equipment > ", sourceRef.GetProperty("locator").GetString());
    }

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
