using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace DantesRoleplay.Web.Security;

/// <summary>Applies the closed browser-access filter with one explicit traffic policy.</summary>
public static class WebEndpointConventions
{
    public static RouteHandlerBuilder RequireDantesRoleplayReadAccess(this RouteHandlerBuilder route) =>
        RequireAccess(route, WebInterfaceSecurity.ReadRateLimitPolicy);

    public static RouteHandlerBuilder RequireDantesRoleplayUploadAccess(this RouteHandlerBuilder route) =>
        RequireAccess(route, WebInterfaceSecurity.UploadRateLimitPolicy);

    public static RouteHandlerBuilder RequireDantesRoleplayStreamAccess(this RouteHandlerBuilder route) =>
        RequireAccess(route, WebInterfaceSecurity.StreamRateLimitPolicy);

    private static RouteHandlerBuilder RequireAccess(RouteHandlerBuilder route, string rateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route
            .AddEndpointFilter<WebInterfaceSecurityFilter>()
            .RequireRateLimiting(rateLimitPolicy);
    }
}
