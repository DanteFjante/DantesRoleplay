using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class ApplicationCandidateReuseJudgmentContractTests
{
    [Fact]
    public void Input_freezes_complete_source_and_reason_and_rejects_transport_construction()
    {
        var candidate = Candidate("exact source");
        var input = Input(candidate);
        candidate.EffectiveDocuments[0].RetainedBytes[0] = 0;
        Assert.Contains("exact source", input.ModelInputJson);
        Assert.Contains("Review this complete reason.", input.ModelInputJson);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(input));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateReuseInput>("{}"));
        Assert.Throws<InteractionContractException>(() => Input(Candidate(new string('x', 65_000))));
    }

    [Fact]
    public void Every_exact_pin_and_alternative_is_required_and_model_evidence_is_rejected()
    {
        var input = Input(Candidate("source"));
        var valid = Output(input);
        Assert.Equal(ApplicationCandidateReuseJudgment.Uncertain,
            ApplicationCandidateReuseJudgmentOutput.Parse(valid.ToJsonString(), input).Judgment);
        foreach (var field in new[] { "inputFingerprint", "candidateFingerprint", "manualPacketResultFingerprint" })
        {
            var changed = Output(input);
            changed[field] = new string('F', 64);
            Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(changed.ToJsonString(), input));
        }
        var missing = Output(input);
        missing["assessments"] = new JsonArray();
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(missing.ToJsonString(), input));
        var duplicate = Output(input);
        duplicate["assessments"]!.AsArray().Add(duplicate["assessments"]![0]!.DeepClone());
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(duplicate.ToJsonString(), input));
        var injected = Output(input);
        injected["evidenceReference"] = "pretend.worker";
        Assert.Throws<JsonException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(injected.ToJsonString(), input));
        var oversized = Output(input);
        oversized["reason"] = new string('x', 8_001);
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(oversized.ToJsonString(), input));
    }

    [Fact]
    public void Changed_exact_bytes_or_manual_hash_cannot_reuse_preparation_pins()
    {
        var a = Input(Candidate("one"));
        var b = Input(Candidate("two"));
        Assert.NotEqual(a.InputFingerprint, b.InputFingerprint);
        Assert.Throws<InteractionContractException>(() => ApplicationCandidateReuseJudgmentOutput.Parse(Output(a).ToJsonString(), b));
        var bad = Candidate("source");
        bad.EffectiveDocuments[0].RetainedBytes[0] = 0;
        Assert.Throws<InteractionContractException>(() => Input(bad));
    }

    private static ApplicationCandidateReuseInput Input(ApplicationCandidateSnapshot candidate)
    {
        const string contract = "{\"id\":\"sample-app.procedure.existing\",\"instructions\":\"Read.\"}";
        var manual = new JsonObject { ["resultFingerprint"] = new string('0', 64), ["candidates"] = new JsonArray() };
        manual["resultFingerprint"] = Hash(InteractionCanonicalJson.CanonicalizeObject(manual.ToJsonString()));
        return ApplicationCandidateReuseInput.Create(candidate, manual.ToJsonString(),
            [new(new("sample-app.procedure.existing", "procedure", 1, Hash(contract)), contract)]);
    }
    private static JsonObject Output(ApplicationCandidateReuseInput input) => new()
    {
        ["inputFingerprint"] = input.InputFingerprint, ["candidateFingerprint"] = input.CandidateFingerprint,
        ["manualPacketResultFingerprint"] = input.ManualPacketResultFingerprint, ["judgment"] = "uncertain", ["reason"] = "Needs review.",
        ["assessments"] = new JsonArray(new JsonObject
        {
            ["target"] = JsonSerializer.SerializeToNode(input.Alternatives[0], new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            ["judgment"] = "uncertain", ["reason"] = "No equivalence established."
        })
    };
    private static ApplicationCandidateSnapshot Candidate(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var app = ApplicationIdentifier.Parse("sample-app");
        return new(new(app, new string('a', 32), 1, Hash(text)), 1, Hash("app"), Hash("active"), "runtime", null,
            "Review this complete reason.", "grant.1", "operation.1",
            [new(new("source.document", "source", SourceTrust.Trusted, 0, "procedures/one.md", "text/markdown", Hash(text), bytes.Length, true), bytes)], []);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
