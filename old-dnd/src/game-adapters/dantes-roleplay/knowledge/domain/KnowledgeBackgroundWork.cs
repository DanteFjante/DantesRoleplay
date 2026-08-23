namespace DantesRoleplay.World;

public enum KnowledgeBackgroundJobKind
{
    EmbeddingSync,
    KnowledgeProposals
}

public sealed record KnowledgeBackgroundEnqueueRequest(
    KnowledgeBackgroundJobKind Kind,
    string WorldId,
    IReadOnlyList<string>? KnowledgeIds = null);

public sealed record KnowledgeBackgroundJobSnapshot(
    string JobId,
    KnowledgeBackgroundJobKind Kind,
    string WorldId,
    string Status,
    int Attempt,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string Model = "",
    string ModelRevision = "",
    string InputFingerprint = "",
    string SafeSummary = "",
    string FallbackCode = "",
    string ErrorCode = "",
    string ErrorMessage = "",
    string ModelProfile = "");

public sealed record KnowledgeAliasProposal(string KnowledgeId, IReadOnlyList<string> Aliases);
public sealed record KnowledgeTagProposal(string KnowledgeId, IReadOnlyList<string> Tags);
public sealed record KnowledgePairProposal(IReadOnlyList<string> KnowledgeIds, string Reason);

public sealed record KnowledgeProposalSet(
    string JobId,
    string WorldId,
    string SourceFingerprint,
    IReadOnlyList<KnowledgeAliasProposal> Aliases,
    IReadOnlyList<KnowledgeTagProposal> Tags,
    IReadOnlyList<KnowledgePairProposal> Duplicates,
    IReadOnlyList<KnowledgePairProposal> Contradictions);

/// <summary>Trusted-host review queue. Results are proposals only and never mutate knowledge.</summary>
public interface IKnowledgeBackgroundQueue
{
    Task<KnowledgeBackgroundJobSnapshot> EnqueueAsync(
        KnowledgeBackgroundEnqueueRequest request,
        CancellationToken cancellationToken = default);

    KnowledgeBackgroundJobSnapshot? Get(string jobId);

    KnowledgeProposalSet? GetProposal(string jobId);

    bool Cancel(string jobId);
}
