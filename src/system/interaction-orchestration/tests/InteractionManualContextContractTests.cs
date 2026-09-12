using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Interactions;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Tests;

public sealed class InteractionManualContextContractTests
{
    [Fact]
    public void Packet_has_explicit_camel_case_canonical_wire_and_reproducible_result_evidence()
    {
        using var known = JsonDocument.Parse("{}");
        var packet = new InteractionManualContextPacket("unresolved", new string('A', 64), new string('0', 64),
            "inspect", known.RootElement, [], [], null, [], "inspect-existing", "unavailable",
            "LexicalFallback", "VECTOR_INDEX_DISABLED", false, [], ["read-exact"]);
        var expected = """
            {"boundReasons":[],"bounded":false,"candidates":[],"intent":"inspect","knownInputs":{},"manualSections":[],"nextSteps":["read-exact"],"publication":"unavailable","resolution":"unresolved","resolutionFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","resultFingerprint":"0000000000000000000000000000000000000000000000000000000000000000","retrievalAvailability":"VECTOR_INDEX_DISABLED","retrievalMode":"LexicalFallback","reusableTasks":[],"reuseDecision":"inspect-existing","selectedAction":null}
            """;
        var canonical = InteractionCanonicalJson.CanonicalizeObject(packet.ToJson());
        Assert.Equal(expected, canonical);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        var completed = packet with { ResultFingerprint = hash };
        var wire = JsonNode.Parse(completed.ToJson())!;
        var actual = wire["resultFingerprint"]!.GetValue<string>();
        wire["resultFingerprint"] = new string('0', 64);
        Assert.Equal(actual, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            InteractionCanonicalJson.CanonicalizeObject(wire.ToJsonString())))));
    }

    [Fact]
    public void Compact_target_retains_exact_authorized_identity_without_catalog_generation_disclosure()
    {
        var raw = new InteractionFeatureReference(ApplicationIdentifier.Parse("sample-app"),
            InteractionRetrievalLane.TrustedFeature, new string('A', 64), "query", "sample-app.query.inspect", 3,
            new string('B', 64));
        var target = InteractionManualTargetReference.From(raw);
        Assert.Equal(target, InteractionManualTargetReference.From(raw with { CatalogFingerprint = new string('C', 64) }));
        Assert.NotEqual(target, InteractionManualTargetReference.From(raw with { Version = 4 }));
        using var known = JsonDocument.Parse("{}");
        var packet = new InteractionManualContextPacket("unresolved", new string('D', 64), new string('0', 64),
            "inspect", known.RootElement, [new(target, "Inspect", "exact-authored-match", "", [], "required-at-execution")],
            [], null, [], "inspect-existing", "unavailable", "Lexical", "", false, [], []);
        using var wire = JsonDocument.Parse(packet.ToJson());
        var reference = wire.RootElement.GetProperty("candidates")[0].GetProperty("reference");
        Assert.Equal(6, reference.EnumerateObject().Count());
        Assert.Equal("sample-app", reference.GetProperty("applicationId").GetString());
        Assert.Equal("trustedFeature", reference.GetProperty("lane").GetString());
        Assert.Equal("sample-app.query.inspect", reference.GetProperty("qualifiedId").GetString());
        Assert.Equal("query", reference.GetProperty("kind").GetString());
        Assert.Equal(3, reference.GetProperty("version").GetInt32());
        Assert.Equal(raw.ContentFingerprint, reference.GetProperty("contentFingerprint").GetString());
        Assert.False(reference.TryGetProperty("catalogFingerprint", out _));
        Assert.Equal(new string('A', 64), raw.CatalogFingerprint);
    }

    [Fact]
    public void Packet_serializer_rejects_oversized_content_instead_of_truncating_known_json()
    {
        using var known = JsonDocument.Parse(JsonSerializer.Serialize(new { value = new string('x', 2001) }));
        var packet = new InteractionManualContextPacket("unresolved", new string('A', 64), new string('0', 64),
            "inspect", known.RootElement, [], [], null, [], "inspect-existing", "unavailable",
            "LexicalFallback", "VECTOR_INDEX_DISABLED", false, [], []);
        Assert.Throws<InteractionContractException>(() => packet.ToJson());
        Assert.Equal(2001, known.RootElement.GetProperty("value").GetString()!.Length);
    }
}
