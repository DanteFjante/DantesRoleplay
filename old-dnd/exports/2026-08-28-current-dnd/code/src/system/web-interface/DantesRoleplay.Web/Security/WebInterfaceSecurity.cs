using System.Net;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Security;

public static class WebInterfaceSecurity
{
    public const string ReadRateLimitPolicy = "dantes-web-read";
    public const string UploadRateLimitPolicy = "dantes-web-upload";
    public const string StreamRateLimitPolicy = "dantes-web-stream";

    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'none'; " +
        "connect-src 'self'; " +
        "font-src 'self' data:; " +
        "form-action 'none'; " +
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

    public WebInterfaceSecurityFilter(WebPrivateOperatorGuard guard)
    {
        this.guard = guard;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        WebInterfaceSecurity.ApplyHeaders(context.HttpContext.Response);
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
