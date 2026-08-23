using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using Json.Schema;

namespace DantesRoleplay.Tests;

/// <summary>Slice 4 deliberately proves immutable feat identity only, not any feat benefit.</summary>
public sealed class CatalogFeature28Slice4Tests : IDisposable
{
    private const string Content = "dnd2024.character.content-definition";
    private const string Profile = "dnd2024.feat-profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-28-slice-4-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Alert_and_savage_attacker_import_as_closed_source_cited_origin_feat_profiles()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        foreach (var (id, key) in new[]
        {
            ("content.dnd2024.feature.alert.v1", "alert"),
            ("content.dnd2024.feature.savage-attacker.v1", "savage-attacker")
        })
        {
            var entity = Assert.Single(contents.Entities, entity => entity.Id == id);
            using var identity = Data(entity, Content);
            using var profile = Data(entity, Profile);
            Assert.Equal("feature", identity.RootElement.GetProperty("kind").GetString());
            Assert.Equal(key, identity.RootElement.GetProperty("contentKey").GetString());
            Assert.Equal(1, identity.RootElement.GetProperty("contentVersion").GetInt32());
            Assert.Equal("active", identity.RootElement.GetProperty("status").GetString());
            Assert.Equal(identity.RootElement.GetProperty("sourceRef").GetRawText(), profile.RootElement.GetProperty("sourceRef").GetRawText());
            Assert.Equal("origin", profile.RootElement.GetProperty("category").GetString());
            Assert.False(profile.RootElement.GetProperty("repeatable").GetBoolean());
            Assert.Equal(new[] { "category", "contentKey", "contentVersion", "repeatable", "sourceRef" }, profile.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.NotNull(await world.GetEntityAsync(id));
        }
    }

    [Fact]
    public async Task Profile_schema_rejects_benefit_or_non_origin_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Profile).Schema);
        using var valid = Data(Assert.Single(contents.Entities, entity => entity.Id == "content.dnd2024.feature.alert.v1"), Profile);
        using var benefit = JsonDocument.Parse("""{"contentKey":"alert","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Feats > Origin Feats > Alert, PDF page 87"},"category":"origin","repeatable":false,"benefit":"initiative"}""");
        using var wrongCategory = JsonDocument.Parse("""{"contentKey":"alert","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Feats > Origin Feats > Alert, PDF page 87"},"category":"general","repeatable":false}""");
        using var wrongPair = JsonDocument.Parse("""{"contentKey":"alert","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Feats > Origin Feats > Savage Attacker, PDF page 87"},"category":"origin","repeatable":false}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(benefit.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongCategory.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongPair.RootElement).IsValid);
    }

    [Fact]
    public async Task Catalog_has_no_feat_selection_or_benefit_mechanic()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        Assert.Contains(contents.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.feat-profile");
        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("feat", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.Components, component => component.Id.Contains("selected-feat", StringComparison.Ordinal));
    }

    private static JsonDocument Data(EntityFile entity, string definition) => JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == definition).Data);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
