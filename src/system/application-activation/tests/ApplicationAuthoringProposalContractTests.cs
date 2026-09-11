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
    public void Grant_administration_has_named_intent_exact_application_and_no_caller_generated_evidence()
    {
        const string example = """
            {"mutation":"issue","grantId":"authoring","expectedCurrentRevision":0,
             "principalReference":"principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "applicationId":"demo","scope":"application","stateSpaceId":null,"capabilities":["author"],
             "definitions":{"mode":"applicationOwned","exactIds":[],"applicationOwnedNamespaces":[
                {"namespaceId":"demo.runtime","includeDescendants":false,"definitionKinds":["procedure"]}]},
             "effectKinds":[],"maximumOperations":2,"expiresAtUtc":"2026-12-31T00:00:00Z"}
            """;
        var request = JsonSerializer.Deserialize<StandingGrantMutationRequest>(example, Wire)!;
        Assert.Equal(StandingGrantIssuerMutation.Issue, request.Mutation);
        Assert.Equal("demo", request.ApplicationId.Value);
        Assert.Equal(InteractionCanonicalJson.Canonicalize(example),
            InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(request, Wire)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantMutationRequest>(
            example.Replace("\"mutation\":\"issue\"", "\"mutation\":0"), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantMutationRequest>(
            example.Replace("\"applicationId\":\"demo\"", "\"applicationId\":\"system\""), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantMutationRequest>(
            example.Replace("\"grantId\":", "\"contentFingerprint\":\"self-issued\",\"grantId\":"), Wire));
    }

    [Fact]
    public void Expanding_allowance_requires_explicit_mode_and_descendant_choice()
    {
        const string example = """
            {"mode":"applicationOwned","exactIds":[],"applicationOwnedNamespaces":[
              {"namespaceId":"demo.rules","includeDescendants":false,"definitionKinds":["mechanic"]}]}
            """;
        var allowance = JsonSerializer.Deserialize<StandingGrantDefinitionAllowance>(example, Wire)!;
        Assert.Equal(StandingGrantDefinitionMode.ApplicationOwned, allowance.Mode);
        Assert.False(Assert.Single(allowance.ApplicationOwnedNamespaces).IncludeDescendants);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantDefinitionAllowance>(
            example.Replace("\"mode\":\"applicationOwned\",", ""), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantDefinitionAllowance>(
            example.Replace("\"includeDescendants\":false,", ""), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantDefinitionAllowance>(
            example.Replace("\"mode\":\"applicationOwned\"", "\"mode\":1"), Wire));
    }

    [Fact]
    public void Internal_snapshot_cannot_be_supplied_as_json_and_statuses_have_named_wire_values()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationCandidateSnapshot>("{}", Wire));
        Assert.Equal("\"unavailable\"", JsonSerializer.Serialize(ApplicationCandidateCheckStatus.Unavailable, Wire));
        Assert.Equal("\"activate\"", JsonSerializer.Serialize(StandingGrantCapability.Activate, Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantCapability>("2", Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StandingGrantRequirement>("{}", Wire));
        Assert.Equal("\"read\"", JsonSerializer.Serialize(StandingGrantCapability.Read, Wire));
        Assert.Equal("\"readTask\"", JsonSerializer.Serialize(StandingGrantCapability.ReadTask, Wire));
        Assert.Equal("\"cancelTask\"", JsonSerializer.Serialize(StandingGrantCapability.CancelTask, Wire));
        Assert.Equal("\"applicationOwned\"", JsonSerializer.Serialize(StandingGrantDefinitionMode.ApplicationOwned, Wire));
    }
}
