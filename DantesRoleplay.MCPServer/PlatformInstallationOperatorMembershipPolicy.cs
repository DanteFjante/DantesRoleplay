using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Security;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.MCPServer;

/// <summary>Reconstructs the current installation-operator set from host configuration for each decision.</summary>
public sealed class PlatformInstallationOperatorMembershipPolicy(
    IOptionsMonitor<WebRemoteAccessOptions> remoteAccess) : IInstallationOperatorMembershipPolicy
{
    public Task<InstallationOperatorMembershipDecision> EvaluateAsync(TrustedPrincipalContext principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();
        if (!principal.Verified)
            return Task.FromResult(new InstallationOperatorMembershipDecision(
                InstallationOperatorMembershipStatus.Denied, "INSTALLATION_OPERATOR_DENIED"));
        try
        {
            var operators = CurrentOperators(remoteAccess.CurrentValue);
            return Task.FromResult(new InstallationOperatorMembershipDecision(
                operators.Contains(principal.PrincipalId)
                    ? InstallationOperatorMembershipStatus.Member
                    : InstallationOperatorMembershipStatus.Denied,
                operators.Contains(principal.PrincipalId)
                    ? "INSTALLATION_OPERATOR_MEMBER"
                    : "INSTALLATION_OPERATOR_DENIED"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(new InstallationOperatorMembershipDecision(
                InstallationOperatorMembershipStatus.Unavailable, "INSTALLATION_OPERATOR_UNAVAILABLE"));
        }
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

        if (!ValidHost(options.TailscaleHost)) throw Unavailable();
        var allowed = NormalizeLogins(options.AllowedLogins);
        var invited = NormalizeLogins(options.InvitedLogins);
        if (allowed.Overlaps(invited)) throw Unavailable();
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
            if (string.IsNullOrWhiteSpace(value) || value.Length > 320
                || value.Any(character => char.IsControl(character) || character == ',')) throw Unavailable();
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

    private static OptionsValidationException Unavailable() => new(WebRemoteAccessOptions.SectionName,
        typeof(WebRemoteAccessOptions), ["Remote operator access is unavailable."]);
}
