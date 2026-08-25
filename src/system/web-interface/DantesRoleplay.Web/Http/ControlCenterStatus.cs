using System.Security.Claims;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed record ControlCenterStatusDocument(
    string Status,
    ControlCenterAccessStatus Access,
    IReadOnlyList<ControlCenterPanelStatus> Panels);

public sealed record ControlCenterAccessStatus(
    string Mode,
    string? Login);

public sealed record ControlCenterPanelStatus(
    string Id,
    string State,
    string Message);

/// <summary>
/// Bounded read model for the control-center shell. It describes only the control boundary and
/// planned panel delivery; it is deliberately not a host, world, provider, or Codex health model.
/// </summary>
public static class ControlCenterStatus
{
    public const string CacheControl = "no-store";

    private static readonly IReadOnlyList<ControlCenterPanelStatus> Panels =
    [
        new("server-settings", "ready", "Versioned host setting overrides and restart status are available."),
        new("effect-history", "unavailable", "Past effects are not available yet."),
        new("assistant", "ready", "Durable local assistant conversations are available."),
        new("ecs-explorer", "unavailable", "ECS and contract browsing are not available yet."),
        new("site-editor", "ready", "Existing pages can be drafted, previewed, published, or rolled back.")
    ];

    public static ControlCenterStatusDocument Create(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var tailscale = string.Equals(
            principal.Identity?.AuthenticationType,
            WebAccessPolicy.TailscaleAuthenticationType,
            StringComparison.Ordinal);
        return new(
            "ready",
            new(tailscale ? "tailscale" : "local", tailscale ? principal.Identity?.Name : null),
            Panels);
    }

    public static void ApplyCacheHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.CacheControl = CacheControl;
    }
}
