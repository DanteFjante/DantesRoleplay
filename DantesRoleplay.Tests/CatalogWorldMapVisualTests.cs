using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogWorldMapVisualTests : IDisposable
{
    private const string Visual = "game.core.world.map.visual";
    private const string AssetKey = "fixture-map-v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-map-visual-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_copy)) Directory.Delete(_copy, true);
    }

    [Fact]
    public async Task Fresh_import_includes_the_closed_visual_owner_and_revised_procedures()
    {
        Copy(Catalog(), _copy);
        var contents = await CatalogReader.ReadAsync(_copy);
        var component = Assert.Single(contents.Components, candidate => candidate.Id == Visual);

        AssertSchema(component.Schema,
            VariantSet("player", "Map of Thalos"),
            SchemaValueStatus.Valid);

        await using var db = _fixture.CreateContext();
        var result = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db),
            new WorldStore(db)).ApplyAsync(_copy, new CatalogImportOptions());

        Assert.False(result.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.location"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.spatial"));
        Assert.Contains("child region may represent",
            contents.Procedures.Single(p => p.Id == "procedure.game.core.world.location").Instructions,
            StringComparison.Ordinal);
        Assert.Contains("exact requested audience variant",
            contents.Procedures.Single(p => p.Id == "procedure.game.core.world.spatial").Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audience_variants_are_exact_and_missing_variants_fail_closed()
    {
        var component = (await CatalogReader.ReadAsync(Catalog())).Components.Single(candidate => candidate.Id == Visual);
        var playerOnly = VariantSet("player", "Map of Thalos");
        var dmOnly = VariantSet("dm", "DM map of Thalos");

        AssertSchema(component.Schema, playerOnly, SchemaValueStatus.Valid);
        AssertSchema(component.Schema, dmOnly, SchemaValueStatus.Valid);
        Assert.Equal(AssetKey, SelectAsset(playerOnly, "player"));
        Assert.Null(SelectAsset(playerOnly, "dm"));
        Assert.Equal(AssetKey, SelectAsset(dmOnly, "dm"));
        Assert.Null(SelectAsset(dmOnly, "player"));
        Assert.Null(SelectAsset(dmOnly, "spectator"));
    }

    [Theory]
    [InlineData("{}")] 
    [InlineData("{\"status\":\"active\",\"variants\":{}}")]
    [InlineData("{\"status\":\"active\",\"variants\":{\"player\":{\"sha256\":\"bad\",\"mimeType\":\"image/png\",\"width\":1,\"height\":1,\"alt\":\"Map\",\"caption\":\"\",\"order\":0,\"provenance\":{\"kind\":\"original\",\"credit\":\"x\",\"source\":\"x\",\"reviewedOn\":\"2026-09-01\",\"version\":1}}}}")]
    [InlineData("{\"status\":\"active\",\"variants\":{\"player\":{\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"mimeType\":\"image/svg+xml\",\"width\":1,\"height\":1,\"alt\":\"Map\",\"caption\":\"\",\"order\":0,\"provenance\":{\"kind\":\"original\",\"credit\":\"x\",\"source\":\"x\",\"reviewedOn\":\"2026-09-01\",\"version\":1}}}}")]
    [InlineData("{\"status\":\"active\",\"variants\":{\"player\":{\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"mimeType\":\"image/png\",\"width\":1,\"height\":1,\"alt\":\"   \",\"caption\":\"\",\"order\":0,\"provenance\":{\"kind\":\"original\",\"credit\":\"x\",\"source\":\"x\",\"reviewedOn\":\"2026-09-01\",\"version\":1}}}}")]
    public async Task Closed_visual_schema_rejects_unsafe_or_malformed_values(string value)
    {
        var schema = (await CatalogReader.ReadAsync(Catalog())).Components.Single(candidate => candidate.Id == Visual).Schema;
        AssertSchema(schema, value, SchemaValueStatus.Invalid);
    }

    [Theory]
    [InlineData("root", "active", "region", "active", "region", true)]
    [InlineData("region", "active", "region", "active", "region", true)]
    [InlineData("region", "active", "settlement", "active", "location", true)]
    [InlineData("settlement", "active", "site", "active", "location", true)]
    [InlineData("settlement", "active", "interior", "active", "location", true)]
    [InlineData("root", "active", "settlement", "active", "location", false)]
    [InlineData("settlement", "active", "region", "active", "region", false)]
    [InlineData("site", "active", "interior", "active", "location", false)]
    [InlineData("region", "draft", "settlement", "active", "location", false)]
    [InlineData("region", "active", "settlement", "draft", "location", false)]
    public void Multi_plane_anchor_scope_uses_active_plane_and_topology_slot(
        string planeKind, string planeStatus, string childKind, string childStatus, string slot, bool expected)
    {
        Assert.Equal(expected, IsValidAnchorScope(planeKind, planeStatus, childKind, childStatus, slot));
    }

    private static void AssertSchema(string schema, string value, SchemaValueStatus expected)
    {
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(expected,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, value).Status);
    }

    private static string? SelectAsset(string value, string audience)
    {
        if (audience is not ("player" or "dm")) return null;
        using var document = System.Text.Json.JsonDocument.Parse(value);
        return document.RootElement.GetProperty("status").GetString() == "active"
            && document.RootElement.GetProperty("variants").TryGetProperty(audience, out var variant)
                ? variant.GetProperty("assetKey").GetString()
                : null;
    }

    private static string VariantSet(string audience, string alt) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "active",
            variants = new Dictionary<string, object>
            {
                [audience] = new
                {
                    assetKey = AssetKey,
                    alt
                }
            }
        });

    private static bool IsValidAnchorScope(string planeKind, string planeStatus,
        string childKind, string childStatus, string slot)
    {
        if (planeStatus != "active" || childStatus != "active" ||
            planeKind is not ("root" or "region" or "settlement") ||
            childKind is not ("region" or "settlement" or "site" or "interior")) return false;
        return planeKind switch
        {
            "root" => childKind == "region" && slot == "region",
            "region" => childKind == "region" ? slot == "region" : slot == "location",
            "settlement" => childKind != "region" && slot == "location",
            _ => false
        };
    }

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
