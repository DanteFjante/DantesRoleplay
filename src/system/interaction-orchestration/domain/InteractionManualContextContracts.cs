using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Additive context seam for coordinator review. The host is trusted C# authority; its existing
/// converter rejects authored JSON. Discovery consumes one shared operation. It never executes.
/// </summary>
public sealed record InteractionManualContextRequest(
    InteractionInvocationHost Host,
    string Intent,
    string KnownInputJson = "{}",
    int MaximumCharacters = 16_000,
    string? ExpectedResolutionFingerprint = null);

public interface IInteractionManualContextService
{
    Task<InteractionInvocationResult> DiscoverAsync(InteractionManualContextRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compact discovery data inside the existing CompletedComputation envelope. All property names
/// serialize as camelCase through ToJson. SelectedAction is an optional recommendation, never write
/// authority. Current implementation returns unresolved, ambiguous or refresh-required; future exact
/// validated recommendations may use resolved. Exact contract reads and the execution
/// adapter remain responsible for binding/schema/grant checks. No durable task or receipt is created.
///
/// ResultFingerprint is SHA-256 of canonical packet JSON with that field set to 64 zeroes.
/// ResolutionFingerprint binds host identity/grant, intent/input and only authorized packet targets,
/// manual evidence and associations. Whole catalog/activation hashes and denied source
/// hashes are not model-facing evidence. Raw generation pins remain internal for freshness checks.
/// It is opaque to callers; resubmit it for authorized-view drift detection.
/// Bounds: intent 256 chars, known input 2000 chars, result 4000..24000 chars (default 16000),
/// at most 8 feature candidates, 4 recipes and 8 sections, each section at most 2000 chars.
/// Bounded means content was omitted; exact source reads are required before relying on constraints.
/// Global source categories are an immutable trusted host allow-list, never a request field.
/// </summary>
public sealed record InteractionManualContextPacket(
    string Resolution,
    string ResolutionFingerprint,
    string ResultFingerprint,
    string Intent,
    JsonElement KnownInputs,
    IReadOnlyList<InteractionManualFeatureCandidate> Candidates,
    IReadOnlyList<InteractionManualRecipeCandidate> ReusableTasks,
    string? SelectedAction,
    IReadOnlyList<InteractionManualSectionEvidence> ManualSections,
    string ReuseDecision,
    string Publication,
    string RetrievalMode,
    string RetrievalAvailability,
    bool Bounded,
    IReadOnlyList<string> NextSteps)
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<InteractionRetrievalLane>(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Explicit bounded camelCase wire serialization; input JSON is never truncated.</summary>
    public string ToJson(int maximumCharacters = 16_000)
    {
        if (maximumCharacters is < 4000 or > 24_000 || Intent.Length is 0 or > 256
            || KnownInputs.ValueKind != JsonValueKind.Object || KnownInputs.GetRawText().Length > 2000
            || Candidates.Count > 8 || ReusableTasks.Count > 4 || ManualSections.Count > 8
            || ManualSections.Any(value => value.Section.Text.Length > 2000)
            || Resolution is not ("unresolved" or "ambiguous" or "refresh-required" or "resolved"))
            throw new InteractionContractException("MANUAL_CONTEXT_LIMIT", "The manual packet exceeds the closed limits.");
        var json = JsonSerializer.Serialize(this, Wire);
        if (json.Length > maximumCharacters)
            throw new InteractionContractException("MANUAL_CONTEXT_LIMIT", "The serialized manual packet exceeds the context budget.");
        return json;
    }
}

/// <summary>
/// Exact authorized target evidence for compact manual packets. ApplicationId is the canonical
/// application identifier value; Lane uses its camelCase enum name in packet JSON. This omits the
/// whole-catalog fingerprint retained by InteractionFeatureReference, which must remain host-only
/// for restricted discovery. It does not confer execution or state-query permission.
/// </summary>
public sealed record InteractionManualTargetReference(string ApplicationId, InteractionRetrievalLane Lane,
    string QualifiedId, string Kind, int Version, string ContentFingerprint)
{
    public static InteractionManualTargetReference From(InteractionFeatureReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(reference.ApplicationId.Value, reference.Lane, reference.QualifiedId, reference.Kind,
            reference.Version, reference.ContentFingerprint);
    }
}

public sealed record InteractionManualFeatureCandidate(InteractionManualTargetReference Reference, string Name,
    string Reason, string Prerequisites, IReadOnlyList<string> MissingInputs, string InputValidation);

public sealed record InteractionManualRecipeCandidate(InteractionRecipeReference Reference, bool Compatible,
    string Resolution, IReadOnlyList<InteractionManualRecipeStep> Steps);

public sealed record InteractionManualRecipeStep(string QualifiedId, int ContractVersion,
    string ContractFingerprint, IReadOnlyList<InteractionRecipeInputBinding> InputBindings);

/// <summary>
/// ProcedureId is the actual source ID, including for global procedures. Section references are
/// derived from source ID, field, heading ancestry, repeated heading occurrence and chunk ordinal;
/// revision/hash remain mandatory because text edits can change chunk boundaries.
/// Governs is authored applicability text, never an inferred executable contract reference.
/// SourceFingerprint is derived full manual evidence (including Matches) for global procedures,
/// or the exact activated feature content fingerprint for application procedures. StoredSourceHash
/// retains the procedure store's synchronization hash verbatim where available, otherwise null.
/// AssociationFingerprint independently hashes the exact authored Matches string; it is not a grant.
/// </summary>
public sealed record InteractionManualSectionEvidence(string ProcedureId, int Version,
    string SourceFingerprint, string? StoredSourceHash, string AssociationFingerprint,
    string Governs, InteractionManualSection Section);

public sealed record InteractionManualSection(string Reference, string Heading, string ParentContext,
    string Text, string ContentFingerprint);
