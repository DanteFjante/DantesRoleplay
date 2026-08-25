using System.Security.Claims;
using DantesRoleplay.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Web.Security;

public sealed class WebRemoteAccessOptions
{
    public const string SectionName = "WebInterface:RemoteAccess";

    public bool Enabled { get; set; }

    public string? TailscaleHost { get; set; }

    public string[] AllowedLogins { get; set; } = [];
}

public enum WebAccessMode
{
    Local,
    Tailscale
}

public sealed record WebAccessDecision(
    bool Allowed,
    WebAccessMode Mode,
    string? Login = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record WebPrivateOperatorDecision(
    bool Allowed,
    ClaimsPrincipal? Principal,
    AuthorizationAuditEvidence Evidence,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed class WebAccessPolicy(IOptions<WebRemoteAccessOptions> options)
{
    public const string TailscaleLoginHeader = "Tailscale-User-Login";
    public const string LocalAuthenticationType = "DantesRoleplay.Local";
    public const string TailscaleAuthenticationType = "TailscaleServe";

    private readonly WebRemoteAccessOptions remote = options.Value;

    public WebAccessDecision Evaluate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!WebInterfaceSecurity.IsLoopback(context.Connection.RemoteIpAddress))
        {
            return Denied(
                "LOCAL_ACCESS_REQUIRED",
                "The web interface accepts direct requests only from this computer.");
        }

        var host = NormaliseHost(context.Request.Host.Host);
        var login = context.Request.Headers[TailscaleLoginHeader].FirstOrDefault();
        if (!IsRemoteCandidate(host, login))
        {
            return new WebAccessDecision(true, WebAccessMode.Local);
        }

        var configuredHost = NormaliseHost(remote.TailscaleHost);
        if (!remote.Enabled ||
            configuredHost is null ||
            !IsTailscaleHost(configuredHost) ||
            !string.Equals(host, configuredHost, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(
                "REMOTE_ACCESS_DENIED",
                "This private web hostname is not enabled for remote access.");
        }

        if (string.IsNullOrWhiteSpace(login))
        {
            return Denied(
                "REMOTE_IDENTITY_REQUIRED",
                "A verified Tailscale user identity is required.");
        }

        var allowed = (remote.AllowedLogins ?? []).Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            string.Equals(candidate.Trim(), login, StringComparison.OrdinalIgnoreCase));
        return allowed
            ? new WebAccessDecision(true, WebAccessMode.Tailscale, login)
            : Denied(
                "REMOTE_ACCESS_DENIED",
                "This Tailscale user is not allowed to use the web interface.");
    }

    public static bool IsRemoteCandidate(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsRemoteCandidate(
            NormaliseHost(request.Host.Host),
            request.Headers[TailscaleLoginHeader].FirstOrDefault());
    }

    public static bool IsAllowedRemotePath(PathString path) =>
        path == "/" ||
        path.StartsWithSegments("/ui") ||
        path.StartsWithSegments("/api/pages") ||
        path.StartsWithSegments("/api/data") ||
        path.StartsWithSegments("/api/changes") ||
        path.StartsWithSegments("/api/session") ||
        path.StartsWithSegments(WebControlEndpointConventions.RoutePrefix);

    public static ClaimsPrincipal CreatePrincipal(WebAccessDecision decision)
    {
        if (!decision.Allowed)
        {
            throw new ArgumentException("A denied access decision cannot create a principal.", nameof(decision));
        }

        var authenticationType = decision.Mode == WebAccessMode.Tailscale
            ? TailscaleAuthenticationType
            : LocalAuthenticationType;
        var name = decision.Login ?? "local";
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, name),
                new Claim(ClaimTypes.Name, name),
                new Claim("dantesroleplay:access-mode", decision.Mode.ToString().ToLowerInvariant())
            ],
            authenticationType));
    }

    private static WebAccessDecision Denied(string code, string message) =>
        new(false, WebAccessMode.Local, ErrorCode: code, ErrorMessage: message);

    private static bool IsRemoteCandidate(string? host, string? login) =>
        IsTailscaleHost(host) || !string.IsNullOrWhiteSpace(login);

    private static bool IsTailscaleHost(string? host) =>
        host is not null && host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase);

    private static string? NormaliseHost(string? host)
    {
        var normalised = host?.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(normalised) ? null : normalised;
    }
}

/// <summary>Converts accepted web identity into the provider-neutral private-operator boundary.</summary>
public sealed class WebPrivateOperatorGuard(
    WebAccessPolicy access,
    IPrivateOperatorAuthorizationPolicy authorization)
{
    public WebPrivateOperatorDecision Evaluate(
        HttpContext context,
        PrivateOperatorCapability? selectedCapability = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var accessDecision = access.Evaluate(context);
        var principal = accessDecision.Allowed
            ? WebTrustedPrincipalContextFactory.Create(accessDecision)
            : TrustedPrincipalContext.Unauthenticated(accessDecision.ErrorCode ?? "WEB_IDENTITY_UNAVAILABLE");
        var capability = selectedCapability ??
            (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
                ? PrivateOperatorCapability.Read
                : PrivateOperatorCapability.Modify);
        var decision = authorization.Evaluate(new(
            principal,
            capability,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            Correlation(context.TraceIdentifier)));

        if (!accessDecision.Allowed)
            return new(false, null, decision.Evidence, accessDecision.ErrorCode, accessDecision.ErrorMessage);
        if (!decision.Allowed)
            return new(false, null, decision.Evidence, "PRIVATE_OPERATOR_DENIED", decision.Recovery);
        return new(true, WebAccessPolicy.CreatePrincipal(accessDecision), decision.Evidence);
    }

    private static string Correlation(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "request" : value.Length <= 128 ? value : value[..128];
}

public static class WebTrustedPrincipalContextFactory
{
    public static TrustedPrincipalContext Create(WebAccessDecision decision)
    {
        if (!decision.Allowed)
            return TrustedPrincipalContext.Unauthenticated(decision.ErrorCode ?? "WEB_IDENTITY_UNAVAILABLE");
        var method = decision.Mode == WebAccessMode.Tailscale ? "tailscale-serve" : "local-loopback";
        var subject = decision.Mode == WebAccessMode.Tailscale
            ? decision.Login?.Trim().ToLowerInvariant()
            : "local-operator";
        if (string.IsNullOrWhiteSpace(subject))
            return TrustedPrincipalContext.Unauthenticated("WEB_IDENTITY_UNAVAILABLE");
        return PrivateOperatorPrincipal.Create(method, subject);
    }
}

public static class WebRemoteAccessApplicationBuilderExtensions
{
    public static IApplicationBuilder UseDantesRoleplayRemoteWebBoundary(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Use(async (context, next) =>
        {
            if (WebAccessPolicy.IsRemoteCandidate(context.Request) &&
                !WebAccessPolicy.IsAllowedRemotePath(context.Request.Path))
            {
                WebInterfaceSecurity.ApplyHeaders(context.Response);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "REMOTE_WEB_ROUTE_NOT_FOUND",
                        message = "This route is not part of the private web interface."
                    },
                    context.RequestAborted);
                return;
            }

            await next(context);
        });
    }
}
