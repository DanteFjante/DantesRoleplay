using System.Text.Json;

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
/// serialize as camelCase. Null SelectedAction is mandatory: this result confers no write authority.
/// Resolution is unresolved, ambiguous or refresh-required. Exact contract reads and the execution
/// adapter remain responsible for binding/schema/grant checks. No durable task or receipt is created.
///
/// ResultFingerprint is SHA-256 of canonical packet JSON with that field set to 64 zeroes.
/// ResolutionFingerprint binds host scope/grant, intent/input, definition and source revisions,
/// associations and candidate references. It is opaque to callers; resubmit it for drift detection.
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
    IReadOnlyList<string> NextSteps);

public sealed record InteractionManualFeatureCandidate(InteractionFeatureReference Reference, string Name,
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
/// </summary>
public sealed record InteractionManualSectionEvidence(string ProcedureId, int Version,
    string SourceFingerprint, string Governs, InteractionManualSection Section);

public sealed record InteractionManualSection(string Reference, string Heading, string ParentContext,
    string Text, string ContentFingerprint);
