namespace DantesRoleplay.Campaign;

/// <summary>Read-only C15 scope result. Campaign membership remains campaign-owned state.</summary>
public sealed record CampaignCharacterParticipationProblem(string Code, string Path, string Reason, string Recovery);

public sealed record CampaignCharacterParticipationScope(
    string Status,
    string ActorId,
    string? CampaignId,
    string? ParticipationId,
    IReadOnlyList<CampaignCharacterParticipationProblem> Problems)
{
    public bool Valid => Status == "active";
}

/// <summary>
/// Resolves the one canonical active campaign scope for an actor. It never infers scope from
/// character-owned data and never changes campaign, actor, or relationship state.
/// </summary>
public interface ICampaignCharacterParticipationVerifier
{
    Task<CampaignCharacterParticipationScope> ResolveActiveScopeAsync(
        string actorId,
        CancellationToken cancellationToken = default);
}

/// <summary>Closed trusted-host request for C15's one-time campaign attachment.</summary>
public sealed record CampaignCharacterParticipationAttachRequest(string Operation, string CampaignId, string ActorId);
public sealed record CampaignCharacterParticipationResult(
    string Status,
    string CampaignId,
    string ActorId,
    string? ParticipationId,
    string OperationId,
    IReadOnlyList<CampaignCharacterParticipationProblem> Problems,
    string Next)
{
    public bool Attached => Status == "attached";
}

/// <summary>Owns the one transaction that creates a campaign participation record.</summary>
public interface ICampaignCharacterParticipationAttacher
{
    Task<CampaignCharacterParticipationResult> AttachAsync(
        CampaignCharacterParticipationAttachRequest request,
        string intent = "",
        IReadOnlyList<string>? proceduresUsed = null,
        CancellationToken cancellationToken = default);
}
