using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 29 Slice 1 is static catalog data only. A magic profile cannot create an item instance,
/// attunement, charge balance, action, or effect.
/// </summary>
public sealed class CatalogFeature29Tests : IDisposable
{
    private const string Profile = "dnd2024.magic-item-profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-29-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_three_source_cited_effect_free_magic_item_profiles()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var expected = new Dictionary<string, MagicExpectation>(StringComparer.Ordinal)
        {
            ["potion-of-healing"] = new("potion", "common", false, "consume", "drink", true, "healing", 234),
            ["boots-of-elvenkind"] = new("wondrous-item", "uncommon", false, "worn", "passive-while-worn", false, "stealth", 213),
            ["amulet-of-health"] = new("wondrous-item", "rare", true, "worn", "passive-while-worn", false, "ability-score", 209)
        };

        var profiles = contents.Entities.Where(entity => entity.Components.Any(component => component.DefinitionId == Profile)).ToArray();
        Assert.Equal(3, profiles.Length);
        foreach (var entity in profiles)
        {
            using var data = ProfileData(entity);
            var root = data.RootElement;
            var key = root.GetProperty("itemKey").GetString()!;
            var expectation = expected[key];
            Assert.Equal($"content.dnd2024.magic-item.{key}.v1", entity.Id);
            Assert.Equal(1, root.GetProperty("itemVersion").GetInt32());
            Assert.Equal(expectation.Category, root.GetProperty("magicCategory").GetString());
            Assert.Equal(expectation.Rarity, root.GetProperty("rarity").GetString());
            Assert.Equal(expectation.RequiresAttunement, root.GetProperty("requiresAttunement").GetBoolean());
            Assert.Equal(expectation.UseMode, root.GetProperty("physicalUseMode").GetString());
            Assert.Equal(expectation.Activation, root.GetProperty("activationFamily").GetString());
            Assert.Equal(expectation.Consumable, root.GetProperty("consumable").GetBoolean());
            Assert.Equal("none", root.GetProperty("chargePolicyKind").GetString());
            Assert.Equal([expectation.EffectFamily], root.GetProperty("effectFamilyKeys").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            Assert.EndsWith($"PDF page {expectation.Page}", root.GetProperty("sourceRef").GetProperty("locator").GetString());
        }

        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("magic-item", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Profile_schema_rejects_effects_attunement_state_and_invalid_declarations()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Profile).Schema);
        using var valid = ProfileData(Assert.Single(contents.Entities, entity => entity.Id == "content.dnd2024.magic-item.amulet-of-health.v1"));
        using var falseAttunement = JsonDocument.Parse("""{"itemKey":"amulet-of-health","itemVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Gameplay Toolbox > Magic Items A–Z > Amulet of Health, PDF page 209"},"magicCategory":"wondrous-item","rarity":"rare","requiresAttunement":false,"physicalUseMode":"worn","activationFamily":"passive-while-worn","consumable":false,"chargePolicyKind":"none","effectFamilyKeys":["ability-score"]}""");
        using var attunementState = JsonDocument.Parse("""{"itemKey":"amulet-of-health","itemVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Gameplay Toolbox > Magic Items A–Z > Amulet of Health, PDF page 209"},"magicCategory":"wondrous-item","rarity":"rare","requiresAttunement":true,"physicalUseMode":"worn","activationFamily":"passive-while-worn","consumable":false,"chargePolicyKind":"none","effectFamilyKeys":["ability-score"],"attunedBy":"actor.example"}""");
        using var encodedBenefit = JsonDocument.Parse("""{"itemKey":"potion-of-healing","itemVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Gameplay Toolbox > Magic Items A–Z > Potion of Healing, PDF page 234"},"magicCategory":"potion","rarity":"common","requiresAttunement":false,"physicalUseMode":"consume","activationFamily":"drink","consumable":true,"chargePolicyKind":"none","effectFamilyKeys":["healing"],"healing":7}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(falseAttunement.RootElement).IsValid);
        Assert.False(schema.Evaluate(attunementState.RootElement).IsValid);
        Assert.False(schema.Evaluate(encodedBenefit.RootElement).IsValid);
    }

    private static JsonDocument ProfileData(EntityFile entity) => JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Profile).Data);
    private sealed record MagicExpectation(string Category, string Rarity, bool RequiresAttunement, string UseMode, string Activation, bool Consumable, string EffectFamily, int Page);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
