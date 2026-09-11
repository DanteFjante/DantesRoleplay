using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Security;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Resolves the host's current installation operators for standing-grant issuance. Membership is
/// reconstructed for every decision so remote-access removal and disablement take effect before a
/// grant revision can be written.
/// </summary>
public sealed class PlatformStandingGrantIssuerPolicy(
    IOptionsMonitor<WebRemoteAccessOptions> remoteAccess) : IStandingGrantIssuerPolicy
{
    private const int MaximumCommandIdLength = 128;
    private const string Scope = "system.private-host";

    public Task<StandingGrantIssuerDecision> EvaluateAsync(
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
            return Task.FromResult(Deny(issuer, requirement.Mutation, "invalid", "INVALID_STANDING_GRANT_COMMAND"));

        if (!issuer.Verified)
            return Task.FromResult(Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_UNAUTHENTICATED"));

        HashSet<string> operators;
        try
        {
            operators = CurrentOperators(remoteAccess.CurrentValue);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_UNAVAILABLE"));
        }

        if (!operators.Contains(issuer.PrincipalId))
            return Task.FromResult(Deny(issuer, requirement.Mutation, correlation, "STANDING_GRANT_ISSUER_DENIED"));

        try
        {
            StandingGrantContractRules.ValidateIssuerTransition(requirement);
        }
        catch (InteractionContractException exception)
        {
            return Task.FromResult(Deny(issuer, requirement.Mutation, correlation, exception.Code));
        }

        return Task.FromResult(new StandingGrantIssuerDecision(true, "STANDING_GRANT_ISSUER_ALLOWED",
            Evidence(issuer, requirement.Mutation, correlation, true, "STANDING_GRANT_ISSUER_ALLOWED")));
    }

    private static HashSet<string> CurrentOperators(WebRemoteAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var operators = new HashSet<string>(StringComparer.Ordinal)
        {
            PrivateOperatorPrincipal.Create("local-loopback", "local-operator").PrincipalId,
            PrivateOperatorPrincipal.Create("local-loopback-mcp", "local-operator").PrincipalId
        };
        if (!options.Enabled) return operators;

        if (!ValidHost(options.TailscaleHost))
            throw new OptionsValidationException(WebRemoteAccessOptions.SectionName,
                typeof(WebRemoteAccessOptions), ["Remote operator access is unavailable."]);
        var allowed = NormalizeLogins(options.AllowedLogins);
        var invited = NormalizeLogins(options.InvitedLogins);
        if (allowed.Overlaps(invited))
            throw new OptionsValidationException(WebRemoteAccessOptions.SectionName,
                typeof(WebRemoteAccessOptions), ["Remote operator access is unavailable."]);
        foreach (var login in allowed)
            operators.Add(PrivateOperatorPrincipal.Create("tailscale-serve", login).PrincipalId);
        return operators;
    }

    private static HashSet<string> NormalizeLogins(IEnumerable<string>? logins)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var login in logins ?? [])
        {
            var value = login?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 320 ||
                value.Any(character => char.IsControl(character) || character == ','))
                throw new OptionsValidationException(WebRemoteAccessOptions.SectionName,
                    typeof(WebRemoteAccessOptions), ["Remote operator access is unavailable."]);
            normalized.Add(value.ToLowerInvariant());
        }
        return normalized;
    }

    private static bool ValidHost(string? host)
    {
        var normalized = host?.Trim().TrimEnd('.');
        return normalized is { Length: > 0 and <= 253 }
            && normalized.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)
            && !normalized.Any(char.IsControl)
            && Uri.CheckHostName(normalized) == UriHostNameType.Dns;
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
