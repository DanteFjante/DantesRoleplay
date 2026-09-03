using DantesRoleplay.Applications;

namespace DantesRoleplay.Interactions;

public static class InteractionMechanicSandboxProtocol
{
    public const int MaximumActiveDraftsPerApplication = 5;
    public const int MaximumRevisionsPerDraft = 8;
    public const int MaximumScenarios = 8;
    public const int MaximumSourceLength = 65_536;
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromDays(7);
}

public sealed record InteractionMechanicSandboxLimits(
    int MaxStatements = 50_000,
    int TimeoutMilliseconds = 1_000,
    int MemoryBytes = 4 * 1024 * 1024,
    int MaxRecursionDepth = 32,
    int MaxEffects = 50,
    int MaxEvents = 10,
    int MaxNotifications = 10,
    int MaxLogLines = 50);

public sealed record InteractionMechanicSandboxEffectAllowlist(
    IReadOnlyList<string> EffectTypes,
    IReadOnlyList<string> ComponentIds);

public sealed record InteractionMechanicSandboxExpectation(
    bool Successful,
    int MinimumEffects,
    int MaximumEffects,
    IReadOnlyList<string> EffectTypes,
    IReadOnlyList<string> ComponentIds,
    string NarrationContains = "");

public sealed record InteractionMechanicSandboxScenario(
    string Name,
    string ProjectionJson,
    InteractionMechanicSandboxExpectation Expected);

public sealed record InteractionMechanicSandboxCandidate(
    string Name,
    string Category,
    string Description,
    IReadOnlyList<string> MatchPhrases,
    string RequirementsJson,
    string Source,
    InteractionMechanicSandboxEffectAllowlist EffectAllowlist,
    InteractionMechanicSandboxLimits Limits,
    IReadOnlyList<InteractionMechanicSandboxScenario> Scenarios);

public sealed record InteractionMechanicSandboxValidationCheck(
    string Name,
    bool Passed,
    bool Blocking,
    string Summary);

public sealed record InteractionMechanicSandboxScenarioResult(
    string Name,
    bool Passed,
    bool SandboxOk,
    int EffectCount,
    int ElapsedMilliseconds,
    string LimitHit,
    string Summary,
    IReadOnlyList<InteractionMechanicSandboxEffectPreview>? EffectPreviews = null);

public sealed record InteractionMechanicSandboxEffectPreview(
    string Type,
    string EntityId,
    string DefinitionId,
    string ToEntityId,
    string Kind,
    string Slot,
    string Name,
    string DataJson);

public sealed record InteractionMechanicSandboxValidation(
    bool Passed,
    IReadOnlyList<InteractionMechanicSandboxValidationCheck> CatalogChecks,
    IReadOnlyList<InteractionMechanicSandboxValidationCheck> AntiSprawlChecks,
    IReadOnlyList<InteractionMechanicSandboxScenarioResult> ScenarioResults,
    DateTime ValidatedAtUtc);

public sealed record InteractionMechanicSandboxDraftCommand(
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string OpportunityProposalFingerprint,
    InteractionMechanicSandboxCandidate Candidate,
    string IdempotencyKey,
    string? DraftId = null,
    int? ExpectedRevision = null);

public sealed record InteractionMechanicSandboxDraftProjection(
    string DraftId,
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string OpportunityProposalFingerprint,
    int Revision,
    string CandidateFingerprint,
    string Status,
    DateTime CreatedAtUtc,
    DateTime RevisedAtUtc,
    DateTime ExpiresAtUtc,
    InteractionMechanicSandboxCandidate Candidate,
    InteractionMechanicSandboxValidation Validation,
    string ReviewPrincipalReference,
    string ReviewAuthorizationEvidence,
    string? PromotionPrincipalReference = null,
    string? PromotionAuthorizationEvidence = null,
    DateTime? PromotedAtUtc = null);

public sealed record InteractionMechanicSandboxPromotionCommand(
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string DraftId,
    int ExpectedRevision,
    string IdempotencyKey);

public sealed record InteractionMechanicSandboxExportPackage(
    string DraftId,
    int Revision,
    string CandidateFingerprint,
    string OpportunityProposalFingerprint,
    InteractionMechanicSandboxCandidate Candidate,
    InteractionMechanicSandboxValidation Validation,
    bool PermanentIdRequired,
    bool FilesystemWritePerformed,
    bool Activated);

public sealed record InteractionMechanicSandboxWriteAuthority(
    string PrincipalReference,
    string AuthorizationEvidenceReference,
    string RequestToken,
    string Intent,
    string OperationId);

public interface IInteractionMechanicSandboxService
{
    Task<InteractionMechanicSandboxValidation> ValidateAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        InteractionMechanicSandboxCandidate candidate,
        string? excludedDraftId = null,
        CancellationToken cancellationToken = default);

    Task<InteractionMechanicSandboxDraftProjection> CreateOrReviseAsync(
        InteractionMechanicSandboxDraftCommand command,
        InteractionMechanicSandboxWriteAuthority authority,
        CancellationToken cancellationToken = default);

    Task<InteractionMechanicSandboxDraftProjection?> GetAsync(
        ApplicationIdentifier applicationId,
        string draftId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InteractionMechanicSandboxDraftProjection>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<(InteractionMechanicSandboxDraftProjection Draft, InteractionMechanicSandboxExportPackage Export)> PromoteAsync(
        InteractionMechanicSandboxPromotionCommand command,
        InteractionMechanicSandboxWriteAuthority authority,
        CancellationToken cancellationToken = default);
}

public sealed class InteractionMechanicSandboxDraft
{
    public string Id { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string StateSpaceId { get; set; } = "";
    public string OpportunityProposalFingerprint { get; set; } = "";
    public string Status { get; set; } = "";
    public int QuotaSlot { get; set; }
    public int CurrentRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RevisedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string ReviewPrincipalReference { get; set; } = "";
    public string ReviewAuthorizationEvidence { get; set; } = "";
    public string PromotionPrincipalReference { get; set; } = "";
    public string PromotionAuthorizationEvidence { get; set; } = "";
    public DateTime? PromotedAtUtc { get; set; }
    public string PromotionIdempotencyKey { get; set; } = "";
    public string PromotionRequestFingerprint { get; set; } = "";
    public string PromotionOperationId { get; set; } = "";
    public ICollection<InteractionMechanicSandboxDraftRevision> Revisions { get; } =
        new List<InteractionMechanicSandboxDraftRevision>();
}

public sealed class InteractionMechanicSandboxDraftRevision
{
    public string DraftId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public int Revision { get; set; }
    public string CandidateFingerprint { get; set; } = "";
    public string CandidateJson { get; set; } = "";
    public string ValidationJson { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public string OperationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public InteractionMechanicSandboxDraft? Draft { get; set; }
}
