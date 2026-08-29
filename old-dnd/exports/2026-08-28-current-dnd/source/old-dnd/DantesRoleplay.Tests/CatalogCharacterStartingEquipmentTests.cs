using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

public sealed class CatalogCharacterStartingEquipmentTests : IDisposable
{
    private const string Profile = "dnd2024.weapon-profile";
    private const string Definition = "dnd2024.item-definition";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-starting-equipment-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_source_cited_fighter_package_weapon_profiles_and_definitions()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);

        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        AssertWeapon(contents, "weapon.dnd2024.greatsword", "martial", 2, 6, "slashing", ["heavy", "two-handed"], "graze");
        AssertWeapon(contents, "weapon.dnd2024.flail", "martial", 1, 8, "bludgeoning", [], "sap");
        AssertWeapon(contents, "weapon.dnd2024.javelin", "simple", 1, 6, "piercing", ["thrown"], "slow", 30, 120);

        AssertDefinition(contents, "item.dnd2024.greatsword.v1", "weapon.dnd2024.greatsword", 6);
        AssertDefinition(contents, "item.dnd2024.flail.v1", "weapon.dnd2024.flail", 2);
        AssertDefinition(contents, "item.dnd2024.javelin.v1", "weapon.dnd2024.javelin", 2);
    }

    [Fact]
    public async Task Imported_catalog_has_static_dungeoneers_pack_content_definitions()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);

        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        AssertGearDefinition(contents, "item.dnd2024.caltrops-bag.v1", "separate", 2);
        AssertGearDefinition(contents, "item.dnd2024.crowbar.v1", "separate", 5);
        AssertGearDefinition(contents, "item.dnd2024.oil-flask.v1", "fungible", 1);
        AssertGearDefinition(contents, "item.dnd2024.rations-one-day.v1", "fungible", 2);
        AssertGearDefinition(contents, "item.dnd2024.tinderbox.v1", "separate", 1);
        AssertGearDefinition(contents, "item.dnd2024.torch.v1", "fungible", 1);
        AssertGearDefinition(contents, "item.dnd2024.waterskin.v1", "separate", 5);
    }

    private static void AssertWeapon(
        CatalogContents contents,
        string id,
        string category,
        int count,
        int faces,
        string damageType,
        string[] tags,
        string mastery,
        int? thrownNormal = null,
        int? thrownLong = null)
    {
        var entity = Assert.Single(contents.Entities, entity => entity.Id == id);
        using var data = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Profile).Data);
        var root = data.RootElement;
        Assert.Equal(category, root.GetProperty("category").GetString());
        Assert.Equal("melee", root.GetProperty("kind").GetString());
        Assert.Equal(new[] { "str" }, root.GetProperty("attackAbilities").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(count, root.GetProperty("damage").GetProperty("count").GetInt32());
        Assert.Equal(faces, root.GetProperty("damage").GetProperty("faces").GetInt32());
        Assert.Equal(damageType, root.GetProperty("damage").GetProperty("type").GetString());
        Assert.Equal(tags, root.GetProperty("propertyTags").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(mastery, root.GetProperty("mastery").GetString());
        Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Equipment > Weapons", root.GetProperty("sourceRef").GetProperty("locator").GetString());

        if (thrownNormal is null)
        {
            Assert.False(root.TryGetProperty("thrownRangeFeet", out _));
            return;
        }

        Assert.Equal(thrownNormal.Value, root.GetProperty("thrownRangeFeet").GetProperty("normal").GetInt32());
        Assert.Equal(thrownLong!.Value, root.GetProperty("thrownRangeFeet").GetProperty("long").GetInt32());
    }

    private static void AssertDefinition(CatalogContents contents, string id, string profileId, int pounds)
    {
        var entity = Assert.Single(contents.Entities, entity => entity.Id == id);
        using var data = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Definition).Data);
        var root = data.RootElement;
        Assert.Equal(1, root.GetProperty("definitionVersion").GetInt32());
        Assert.Equal("weapon", root.GetProperty("kind").GetString());
        Assert.Equal("separate", root.GetProperty("stackPolicy").GetString());
        Assert.Equal(pounds, root.GetProperty("massPounds").GetProperty("numerator").GetInt32());
        Assert.Equal(1, root.GetProperty("massPounds").GetProperty("denominator").GetInt32());
        Assert.Equal(profileId, root.GetProperty("weaponProfileId").GetString());
        Assert.Equal(new[] { "held" }, root.GetProperty("equipmentModes").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Equipment > Weapons", root.GetProperty("sourceRef").GetProperty("locator").GetString());
    }

    private static void AssertGearDefinition(CatalogContents contents, string id, string stackPolicy, int pounds)
    {
        var entity = Assert.Single(contents.Entities, entity => entity.Id == id);
        using var data = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Definition).Data);
        var root = data.RootElement;
        Assert.Equal(1, root.GetProperty("definitionVersion").GetInt32());
        Assert.Equal("adventuring-gear", root.GetProperty("kind").GetString());
        Assert.Equal(stackPolicy, root.GetProperty("stackPolicy").GetString());
        Assert.Equal(pounds, root.GetProperty("massPounds").GetProperty("numerator").GetInt32());
        Assert.Equal(1, root.GetProperty("massPounds").GetProperty("denominator").GetInt32());
        Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Equipment > Adventuring Gear", root.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.False(root.TryGetProperty("capacity", out _));
        Assert.False(root.TryGetProperty("equipmentModes", out _));
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
