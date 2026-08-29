using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.SchemaValidation.Tests;

public sealed class SchemaValidationTests
{
    private readonly BoundedJsonSchemaValidator _validator = new();

    [Theory]
    [InlineData("{\"type\":\"object\"}", "{}", true)]
    [InlineData("{\"type\":\"array\"}", "[]", true)]
    [InlineData("{\"type\":\"string\"}", "\"value\"", true)]
    [InlineData("{\"type\":\"integer\"}", "4", true)]
    [InlineData("{\"type\":\"integer\"}", "4.5", false)]
    [InlineData("{\"type\":\"number\"}", "4.5", true)]
    [InlineData("{\"type\":\"boolean\"}", "true", true)]
    [InlineData("{\"type\":\"null\"}", "null", true)]
    [InlineData("true", "[1,2,3]", true)]
    [InlineData("false", "null", false)]
    public void Every_json_kind_is_evaluated_without_object_coercion(string schema, string value, bool valid)
    {
        var compiled = _validator.Compile(schema);
        Assert.True(compiled.IsAccepted);
        Assert.Equal(valid ? SchemaValueStatus.Valid : SchemaValueStatus.Invalid,
            _validator.Validate(compiled.NormalizedSchema, value).Status);
    }

    [Fact]
    public void Profile_supports_local_fragments_and_whitespace_replay()
    {
        const string compact = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"$defs\":{\"name\":{\"type\":\"string\",\"minLength\":1}},\"$ref\":\"#/$defs/name\"}";
        const string spaced = "  \r\n{ \"$schema\" : \"https://json-schema.org/draft/2020-12/schema\", \"$defs\" : { \"name\" : { \"type\" : \"string\", \"minLength\" : 1 } }, \"$ref\" : \"#/$defs/name\" }  ";

        var first = _validator.Compile(compact);
        var second = _validator.Compile(spaced);
        Assert.True(first.IsAccepted);
        Assert.Equal(first.NormalizedSchema, second.NormalizedSchema);
        Assert.Equal(first.SchemaHash, second.SchemaHash);
        Assert.Equal(SchemaValueStatus.Valid, _validator.Validate(first.NormalizedSchema, "\"Ada\"").Status);
        Assert.Equal(SchemaValueStatus.Invalid, _validator.Validate(first.NormalizedSchema, "\"\"").Status);
    }

    [Fact]
    public void Least_profile_selection_preserves_the_v1_identity_and_hash()
    {
        var legacy = _validator.Compile("{\"type\":\"object\"}");
        var extended = _validator.Compile("{\"type\":\"string\",\"pattern\":\"^[0-9a-f]{64}$\"}");

        Assert.True(legacy.IsAccepted);
        Assert.Equal(SystemJsonSchemaProfile.Version1Id, legacy.ProfileId);
        Assert.Equal("C2C7529D3F9283F0D0D2F1E5E64C28C3300BA89B9AE84F90606F4A3FC54CF51D", legacy.SchemaHash);
        Assert.True(extended.IsAccepted);
        Assert.Equal(SystemJsonSchemaProfile.Version2Id, extended.ProfileId);
        Assert.Equal(SchemaValueStatus.Rejected,
            _validator.Validate(SystemJsonSchemaProfile.Version1Id, extended.NormalizedSchema,
                "\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"").Status);
    }

    [Theory]
    [InlineData("{\"type\":\"string\",\"pattern\":\"^session\\\\.[a-z0-9.-]+$\",\"maxLength\":200}", "\"session.alpha-1\"", true)]
    [InlineData("{\"type\":\"string\",\"pattern\":\"^session\\\\.[a-z0-9.-]+$\",\"maxLength\":200}", "\"Session.alpha\"", false)]
    [InlineData("{\"type\":\"string\",\"pattern\":\"^[0-9a-f]{64}$\"}", "\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"", true)]
    [InlineData("{\"type\":\"string\",\"pattern\":\"^[0-9a-f]{64}$\"}", "\"xyz\"", false)]
    public void V2_bounded_patterns_are_asserted(string schema, string value, bool expected)
    {
        var compiled = _validator.Compile(schema);

        Assert.True(compiled.IsAccepted);
        Assert.Equal(SystemJsonSchemaProfile.Version2Id, compiled.ProfileId);
        Assert.Equal(expected ? SchemaValueStatus.Valid : SchemaValueStatus.Invalid,
            _validator.Validate(compiled.ProfileId, compiled.NormalizedSchema, value).Status);
    }

    [Theory]
    [InlineData("\"2026-08-24T12:34:56Z\"", true)]
    [InlineData("\"2026-08-24T12:34:56.123+02:00\"", true)]
    [InlineData("\"2026-02-30T12:34:56Z\"", false)]
    [InlineData("\"2026-08-24 12:34:56\"", false)]
    [InlineData("\"not-a-date\"", false)]
    public void V2_date_time_format_is_asserted(string value, bool expected)
    {
        var compiled = _validator.Compile("{\"type\":\"string\",\"format\":\"date-time\"}");

        Assert.True(compiled.IsAccepted);
        Assert.Equal(SystemJsonSchemaProfile.Version2Id, compiled.ProfileId);
        Assert.Equal(expected ? SchemaValueStatus.Valid : SchemaValueStatus.Invalid,
            _validator.Validate(compiled.ProfileId, compiled.NormalizedSchema, value).Status);
    }

    [Theory]
    [InlineData("not json", "SCHEMA_JSON")]
    [InlineData("{\"format\":\"date\"}", "SCHEMA_FORMAT")]
    [InlineData("{\"pattern\":\".*\"}", "SCHEMA_PATTERN")]
    [InlineData("{\"pattern\":\"^(a+)+$\",\"maxLength\":100}", "SCHEMA_PATTERN")]
    [InlineData("{\"pattern\":\"^[a-z]+$\"}", "SCHEMA_PATTERN")]
    [InlineData("{\"pattern\":\"^[a-z]+$\",\"maxLength\":1001}", "SCHEMA_PATTERN")]
    [InlineData("{\"$ref\":\"https://example.invalid/schema\"}", "SCHEMA_REFERENCE")]
    [InlineData("{\"$ref\":\"#/missing\"}", "SCHEMA_REFERENCE_MISSING")]
    [InlineData("{\"$schema\":\"https://json-schema.org/draft/2019-09/schema\"}", "SCHEMA_DIALECT")]
    [InlineData("{\"minLength\":\"not-a-number\"}", "SCHEMA_SEMANTICS")]
    public void Profile_rejects_unsupported_or_unsafe_schemas(string schema, string code)
    {
        var result = _validator.Compile(schema);
        Assert.False(result.IsAccepted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Profile_supports_bounded_recursive_local_references_and_conditionals()
    {
        const string schema = """
            {"$defs":{"node":{"type":"object","additionalProperties":false,"required":["kind"],"properties":{"kind":{"enum":["leaf","branch"]},"child":{"anyOf":[{"type":"null"},{"$ref":"#/$defs/node"}]}},"if":{"properties":{"kind":{"const":"branch"}},"required":["kind"]},"then":{"required":["child"]}}},"$ref":"#/$defs/node"}
            """;

        var compiled = _validator.Compile(schema);

        Assert.True(compiled.IsAccepted);
        Assert.Equal(SchemaValueStatus.Valid, _validator.Validate(compiled.NormalizedSchema,
            "{\"kind\":\"branch\",\"child\":{\"kind\":\"leaf\"}}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, _validator.Validate(compiled.NormalizedSchema,
            "{\"kind\":\"branch\"}").Status);
    }

    [Fact]
    public void Schema_resource_limits_are_closed_and_deterministic()
    {
        var oversized = new string(' ', SystemJsonSchemaProfile.MaximumSchemaBytes + 1);
        var deep = new string('[', SystemJsonSchemaProfile.MaximumSchemaDepth + 1)
            + "true" + new string(']', SystemJsonSchemaProfile.MaximumSchemaDepth + 1);
        var nodeHeavy = "{\"properties\":{" + string.Join(',',
            Enumerable.Range(0, SystemJsonSchemaProfile.MaximumSchemaNodes).Select(i => $"\"p{i}\":true")) + "}}";
        var definitions = "{\"$defs\":{" + string.Join(',',
            Enumerable.Range(0, SystemJsonSchemaProfile.MaximumDefinitions + 1).Select(i => $"\"d{i}\":true")) + "}}";
        var references = "{\"$defs\":{\"x\":true},\"allOf\":[" + string.Join(',',
            Enumerable.Repeat("{\"$ref\":\"#/$defs/x\"}", SystemJsonSchemaProfile.MaximumReferences + 1)) + "]}";

        Assert.Contains(_validator.Compile(oversized).Diagnostics, x => x.Code == "SCHEMA_SIZE");
        Assert.Contains(_validator.Compile(deep).Diagnostics, x => x.Code == "SCHEMA_JSON");
        Assert.Contains(_validator.Compile(nodeHeavy).Diagnostics, x => x.Code == "SCHEMA_NODES");
        Assert.Contains(_validator.Compile(definitions).Diagnostics, x => x.Code == "SCHEMA_DEFINITIONS");
        Assert.Contains(_validator.Compile(references).Diagnostics, x => x.Code == "SCHEMA_REFERENCES");
    }

    [Fact]
    public void Value_resource_limits_reject_without_throw_and_diagnostics_are_bounded()
    {
        var oversized = "\"" + new string('x', SystemJsonSchemaProfile.MaximumValueBytes) + "\"";
        var deep = new string('[', SystemJsonSchemaProfile.MaximumValueDepth + 1)
            + "0" + new string(']', SystemJsonSchemaProfile.MaximumValueDepth + 1);
        var nodeHeavy = "[" + string.Join(',', Enumerable.Repeat("0", SystemJsonSchemaProfile.MaximumValueNodes)) + "]";
        var manyFailures = "{\"allOf\":[" + string.Join(',', Enumerable.Range(0, 50).Select(i => $"{{\"const\":{i}}}")) + "]}";

        Assert.Equal(SchemaValueStatus.Rejected, _validator.Validate("true", oversized).Status);
        Assert.Equal(SchemaValueStatus.Rejected, _validator.Validate("true", deep).Status);
        Assert.Equal(SchemaValueStatus.Rejected, _validator.Validate("true", nodeHeavy).Status);
        var invalid = _validator.Validate(manyFailures, "-1");
        Assert.Equal(SchemaValueStatus.Invalid, invalid.Status);
        Assert.InRange(invalid.Diagnostics.Count, 1, SystemJsonSchemaProfile.MaximumDiagnostics);
        Assert.DoesNotContain(invalid.Diagnostics, x => x.Message.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }
}
