using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 26 Slice 1 is intentionally catalog-only. It declares source facts for later character
/// creation without selecting a species or granting any trait behaviour to a creature.
/// </summary>
public sealed class CatalogFeature26Tests : IDisposable
{
    private const string ContentDefinition = "dnd2024.character.content-definition";
    private const string ProfileDefinition = "dnd2024.species-profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-26-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_the_nine_closed_source_cited_species_profiles()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var expected = new Dictionary<string, SpeciesExpectation>(StringComparer.Ordinal)
        {
            ["dragonborn"] = new(84, ["medium"], 30, ["draconic-ancestry", "breath-weapon", "damage-resistance", "darkvision", "draconic-flight"], ["draconic-ancestry"]),
            ["dwarf"] = new(84, ["medium"], 30, ["darkvision", "dwarven-resilience", "dwarven-toughness", "stonecunning"], []),
            ["elf"] = new(84, ["medium"], 30, ["darkvision", "elven-lineage", "fey-ancestry", "keen-senses", "trance"], ["elven-lineage"]),
            ["gnome"] = new(85, ["small"], 30, ["darkvision", "gnomish-cunning", "gnomish-lineage"], ["gnomish-lineage"]),
            ["goliath"] = new(85, ["medium"], 35, ["giant-ancestry", "large-form", "powerful-build"], ["giant-ancestry"]),
            ["halfling"] = new(86, ["small"], 30, ["brave", "halfling-nimbleness", "luck", "naturally-stealthy"], []),
            ["human"] = new(86, ["small", "medium"], 30, ["resourceful", "skillful", "versatile"], []),
            ["orc"] = new(86, ["medium"], 30, ["adrenaline-rush", "darkvision", "relentless-endurance", "powerful-build"], []),
            ["tiefling"] = new(86, ["small", "medium"], 30, ["darkvision", "fiendish-legacy", "otherworldly-presence"], ["fiendish-legacy"])
        };

        var species = contents.Entities
            .Where(entity => entity.Id.StartsWith("content.dnd2024.species.", StringComparison.Ordinal))
            .ToDictionary(entity => entity.Id.Split('.')[3], StringComparer.Ordinal);
        Assert.Equal(expected.Count, species.Count);

        foreach (var (key, expectation) in expected)
        {
            var entity = species[key];
            using var identity = ComponentData(entity, ContentDefinition);
            using var profile = ComponentData(entity, ProfileDefinition);
            var identityRoot = identity.RootElement;
            var root = profile.RootElement;

            Assert.Equal("species", identityRoot.GetProperty("kind").GetString());
            Assert.Equal(key, identityRoot.GetProperty("contentKey").GetString());
            Assert.Equal(1, identityRoot.GetProperty("contentVersion").GetInt32());
            Assert.Equal("active", identityRoot.GetProperty("status").GetString());
            Assert.Equal(key, root.GetProperty("contentKey").GetString());
            Assert.Equal(1, root.GetProperty("contentVersion").GetInt32());
            Assert.Equal("humanoid", root.GetProperty("creatureType").GetString());
            Assert.EndsWith($"PDF page {expectation.Page}", identityRoot.GetProperty("sourceRef").GetProperty("locator").GetString());
            Assert.Equal(identityRoot.GetProperty("sourceRef").GetRawText(), root.GetProperty("sourceRef").GetRawText());
            Assert.Equal(expectation.Sizes, Strings(root.GetProperty("allowedSizes")));
            Assert.Equal(expectation.Traits, Strings(root.GetProperty("traitKeys")));
            Assert.Equal(expectation.Choices, Strings(root.GetProperty("choiceFamilies")));

            var speed = root.GetProperty("baseSpeed");
            Assert.Equal(expectation.WalkFeet, speed.GetProperty("walkFeet").GetInt32());
            Assert.Equal(0, speed.GetProperty("burrowFeet").GetInt32());
            Assert.Equal(0, speed.GetProperty("climbFeet").GetInt32());
            Assert.Equal(0, speed.GetProperty("flyFeet").GetInt32());
            Assert.Equal(0, speed.GetProperty("swimFeet").GetInt32());
        }

        var world = new WorldStore(db);
        foreach (var key in expected.Keys)
        {
            var entity = await world.GetEntityAsync($"content.dnd2024.species.{key}.v1");
            Assert.NotNull(entity);
            Assert.Equal(2, entity!.Components.Count(component => component.DefinitionId is ContentDefinition or ProfileDefinition));
        }
    }

    [Fact]
    public async Task Profile_schema_rejects_noncanonical_or_executable_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == ProfileDefinition).Schema);

        using var valid = ComponentData(Assert.Single(contents.Entities, entity => entity.Id == "content.dnd2024.species.dragonborn.v1"), ProfileDefinition);
        using var reorderedTraits = JsonDocument.Parse("""{"contentKey":"dragonborn","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Origins > Character Species > Dragonborn, PDF page 84"},"creatureType":"humanoid","allowedSizes":["medium"],"baseSpeed":{"walkFeet":30,"burrowFeet":0,"climbFeet":0,"flyFeet":0,"swimFeet":0},"traitKeys":["darkvision","draconic-ancestry","breath-weapon","damage-resistance","draconic-flight"],"choiceFamilies":["draconic-ancestry"]}""");
        using var badSpeed = JsonDocument.Parse("""{"contentKey":"dragonborn","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Origins > Character Species > Dragonborn, PDF page 84"},"creatureType":"humanoid","allowedSizes":["medium"],"baseSpeed":{"walkFeet":31,"burrowFeet":0,"climbFeet":0,"flyFeet":0,"swimFeet":0},"traitKeys":["draconic-ancestry","breath-weapon","damage-resistance","darkvision","draconic-flight"],"choiceFamilies":["draconic-ancestry"]}""");
        using var selectedAncestry = JsonDocument.Parse("""{"contentKey":"dragonborn","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Origins > Character Species > Dragonborn, PDF page 84"},"creatureType":"humanoid","allowedSizes":["medium"],"baseSpeed":{"walkFeet":30,"burrowFeet":0,"climbFeet":0,"flyFeet":0,"swimFeet":0},"traitKeys":["draconic-ancestry","breath-weapon","damage-resistance","darkvision","draconic-flight"],"choiceFamilies":["draconic-ancestry"],"selectedAncestry":"red"}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(reorderedTraits.RootElement).IsValid);
        Assert.False(schema.Evaluate(badSpeed.RootElement).IsValid);
        Assert.False(schema.Evaluate(selectedAncestry.RootElement).IsValid);
    }

    [Fact]
    public async Task Profiles_are_static_definitions_with_no_selection_or_effect_mechanic()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("species-profile", StringComparison.Ordinal));
        Assert.Contains(contents.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.species-profile");

        foreach (var entity in contents.Entities.Where(entity => entity.Components.Any(component => component.DefinitionId == ProfileDefinition)))
        {
            using var profile = ComponentData(entity, ProfileDefinition);
            var propertyNames = profile.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(["contentKey", "contentVersion", "sourceRef", "creatureType", "allowedSizes", "baseSpeed", "traitKeys", "choiceFamilies"], propertyNames);
        }
    }

    private static JsonDocument ComponentData(EntityFile entity, string definitionId) =>
        JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == definitionId).Data);
    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()!).ToArray();
    private sealed record SpeciesExpectation(int Page, string[] Sizes, int WalkFeet, string[] Traits, string[] Choices);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
