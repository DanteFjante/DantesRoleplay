using DantesRoleplay.EcsEffects;

namespace DantesRoleplay.Knowledge;

/// <summary>One exact, human-reviewed epistemic-state decision for the ambient actor.</summary>
public sealed record ReviewedKnowledgeStateEntry(string KnowledgeId, string State);

public sealed record ReviewedKnowledgeStateSyncRequest(
    string RequestToken,
    string CampaignId,
    IReadOnlyList<ReviewedKnowledgeStateEntry> Entries);

public sealed record ReviewedKnowledgeStateSyncResult(
    bool Accepted,
    bool DryRun,
    bool Replayed,
    int ReviewedCount,
    int ChangedCount,
    string OperationId,
    string ErrorCode = "",
    IReadOnlyList<ApplicationEcsEffectProblem>? Problems = null);

/// <summary>
/// Private reviewed synchronization boundary. It resolves the actor from ambient policy, validates
/// every target against the canonical campaign world, and delegates the atomic write to the generic
/// application ECS transaction owner.
/// </summary>
public interface IReviewedKnowledgeStateSynchronizer
{
    Task<ReviewedKnowledgeStateSyncResult> SynchronizeAsync(
        ReviewedKnowledgeStateSyncRequest request,
        bool dryRun,
        CancellationToken cancellationToken = default);
}
