using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Retrieval;

public sealed record KnowledgeVectorGeneration(
    string Id,
    EmbeddingProviderIdentity Embedding,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeVectorDocument(
    string KnowledgeId,
    string WorldId,
    string ContentHash,
    float[] Vector);

public sealed record KnowledgeVectorCandidate(string KnowledgeId, double Distance);

/// <summary>
/// Provider-neutral derived vector index. Canonical knowledge remains in world entities,
/// components, relationships, and events.
/// </summary>
public interface IKnowledgeVectorIndex
{
    Task<IReadOnlyDictionary<string, string>> ReadContentHashesAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        CancellationToken cancellationToken = default);

    Task ReplaceWorldAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        IReadOnlyList<KnowledgeVectorDocument> documents,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        KnowledgeVectorGeneration generation,
        IReadOnlyList<KnowledgeVectorDocument> documents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeVectorCandidate>> SearchAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        float[] query,
        int limit,
        CancellationToken cancellationToken = default);

    Task MarkGenerationStaleAsync(
        string generationId,
        CancellationToken cancellationToken = default);

    Task MarkOtherGenerationsStaleAsync(
        string activeGenerationId,
        CancellationToken cancellationToken = default);
}
