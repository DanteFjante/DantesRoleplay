using DantesRoleplay.Retrieval;

namespace DantesRoleplay.World;

public sealed record KnowledgeReadAgentRequest(
    string WorldId,
    string Question,
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? SubjectIds = null,
    long? AsOfMinute = null,
    int CandidateLimitPerRead = 8,
    int MaxReadCalls = 3,
    int MaxTotalCandidates = 20);

public sealed record KnowledgeReadOperation(
    int Step,
    string Operation,
    string Query,
    IReadOnlyList<string> ReturnedKnowledgeIds);

public sealed record KnowledgeReadAgentResult(
    string WorldId,
    long AsOfMinute,
    string Mode,
    string NormalizedQuestion,
    bool Unknown,
    IReadOnlyList<string> SelectedFactIds,
    IReadOnlyList<KnowledgeFactStatement> Statements,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<KnowledgeHybridSearchHit> Candidates,
    IReadOnlyList<KnowledgeReadOperation> Reads,
    LocalModelIdentity? Model = null,
    long ElapsedMilliseconds = 0,
    int PromptTokens = 0,
    int OutputTokens = 0,
    string FallbackCode = "",
    string FallbackMessage = "",
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => ErrorCode.Length == 0;

    public static KnowledgeReadAgentResult Fail(string worldId, string code, string message) =>
        new(worldId, 0, "none", "", true, [], [], [], [], [], ErrorCode: code, ErrorMessage: message);
}

/// <summary>
/// Trusted-GM Mode B. The host owns every read and supplies only bounded canonical results to the
/// local model. This is not a player authorization boundary.
/// </summary>
public interface IKnowledgeReadAgentCoordinator
{
    Task<KnowledgeReadAgentResult> AnswerAsync(
        KnowledgeReadAgentRequest request,
        CancellationToken cancellationToken = default);
}
