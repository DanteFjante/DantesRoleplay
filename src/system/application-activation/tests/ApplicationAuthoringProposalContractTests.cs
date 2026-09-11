using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation.Tests;

/// <summary>Review fixtures for the proposed ABI; these do not exercise a production authoring service.</summary>
public sealed class ApplicationAuthoringProposalContractTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Candidate_request_preserves_explicit_absence_and_rejects_extra_authority()
    {
        const string example = """
            {"candidateId":"0123456789abcdef0123456789abcdef","expectedCandidateRevision":0,
             "expectedActiveFingerprint":null,"origin":"runtime","synchronizationEvidenceReference":null,
             "newImplementationReason":"No existing definition supplies this output.",
             "documents":[{"logicalIdentity":"file:content/entry.json","sourceId":"catalog",
               "relativePath":"content/entry.json","mediaType":"application/json","text":"{\"value\":1}"}]}
            """;
        var request = JsonSerializer.Deserialize<ApplicationCandidateWriteRequest>(example, Wire)!;
        Assert.Equal(0, request.ExpectedCandidateRevision);
        Assert.Null(request.ExpectedActiveFingerprint);
        Assert.Equal(InteractionCanonicalJson.Canonicalize(example),
            InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(request, Wire)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateWriteRequest>(
            example.Replace("\"expectedActiveFingerprint\":null,", ""), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateWriteRequest>(
            example.Replace("\"origin\":", "\"grantReference\":\"self-issued\",\"origin\":"), Wire));
    }

    [Fact]
    public void Conditional_information_precondition_cannot_be_omitted()
    {
        const string example = """
            {"value":{"id":"record.example","sourceId":"source.example","title":"Example",
            "content":"Retained content","metadataJson":"{}"},"expectedRevision":0}
            """;
        var request = JsonSerializer.Deserialize<InformationRecordConditionalWriteRequest>(example, Wire)!;
        Assert.Equal(0, request.ExpectedRevision);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InformationRecordConditionalWriteRequest>(
            example.Replace(",\"expectedRevision\":0", ""), Wire));
    }

    [Fact]
    public void Internal_snapshot_cannot_be_supplied_as_json_and_statuses_have_named_wire_values()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateSnapshot>("{}", Wire));
        Assert.Equal("\"unavailable\"", JsonSerializer.Serialize(ApplicationCandidateCheckStatus.Unavailable, Wire));
        Assert.Equal("\"activate\"", JsonSerializer.Serialize(StandingGrantCapability.Activate, Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantCapability>("2", Wire));
    }
}
