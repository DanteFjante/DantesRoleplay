using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 32 Slice 1 declares source resolution interfaces only. Profiles cannot cast, target,
/// spend a resource, create an effect, start concentration, roll, or apply a consequence.
/// </summary>
public sealed class CatalogFeature32Tests : IDisposable
{
    private const string Identity = "dnd2024.spell-identity";
    private const string Profile = "dnd2024.spell-resolution-profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-32-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_has_source_cited_effect_free_profiles_matching_each_identity()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var expected = new Dictionary<string, ProfileExpectation>(StringComparer.Ordinal)
        {
            ["fire-bolt"] = new("ranged-creature-or-object", "instantaneous", false, "spell-attack", "damage", 132),
            ["cure-wounds"] = new("touch-creature", "instantaneous", false, "declared-special", "healing", 121),
            ["dancing-lights"] = new("ranged-point", "concentration-duration", true, "declared-special", "light", 121)
        };

        var spellEntities = contents.Entities.Where(entity => entity.Components.Any(component => component.DefinitionId == Profile)).ToArray();
        Assert.Equal(3, spellEntities.Length);
        foreach (var entity in spellEntities)
        {
            using var identity = ComponentData(entity, Identity);
            using var profile = ComponentData(entity, Profile);
            var identityRoot = identity.RootElement;
            var profileRoot = profile.RootElement;
            var key = profileRoot.GetProperty("spellKey").GetString()!;
            var expectation = expected[key];

            Assert.Equal(identityRoot.GetProperty("spellKey").GetString(), key);
            Assert.Equal(identityRoot.GetProperty("spellVersion").GetInt32(), profileRoot.GetProperty("spellVersion").GetInt32());
            Assert.Equal(identityRoot.GetProperty("sourceRef").GetRawText(), profileRoot.GetProperty("sourceRef").GetRawText());
            Assert.Equal(1, profileRoot.GetProperty("profileVersion").GetInt32());
            Assert.Equal("action", profileRoot.GetProperty("actionFamily").GetString());
            Assert.Equal(expectation.RangeTargetArea, profileRoot.GetProperty("rangeTargetAreaFamily").GetString());
            Assert.Equal(expectation.Duration, profileRoot.GetProperty("durationFamily").GetString());
            Assert.Equal(expectation.RequiresConcentration, profileRoot.GetProperty("requiresConcentration").GetBoolean());
            Assert.Equal(expectation.Resolution, profileRoot.GetProperty("resolutionFamily").GetString());
            Assert.Equal([expectation.Consequence], profileRoot.GetProperty("consequenceFamilyKeys").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.EndsWith($"PDF page {expectation.Page}", profileRoot.GetProperty("sourceRef").GetProperty("locator").GetString());
        }

        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("spell-resolution", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Profile_schema_rejects_mismatches_impossible_lifecycle_and_effect_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Profile).Schema);
        using var valid = ComponentData(Assert.Single(contents.Entities, entity => entity.Id == "content.dnd2024.spell.dancing-lights.v1"), Profile);
        using var noConcentration = JsonDocument.Parse("""{"profileVersion":1,"spellKey":"dancing-lights","spellVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Dancing Lights, PDF page 121"},"actionFamily":"action","rangeTargetAreaFamily":"ranged-point","durationFamily":"concentration-duration","requiresConcentration":false,"resolutionFamily":"declared-special","consequenceFamilyKeys":["light"]}""");
        using var wrongIdentity = JsonDocument.Parse("""{"profileVersion":1,"spellKey":"fire-bolt","spellVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Fire Bolt, PDF page 132"},"actionFamily":"action","rangeTargetAreaFamily":"ranged-point","durationFamily":"instantaneous","requiresConcentration":false,"resolutionFamily":"spell-attack","consequenceFamilyKeys":["damage"]}""");
        using var wrongSource = JsonDocument.Parse("""{"profileVersion":1,"spellKey":"cure-wounds","spellVersion":1,"sourceRef":{"sourceId":"source.unapproved","locator":"Spells > Cure Wounds, PDF page 121"},"actionFamily":"action","rangeTargetAreaFamily":"touch-creature","durationFamily":"instantaneous","requiresConcentration":false,"resolutionFamily":"declared-special","consequenceFamilyKeys":["healing"]}""");
        using var encodedEffect = JsonDocument.Parse("""{"profileVersion":1,"spellKey":"cure-wounds","spellVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Spells > Cure Wounds, PDF page 121"},"actionFamily":"action","rangeTargetAreaFamily":"touch-creature","durationFamily":"instantaneous","requiresConcentration":false,"resolutionFamily":"declared-special","consequenceFamilyKeys":["healing"],"healing":8}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(noConcentration.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongIdentity.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongSource.RootElement).IsValid);
        Assert.False(schema.Evaluate(encodedEffect.RootElement).IsValid);
    }

    private static JsonDocument ComponentData(EntityFile entity, string definitionId) => JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == definitionId).Data);
    private sealed record ProfileExpectation(string RangeTargetArea, string Duration, bool RequiresConcentration, string Resolution, string Consequence, int Page);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
