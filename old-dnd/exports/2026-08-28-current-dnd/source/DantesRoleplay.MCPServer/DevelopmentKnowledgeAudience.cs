using DantesRoleplay.Security;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Explicit local-development substitute for a real authenticated campaign audience. It is
/// disabled by default and intentionally represents one fixed seat, never a caller-selected role
/// or actor. Remove this registration when a real transport identity provider is available.
/// </summary>
public sealed class DevelopmentKnowledgeAudienceOptions
{
    public bool Enabled { get; init; }
    public string PrincipalId { get; init; } = "development.local";
    public string CampaignId { get; init; } = "";
    public string Role { get; init; } = CampaignAudienceRoles.GameMaster;
    public string? ActorId { get; init; }

    internal string? Validate()
    {
        if (!Enabled) return null;
        if (!Id(PrincipalId) || !Id(CampaignId)) return "Development knowledge audience requires bounded principalId and campaignId.";
        if (!CampaignAudienceRoles.IsSupported(Role)) return "Development knowledge audience role must be gm or actor.";
        if (Role == CampaignAudienceRoles.GameMaster && ActorId is not null) return "Development GM audience must not specify actorId.";
        if (Role == CampaignAudienceRoles.Actor && (!Id(ActorId) || !ActorId!.StartsWith("actor.", StringComparison.Ordinal))) return "Development actor audience requires an actor.* actorId.";
        return null;
    }

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
}

/// <summary>Host-only fixed development seat. The Program loopback middleware is its second gate.</summary>
public sealed class DevelopmentCampaignAudiencePolicy(DevelopmentKnowledgeAudienceOptions options) : IAuthenticatedCampaignAudiencePolicy
{
    private readonly DevelopmentKnowledgeAudienceOptions _options = options;

    public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || campaignId != _options.CampaignId) return Task.FromResult(AuthenticatedCampaignAudienceResolution.Denied());
        return Task.FromResult(new AuthenticatedCampaignAudienceResolution(new(
            _options.PrincipalId,
            _options.CampaignId,
            _options.Role,
            _options.ActorId,
            "development-static-v1")));
    }
}

/// <summary>Safe disabled-host placeholder so the MCP query surface remains callable.</summary>
public sealed class UnavailableKnowledgeAnswerCoordinator : DantesRoleplay.World.IAuthorizedKnowledgeAnswerCoordinator
{
    public Task<DantesRoleplay.World.AuthorizedKnowledgeAnswerResult> AnswerAsync(
        DantesRoleplay.World.AuthorizedKnowledgeAnswerRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DantesRoleplay.World.AuthorizedKnowledgeAnswerResult(
            "unavailable", [], ["Knowledge answers are not enabled for this host."],
            "KNOWLEDGE_AUDIENCE_UNAVAILABLE", "Knowledge audience is not configured."));
}
