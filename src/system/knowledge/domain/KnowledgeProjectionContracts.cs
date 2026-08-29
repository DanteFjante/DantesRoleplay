namespace DantesRoleplay.Knowledge;

public sealed record CanonicalKnowledgeDocument(
    string KnowledgeId,
    string WorldId,
    string Kind,
    string Status,
    bool Archived,
    string SubjectId,
    string SubjectName,
    bool SubjectIsActiveLocation,
    long? ValidFromMinute,
    long? ValidUntilMinute,
    string DisplayText,
    string SearchText,
    string PresentationKind,
    string Revision);

public sealed record KnowledgeCampaignScope(
    string CampaignId,
    string WorldId,
    long CurrentMinute,
    string Revision);

public sealed record KnowledgeCampaignProjection(
    KnowledgeCampaignScope Scope,
    string Revision,
    IReadOnlyList<CanonicalKnowledgeDocument> Documents);

public interface IKnowledgeCanonicalSource
{
    Task<KnowledgeCampaignScope?> ReadCampaignScopeAsync(
        KnowledgeApplicationBinding binding,
        CancellationToken cancellationToken = default);

    Task<KnowledgeCampaignProjection?> ReadWorldAsync(
        KnowledgeApplicationBinding binding,
        KnowledgeCampaignScope scope,
        CancellationToken cancellationToken = default);

    Task<CanonicalKnowledgeDocument?> ReadDocumentAsync(
        KnowledgeApplicationBinding binding,
        string worldId,
        string knowledgeId,
        CancellationToken cancellationToken = default);
}

public sealed record EffectiveKnowledgeState(
    string KnowledgeId,
    string WorldId,
    string State,
    string SourceKind,
    string? SourceEntityId,
    string Revision);

public interface IKnowledgeEffectiveStateResolver
{
    Task<IReadOnlyDictionary<string, EffectiveKnowledgeState>> ResolveAllAsync(
        KnowledgeApplicationBinding binding,
        string actorId,
        string worldId,
        IReadOnlyList<string> knowledgeIds,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeLexicalRequest(
    string Query,
    IReadOnlyList<string>? Kinds,
    IReadOnlyList<string>? SubjectIds,
    long AsOfMinute,
    int Limit,
    IReadOnlySet<string>? AllowedKnowledgeIds);

public sealed record KnowledgeLexicalHit(CanonicalKnowledgeDocument Document, double Rank);

/// <summary>Derived deterministic retrieval. An allowlist is applied before ranking and limit.</summary>
public interface IKnowledgeLexicalRetriever
{
    IReadOnlyList<KnowledgeLexicalHit> Search(
        IReadOnlyList<CanonicalKnowledgeDocument> documents,
        KnowledgeLexicalRequest request);
}
