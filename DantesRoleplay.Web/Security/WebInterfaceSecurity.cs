using System.Net;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Security;

public static class WebInterfaceSecurity
{
    public const string ReadRateLimitPolicy = "dantes-web-read";
    public const string UploadRateLimitPolicy = "dantes-web-upload";
    public const string StreamRateLimitPolicy = "dantes-web-stream";
    // A complete authored world requires about 1,100 small reads. Allow normal view changes
    // and reloads while retaining a finite allowance; browser reads also limit concurrency.
    public const int ReadRequestsPerMinute = 6_000;
    public const int UploadRequestsPerMinute = 10;
    public const int ConcurrentStreams = 4;

    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'none'; " +
        "connect-src 'self'; " +
        "font-src 'self' data:; " +
        "form-action 'none'; " +
        "frame-src 'self'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' data: blob:; " +
        "object-src 'none'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "worker-src 'self' blob:";

    public static bool IsLoopback(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(address);

    public static void ApplyHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.Append("Referrer-Policy", "no-referrer");
        response.Headers.XFrameOptions = "DENY";
        response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
        response.Headers.Append("Cross-Origin-Resource-Policy", "same-origin");
        response.Headers.Append(
            "Permissions-Policy",
            "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()");
    }
}

public sealed class WebInterfaceSecurityFilter : IEndpointFilter
{
    private readonly WebPrivateOperatorGuard guard;
    private readonly WebAccessPolicy access;

    public WebInterfaceSecurityFilter(WebPrivateOperatorGuard guard, WebAccessPolicy access)
    {
        this.guard = guard;
        this.access = access;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        WebInterfaceSecurity.ApplyHeaders(context.HttpContext.Response);
        var accessDecision = access.Evaluate(context.HttpContext);
        if (accessDecision is { Allowed: true, Mode: WebAccessMode.AnonymousPublic or WebAccessMode.InvitedTailscale })
        {
            if (!(HttpMethods.IsGet(context.HttpContext.Request.Method) ||
                    HttpMethods.IsHead(context.HttpContext.Request.Method)) ||
                !WebAccessPolicy.IsPlayerSafePublicPath(context.HttpContext.Request.Path))
            {
                return Results.Json(new { error = "PLAYER_SAFE_ROUTE_REQUIRED",
                    message = "This route is not available to unprivileged visitors." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            context.HttpContext.User = WebAccessPolicy.CreatePrincipal(accessDecision);
            return await next(context);
        }

        var decision = guard.Evaluate(context.HttpContext);
        if (!decision.Allowed)
        {
            return Results.Json(
                new
                {
                    error = decision.ErrorCode,
                    message = decision.ErrorMessage
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        context.HttpContext.User = decision.Principal!;
        return await next(context);
    }
}
