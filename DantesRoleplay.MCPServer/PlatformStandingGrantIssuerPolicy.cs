using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Security;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Applies standing-grant-specific validation and audit around the current installation membership decision.
/// </summary>
public sealed class PlatformStandingGrantIssuerPolicy : IStandingGrantIssuerPolicy
{
    private const int MaximumCommandIdLength = 128;
    private const string Scope = "system.private-host";
    private readonly IInstallationOperatorMembershipPolicy membership;

    public PlatformStandingGrantIssuerPolicy(IInstallationOperatorMembershipPolicy membership) =>
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));

    /// <summary>Compatibility constructor for direct host tests and existing callers.</summary>
    public PlatformStandingGrantIssuerPolicy(IOptionsMonitor<WebRemoteAccessOptions> remoteAccess)
        : this(new PlatformInstallationOperatorMembershipPolicy(remoteAccess)) { }

    public async Task<StandingGrantIssuerDecision> EvaluateAsync(
        TrustedPrincipalContext issuer,
        StandingGrantIssuerRequirement requirement,
        string commandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(requirement);
        cancellationToken.ThrowIfCancellationRequested();

        var correlation = CanonicalCommandId(commandId);
        if (correlation is null)
            return Deny(issuer, requirement.Mutation, "invalid", "INVALID_STANDING_GRANT_COMMAND");

        if (!issuer.Verified)
            return Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_UNAUTHENTICATED");

        InstallationOperatorMembershipDecision decision;
        try
        {
            decision = await membership.EvaluateAsync(issuer, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_UNAVAILABLE");
        }
        if (decision.Status == InstallationOperatorMembershipStatus.Unavailable)
            return Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_UNAVAILABLE");
        if (decision.Status != InstallationOperatorMembershipStatus.Member)
            return Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_DENIED");

        try
        {
            StandingGrantContractRules.ValidateIssuerTransition(requirement);
        }
        catch (InteractionContractException exception)
        {
            return Deny(issuer, requirement.Mutation, correlation, exception.Code);
        }

        return new StandingGrantIssuerDecision(true, "STANDING_GRANT_ISSUER_ALLOWED",
            Evidence(issuer, requirement.Mutation, correlation, true, "STANDING_GRANT_ISSUER_ALLOWED"));
    }

    private static string? CanonicalCommandId(string? commandId)
    {
        var value = commandId?.Trim();
        return value is { Length: > 0 and <= MaximumCommandIdLength }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-')
            ? value
            : null;
    }

    private static StandingGrantIssuerDecision Deny(
        TrustedPrincipalContext issuer,
        StandingGrantIssuerMutation mutation,
        string correlation,
        string code) => new(false, code, Evidence(issuer, mutation, correlation, false, code));

    private static AuthorizationAuditEvidence Evidence(
        TrustedPrincipalContext issuer,
        StandingGrantIssuerMutation? mutation,
        string correlation,
        bool allowed,
        string code) => new(
            issuer.Verified ? issuer.PrincipalId : "",
            issuer.Verified ? issuer.AuthenticationMethod : "",
            Capability(mutation),
            Scope,
            correlation,
            allowed,
            code);

    private static string Capability(StandingGrantIssuerMutation? mutation) => mutation switch
    {
        StandingGrantIssuerMutation.Issue => "standing-grant.issue",
        StandingGrantIssuerMutation.Replace => "standing-grant.replace",
        StandingGrantIssuerMutation.Revoke => "standing-grant.revoke",
        _ => "standing-grant.invalid"
    };
}
