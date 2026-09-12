using System.Security.Claims;
using DantesRoleplay.Authorization;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Security;

public sealed record WebPlatformAccessDecision(
    bool Authenticated,
    ClaimsPrincipal? Principal,
    TrustedPrincipalContext? TrustedPrincipal,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Authenticates a web transport identity for the platform authorization boundary. This guard does
/// not select capabilities, inspect grants, or authorize an operation.
/// </summary>
public sealed class WebPlatformAccessGuard(WebAccessPolicy access)
{
    public WebPlatformAccessDecision Evaluate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var accessDecision = access.Evaluate(context);
        if (!accessDecision.Allowed)
        {
            return new(
                false,
                null,
                null,
                accessDecision.ErrorCode,
                accessDecision.ErrorMessage);
        }

        if (accessDecision.Mode == WebAccessMode.AnonymousPublic)
        {
            return new(
                false,
                null,
                null,
                "PLATFORM_AUTHENTICATION_REQUIRED",
                "The platform requires an authenticated local or Tailscale identity.");
        }

        var trusted = WebTrustedPrincipalContextFactory.Create(accessDecision);
        if (!trusted.Verified)
        {
            return new(
                false,
                null,
                null,
                trusted.FailureCode,
                "The web identity could not be authenticated for platform access.");
        }

        return new(
            true,
            WebAccessPolicy.CreatePrincipal(accessDecision),
            trusted);
    }
}
