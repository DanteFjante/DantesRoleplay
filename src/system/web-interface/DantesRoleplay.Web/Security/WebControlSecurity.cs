using System.Net;
using System.Security.Claims;
using DantesRoleplay.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Web.Security;

public sealed record WebControlRequestDecision(
    bool Allowed,
    int StatusCode,
    ClaimsPrincipal? Principal,
    AuthorizationAuditEvidence Evidence,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Applies the control-center capability and same-origin boundary without reading request bodies or
/// invoking an owner. Capabilities are selected by server route mapping, never by browser input.
/// </summary>
public sealed class WebControlRequestGuard(WebPrivateOperatorGuard operators)
{
    public WebControlRequestDecision Evaluate(
        HttpContext context,
        PrivateOperatorCapability capability,
        bool mutation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var operatorDecision = operators.Evaluate(context, capability);
        if (!operatorDecision.Allowed)
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                operatorDecision.ErrorCode ?? "CONTROL_OPERATOR_DENIED",
                operatorDecision.ErrorMessage ?? "Private operator access is required.");
        }

        if (!IsAllowedCapability(capability, mutation))
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                "CONTROL_CAPABILITY_DENIED",
                "This control endpoint is not mapped to an allowed capability.");
        }

        var expectedMethod = mutation
            ? HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            : HttpMethods.IsGet(context.Request.Method);
        if (!expectedMethod)
        {
            return Denied(
                StatusCodes.Status405MethodNotAllowed,
                operatorDecision.Evidence,
                "CONTROL_METHOD_DENIED",
                "Control reads use GET and control changes use POST or PUT.");
        }

        if (!mutation)
        {
            return Allowed(operatorDecision);
        }

        if (!IsJson(context.Request.ContentType))
        {
            return Denied(
                StatusCodes.Status415UnsupportedMediaType,
                operatorDecision.Evidence,
                "CONTROL_JSON_REQUIRED",
                "Control changes require the application/json content type.");
        }

        var tailscale = string.Equals(
            operatorDecision.Principal!.Identity?.AuthenticationType,
            WebAccessPolicy.TailscaleAuthenticationType,
            StringComparison.Ordinal);
        var requestHost = NormalizeHost(context.Request.Host.Host);
        if (requestHost is null || (!tailscale && !IsLoopbackHost(requestHost)))
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                "CONTROL_HOST_DENIED",
                "Control changes require the approved local or private web host.");
        }

        var origins = context.Request.Headers.Origin;
        if (origins.Count != 1 || string.IsNullOrWhiteSpace(origins[0]))
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                "CONTROL_ORIGIN_REQUIRED",
                "Control changes require one same-origin browser Origin header.");
        }

        if (!TryReadSerializedOrigin(origins[0]!, out var origin))
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                "CONTROL_ORIGIN_DENIED",
                "The browser Origin is not an approved serialized origin.");
        }

        var expectedScheme = tailscale ? Uri.UriSchemeHttps : context.Request.Scheme;
        if (!IsHttpScheme(expectedScheme) ||
            !string.Equals(origin!.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(NormalizeHost(origin.Host), requestHost, StringComparison.OrdinalIgnoreCase) ||
            EffectivePort(origin.Scheme, origin.IsDefaultPort ? null : origin.Port) !=
            EffectivePort(expectedScheme, context.Request.Host.Port))
        {
            return Denied(
                StatusCodes.Status403Forbidden,
                operatorDecision.Evidence,
                "CONTROL_ORIGIN_DENIED",
                "The browser Origin does not match the approved request origin.");
        }

        return Allowed(operatorDecision);
    }

    private static WebControlRequestDecision Allowed(WebPrivateOperatorDecision decision) =>
        new(
            true,
            StatusCodes.Status200OK,
            decision.Principal,
            decision.Evidence);

    private static WebControlRequestDecision Denied(
        int statusCode,
        AuthorizationAuditEvidence evidence,
        string errorCode,
        string errorMessage) =>
        new(false, statusCode, null, evidence, errorCode, errorMessage);

    private static bool IsAllowedCapability(
        PrivateOperatorCapability capability,
        bool mutation) =>
        mutation
            ? capability is PrivateOperatorCapability.Modify or
                PrivateOperatorCapability.ControlPagesWrite or
                PrivateOperatorCapability.ControlSettingsWrite or
                PrivateOperatorCapability.ControlAiMessage or
                PrivateOperatorCapability.ControlCodexApprove or
                PrivateOperatorCapability.TriggerAdministrationWrite
            : capability is PrivateOperatorCapability.ControlRead or
                PrivateOperatorCapability.TriggerAdministrationRead;

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSerializedOrigin(string value, out Uri? origin)
    {
        origin = null;
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !IsHttpScheme(candidate.Scheme) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            candidate.AbsolutePath != "/")
        {
            return false;
        }

        origin = candidate;
        return true;
    }

    private static bool IsHttpScheme(string? scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static int EffectivePort(string scheme, int? port) =>
        port ?? (string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string? NormalizeHost(string? host)
    {
        var normalized = host?.Trim().Trim('[', ']').TrimEnd('.');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed class WebControlRequestFilter(WebControlRequestGuard guard)
{
    private const string AuthorizationEvidenceItem = "dantes-roleplay.control.authorization-evidence";

    public static AuthorizationAuditEvidence GetAuthorizationEvidence(HttpContext context) =>
        context.Items.TryGetValue(AuthorizationEvidenceItem, out var value) &&
        value is AuthorizationAuditEvidence evidence
            ? evidence
            : throw new InvalidOperationException("Control authorization evidence is unavailable.");

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        PrivateOperatorCapability capability,
        bool mutation)
    {
        WebInterfaceSecurity.ApplyHeaders(context.HttpContext.Response);
        var decision = guard.Evaluate(context.HttpContext, capability, mutation);
        if (!decision.Allowed)
        {
            return Results.Json(
                new
                {
                    error = decision.ErrorCode,
                    message = decision.ErrorMessage
                },
                statusCode: decision.StatusCode);
        }

        context.HttpContext.User = decision.Principal!;
        context.HttpContext.Items[AuthorizationEvidenceItem] = decision.Evidence;
        return await next(context);
    }
}

public static class WebControlEndpointConventions
{
    public const string RoutePrefix = "/api/control";
    private const int MaximumRelativePatternLength = 160;

    public static RouteHandlerBuilder MapDantesRoleplayControlGet(
        this IEndpointRouteBuilder endpoints,
        string relativePattern,
        Delegate handler) =>
        MapDantesRoleplayControlGet(endpoints, relativePattern,
            PrivateOperatorCapability.ControlRead, handler);

    public static RouteHandlerBuilder MapDantesRoleplayControlGet(
        this IEndpointRouteBuilder endpoints,
        string relativePattern,
        PrivateOperatorCapability capability,
        Delegate handler) =>
        Map(
            endpoints,
            HttpMethods.Get,
            relativePattern,
            capability,
            mutation: false,
            handler);

    public static RouteHandlerBuilder MapDantesRoleplayControlPost(
        this IEndpointRouteBuilder endpoints,
        string relativePattern,
        PrivateOperatorCapability capability,
        Delegate handler) =>
        Map(endpoints, HttpMethods.Post, relativePattern, capability, mutation: true, handler);

    public static RouteHandlerBuilder MapDantesRoleplayControlPut(
        this IEndpointRouteBuilder endpoints,
        string relativePattern,
        PrivateOperatorCapability capability,
        Delegate handler) =>
        Map(endpoints, HttpMethods.Put, relativePattern, capability, mutation: true, handler);

    private static RouteHandlerBuilder Map(
        IEndpointRouteBuilder endpoints,
        string method,
        string relativePattern,
        PrivateOperatorCapability capability,
        bool mutation,
        Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        var pattern = BuildPattern(relativePattern);
        if (mutation && capability is not (
            PrivateOperatorCapability.Modify or
            PrivateOperatorCapability.ControlPagesWrite or
            PrivateOperatorCapability.ControlSettingsWrite or
            PrivateOperatorCapability.ControlAiMessage or
            PrivateOperatorCapability.ControlCodexApprove or
            PrivateOperatorCapability.TriggerAdministrationWrite))
        {
            throw new ArgumentOutOfRangeException(
                nameof(capability),
                "Control changes require one of the closed control mutation capabilities.");
        }

        var route = endpoints.MapMethods(pattern, [method], handler);
        route.AddEndpointFilterFactory((_, next) => async invocation =>
        {
            var filter = invocation.HttpContext.RequestServices
                .GetRequiredService<WebControlRequestFilter>();
            return await filter.InvokeAsync(invocation, next, capability, mutation);
        });
        route.RequireRateLimiting(
            mutation
                ? WebInterfaceSecurity.UploadRateLimitPolicy
                : WebInterfaceSecurity.ReadRateLimitPolicy);
        return route;
    }

    private static string BuildPattern(string relativePattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePattern);
        if (relativePattern.Length > MaximumRelativePatternLength ||
            relativePattern[0] != '/' ||
            relativePattern.StartsWith("//", StringComparison.Ordinal) ||
            relativePattern.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            relativePattern.Contains("..", StringComparison.Ordinal) ||
            relativePattern.Contains('\\') ||
            relativePattern.Contains('?') ||
            relativePattern.Contains('#') ||
            relativePattern.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Control route patterns must be bounded relative paths below /api/control.",
                nameof(relativePattern));
        }

        return RoutePrefix + relativePattern;
    }
}
