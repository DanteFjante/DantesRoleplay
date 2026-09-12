using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Web.Security;

public sealed class WebRemoteAccessOptions
{
    public const string SectionName = "WebInterface:RemoteAccess";

    public bool Enabled { get; set; }

    // Legacy explicit host opt-in: every network visitor receives the website's operator capabilities.
    // This does not create an invited identity or a standing platform grant.
    public bool AllowAnonymousPublicAccess { get; set; }

    public string? TailscaleHost { get; set; }

    public string[] AllowedLogins { get; set; } = [];

    public string[] InvitedLogins { get; set; } = [];
}

public enum WebAccessMode
{
    Local,
    Tailscale,
    AnonymousPublic,
    InvitedTailscale
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

public sealed class WebAccessPolicy
{
    private const int MaximumLoginLength = 320;

    public const string TailscaleLoginHeader = "Tailscale-User-Login";
    public const string LocalAuthenticationType = "DantesRoleplay.Local";
    public const string TailscaleAuthenticationType = "TailscaleServe";
    public const string InvitedTailscaleAuthenticationType = "DantesRoleplay.InvitedTailscale";
    public const string AnonymousPublicAuthenticationType = "DantesRoleplay.AnonymousPublic";

    private readonly WebRemoteAccessOptions remote;

    public WebAccessPolicy(IOptions<WebRemoteAccessOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        remote = options.Value;
        EnsureDistinctLoginClasses(remote);
    }

    public WebAccessDecision Evaluate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!WebInterfaceSecurity.IsLoopback(context.Connection.RemoteIpAddress))
        {
            if (remote.AllowAnonymousPublicAccess && context.Connection.RemoteIpAddress is not null)
                return new WebAccessDecision(true, WebAccessMode.AnonymousPublic);
            return Denied(
                "LOCAL_ACCESS_REQUIRED",
                "The web interface accepts direct requests only from this computer.");
        }

        var host = NormaliseHost(context.Request.Host.Host);
        var login = ReadLogin(context.Request);
        if (!IsRemoteCandidate(host, login.Present))
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

        if (!login.Valid)
        {
            return Denied(
                login.ErrorCode,
                login.ErrorMessage);
        }

        var allowed = (remote.AllowedLogins ?? []).Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            string.Equals(candidate.Trim(), login.Value, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return new WebAccessDecision(true, WebAccessMode.Tailscale, login.Value);

        var invited = (remote.InvitedLogins ?? []).Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            string.Equals(candidate.Trim(), login.Value, StringComparison.OrdinalIgnoreCase));
        return invited
            ? new WebAccessDecision(true, WebAccessMode.InvitedTailscale, login.Value)
            : Denied(
                "REMOTE_ACCESS_DENIED",
                "This Tailscale user is not allowed to use the web interface.");
    }

    public static bool IsRemoteCandidate(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsRemoteCandidate(
            NormaliseHost(request.Host.Host),
            request.Headers.ContainsKey(TailscaleLoginHeader));
    }

    public static bool IsAllowedRemotePath(PathString path) =>
        path == "/" ||
        path.StartsWithSegments("/ui") ||
        path.StartsWithSegments("/components") ||
        path.StartsWithSegments("/api/data") ||
        path.StartsWithSegments("/api/changes") ||
        path.StartsWithSegments("/api/session") ||
        path == "/api/audience-context" ||
        IsReadinessPath(path) ||
        IsReadModelMediaPath(path) ||
        IsApplicationAuxiliaryPath(path) ||
        path.StartsWithSegments("/api/blob-uploads") ||
        path.StartsWithSegments("/api/blobs") ||
        IsApplicationCatalogReadPath(path) ||
        IsApplicationResolvedReadPath(path) ||
        IsApplicationMechanicPath(path) ||
        IsApplicationStateReadPath(path) ||
        IsObservationPath(path) ||
        path.StartsWithSegments(WebControlEndpointConventions.RoutePrefix);

    private static bool IsApplicationMechanicPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal)) return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 7 or 8 &&
            segments[0] == "api" && segments[1] == "applications" &&
            IsRouteIdentifier(segments[2], 63) && segments[3] == "state-spaces" &&
            IsRouteIdentifier(segments[4], 200) && segments[5] == "mechanics" &&
            IsRouteIdentifier(segments[6], 200) &&
            (segments.Length == 7 || segments[7] is "prepare" or "execute");
    }

    private static bool IsReadinessPath(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is { Length: 4 } && segments[0] == "api" &&
            segments[1] == "readiness" && segments[2] == "applications" &&
            IsRouteIdentifier(segments[3], 63);
    }

    private static bool IsReadModelMediaPath(PathString path)
    {
        var parts = path.Value?.Split('/');
        return parts is { Length: 5 } && parts[1] == "api" && parts[2] == "read-model-media" &&
            IsRouteIdentifier(parts[3], 4096) && parts[4] == "content";
    }

    private static bool IsApplicationAuxiliaryPath(PathString path)
    {
        var parts = path.Value?.Split('/');
        return parts is { Length: 5 or 7 } && parts[1] == "api" && parts[2] == "applications" &&
            IsRouteIdentifier(parts[3], 63) && (parts.Length == 5 ? parts[4] == "visual-drafts" :
                parts[4] == "campaigns" && IsRouteIdentifier(parts[5], 200) && parts[6] == "chronology");
    }

    private static bool IsApplicationCatalogReadPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal)) return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 5 or 6 &&
            segments[0] == "api" &&
            segments[1] == "applications" &&
            IsRouteIdentifier(segments[2], 63) &&
            segments[3] == "catalog" &&
            (segments.Length == 5
                ? segments[4] == "browse"
                : segments[4] == "records" && IsRouteIdentifier(segments[5], 400));
    }

    private static bool IsApplicationResolvedReadPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal)) return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4 &&
            segments[0] == "api" && segments[1] == "applications" &&
            IsRouteIdentifier(segments[2], 63) && segments[3] is "content" or "rules";
    }

    private static bool IsApplicationStateReadPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal)) return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 ||
            segments[0] != "api" ||
            segments[1] != "applications" ||
            segments[3] != "state-spaces" ||
            !IsRouteIdentifier(segments[2], 63)) return false;
        if (segments.Length == 4) return true;
        if (segments.Length < 6 || !IsRouteIdentifier(segments[4], 200)) return false;
        if (segments.Length == 6 && segments[5] is "containments" or "media-batch") return true;
        if (segments[5] != "entities") return false;
        if (segments.Length == 6) return true;
        if (!IsRouteIdentifier(segments[6], 200)) return false;
        if (segments.Length == 7) return true;
        if (segments.Length == 8 && segments[7] == "containment") return true;
        if (segments.Length == 8 && segments[7] == "media") return true;
        if (segments.Length == 9 && segments[7] == "read-models" &&
            IsRouteIdentifier(segments[8], 200)) return true;
        if (segments.Length == 10 && segments[7] == "media" &&
            IsRouteIdentifier(segments[8], 200) && segments[9] == "content") return true;
        if (segments.Length < 8 || segments[7] != "components") return false;
        if (segments.Length == 8) return true;
        return segments.Length == 9 && IsRouteIdentifier(segments[8], 200);
    }

    private static bool IsRouteIdentifier(string value, int maximum) =>
        value.Length is >= 1 && value.Length <= maximum && !value.Any(char.IsControl);

    private static bool IsObservationPath(PathString path)
    {
        const string prefix = "/api/applications/";
        const string suffix = "/observations";
        var value = path.Value;
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !value.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var application = value[prefix.Length..^suffix.Length];
        return application.Length is >= 1 and <= 63 && !application.Contains('/');
    }

    public static ClaimsPrincipal CreatePrincipal(WebAccessDecision decision)
    {
        if (!decision.Allowed)
        {
            throw new ArgumentException("A denied access decision cannot create a principal.", nameof(decision));
        }

        var authenticationType = decision.Mode switch
        {
            WebAccessMode.Tailscale => TailscaleAuthenticationType,
            WebAccessMode.InvitedTailscale => InvitedTailscaleAuthenticationType,
            WebAccessMode.AnonymousPublic => AnonymousPublicAuthenticationType,
            _ => LocalAuthenticationType
        };
        var name = decision.Mode == WebAccessMode.AnonymousPublic ? "anonymous-public" : decision.Login ?? "local";
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

    private static bool IsRemoteCandidate(string? host, bool loginHeaderPresent) =>
        IsTailscaleHost(host) || loginHeaderPresent;

    private static bool IsTailscaleHost(string? host) =>
        host is not null && host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase);

    private static string? NormaliseHost(string? host)
    {
        var normalised = host?.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(normalised) ? null : normalised;
    }

    private static LoginHeader ReadLogin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(TailscaleLoginHeader, out var values))
            return LoginHeader.Missing();
        if (values.Count != 1)
            return LoginHeader.Invalid(
                "REMOTE_IDENTITY_AMBIGUOUS",
                "Exactly one Tailscale user identity is required.");

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value))
            return LoginHeader.Missing(present: true);
        var normalized = value.Trim();
        if (value.Length > MaximumLoginLength || normalized.Any(char.IsControl) || normalized.Contains(','))
            return LoginHeader.Invalid(
                "REMOTE_IDENTITY_INVALID",
                "The Tailscale user identity is invalid.");
        return LoginHeader.Accepted(normalized);
    }

    private static void EnsureDistinctLoginClasses(WebRemoteAccessOptions options)
    {
        var operators = (options.AllowedLogins ?? [])
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = (options.InvitedLogins ?? [])
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.Trim())
            .FirstOrDefault(operators.Contains);
        if (overlap is not null)
        {
            throw new OptionsValidationException(
                WebRemoteAccessOptions.SectionName,
                typeof(WebRemoteAccessOptions),
                ["AllowedLogins and InvitedLogins must not contain the same normalized login."]);
        }
    }

    private readonly record struct LoginHeader(
        bool Present,
        bool Valid,
        string? Value,
        string ErrorCode,
        string ErrorMessage)
    {
        public static LoginHeader Accepted(string value) => new(true, true, value, "", "");

        public static LoginHeader Missing(bool present = false) => new(
            present,
            false,
            null,
            "REMOTE_IDENTITY_REQUIRED",
            "A verified Tailscale user identity is required.");

        public static LoginHeader Invalid(string code, string message) =>
            new(true, false, null, code, message);
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

        if (accessDecision is { Allowed: true, Mode: WebAccessMode.InvitedTailscale })
        {
            PrivateOperatorCapabilityNames.TryGetAuditName(capability, out var capabilityName);
            var evidence = new AuthorizationAuditEvidence(
                principal.PrincipalId,
                principal.AuthenticationMethod,
                capabilityName ?? "invalid",
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                Correlation(context.TraceIdentifier),
                false,
                "PRIVATE_OPERATOR_INVITED_IDENTITY");
            return new(
                false,
                null,
                evidence,
                "PRIVATE_OPERATOR_DENIED",
                "Invited identities cannot use private-operator endpoints.");
        }

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
    public static TrustedPrincipalContext FromPrincipal(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated != true
            ? TrustedPrincipalContext.Unauthenticated("WEB_IDENTITY_UNAVAILABLE")
            : principal.Identity.AuthenticationType switch
            {
                WebAccessPolicy.LocalAuthenticationType => Create(new(true, WebAccessMode.Local)),
                WebAccessPolicy.TailscaleAuthenticationType => Create(new(true, WebAccessMode.Tailscale, principal.Identity.Name)),
                WebAccessPolicy.InvitedTailscaleAuthenticationType => Create(new(true, WebAccessMode.InvitedTailscale, principal.Identity.Name)),
                WebAccessPolicy.AnonymousPublicAuthenticationType => Create(new(true, WebAccessMode.AnonymousPublic)),
                _ => TrustedPrincipalContext.Unauthenticated("WEB_IDENTITY_UNAVAILABLE")
            };

    public static TrustedPrincipalContext Create(WebAccessDecision decision)
    {
        if (!decision.Allowed)
            return TrustedPrincipalContext.Unauthenticated(decision.ErrorCode ?? "WEB_IDENTITY_UNAVAILABLE");
        var (method, subject) = decision.Mode switch
        {
            WebAccessMode.Tailscale => ("tailscale-serve", decision.Login?.Trim().ToLowerInvariant()),
            WebAccessMode.InvitedTailscale => ("tailscale-invited-web", decision.Login?.Trim().ToLowerInvariant()),
            WebAccessMode.AnonymousPublic => ("anonymous-public-web", "anonymous-public-operator"),
            _ => ("local-loopback", "local-operator")
        };
        if (string.IsNullOrWhiteSpace(subject))
            return TrustedPrincipalContext.Unauthenticated("WEB_IDENTITY_UNAVAILABLE");
        return decision.Mode == WebAccessMode.InvitedTailscale
            ? InvitedWebPrincipal.Create(method, subject)
            : PrivateOperatorPrincipal.Create(method, subject);
    }
}

internal static class InvitedWebPrincipal
{
    private const string Domain = "dantes-roleplay/invited-web/v1\0";

    public static TrustedPrincipalContext Create(string authenticationMethod, string trustedSubject)
    {
        if (string.IsNullOrWhiteSpace(authenticationMethod) || authenticationMethod.Length > 64)
            throw new ArgumentException("The authentication method is invalid.", nameof(authenticationMethod));
        if (string.IsNullOrWhiteSpace(trustedSubject) || trustedSubject.Length > 320)
            throw new ArgumentException("The trusted subject is invalid.", nameof(trustedSubject));
        var normalizedSubject = trustedSubject.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            Domain + authenticationMethod + "\0" + normalizedSubject));
        return TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + Convert.ToHexStringLower(hash),
            authenticationMethod);
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
