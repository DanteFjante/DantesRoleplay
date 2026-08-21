using DantesRoleplay.Retrieval;

namespace DantesRoleplay.World;

public sealed record KnowledgeHybridRebuildResult(
    string WorldId,
    int LexicalDocuments,
    int VectorDocuments,
    bool VectorReady,
    string GenerationId = "",
    string FallbackCode = "",
    string FallbackMessage = "",
    int EmbeddedDocuments = 0);

public sealed record KnowledgeHybridSearchHit(
    string KnowledgeId,
    string Kind,
    string Status,
    string SubjectId,
    string Sensitivity,
    string Summary,
    double Score,
    int? LexicalRank,
    int? VectorRank,
    double? VectorDistance,
    bool ExactIdMatch);

public sealed record KnowledgeHybridSearchResult(
    string WorldId,
    long AsOfMinute,
    string Mode,
    IReadOnlyList<KnowledgeHybridSearchHit> Hits,
    string GenerationId = "",
    string FallbackCode = "",
    string FallbackMessage = "",
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => ErrorCode.Length == 0;

    public static KnowledgeHybridSearchResult Fail(string worldId, string code, string message) =>
        new(worldId, 0, "none", [], ErrorCode: code, ErrorMessage: message);
}

/// <summary>
/// Trusted-GM hybrid retrieval. It is not an audience authorization boundary and is deliberately
/// not exposed through MCP or a player transport.
/// </summary>
public interface IKnowledgeHybridSearchCoordinator
{
    Task<KnowledgeHybridRebuildResult> RebuildWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeHybridRebuildResult> SynchronizeWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeHybridSearchResult> SearchAsync(
        KnowledgeLexicalSearchRequest request,
        CancellationToken cancellationToken = default);
}
