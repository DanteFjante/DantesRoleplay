using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationActivation;

// Coordinator proposal. V1 CandidateFingerprint semantics are intentionally unchanged.
// The retained owner separately binds this authorized selection to full candidate/base references.
public enum ApplicationCandidateReviewDocumentRole { Changed, Dependency, Sidecar }

public sealed record ApplicationCandidateReviewDocumentV2(
    StandingGrantDefinitionReference Definition, ApplicationCandidateReviewDocumentRole Role,
    string LogicalIdentity, string RelativePath, string MediaType, string ContentFingerprint, string Text);

/// <summary>Model-visible material only. It contains no full candidate/base generation identifiers or hashes.</summary>
public sealed record ApplicationCandidateReuseMaterialV2(
    string ApplicationId, string NewImplementationReason,
    IReadOnlyList<ApplicationCandidateReviewDocumentV2> Documents,
    JsonElement ManualPacket, IReadOnlyList<ApplicationCandidateReuseAlternative> Alternatives);

/// <summary>
/// Prepared only after the retained owner proves complete changed/dependency/sidecar coverage and
/// the review owner checks Read authority for every selected source. The boolean guard is not a
/// proof: actual owner evidence must also be retained/revalidated outside this model-visible value.
/// No worker or attestation is created. Final serialized provider-request bounds remain a separate check.
/// </summary>
[JsonConverter(typeof(RejectApplicationCandidateReuseInputV2JsonConverter))]
public sealed class ApplicationCandidateReuseInputV2
{
    public const string SelectionDomain = "dantes-roleplay/application-candidate-reuse-selection/v2";
    public const string InputDomain = "dantes-roleplay/application-candidate-reuse-input/v2";
    private ApplicationCandidateReuseInputV2(string selection, string input, string manual, string json,
        IReadOnlyList<StandingGrantDefinitionReference> alternatives)
    { SelectionFingerprint = selection; InputFingerprint = input; ManualResultFingerprint = manual;
        ModelInputJson = json; Alternatives = Array.AsReadOnly(alternatives.ToArray()); }
    public string SelectionFingerprint { get; }
    public string InputFingerprint { get; }
    public string ManualResultFingerprint { get; }
    public string ModelInputJson { get; }
    public IReadOnlyList<StandingGrantDefinitionReference> Alternatives { get; }

    // Cross-assembly host consumers use this context factory only after owner proof validation.
    // Its guard is not a transferable completeness assertion, grant or attestation.
    public static ApplicationCandidateReuseInputV2 Create(ApplicationCandidateReuseMaterialV2 material, bool closureComplete)
    {
        if (!closureComplete) throw new InteractionContractException("REUSE_SELECTION_INCOMPLETE", "Complete owner-resolved dependency coverage is required.");
        ArgumentNullException.ThrowIfNull(material);
        _ = ApplicationIdentifier.Parse(material.ApplicationId);
        if (material.Documents.Count is 0 or > ApplicationCandidateReuseJudgmentLimits.Documents
            || material.Alternatives.Count > ApplicationCandidateReuseJudgmentLimits.Alternatives
            || string.IsNullOrWhiteSpace(material.NewImplementationReason)
            || material.NewImplementationReason.Length > ApplicationAuthoringLimits.ReasonCharacters
            || material.Documents.Select(value => value.LogicalIdentity).Distinct(StringComparer.Ordinal).Count() != material.Documents.Count
            || material.Documents.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != material.Documents.Count
            || material.Alternatives.Select(value => value.Target.DefinitionId).Distinct(StringComparer.Ordinal).Count() != material.Alternatives.Count)
            throw Invalid();
        long totalText = 0;
        foreach (var document in material.Documents)
        {
            totalText += Encoding.UTF8.GetByteCount(document.Text);
            if (totalText > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes || !Enum.IsDefined(document.Role)
                || document.LogicalIdentity != "file:" + document.RelativePath
                || !GenericSourceDocument.IsNormalizedRelativePath(document.RelativePath) || string.IsNullOrWhiteSpace(document.MediaType)
                || !Exact(document.Definition, material.ApplicationId)
                || ApplicationCandidateReuseInput.Hash(document.Text) != document.ContentFingerprint) throw Invalid();
        }
        foreach (var alternative in material.Alternatives)
        {
            totalText += Encoding.UTF8.GetByteCount(alternative.ContractJson);
            if (totalText > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes || !Exact(alternative.Target, material.ApplicationId)
                || ApplicationCandidateReuseInput.Hash(alternative.ContractJson) != alternative.Target.ContentFingerprint) throw Invalid();
            _ = InteractionCanonicalJson.CanonicalizeObject(alternative.ContractJson);
        }
        var manualJson = material.ManualPacket.GetRawText();
        if (Encoding.UTF8.GetByteCount(manualJson) > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes) throw Invalid();
        using var parsedManual = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(manualJson));
        if (!parsedManual.RootElement.TryGetProperty("resultFingerprint", out var resultHash)
            || resultHash.ValueKind != JsonValueKind.String) throw Invalid();
        var manualHash = resultHash.GetString()!;
        var manual = System.Text.Json.Nodes.JsonNode.Parse(parsedManual.RootElement.GetRawText())!.AsObject();
        manual["resultFingerprint"] = new string('0', 64);
        if (ApplicationCandidateReuseInput.Hash(InteractionCanonicalJson.CanonicalizeObject(manual.ToJsonString())) != manualHash) throw Invalid();
        // These hashes contain exclusively authorized selected material. Full generation pins are
        // intentionally absent, including from the inputs to these model-visible hashes.
        var selected = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        { material.ApplicationId, documents = material.Documents.OrderBy(value => value.LogicalIdentity, StringComparer.Ordinal) }, Wire));
        var selectionHash = InteractionCanonicalJson.Fingerprint(SelectionDomain, selected);
        var payload = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            format = InputDomain, selectionFingerprint = selectionHash, manualResultFingerprint = manualHash,
            material.ApplicationId, material.NewImplementationReason,
            documents = material.Documents.OrderBy(value => value.LogicalIdentity, StringComparer.Ordinal),
            manualPacket = material.ManualPacket,
            alternatives = material.Alternatives.OrderBy(value => value.Target.DefinitionId, StringComparer.Ordinal)
        }, Wire));
        var inputHash = InteractionCanonicalJson.Fingerprint(InputDomain, payload);
        using var parsed = JsonDocument.Parse(payload);
        var json = JsonSerializer.Serialize(new { format = InputDomain, inputFingerprint = inputHash, input = parsed.RootElement }, Wire);
        if (Encoding.UTF8.GetByteCount(json) > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes) throw Invalid();
        return new(selectionHash, inputHash, manualHash, json, material.Alternatives.Select(value => value.Target).ToArray());
    }

    internal static readonly JsonSerializerOptions Wire = new(ApplicationCandidateReuseInput.Wire)
    { Converters = { new JsonStringEnumConverter<ApplicationCandidateReviewDocumentRole>(JsonNamingPolicy.CamelCase, false) } };
    private static bool Exact(StandingGrantDefinitionReference target, string applicationId) => target.Revision > 0
        && target.DefinitionId.StartsWith(applicationId + ".", StringComparison.Ordinal)
        && target.Kind is "procedure" or "mechanic" or "query"
        && target.ContentFingerprint is { Length: 64 } && target.ContentFingerprint.All(char.IsAsciiHexDigitUpper);
    private static InteractionContractException Invalid() => new("REUSE_SELECTION_INVALID", "Exact selected review context is invalid or exceeds its bounds.");
}

public sealed class RejectApplicationCandidateReuseInputV2JsonConverter : JsonConverter<ApplicationCandidateReuseInputV2>
{
    public override ApplicationCandidateReuseInputV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException("Selected review inputs are host-owned.");
    public override void Write(Utf8JsonWriter writer, ApplicationCandidateReuseInputV2 value, JsonSerializerOptions options) => throw new JsonException("Only bounded ModelInputJson may be sent to the worker.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationCandidateReuseJudgmentOutputV2(
    [property: JsonRequired] string Format,
    [property: JsonRequired] string SelectionFingerprint,
    [property: JsonRequired] string InputFingerprint,
    [property: JsonRequired] string ManualResultFingerprint,
    [property: JsonRequired] ApplicationCandidateReuseJudgment Judgment,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] IReadOnlyList<ApplicationCandidateReuseAssessment> Assessments)
{
    public const string OutputDomain = "dantes-roleplay/application-candidate-reuse-judgment/v2";
    public static ApplicationCandidateReuseJudgmentOutputV2 Parse(string json, ApplicationCandidateReuseInputV2 input)
    {
        if (Encoding.UTF8.GetByteCount(json) > ApplicationCandidateReuseJudgmentLimits.OutputUtf8Bytes) throw Invalid();
        var result = JsonSerializer.Deserialize<ApplicationCandidateReuseJudgmentOutputV2>(InteractionCanonicalJson.CanonicalizeObject(json), ApplicationCandidateReuseInputV2.Wire);
        if (result is null || result.Format != OutputDomain || result.SelectionFingerprint != input.SelectionFingerprint
            || result.InputFingerprint != input.InputFingerprint || result.ManualResultFingerprint != input.ManualResultFingerprint
            || !ReasonValid(result.Reason) || result.Assessments is null || result.Assessments.Count != input.Alternatives.Count
            || result.Assessments.Any(value => value is null || !ReasonValid(value.Reason) || !input.Alternatives.Contains(value.Target))
            || result.Assessments.Select(value => value.Target.DefinitionId).Distinct(StringComparer.Ordinal).Count() != result.Assessments.Count) throw Invalid();
        return result; // Strict syntax/coverage only; fresh owner authority and verified worker accounting remain mandatory.
    }
    private static bool ReasonValid(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= ApplicationCandidateReuseJudgmentLimits.ReasonCharacters;
    private static InteractionContractException Invalid() => new("REUSE_JUDGMENT_INVALID", "The v2 review judgment has invalid pins, coverage or bounds.");
}
