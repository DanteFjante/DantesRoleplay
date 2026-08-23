using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 24 Slice 1 records the immutable SRD Armor table only. It must not create an item
/// instance or imply equipped state, Armor Class, armor training, movement, or action behavior.
/// </summary>
public sealed class CatalogFeature24Tests : IDisposable
{
    private const string Definition = "dnd2024.item-definition";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-24-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_closed_source_cited_armor_and_shield_definitions()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var expected = new Dictionary<string, ArmorExpectation>(StringComparer.Ordinal)
        {
            ["item.dnd2024.padded-armor.v1"] = new("armor", "light", 11, "full", null, true, 8, "worn", 1, 1),
            ["item.dnd2024.leather-armor.v1"] = new("armor", "light", 11, "full", null, false, 10, "worn", 1, 1),
            ["item.dnd2024.studded-leather-armor.v1"] = new("armor", "light", 12, "full", null, false, 13, "worn", 1, 1),
            ["item.dnd2024.hide-armor.v1"] = new("armor", "medium", 12, "max-2", null, false, 12, "worn", 5, 1),
            ["item.dnd2024.chain-shirt.v1"] = new("armor", "medium", 13, "max-2", null, false, 20, "worn", 5, 1),
            ["item.dnd2024.scale-mail.v1"] = new("armor", "medium", 14, "max-2", null, true, 45, "worn", 5, 1),
            ["item.dnd2024.breastplate.v1"] = new("armor", "medium", 14, "max-2", null, false, 20, "worn", 5, 1),
            ["item.dnd2024.half-plate-armor.v1"] = new("armor", "medium", 15, "max-2", null, true, 40, "worn", 5, 1),
            ["item.dnd2024.ring-mail.v1"] = new("armor", "heavy", 14, "none", null, true, 40, "worn", 10, 5),
            ["item.dnd2024.chain-mail.v1"] = new("armor", "heavy", 16, "none", 13, true, 55, "worn", 10, 5),
            ["item.dnd2024.splint-armor.v1"] = new("armor", "heavy", 17, "none", 15, true, 60, "worn", 10, 5),
            ["item.dnd2024.plate-armor.v1"] = new("armor", "heavy", 18, "none", 15, true, 65, "worn", 10, 5)
        };

        foreach (var (id, expectation) in expected)
        {
            var entity = Assert.Single(contents.Entities, entity => entity.Id == id);
            using var data = DefinitionData(entity);
            var root = data.RootElement;
            Assert.Equal(expectation.Kind, root.GetProperty("kind").GetString());
            Assert.Equal("separate", root.GetProperty("stackPolicy").GetString());
            Assert.Equal(expectation.MassPounds, root.GetProperty("massPounds").GetProperty("numerator").GetInt32());
            Assert.Equal(1, root.GetProperty("massPounds").GetProperty("denominator").GetInt32());
            Assert.Equal(expectation.Mode, root.GetProperty("equipmentModes")[0].GetString());
            var profile = root.GetProperty("armorProfile");
            Assert.Equal(expectation.Category, profile.GetProperty("category").GetString());
            Assert.Equal(expectation.BaseArmorClass, profile.GetProperty("baseArmorClass").GetInt32());
            Assert.Equal(expectation.DexterityRule, profile.GetProperty("dexterityRule").GetString());
            Assert.Equal(expectation.StrengthMinimum, profile.TryGetProperty("strengthMinimum", out var strength) ? strength.GetInt32() : null);
            Assert.Equal(expectation.StealthDisadvantage, profile.GetProperty("stealthDisadvantage").GetBoolean());
            Assert.Equal(expectation.DonMinutes, profile.GetProperty("donDoff").GetProperty("donMinutes").GetInt32());
            Assert.Equal(expectation.DoffMinutes, profile.GetProperty("donDoff").GetProperty("doffMinutes").GetInt32());
            AssertSource(root);
        }

        var shield = Assert.Single(contents.Entities, entity => entity.Id == "item.dnd2024.shield.v1");
        using (var data = DefinitionData(shield))
        {
            var root = data.RootElement;
            Assert.Equal("shield", root.GetProperty("kind").GetString());
            Assert.Equal(6, root.GetProperty("massPounds").GetProperty("numerator").GetInt32());
            Assert.Equal("held", root.GetProperty("equipmentModes")[0].GetString());
            Assert.Equal("shield", root.GetProperty("armorProfile").GetProperty("category").GetString());
            Assert.Equal(2, root.GetProperty("armorProfile").GetProperty("armorClassBonus").GetInt32());
            Assert.Equal("utilize-action", root.GetProperty("armorProfile").GetProperty("donDoff").GetProperty("kind").GetString());
            AssertSource(root);
        }

        using var dagger = DefinitionData(Assert.Single(contents.Entities, entity => entity.Id == "item.dnd2024.dagger.v1"));
        Assert.False(dagger.RootElement.TryGetProperty("armorProfile", out _));
    }

    [Fact]
    public async Task Item_definition_schema_rejects_invalid_armor_and_shield_profiles()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Definition).Schema);
        foreach (var invalid in new[]
                 {
                     """{"definitionVersion":1,"kind":"armor","stackPolicy":"separate","massPounds":{"numerator":8,"denominator":1},"equipmentModes":["worn"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Armor"}}""",
                     """{"definitionVersion":1,"kind":"armor","stackPolicy":"separate","massPounds":{"numerator":8,"denominator":1},"armorProfile":{"category":"light","baseArmorClass":11,"dexterityRule":"max-2","stealthDisadvantage":false,"donDoff":{"donMinutes":1,"doffMinutes":1}},"equipmentModes":["worn"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Armor"}}""",
                     """{"definitionVersion":1,"kind":"armor","stackPolicy":"separate","massPounds":{"numerator":8,"denominator":1},"armorProfile":{"category":"light","baseArmorClass":11,"dexterityRule":"full","stealthDisadvantage":false,"donDoff":{"donMinutes":1,"doffMinutes":1}},"equipmentModes":["held"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Armor"}}""",
                     """{"definitionVersion":1,"kind":"shield","stackPolicy":"separate","massPounds":{"numerator":6,"denominator":1},"armorProfile":{"category":"shield","armorClassBonus":1,"donDoff":{"kind":"utilize-action"}},"equipmentModes":["held"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Armor"}}""",
                     """{"definitionVersion":1,"kind":"weapon","stackPolicy":"separate","massPounds":{"numerator":1,"denominator":1},"weaponProfileId":"weapon.dnd2024.dagger","armorProfile":{"category":"shield","armorClassBonus":2,"donDoff":{"kind":"utilize-action"}},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}"""
                 })
        {
            using var data = JsonDocument.Parse(invalid);
            Assert.False(schema.Evaluate(data.RootElement).IsValid, invalid);
        }
    }

    private static JsonDocument DefinitionData(EntityFile entity) =>
        JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Definition).Data);

    private static void AssertSource(JsonElement root)
    {
        var source = root.GetProperty("sourceRef");
        Assert.Equal("source.dnd2024.srd-5.2.1", source.GetProperty("sourceId").GetString());
        Assert.Equal("Equipment > Armor", source.GetProperty("locator").GetString());
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

    private sealed record ArmorExpectation(string Kind, string Category, int BaseArmorClass,
        string DexterityRule, int? StrengthMinimum, bool StealthDisadvantage, int MassPounds,
        string Mode, int DonMinutes, int DoffMinutes);
}
