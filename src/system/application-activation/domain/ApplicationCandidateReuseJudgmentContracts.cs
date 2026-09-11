using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

// Additive coordinator review proposal. This supplies no worker, accounting verifier or attestation.
public static class ApplicationCandidateReuseJudgmentLimits
{
    public const int InputUtf8Bytes = 64_000;
    public const int OutputUtf8Bytes = 8_000;
    public const int Documents = 16;
    public const int Alternatives = 16;
    public const int ReasonCharacters = 500;
}

public sealed record ApplicationCandidateReuseAlternative(
    StandingGrantDefinitionReference Target, string ContractJson);

/// <summary>
/// Constructed only by the review owner after exact retained-source and Read authority checks.
/// Frozen model input contains complete retained text, reason and authorized alternatives; never a
/// clipped source. This value is context, not authority, accounting evidence or a review attestation.
/// Whole active-generation/application fingerprints remain host-only.
/// </summary>
[JsonConverter(typeof(RejectApplicationCandidateReuseInputJsonConverter))]
public sealed class ApplicationCandidateReuseInput
{
    private ApplicationCandidateReuseInput(string inputFingerprint, string candidateFingerprint,
        string manualPacketResultFingerprint, string modelInputJson, IReadOnlyList<StandingGrantDefinitionReference> alternatives)
    {
        InputFingerprint = inputFingerprint;
        CandidateFingerprint = candidateFingerprint;
        ManualPacketResultFingerprint = manualPacketResultFingerprint;
        ModelInputJson = modelInputJson;
        Alternatives = Array.AsReadOnly(alternatives.ToArray());
    }

    public string InputFingerprint { get; }
    public string CandidateFingerprint { get; }
    public string ManualPacketResultFingerprint { get; }
    public string ModelInputJson { get; }
    public IReadOnlyList<StandingGrantDefinitionReference> Alternatives { get; }

    public static ApplicationCandidateReuseInput Create(ApplicationCandidateSnapshot candidate,
        string manualPacketJson, IReadOnlyList<ApplicationCandidateReuseAlternative> alternatives)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(alternatives);
        if (candidate.EffectiveDocuments.Count is 0 or > ApplicationCandidateReuseJudgmentLimits.Documents
            || alternatives.Count > ApplicationCandidateReuseJudgmentLimits.Alternatives
            || Encoding.UTF8.GetByteCount(manualPacketJson) > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes
            || string.IsNullOrWhiteSpace(candidate.NewImplementationReason)
            || candidate.NewImplementationReason.Length > ApplicationAuthoringLimits.ReasonCharacters)
            throw Invalid("Review context exceeds its finite bounds.");
        if (alternatives.Select(value => value.Target.DefinitionId).Distinct(StringComparer.Ordinal).Count() != alternatives.Count)
            throw Invalid("Review alternatives must have unique exact identities.");
        if (candidate.EffectiveDocuments.Select(value => value.Document.LogicalIdentity).Distinct(StringComparer.Ordinal).Count() != candidate.EffectiveDocuments.Count
            || candidate.EffectiveDocuments.Select(value => value.Document.RelativePath).Distinct(StringComparer.Ordinal).Count() != candidate.EffectiveDocuments.Count)
            throw Invalid("Retained documents must have unique exact identities and paths.");
        using var manual = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(manualPacketJson));
        if (!manual.RootElement.TryGetProperty("resultFingerprint", out var resultHash)
            || resultHash.ValueKind != JsonValueKind.String || !UpperHash(resultHash.GetString())
            || !UpperHash(candidate.Candidate.ContentFingerprint))
            throw Invalid("Exact candidate and manual result fingerprints are required.");
        var manualHash = resultHash.GetString()!;
        var checkedManual = System.Text.Json.Nodes.JsonNode.Parse(manual.RootElement.GetRawText())!.AsObject();
        checkedManual["resultFingerprint"] = new string('0', 64);
        if (Hash(InteractionCanonicalJson.CanonicalizeObject(checkedManual.ToJsonString())) != manualHash)
            throw Invalid("The manual packet result fingerprint does not match its contents.");
        var utf8 = new UTF8Encoding(false, true);
        long retainedLength = 0;
        var documents = candidate.EffectiveDocuments.OrderBy(value => value.Document.LogicalIdentity, StringComparer.Ordinal)
            .Select(value =>
            {
                var copied = value.RetainedBytes.ToArray();
                retainedLength += copied.Length;
                if (retainedLength > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes
                    || !value.Document.IsText || copied.LongLength != value.Document.Length
                    || Convert.ToHexString(SHA256.HashData(copied)) != value.Document.ContentFingerprint)
                    throw Invalid("Complete exact retained text is required.");
                return new { value.Document.LogicalIdentity, value.Document.RelativePath, value.Document.MediaType,
                    value.Document.ContentFingerprint, Text = utf8.GetString(copied) };
            }).ToArray();
        var exactAlternatives = alternatives.OrderBy(value => value.Target.DefinitionId, StringComparer.Ordinal).Select(value =>
        {
            if (Encoding.UTF8.GetByteCount(value.ContractJson) > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes
                || value.Target.Revision < 1 || !UpperHash(value.Target.ContentFingerprint)
                || !value.Target.DefinitionId.StartsWith(candidate.Candidate.ApplicationId.Value + ".", StringComparison.Ordinal)
                || value.Target.Kind is not ("procedure" or "mechanic" or "query")
                || Hash(value.ContractJson) != value.Target.ContentFingerprint)
                throw Invalid("An alternative contract does not match its exact content fingerprint.");
            using var content = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(value.ContractJson));
            return new { value.Target, Contract = content.RootElement.Clone() };
        }).ToArray();
        var input = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            candidate = new { applicationId = candidate.Candidate.ApplicationId.Value, candidate.Candidate.CandidateId,
                candidate.Candidate.Revision, candidate.Candidate.ContentFingerprint },
            candidate.NewImplementationReason, documents, manualPacket = manual.RootElement,
            manualPacketResultFingerprint = manualHash, alternatives = exactAlternatives
        }, Wire));
        var fingerprint = Hash(input);
        using var parsedInput = JsonDocument.Parse(input);
        var modelInput = JsonSerializer.Serialize(new { inputFingerprint = fingerprint, input = parsedInput.RootElement }, Wire);
        if (Encoding.UTF8.GetByteCount(modelInput) > ApplicationCandidateReuseJudgmentLimits.InputUtf8Bytes)
            throw Invalid("The complete review input exceeds its aggregate bound; no content was truncated.");
        return new(fingerprint, candidate.Candidate.ContentFingerprint, manualHash, modelInput,
            alternatives.Select(value => value.Target).ToArray());
    }

    internal static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<ApplicationCandidateReuseJudgment>(JsonNamingPolicy.CamelCase, false) }
    };
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool UpperHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitUpper);
    internal static InteractionContractException Invalid(string message) => new("REUSE_JUDGMENT_INVALID", message);
}

public sealed class RejectApplicationCandidateReuseInputJsonConverter : JsonConverter<ApplicationCandidateReuseInput>
{
    public override ApplicationCandidateReuseInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Candidate reuse inputs must be constructed by the trusted review owner.");
    public override void Write(Utf8JsonWriter writer, ApplicationCandidateReuseInput value, JsonSerializerOptions options) =>
        throw new JsonException("Use the owner's bounded ModelInputJson for the read-only worker.");
}

public enum ApplicationCandidateReuseJudgment { ReuseExisting, ExtendExisting, JustifiedNew, Uncertain }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationCandidateReuseAssessment(
    [property: JsonRequired] StandingGrantDefinitionReference Target,
    [property: JsonRequired] ApplicationCandidateReuseJudgment Judgment,
    [property: JsonRequired] string Reason);

/// <summary>
/// Model-only judgment. Parsing validates shape and exact coverage, not semantic correctness or
/// acceptance. Zero alternatives and a justifiedNew label never establish a Valid attestation.
/// The host must reject contradictory judgments and verify fresh authority and real worker accounting.
/// No evidence, worker identity, permission or accounting fields are accepted from the model.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationCandidateReuseJudgmentOutput(
    [property: JsonRequired] string InputFingerprint,
    [property: JsonRequired] string CandidateFingerprint,
    [property: JsonRequired] string ManualPacketResultFingerprint,
    [property: JsonRequired] ApplicationCandidateReuseJudgment Judgment,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] IReadOnlyList<ApplicationCandidateReuseAssessment> Assessments)
{
    public static ApplicationCandidateReuseJudgmentOutput Parse(string json, ApplicationCandidateReuseInput input)
    {
        if (Encoding.UTF8.GetByteCount(json) > ApplicationCandidateReuseJudgmentLimits.OutputUtf8Bytes)
            throw ApplicationCandidateReuseInput.Invalid("The review output exceeds its aggregate bound.");
        // The canonical owner rejects duplicate object members before deserialization.
        var output = JsonSerializer.Deserialize<ApplicationCandidateReuseJudgmentOutput>(
            InteractionCanonicalJson.CanonicalizeObject(json), ApplicationCandidateReuseInput.Wire)
            ?? throw ApplicationCandidateReuseInput.Invalid("A structured review output is required.");
        if (output.InputFingerprint != input.InputFingerprint || output.CandidateFingerprint != input.CandidateFingerprint
            || output.ManualPacketResultFingerprint != input.ManualPacketResultFingerprint
            || !ReasonValid(output.Reason) || output.Assessments is null
            || output.Assessments.Count != input.Alternatives.Count
            || output.Assessments.Any(value => value is null || !ReasonValid(value.Reason) || !input.Alternatives.Contains(value.Target))
            || output.Assessments.Select(value => value.Target.DefinitionId).Distinct(StringComparer.Ordinal).Count() != output.Assessments.Count)
            throw ApplicationCandidateReuseInput.Invalid("Review pins, exact reference coverage or reasons are invalid.");
        return output;
    }

    private static bool ReasonValid(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= ApplicationCandidateReuseJudgmentLimits.ReasonCharacters;
}
