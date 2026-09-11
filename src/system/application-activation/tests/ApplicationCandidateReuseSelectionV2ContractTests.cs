using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

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

    [Fact]
    public void Aggregate_oversize_rejects_without_truncation()
    {
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseInputV2.Create(Material(new string('x', 64_001)), true));
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
