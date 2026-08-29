using DantesRoleplay.Retrieval;

namespace DantesRoleplay.World;

public sealed record KnowledgeFactAnswerRequest(
    string WorldId,
    string Question,
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? SubjectIds = null,
    long? AsOfMinute = null,
    int CandidateLimit = 12);

public sealed record KnowledgeFactCitation(string KnowledgeId, string Kind);

public sealed record KnowledgeFactStatement(
    string Text,
    IReadOnlyList<KnowledgeFactCitation> Citations);

public sealed record KnowledgeFactAnswerResult(
    string WorldId,
    long AsOfMinute,
    string Mode,
    string NormalizedQuestion,
    bool Unknown,
    IReadOnlyList<string> SelectedFactIds,
    IReadOnlyList<KnowledgeFactStatement> Statements,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<KnowledgeHybridSearchHit> Candidates,
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

    public static KnowledgeFactAnswerResult Fail(string worldId, string code, string message) =>
        new(worldId, 0, "none", "", true, [], [], [], [], ErrorCode: code, ErrorMessage: message);
}

/// <summary>Trusted-GM Mode A answering. This is not a player authorization boundary.</summary>
public interface IKnowledgeFactAnswerCoordinator
{
    Task<KnowledgeFactAnswerResult> AnswerAsync(
        KnowledgeFactAnswerRequest request,
        CancellationToken cancellationToken = default);
}
