namespace DantesRoleplay.Retrieval;

/// <summary>
/// Exact identity of one mutually compatible embedding generation. Vectors from different
/// identities must never be compared or stored in the same generation.
/// </summary>
public sealed record EmbeddingProviderIdentity(
    string Provider,
    string Model,
    string Revision,
    int Dimensions);

public sealed record EmbeddingProviderStatus(
    bool Ready,
    EmbeddingProviderIdentity? Identity,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public static EmbeddingProviderStatus Unavailable(string code, string message) =>
        new(false, null, code, message);
}

public sealed record EmbeddingBatchResult(
    EmbeddingProviderIdentity? Identity,
    IReadOnlyList<float[]> Vectors,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => Identity is not null && ErrorCode.Length == 0;

    public static EmbeddingBatchResult Failure(string code, string message) =>
        new(null, [], code, message);
}

/// <summary>
/// Host-level text embedding boundary. Game mechanics never receive this dependency.
/// </summary>
public interface ITextEmbeddingProvider
{
    Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingBatchResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}

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
