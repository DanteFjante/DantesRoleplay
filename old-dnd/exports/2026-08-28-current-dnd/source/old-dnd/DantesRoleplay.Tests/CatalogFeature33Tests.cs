using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature33Tests : IDisposable
{
    private const string Policy = "dnd2024.rest-policy", Entity = "content.dnd2024.rest-policy.standard.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-33-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Standard_rest_policy_imports_as_immutable_source_cited_data_with_no_rest_runtime()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.rest-policy"));
        Assert.NotNull(await world.GetEntityAsync(Entity));

        using var data = JsonDocument.Parse(Assert.Single(Assert.Single(contents.Entities, entity => entity.Id == Entity).Components, component => component.DefinitionId == Policy).Data);
        Assert.Equal("standard", data.RootElement.GetProperty("policyKey").GetString());
        Assert.Equal(1, data.RootElement.GetProperty("policyVersion").GetInt32());
        Assert.Equal(60, data.RootElement.GetProperty("shortRest").GetProperty("minimumMinutes").GetInt32());
        Assert.Equal(480, data.RootElement.GetProperty("longRest").GetProperty("minimumMinutes").GetInt32());
        Assert.Equal(360, data.RootElement.GetProperty("longRest").GetProperty("minimumSleepMinutes").GetInt32());
        Assert.Equal(960, data.RootElement.GetProperty("longRest").GetProperty("restartWaitMinutes").GetInt32());
        Assert.Equal(new[] { "initiative", "non-cantrip-spell", "damage" }, data.RootElement.GetProperty("shortRest").GetProperty("interruptions").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "initiative", "non-cantrip-spell", "damage", "walking-or-physical-exertion" }, data.RootElement.GetProperty("longRest").GetProperty("interruptions").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Single(Assert.Single(contents.Entities, entity => entity.Id == Entity).Components, component => component.DefinitionId == Policy);
    }

    [Fact]
    public async Task Rest_policy_schema_rejects_mutable_or_unordered_runtime_data()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Policy).Schema);
        using var valid = Data(contents);
        using var wrongShortDuration = JsonDocument.Parse(valid.RootElement.GetRawText().Replace("\"minimumMinutes\":60", "\"minimumMinutes\":59", StringComparison.Ordinal));
        using var reorderedInterruptions = JsonDocument.Parse(valid.RootElement.GetRawText().Replace("[\"initiative\",\"non-cantrip-spell\",\"damage\"]", "[\"damage\",\"initiative\",\"non-cantrip-spell\"]", StringComparison.Ordinal));
        using var actorState = JsonDocument.Parse(valid.RootElement.GetRawText()[..^1] + ",\"elapsedMinutes\":60}");
        using var effect = JsonDocument.Parse(valid.RootElement.GetRawText()[..^1] + ",\"effects\":[]}");
        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(wrongShortDuration.RootElement).IsValid);
        Assert.False(schema.Evaluate(reorderedInterruptions.RootElement).IsValid);
        Assert.False(schema.Evaluate(actorState.RootElement).IsValid);
        Assert.False(schema.Evaluate(effect.RootElement).IsValid);
    }

    private static JsonDocument Data(CatalogContents contents) => JsonDocument.Parse(Assert.Single(Assert.Single(contents.Entities, entity => entity.Id == Entity).Components, component => component.DefinitionId == Policy).Data);
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
