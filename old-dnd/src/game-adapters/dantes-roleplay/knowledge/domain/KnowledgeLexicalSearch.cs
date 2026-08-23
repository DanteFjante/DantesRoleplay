namespace DantesRoleplay.World;

public sealed record KnowledgeLexicalSearchHit(
    string KnowledgeId,
    string Kind,
    string Status,
    string SubjectId,
    string Sensitivity,
    string Summary,
    double Rank);

public sealed record KnowledgeLexicalSearchResult(
    string WorldId,
    long AsOfMinute,
    IReadOnlyList<KnowledgeLexicalSearchHit> Hits,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => ErrorCode.Length == 0;
    public static KnowledgeLexicalSearchResult Fail(string world, string code, string message) => new(world, 0, [], code, message);
}

/// <summary>Trusted-GM lexical retrieval only. This does not bind a caller to an actor or authorize players.</summary>
public interface IKnowledgeLexicalSearchCoordinator
{
    Task<int> RebuildWorldAsync(string worldId, CancellationToken cancellationToken = default);

    Task<KnowledgeLexicalSearchResult> SearchAsync(
        DantesRoleplay.Retrieval.KnowledgeLexicalSearchRequest request,
        CancellationToken cancellationToken = default);
}
