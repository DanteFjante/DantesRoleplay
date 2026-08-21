namespace DantesRoleplay.World;

/// <summary>
/// Host-only player-safe knowledge request. Audience identity comes exclusively from
/// <c>IAuthenticatedCampaignAudiencePolicy</c>, not from this request.
/// </summary>
public sealed record AuthorizedKnowledgeAnswerRequest(
    string CampaignId,
    string Question,
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? SubjectIds = null,
    long? AsOfMinute = null,
    int CandidateLimit = 12);

/// <summary>One perspective-safe statement. It intentionally has no knowledge id or sensitivity.</summary>
public sealed record AuthorizedKnowledgeStatement(
    string Text,
    string Stance,
    string PresentationKind);

/// <summary>
/// Player-safe result surface. Canonical record ids, candidate lists, source kinds, and
/// sensitivity classifications never cross this boundary.
/// </summary>
public sealed record AuthorizedKnowledgeAnswerResult(
    string Status,
    IReadOnlyList<AuthorizedKnowledgeStatement> Statements,
    IReadOnlyList<string> Unresolved,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Answered => Status == "answered";

    public static AuthorizedKnowledgeAnswerResult Denied() =>
        new("denied", [], ["You do not have access to this campaign knowledge."], "KNOWLEDGE_AUDIENCE_DENIED", "Knowledge access was denied.");

    public static AuthorizedKnowledgeAnswerResult Unknown(string code = "KNOWLEDGE_NOT_FOUND") =>
        new("unknown", [], ["You do not have enough information to answer that."], code, "No authorized knowledge supports an answer.");
}

/// <summary>
/// Internal host candidate passed between authorization and answer orchestration. This is not a
/// transport contract; its canonical id is retained only to validate model citations locally.
/// </summary>
public sealed record AuthorizedKnowledgeCandidate(
    string KnowledgeId,
    string Text,
    string Stance,
    string PresentationKind,
    string Revision);

/// <summary>Host-only resolved input. A repeat resolution detects policy or knowledge changes.</summary>
public sealed record AuthorizedKnowledgeCandidateSet(
    bool Granted,
    bool ActorAudience,
    string PolicyRevision,
    IReadOnlyList<AuthorizedKnowledgeCandidate> Candidates,
    bool FamiliarMatch,
    string ErrorCode = "")
{
    public static AuthorizedKnowledgeCandidateSet Denied() => new(false, false, "", [], false, "KNOWLEDGE_AUDIENCE_DENIED");
}

/// <summary>Authorization, campaign binding, state resolution, and constrained retrieval boundary.</summary>
public interface IAuthorizedKnowledgeCandidateResolver
{
    Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(
        AuthorizedKnowledgeAnswerRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Answers only from candidates supplied by the authorization boundary.</summary>
public interface IAuthorizedKnowledgeAnswerCoordinator
{
    Task<AuthorizedKnowledgeAnswerResult> AnswerAsync(
        AuthorizedKnowledgeAnswerRequest request,
        CancellationToken cancellationToken = default);
}
