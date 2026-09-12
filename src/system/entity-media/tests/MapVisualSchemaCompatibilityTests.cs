using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Media.Tests;

public sealed class MapVisualSchemaCompatibilityTests : IDisposable
{
    private const string QualifiedId = "game.core.world.map.visual";
    private const string RetainedRichSchemaHash = "7C443530D2436D1089E5639154DDA0B1122D9F4CAE92F80806891E39DA42A760";
    private const string CombinedSchemaHash = "096846284D7D198FAD231F32CDB301FA7C498836E67D986CE42231F6420E5D1A";
    private const string RetainedRichSchema = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["status","variants"],"properties":{"status":{"type":"string","enum":["draft","active","archived"]},"variants":{"type":"object","additionalProperties":false,"minProperties":1,"properties":{"player":{"$ref":"#/$defs/variant"},"dm":{"$ref":"#/$defs/variant"}}}},"$defs":{"variant":{"type":"object","additionalProperties":false,"required":["sha256","mimeType","width","height","alt","caption","order","provenance"],"properties":{"sha256":{"type":"string","pattern":"^[a-f0-9]{64}$"},"mimeType":{"type":"string","enum":["image/png","image/jpeg","image/webp"]},"width":{"type":"integer","minimum":1,"maximum":10000},"height":{"type":"integer","minimum":1,"maximum":10000},"alt":{"type":"string","minLength":1,"maxLength":500,"pattern":"\\S"},"caption":{"type":"string","maxLength":1000},"order":{"type":"integer","minimum":0,"maximum":10000},"provenance":{"$ref":"#/$defs/provenance"}}},"provenance":{"type":"object","additionalProperties":false,"required":["kind","credit","source","reviewedOn","version"],"properties":{"kind":{"type":"string","enum":["generated","original","commissioned","licensed"]},"credit":{"type":"string","minLength":1,"maxLength":500,"pattern":"\\S"},"source":{"type":"string","minLength":1,"maxLength":500,"pattern":"\\S"},"reviewedOn":{"type":"string","pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"},"version":{"type":"integer","minimum":1,"maximum":1000000}}}}}
        """;
    private const string RichValue = """
        {"status":"active","variants":{"player":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":2000,"height":1500,"alt":"A reviewed player map.","caption":"","order":0,"provenance":{"kind":"generated","credit":"Map team","source":"catalog asset","reviewedOn":"2026-09-12","version":2}}}}
        """;
    private const string LegacyValue = """
        {"status":"active","variants":{"player":{"assetKey":"fixture-map-v1","alt":"A legacy player map."}}}
        """;
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Authored_schema_accepts_disjoint_rich_and_legacy_variants_only()
    {
        var validator = new BoundedJsonSchemaValidator();
        var compiled = validator.Compile(File.ReadAllText(MapVisualSchemaPath()));

        Assert.True(compiled.IsAccepted, string.Join("; ", compiled.Diagnostics));
        AssertStatus(validator, compiled, RichValue, SchemaValueStatus.Valid);
        AssertStatus(validator, compiled, LegacyValue, SchemaValueStatus.Valid);
        AssertStatus(validator, compiled, """
            {"status":"active","variants":{"player":{"assetKey":"fixture-map-v1","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":2000,"height":1500,"alt":"Ambiguous map.","caption":"","order":0,"provenance":{"kind":"generated","credit":"Map team","source":"catalog asset","reviewedOn":"2026-09-12","version":2}}}}
            """, SchemaValueStatus.Invalid);
        AssertStatus(validator, compiled, """
            {"status":"active","variants":{"player":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":2000,"height":1500,"alt":"Incomplete map."}}}
            """, SchemaValueStatus.Invalid);
        AssertStatus(validator, compiled, """
            {"status":"active","variants":{"player":{"assetKey":"fixture-map-v1","alt":"Legacy map.","url":"/untrusted"}}}
            """, SchemaValueStatus.Invalid);
    }

    [Fact]
    public void Registration_appends_the_combined_contract_and_retains_exact_rich_version_one()
    {
        using var db = fixture.CreateContext();
        var owner = ApplicationIdentifier.Parse("game");
        new SqliteApplicationRegistry(db).Register(new(owner, "Game", "Map visual compatibility fixture.", []));
        var validator = new BoundedJsonSchemaValidator();
        var registry = new SqliteComponentTypeRegistry(db, validator);

        var retained = registry.Define(new(owner, QualifiedId, RetainedRichSchema));
        var combined = registry.Define(new(owner, QualifiedId, File.ReadAllText(MapVisualSchemaPath())));

        Assert.Equal(1, retained.Version);
        Assert.Equal(RetainedRichSchemaHash, retained.SchemaHash);
        Assert.Equal(2, combined.Version);
        Assert.Equal(CombinedSchemaHash, combined.SchemaHash);
        Assert.Equal(retained, registry.Get(QualifiedId, 1));
        Assert.Equal(combined, registry.GetLatest(QualifiedId));
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(retained.ProfileId, retained.SchemaJson, RichValue).Status);
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(combined.ProfileId, combined.SchemaJson, RichValue).Status);
        Assert.Equal(SchemaValueStatus.Invalid,
            validator.Validate(retained.ProfileId, retained.SchemaJson, LegacyValue).Status);
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(combined.ProfileId, combined.SchemaJson, LegacyValue).Status);
    }

    private static void AssertStatus(BoundedJsonSchemaValidator validator, SchemaCompilationResult schema,
        string value, SchemaValueStatus expected) => Assert.Equal(expected,
        validator.Validate(schema.ProfileId, schema.NormalizedSchema, value).Status);

    private static string MapVisualSchemaPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog", "components", "game", "core", "world",
                    "map", "visual.schema.json");
        throw new DirectoryNotFoundException();
    }

    public void Dispose() => fixture.Dispose();
}
