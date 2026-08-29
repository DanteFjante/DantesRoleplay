using DantesRoleplay.Snapshots;

namespace DantesRoleplay.Campaign;

/// <summary>Closed result of producing one private, ended-session evidence package.</summary>
public sealed record CampaignSessionEvidenceProductionResult(
    string Status,
    string SessionId,
    string? CampaignId,
    string? WorldId,
    SnapshotCaptureProposal? Proposal,
    IReadOnlyList<CampaignSessionProblem> Problems)
{
    public bool Produced => Status == "produced";
}

/// <summary>
/// Campaign owns the selection and serialization of its ended-session evidence. Storage owns
/// neither Campaign vocabulary nor the contents of this proposal.
/// </summary>
public interface ICampaignSessionEvidenceProducer
{
    Task<CampaignSessionEvidenceProductionResult> ProduceAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
