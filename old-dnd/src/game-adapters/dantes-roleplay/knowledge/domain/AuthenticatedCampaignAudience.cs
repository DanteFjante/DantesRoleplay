namespace DantesRoleplay.Security;

/// <summary>Closed roles that a transport-authenticated campaign caller may receive.</summary>
public static class CampaignAudienceRoles
{
    public const string GameMaster = "gm";
    public const string Actor = "actor";

    public static bool IsSupported(string? role) => role is GameMaster or Actor;
}

/// <summary>
/// A host-issued audience grant. The transport adapter owns authentication and maps its principal
/// into this small, explicit capability; knowledge code never accepts a caller supplied actor id.
/// </summary>
public sealed record AuthenticatedCampaignAudienceGrant(
    string PrincipalId,
    string CampaignId,
    string Role,
    string? ActorId,
    string PolicyRevision)
{
    public bool Valid =>
        Id(PrincipalId) && Id(CampaignId) && Id(PolicyRevision) &&
        CampaignAudienceRoles.IsSupported(Role) &&
        (Role == CampaignAudienceRoles.GameMaster
            ? ActorId is null
            : ActorId is not null && Id(ActorId) && ActorId.StartsWith("actor.", StringComparison.Ordinal));

    private static bool Id(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
}

public sealed record AuthenticatedCampaignAudienceResolution(
    AuthenticatedCampaignAudienceGrant? Grant,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Granted => Grant is not null && Grant.Valid && ErrorCode.Length == 0;

    public static AuthenticatedCampaignAudienceResolution Denied() =>
        new(null, "KNOWLEDGE_AUDIENCE_DENIED", "The caller is not authorized for this campaign knowledge request.");
}

/// <summary>
/// Host boundary for authenticated transport identity. Implementations read the current request
/// context themselves and must fail closed; callers cannot pass a principal, role, or actor id.
/// No default implementation is registered by the data-access package.
/// </summary>
public interface IAuthenticatedCampaignAudiencePolicy
{
    Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default);
}
