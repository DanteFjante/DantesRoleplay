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
        RequireAccess(route, WebInterfaceSecurity.UploadRateLimitPolicy)
            .AddEndpointFilter<WebUploadOriginFilter>();

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

/// <summary>Website admission does not authorize cross-origin browser mutations.</summary>
public sealed class WebUploadOriginFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var origins = context.Request.Headers.Origin;
        var publicVisitor = context.User.Identity?.AuthenticationType == WebAccessPolicy.AnonymousPublicAuthenticationType;
        // Existing local administrative clients need not impersonate a browser. Public visitors
        // must supply one exact same-origin value; browser cross-origin requests are always denied.
        if (!publicVisitor && origins.Count == 0) return next(invocation);
        if (origins.Count == 1 && Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) &&
            origin.Scheme == context.Request.Scheme && origin.Authority.Equals(context.Request.Host.Value,
                StringComparison.OrdinalIgnoreCase) && origin.UserInfo.Length == 0 &&
            origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0 &&
            origins[0]!.TrimEnd('/') == origin.GetLeftPart(UriPartial.Authority))
            return next(invocation);
        return ValueTask.FromResult<object?>(Results.Json(new { code = "WEB_ORIGIN_DENIED",
            message = "Changes must be sent from this website's origin." }, statusCode: StatusCodes.Status403Forbidden));
    }
}
