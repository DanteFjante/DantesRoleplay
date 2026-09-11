using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class ApplicationCandidateReuseSelectionV2ContractTests
{
    [Fact]
    public void Create_requires_complete_owner_closure()
    {
        var error = Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseInputV2.Create(Material(), false));
        Assert.Equal("REUSE_SELECTION_INCOMPLETE", error.Code);
    }

    [Fact]
    public void Frozen_v2_input_excludes_candidate_and_base_generation_fields_and_changes_with_text()
    {
        var material = Material("read one");
        var documents = material.Documents.ToArray();
        var left = ApplicationCandidateReuseInputV2.Create(material with { Documents = documents }, true);
        documents[0] = Material("changed after preparation").Documents[0];
        var right = ApplicationCandidateReuseInputV2.Create(Material("read two"), true);

        Assert.NotEqual(left.SelectionFingerprint, right.SelectionFingerprint);
        Assert.Contains(ApplicationCandidateReuseInputV2.InputDomain, left.ModelInputJson);
        Assert.DoesNotContain("CandidateFingerprint", left.ModelInputJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Base", left.ModelInputJson, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(left.InputFingerprint, right.InputFingerprint);
        Assert.Contains("read one", left.ModelInputJson);
        Assert.DoesNotContain("changed after preparation", left.ModelInputJson);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(left));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateReuseInputV2>("{}"));
    }

    [Fact]
    public void Output_requires_exact_v2_pins_and_complete_alternative_coverage()
    {
        var input = ApplicationCandidateReuseInputV2.Create(Material(), true);
        var valid = JsonSerializer.Serialize(new { format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
            selectionFingerprint = input.SelectionFingerprint, inputFingerprint = input.InputFingerprint, manualResultFingerprint = input.ManualResultFingerprint,
            judgment = "justifiedNew", reason = "Distinct retained purpose.", assessments = new[] { new { target = new { definitionId = input.Alternatives[0].DefinitionId, kind = input.Alternatives[0].Kind, revision = input.Alternatives[0].Revision, contentFingerprint = input.Alternatives[0].ContentFingerprint }, judgment = "justifiedNew", reason = "Not reusable." } } });
        Assert.Equal(ApplicationCandidateReuseJudgment.JustifiedNew, ApplicationCandidateReuseJudgmentOutputV2.Parse(valid, input).Judgment);
        Assert.Throws<JsonException>(() => ApplicationCandidateReuseJudgmentOutputV2.Parse(valid.Replace("selectionFingerprint", "candidateFingerprint"), input));
        foreach (var pin in new[] { "selectionFingerprint", "inputFingerprint", "manualResultFingerprint", "format" })
        {
            var stale = JsonNode.Parse(valid)!.AsObject();
            stale[pin] = "stale";
            Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutputV2.Parse(stale.ToJsonString(), input));
        }
        var missing = JsonNode.Parse(valid)!.AsObject(); missing["assessments"] = new JsonArray();
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutputV2.Parse(missing.ToJsonString(), input));
    }

    [Theory]
    [InlineData(true, "justifiedNew", "justifiedNew", true)]
    [InlineData(true, "uncertain", "uncertain", false)]
    [InlineData(true, "reuseExisting", "reuseExisting", false)]
    [InlineData(true, "justifiedNew", "uncertain", false)]
    [InlineData(false, "extendExisting", "extendExisting", true)]
    [InlineData(false, "extendExisting", "uncertain", false)]
    public void Reviewed_publication_requires_an_explicit_consistent_positive_judgment(
        bool hasNew, string overall, string assessment, bool expected)
    {
        var input = ApplicationCandidateReuseInputV2.Create(Material(), true);
        var json = JsonSerializer.Serialize(new
        {
            format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
            selectionFingerprint = input.SelectionFingerprint,
            inputFingerprint = input.InputFingerprint,
            manualResultFingerprint = input.ManualResultFingerprint,
            judgment = overall,
            reason = "Bounded fixture judgment.",
            assessments = input.Alternatives.Select(target => new
            { target, judgment = assessment, reason = "Exact alternative assessment." })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var parsed = ApplicationCandidateReuseJudgmentOutputV2.Parse(json, input);

        Assert.Equal(expected, ApplicationCandidateReviewedPureUpdateReader.JudgmentSupports(hasNew, parsed));
    }

    [Fact]
    public void Aggregate_oversize_rejects_without_truncation()
    {
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseInputV2.Create(Material(new string('x', 64_001)), true));
    }

    [Fact]
    public void Output_schema_is_host_bound_and_strict()
    {
        var validator = new BoundedJsonSchemaValidator();
        foreach (var material in new[] { Material(), Material() with { Alternatives = [] } })
        {
            var input = ApplicationCandidateReuseInputV2.Create(material, true);
            var schema = validator.Compile(ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input));
            Assert.True(schema.IsAccepted, string.Join(";", schema.Diagnostics));
            var output = JsonSerializer.SerializeToNode(new
            {
                format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
                selectionFingerprint = input.SelectionFingerprint,
                inputFingerprint = input.InputFingerprint,
                manualResultFingerprint = input.ManualResultFingerprint,
                judgment = "uncertain", reason = "Needs review.",
                assessments = input.Alternatives.Select(target => new { target, judgment = "uncertain", reason = "Needs review." })
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
            Assert.Equal(SchemaValueStatus.Valid, validator.Validate(schema.NormalizedSchema, output.ToJsonString()).Status);
            _ = ApplicationCandidateReuseJudgmentOutputV2.Parse(output.ToJsonString(), input);
            foreach (var field in new[] { "selectionFingerprint", "inputFingerprint", "manualResultFingerprint", "format", "evidenceReference" })
            {
                var changed = output.DeepClone().AsObject();
                changed[field] = "untrusted";
                Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(schema.NormalizedSchema, changed.ToJsonString()).Status);
            }
            var coverage = output.DeepClone().AsObject();
            if (input.Alternatives.Count > 0) coverage["assessments"] = new JsonArray();
            else coverage["assessments"]!.AsArray().Add(new JsonObject());
            Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(schema.NormalizedSchema, coverage.ToJsonString()).Status);
            if (input.Alternatives.Count > 0)
            {
                foreach (var invalidId in new[] { "", new string('x', 201) })
                {
                    var changed = output.DeepClone().AsObject();
                    changed["assessments"]![0]!["target"]!["definitionId"] = invalidId;
                    Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(schema.NormalizedSchema, changed.ToJsonString()).Status);
                }
                foreach (var invalidRevision in new[] { 0L, (long)int.MaxValue + 1 })
                {
                    var changed = output.DeepClone().AsObject();
                    changed["assessments"]![0]!["target"]!["revision"] = invalidRevision;
                    Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(schema.NormalizedSchema, changed.ToJsonString()).Status);
                }
            }
        }
    }

    private static ApplicationCandidateReuseMaterialV2 Material(string text = "read")
    {
        var hash = Hash(text);
        var target = new StandingGrantDefinitionReference("sample-app.procedure.new", "procedure", 1, hash);
        var contract = "{\"id\":\"sample-app.query.old\"}"; var alternative = new StandingGrantDefinitionReference("sample-app.query.old", "query", 1, Hash(contract));
        var manual = new JsonObject { ["resultFingerprint"] = new string('0', 64) };
        manual["resultFingerprint"] = Hash(InteractionCanonicalJson.CanonicalizeObject(manual.ToJsonString()));
        using var parsed = JsonDocument.Parse(manual.ToJsonString());
        return new("sample-app", "new purpose", [new(target, ApplicationCandidateReviewDocumentRole.Changed,
            "file:content/procedures/new.md", "content/procedures/new.md", "text/markdown", hash, text)], parsed.RootElement.Clone(), [new(alternative, contract)]);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
