using DantesRoleplay.EcsEffects;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// A private player-notebook request. Ambient host policy owns the principal, role, actor, and
/// world; callers can select only the campaign and harmless presentation filters.
/// </summary>
public sealed record AuthorizedKnowledgeNotebookRequest(
    string CampaignId,
    string? Query = null,
    IReadOnlyList<string>? Kinds = null,
    int Limit = 100);

public sealed record AuthorizedKnowledgeNotebookEntry(
    string Text,
    string Stance,
    string PresentationKind);

/// <summary>
/// A player-safe location label derived only from already-admitted knowledge. It carries neither
/// an entity ID nor location data; its entries repeat notebook content that is visible already.
/// </summary>
public sealed record AuthorizedKnowledgeNotebookLocation(
    string Name,
    IReadOnlyList<AuthorizedKnowledgeNotebookEntry> Entries);

public sealed record AuthorizedKnowledgeNotebookResult(
    string Status,
    IReadOnlyList<AuthorizedKnowledgeNotebookEntry> Entries,
    IReadOnlyList<AuthorizedKnowledgeNotebookLocation> Locations,
    string ErrorCode = "")
{
    public static AuthorizedKnowledgeNotebookResult Denied() =>
        new("denied", [], [], "KNOWLEDGE_AUDIENCE_DENIED");

    public static AuthorizedKnowledgeNotebookResult Unavailable(string code = "KNOWLEDGE_UNAVAILABLE") =>
        new("unavailable", [], [], code);
}

public interface IAuthorizedKnowledgeNotebookReader
{
    Task<AuthorizedKnowledgeNotebookResult> ReadAsync(
        AuthorizedKnowledgeNotebookRequest request,
        CancellationToken cancellationToken = default);
}

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
