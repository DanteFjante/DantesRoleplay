using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.Authorization;

public enum PrivateOperatorCapability
{
    Read,
    Modify,
    ControlRead,
    ControlPagesWrite,
    ControlSettingsWrite,
    ControlAiMessage,
    ControlCodexApprove,
    TriggerObservationSubmit,
    TriggerAdministrationRead,
    TriggerAdministrationWrite
}

public static class PrivateOperatorCapabilityNames
{
    public static bool TryGetAuditName(
        PrivateOperatorCapability capability,
        out string name)
    {
        name = capability switch
        {
            PrivateOperatorCapability.Read => "read",
            PrivateOperatorCapability.Modify => "modify",
            PrivateOperatorCapability.ControlRead => "control.read",
            PrivateOperatorCapability.ControlPagesWrite => "control.pages.write",
            PrivateOperatorCapability.ControlSettingsWrite => "control.settings.write",
            PrivateOperatorCapability.ControlAiMessage => "control.ai.message",
            PrivateOperatorCapability.ControlCodexApprove => "control.codex.approve",
            PrivateOperatorCapability.TriggerObservationSubmit => "trigger.observation.submit",
            PrivateOperatorCapability.TriggerAdministrationRead => "trigger.admin.read",
            PrivateOperatorCapability.TriggerAdministrationWrite => "trigger.admin.write",
            _ => "invalid"
        };
        return name != "invalid";
    }
}

/// <summary>Request-lifetime identity produced only by a trusted host adapter.</summary>
public sealed record TrustedPrincipalContext
{
    private static readonly Regex PrincipalPattern = new(
        "^principal\\.[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private TrustedPrincipalContext(
        bool verified,
        string principalId,
        string authenticationMethod,
        string failureCode)
    {
        Verified = verified;
        PrincipalId = principalId;
        AuthenticationMethod = authenticationMethod;
        FailureCode = failureCode;
    }

    public bool Verified { get; }
    public string PrincipalId { get; }
    public string AuthenticationMethod { get; }
    public string FailureCode { get; }

    public static TrustedPrincipalContext VerifiedPrincipal(
        string principalId,
        string authenticationMethod)
    {
        if (!PrincipalPattern.IsMatch(principalId))
            throw new ArgumentException("The principal reference must be an opaque SHA-256 identifier.", nameof(principalId));
        if (!Bounded(authenticationMethod, 64))
            throw new ArgumentException("The authentication method is invalid.", nameof(authenticationMethod));
        return new(true, principalId, authenticationMethod, "");
    }

    public static bool IsValidPrincipalId(string? value) =>
        value is not null && PrincipalPattern.IsMatch(value);

    public static TrustedPrincipalContext Unauthenticated(string failureCode)
    {
        if (!Bounded(failureCode, 80))
            throw new ArgumentException("The authentication failure code is invalid.", nameof(failureCode));
        return new(false, "", "", failureCode);
    }

    private static bool Bounded(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}

public sealed record PrivateOperatorAuthorizationRequest(
    TrustedPrincipalContext Principal,
    PrivateOperatorCapability Capability,
    string Scope,
    string CorrelationId);

public sealed record AuthorizationAuditEvidence(
    string PrincipalReference,
    string AuthenticationMethod,
    string Capability,
    string Scope,
    string CorrelationId,
    bool Allowed,
    string ReasonCode);

public sealed record PrivateOperatorAuthorizationDecision(
    bool Allowed,
    string Code,
    string Recovery,
    AuthorizationAuditEvidence Evidence);

public interface IPrivateOperatorAuthorizationPolicy
{
    PrivateOperatorAuthorizationDecision Evaluate(PrivateOperatorAuthorizationRequest request);
}

/// <summary>Authorizes one capability from context owned by the current transport adapter.</summary>
public interface IPrivateOperatorRequestAuthorizer
{
    PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability);
}

public static class PrivateOperatorPrincipal
{
    private const string Domain = "dantes-roleplay/private-operator/v1\0";

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

/// <summary>
/// Closed single-operator policy for the private host. It stores no grants: authentication is the
/// administrator grant, and every request is evaluated again.
/// </summary>
public sealed class PrivateOperatorAuthorizationPolicy : IPrivateOperatorAuthorizationPolicy
{
    public const string PrivateHostScope = "system.private-host";

    public PrivateOperatorAuthorizationDecision Evaluate(PrivateOperatorAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Principal);
        var correlation = Bounded(request.CorrelationId, 128) ? request.CorrelationId : "invalid";
        if (!request.Principal.Verified)
            return Denied(request, correlation, "PRIVATE_OPERATOR_UNAUTHENTICATED",
                "Authenticate through local access or the configured private Tailscale host.");
        if (!PrivateOperatorCapabilityNames.TryGetAuditName(request.Capability, out _))
            return Denied(request, correlation, "PRIVATE_OPERATOR_UNSUPPORTED_CAPABILITY",
                "Use one of the closed private-operator capabilities.");
        if (!string.Equals(request.Scope, PrivateHostScope, StringComparison.Ordinal))
            return Denied(request, correlation, "PRIVATE_OPERATOR_WRONG_SCOPE",
                "Use the configured private-host administration scope.");
        if (!Bounded(request.Principal.AuthenticationMethod, 64) ||
            !Bounded(request.Principal.PrincipalId, 80) ||
            !Bounded(request.CorrelationId, 128))
            return Denied(request, correlation, "PRIVATE_OPERATOR_DENIED",
                "Re-authenticate through the configured private host.");

        var evidence = Evidence(request, correlation, allowed: true, "PRIVATE_OPERATOR_ALLOWED");
        return new(true, "PRIVATE_OPERATOR_ALLOWED", "", evidence);
    }

    private static PrivateOperatorAuthorizationDecision Denied(
        PrivateOperatorAuthorizationRequest request,
        string correlation,
        string code,
        string recovery) =>
        new(false, code, recovery, Evidence(request, correlation, allowed: false, code));

    private static AuthorizationAuditEvidence Evidence(
        PrivateOperatorAuthorizationRequest request,
        string correlation,
        bool allowed,
        string code) =>
        new(
            request.Principal.Verified ? request.Principal.PrincipalId : "",
            request.Principal.Verified ? request.Principal.AuthenticationMethod : "",
            PrivateOperatorCapabilityNames.TryGetAuditName(request.Capability, out var capability)
                ? capability
                : "invalid",
            Bounded(request.Scope, 80) ? request.Scope : "invalid",
            correlation,
            allowed,
            code);

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}
