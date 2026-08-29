namespace DantesRoleplay.Knowledge;

public enum KnowledgeAudienceRole
{
    GameMaster,
    Actor
}

/// <summary>
/// A host-issued campaign grant. Implementations resolve ambient identity themselves; no answer
/// request can nominate a principal, role, or actor.
/// </summary>
public sealed record KnowledgeAudienceGrant(
    string PrincipalId,
    string CampaignId,
    KnowledgeAudienceRole Role,
    string? ActorId,
    string PolicyRevision)
{
    public bool Valid =>
        Bounded(PrincipalId) && Bounded(CampaignId) && Bounded(PolicyRevision) &&
        (Role == KnowledgeAudienceRole.GameMaster
            ? ActorId is null
            : Bounded(ActorId));

    private static bool Bounded(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
}

public sealed record KnowledgeAudienceResolution(
    KnowledgeAudienceGrant? Grant,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Granted => Grant is { Valid: true } && ErrorCode.Length == 0;

    public static KnowledgeAudienceResolution Denied() =>
        new(null, "KNOWLEDGE_AUDIENCE_DENIED",
            "The caller is not authorized for this campaign knowledge request.");
}

/// <summary>
/// Host boundary for ambient authenticated or explicitly local-only identity. There is no default
/// implementation and its only caller input is the requested campaign.
/// </summary>
public interface IAuthorizedKnowledgeAudiencePolicy
{
    Task<KnowledgeAudienceResolution> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default);
}

public sealed record AuthorizedKnowledgeRequest(
    string CampaignId,
    string Question,
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? SubjectIds = null,
    long? AsOfMinute = null,
    int CandidateLimit = 12);

/// <summary>One perspective-safe statement. It intentionally has no canonical record identity.</summary>
public sealed record AuthorizedKnowledgeStatement(
    string Text,
    string Stance,
    string PresentationKind);

public sealed record AuthorizedKnowledgeResult(
    string Status,
    IReadOnlyList<AuthorizedKnowledgeStatement> Statements,
    IReadOnlyList<string> Unresolved,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Answered => Status == "answered";

    public static AuthorizedKnowledgeResult Denied() => new(
        "denied", [], ["You do not have access to this campaign knowledge."],
        "KNOWLEDGE_AUDIENCE_DENIED", "Knowledge access was denied.");

    public static AuthorizedKnowledgeResult Unknown(
        string code = "KNOWLEDGE_NOT_FOUND",
        string message = "No authorized knowledge supports an answer.") =>
        new("unknown", [], ["You do not have enough information to answer that."], code, message);
}

/// <summary>Internal host candidate. Its identity exists only for local citation validation.</summary>
public sealed record AuthorizedKnowledgeCandidate(
    string KnowledgeId,
    string Text,
    string Stance,
    string PresentationKind,
    string Revision);

public sealed record AuthorizedKnowledgeCandidateSet(
    bool Granted,
    bool ActorAudience,
    string PolicyRevision,
    string ScopeRevision,
    IReadOnlyList<AuthorizedKnowledgeCandidate> Candidates,
    bool FamiliarMatch,
    string ErrorCode = "")
{
    public static AuthorizedKnowledgeCandidateSet Denied() =>
        new(false, false, "", "", [], false, "KNOWLEDGE_AUDIENCE_DENIED");
}

public interface IAuthorizedKnowledgeCandidateResolver
{
    Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(
        AuthorizedKnowledgeRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAuthorizedKnowledgeCoordinator
{
    Task<AuthorizedKnowledgeResult> AnswerAsync(
        AuthorizedKnowledgeRequest request,
        CancellationToken cancellationToken = default);
}
