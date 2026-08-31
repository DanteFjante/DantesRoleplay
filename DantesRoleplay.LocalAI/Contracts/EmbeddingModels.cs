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

/// <summary>Provider-neutral text embedding boundary.</summary>
public interface ITextEmbeddingProvider
{
    Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingBatchResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
