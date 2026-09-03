using DantesRoleplay.Applications;

namespace DantesRoleplay.Interactions;

public static class InteractionMechanicOpportunityProtocol
{
    public const string ProposalFingerprintDomain = "dantes-roleplay/interaction-mechanic-opportunity/v1";
    public const int SuccessfulUseThreshold = 3;
    public const int MaximumSupportingReceipts = 8;
    public const int MaximumOverlapCandidates = 8;
}

public sealed record InteractionMechanicOpportunityReceiptEvidence(
    string ResolutionReceiptId,
    string ExecutionReceiptId,
    string IntentFingerprint,
    DateTime CreatedAtUtc);

public sealed record InteractionMechanicOpportunityRole(
    string Role,
    IReadOnlyList<string> RequiredBySteps);

public sealed record InteractionMechanicOpportunityChild(
    string StepId,
    string QualifiedId,
    int Version,
    string Fingerprint,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> RoleSlots);

public sealed record InteractionMechanicOpportunityEffectOwnership(
    string ChildQualifiedId,
    IReadOnlyList<string> EffectComponentIds,
    string Responsibility);

public sealed record InteractionMechanicOpportunityEfficiencyEstimate(
    int ObservedSuccessfulUses,
    int BaselineChildCallsPerUse,
    int ExpectedMechanicCallsPerUse,
    int GrossCallsSavedPerUse,
    int GrossCallsSavedAcrossObservedUses,
    int RecipeToolCallsPerUse,
    int IncrementalToolCallsSavedVersusRecipe);

public sealed record InteractionMechanicOpportunityOverlap(
    string QualifiedId,
    int Version,
    string Fingerprint,
    double Similarity,
    string Reason);

/// <summary>
/// Review-only evidence that a verified recipe may deserve a catalog mechanic. It deliberately has
/// no proposed mechanic ID, lifecycle transition, source path, or activation command.
/// </summary>
public sealed record InteractionMechanicOpportunityDraft(
    ApplicationIdentifier ApplicationId,
    InteractionRecipeReference SourceRecipe,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string EffectiveSetFingerprint,
    string RepeatedIntent,
    IReadOnlyList<InteractionMechanicOpportunityReceiptEvidence> SupportingReceipts,
    IReadOnlyList<InteractionMechanicOpportunityRole> ProposedRoles,
    string ProposedInputSchemaJson,
    IReadOnlyList<InteractionMechanicOpportunityChild> ExactChildDependencies,
    IReadOnlyList<InteractionMechanicOpportunityEffectOwnership> IntendedEffectsAndOwnership,
    IReadOnlyList<string> SuggestedMatchPhrases,
    InteractionMechanicOpportunityEfficiencyEstimate EstimatedCallReduction,
    IReadOnlyList<InteractionMechanicOpportunityOverlap> PossibleOverlap,
    string MechanicPreferenceReason);

public sealed record InteractionMechanicOpportunityProjection(
    string ProposalFingerprint,
    ApplicationIdentifier ApplicationId,
    InteractionRecipeReference SourceRecipe,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string EffectiveSetFingerprint,
    string RepeatedIntent,
    IReadOnlyList<InteractionMechanicOpportunityReceiptEvidence> SupportingReceipts,
    IReadOnlyList<InteractionMechanicOpportunityRole> ProposedRoles,
    string ProposedInputSchemaJson,
    IReadOnlyList<InteractionMechanicOpportunityChild> ExactChildDependencies,
    IReadOnlyList<InteractionMechanicOpportunityEffectOwnership> IntendedEffectsAndOwnership,
    IReadOnlyList<string> SuggestedMatchPhrases,
    InteractionMechanicOpportunityEfficiencyEstimate EstimatedCallReduction,
    IReadOnlyList<InteractionMechanicOpportunityOverlap> PossibleOverlap,
    string MechanicPreferenceReason,
    DateTime CreatedAtUtc);

public enum InteractionMechanicOpportunityWriteDisposition { Created, Replayed, Conflict }

public sealed record InteractionMechanicOpportunityWriteResult(
    InteractionMechanicOpportunityWriteDisposition Disposition,
    InteractionMechanicOpportunityProjection? Proposal,
    string Code);

public interface IInteractionMechanicOpportunityStore
{
    Task<InteractionMechanicOpportunityWriteResult> AppendAsync(
        InteractionMechanicOpportunityDraft draft,
        CancellationToken cancellationToken = default);

    Task<InteractionMechanicOpportunityProjection?> GetAsync(
        ApplicationIdentifier applicationId,
        string sourceRecipeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InteractionMechanicOpportunityProjection>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

public interface IInteractionMechanicOpportunityLearner
{
    Task<InteractionMechanicOpportunityWriteResult?> ObserveAsync(
        InteractionRecipeReference recipe,
        CancellationToken cancellationToken = default);
}

public sealed class InteractionMechanicOpportunity
{
    public string RecipeId { get; set; } = "";
    public int RecipeVersion { get; set; }
    public string RecipeTemplateFingerprint { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string ProposalFingerprint { get; set; } = "";
    public string ProposalJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public InteractionRecipe? Recipe { get; set; }
}
