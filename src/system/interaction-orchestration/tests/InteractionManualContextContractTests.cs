using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Tests;

public sealed class InteractionManualContextContractTests
{
    [Fact]
    public void Packet_has_explicit_camel_case_canonical_wire_and_reproducible_result_evidence()
    {
        using var known = JsonDocument.Parse("{}");
        var packet = new InteractionManualContextPacket("unresolved", new string('A', 64), new string('0', 64),
            "inspect", known.RootElement, [], [], null, [], "inspect-existing", "unavailable",
            "LexicalFallback", "VECTOR_INDEX_DISABLED", false, ["read-exact"]);
        var expected = """
            {"bounded":false,"candidates":[],"intent":"inspect","knownInputs":{},"manualSections":[],"nextSteps":["read-exact"],"publication":"unavailable","resolution":"unresolved","resolutionFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","resultFingerprint":"0000000000000000000000000000000000000000000000000000000000000000","retrievalAvailability":"VECTOR_INDEX_DISABLED","retrievalMode":"LexicalFallback","reusableTasks":[],"reuseDecision":"inspect-existing","selectedAction":null}
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
    public void Packet_serializer_rejects_oversized_content_instead_of_truncating_known_json()
    {
        using var known = JsonDocument.Parse(JsonSerializer.Serialize(new { value = new string('x', 2001) }));
        var packet = new InteractionManualContextPacket("unresolved", new string('A', 64), new string('0', 64),
            "inspect", known.RootElement, [], [], null, [], "inspect-existing", "unavailable",
            "LexicalFallback", "VECTOR_INDEX_DISABLED", false, []);
        Assert.Throws<InteractionContractException>(() => packet.ToJson());
        Assert.Equal(2001, known.RootElement.GetProperty("value").GetString()!.Length);
    }
}
