using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationCandidatePureMechanicClassifierTests
{
    private const string InputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"count\":{\"type\":\"integer\"}}}";
    private readonly BoundedJsonSchemaValidator schemas = new();

    [Fact]
    public void Empty_requirements_preserve_exact_selection_and_opaque_source()
    {
        var record = Record("{}", "not valid JavaScript and deliberately not parsed");

        var result = Classifier().Classify(record);

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Supported, result.Outcome);
        Assert.Null(result.Diagnostic);
        var plan = Assert.IsType<ApplicationCandidatePureMechanicPlan>(result.Plan);
        Assert.Equal(record.Summary.QualifiedId, plan.Definition.DefinitionId);
        Assert.Equal("mechanic", plan.Definition.Kind);
        Assert.Equal(record.Summary.Version, plan.Definition.Revision);
        Assert.Equal(record.Summary.ContentFingerprint, plan.Definition.ContentFingerprint);
        Assert.Equal("not valid JavaScript and deliberately not parsed", plan.Source);
        Assert.Null(plan.NormalizedInputSchema);
        Assert.Null(plan.InputSchemaHash);
        Assert.Equal(64, plan.ClassificationFingerprint.Length);
    }

    [Fact]
    public void Input_schema_uses_the_existing_owners_normalization_and_hash()
    {
        var expected = schemas.Compile(InputSchema);
        var record = Record("{\"INPUTSCHEMA\":" + InputSchema + "}");

        var result = Classifier().Classify(record);

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Supported, result.Outcome);
        var plan = Assert.IsType<ApplicationCandidatePureMechanicPlan>(result.Plan);
        Assert.Equal(expected.NormalizedSchema, plan.NormalizedInputSchema);
        Assert.Equal(expected.SchemaHash, plan.InputSchemaHash);
    }

    [Fact]
    public void Accepted_near_limit_escaped_schema_does_not_overflow_classification_identity()
    {
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            description = new string('"', 10_500)
        });
        var expected = schemas.Compile(schema);
        Assert.True(expected.IsAccepted);

        var result = Classifier().Classify(Record("{\"inputSchema\":" + schema + "}"));

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Supported, result.Outcome);
        var plan = Assert.IsType<ApplicationCandidatePureMechanicPlan>(result.Plan);
        Assert.Equal(expected.NormalizedSchema, plan.NormalizedInputSchema);
        Assert.Equal(expected.SchemaHash, plan.InputSchemaHash);
        Assert.Equal(64, plan.ClassificationFingerprint.Length);
    }

    [Fact]
    public void Case_insensitive_duplicate_requirement_fields_are_invalid()
    {
        var record = Record("{\"inputSchema\":" + InputSchema + ",\"InputSchema\":" + InputSchema + "}");

        var result = Classifier().Classify(record);

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Invalid, result.Outcome);
        Assert.Null(result.Plan);
        Assert.NotNull(result.Diagnostic);
    }

    [Theory]
    [InlineData("{\"roles\":{}}")]
    [InlineData("{\"services\":{}}")]
    [InlineData("{\"children\":{}}")]
    [InlineData("{\"effects\":[]}")]
    [InlineData("{\"event\":{}}")]
    [InlineData("{\"futureRequirement\":{}}")]
    public void Any_dependency_or_unknown_requirement_is_unavailable(string requirements)
    {
        var result = Classifier().Classify(Record(requirements));

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal("PURE_MECHANIC_REQUIREMENTS_UNAVAILABLE", result.Diagnostic!.Code);
    }

    [Fact]
    public void Tampered_fingerprint_and_malformed_source_are_invalid()
    {
        var record = Record("{}");
        var tamperedHash = record with
        {
            Summary = record.Summary with { ContentFingerprint = new string('B', 64) }
        };
        var malformedContent = JsonNode.Parse(record.ContentJson)!.AsObject();
        malformedContent["source"] = 42;
        var malformedJson = malformedContent.ToJsonString();
        var malformedSource = record with
        {
            Summary = record.Summary with
            {
                ContentFingerprint = ApplicationCatalogRecordContent.Fingerprint(malformedJson)
            },
            ContentJson = malformedJson
        };

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Invalid,
            Classifier().Classify(tamperedHash).Outcome);
        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Invalid,
            Classifier().Classify(malformedSource).Outcome);
    }

    [Fact]
    public void Unsupported_record_kind_is_unavailable()
    {
        var record = Record("{}");
        record = record with { Summary = record.Summary with { Kind = "query" } };

        var result = Classifier().Classify(record);

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal("PURE_MECHANIC_KIND_UNAVAILABLE", result.Diagnostic!.Code);
    }

    [Fact]
    public void Numeric_undefined_mechanic_status_is_invalid()
    {
        var record = Record("{}");
        var content = JsonNode.Parse(record.ContentJson)!.AsObject();
        content["status"] = "999";
        var json = content.ToJsonString();
        record = record with
        {
            Summary = record.Summary with
            {
                Status = "999",
                ContentFingerprint = ApplicationCatalogRecordContent.Fingerprint(json)
            },
            ContentJson = json
        };

        Assert.Equal(ApplicationCandidatePureMechanicOutcome.Invalid,
            Classifier().Classify(record).Outcome);
    }

    private ApplicationCandidatePureMechanicClassifier Classifier() => new(schemas);

    private static CatalogRecordView Record(string requirements, string source = "return {data:{ok:true}};")
    {
        var file = new MechanicFile("pure", "fixture", "Pure", "Pure fixture.", "",
            requirements, source, "", MechanicStatus.Active);
        var content = ApplicationCatalogRecordContent.MechanicJson(file);
        return new(new("sample-app", "mechanic", "sample-app.pure", "Pure", "Pure fixture.",
            "mechanics", "active", 1, ApplicationCatalogRecordContent.Fingerprint(content),
            "sample-app-catalog", "pure.md"), content);
    }
}
