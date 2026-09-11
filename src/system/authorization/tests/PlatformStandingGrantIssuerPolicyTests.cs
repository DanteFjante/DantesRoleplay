using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Web.Security;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Tests;

public sealed class PlatformStandingGrantIssuerPolicyTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData("local-loopback")]
    [InlineData("local-loopback-mcp")]
    public async Task Explicit_local_installation_operators_may_issue(string authenticationMethod)
    {
        var monitor = new MutableOptionsMonitor(ValidOptions());
        var issuer = PrivateOperatorPrincipal.Create(authenticationMethod, "local-operator");

        var decision = await new PlatformStandingGrantIssuerPolicy(monitor)
            .EvaluateAsync(issuer, Requirement(), "grant.issue.local");

        Assert.True(decision.Allowed);
        Assert.Equal("STANDING_GRANT_ISSUER_ALLOWED", decision.Code);
        Assert.Equal(issuer.PrincipalId, decision.Evidence.PrincipalReference);
        Assert.Equal(authenticationMethod, decision.Evidence.AuthenticationMethod);
        Assert.Equal("standing-grant.issue", decision.Evidence.Capability);
        Assert.Equal("system.private-host", decision.Evidence.Scope);
        Assert.Equal("grant.issue.local", decision.Evidence.CorrelationId);
    }

    [Fact]
    public async Task Current_allowed_tailscale_operator_may_issue_but_removal_or_disable_revokes_membership()
    {
        var monitor = new MutableOptionsMonitor(ValidOptions(allowed: ["operator@example.com"]));
        var policy = new PlatformStandingGrantIssuerPolicy(monitor);
        var issuer = PrivateOperatorPrincipal.Create("tailscale-serve", "operator@example.com");

        Assert.True((await policy.EvaluateAsync(issuer, Requirement(), "grant.issue.remote")).Allowed);

        monitor.Set(ValidOptions());
        var removed = await policy.EvaluateAsync(issuer, Requirement(), "grant.issue.removed");
        Assert.False(removed.Allowed);
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", removed.Code);

        monitor.Set(ValidOptions(allowed: ["operator@example.com"], enabled: false));
        var disabled = await policy.EvaluateAsync(issuer, Requirement(), "grant.issue.disabled");
        Assert.False(disabled.Allowed);
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", disabled.Code);
    }

    [Fact]
    public async Task Shared_membership_and_issuer_recompute_the_same_host_owned_operator_set()
    {
        var monitor = new MutableOptionsMonitor(ValidOptions(allowed: ["operator@example.com"]));
        var membership = new PlatformInstallationOperatorMembershipPolicy(monitor);
        var policy = new PlatformStandingGrantIssuerPolicy(membership);
        var remote = PrivateOperatorPrincipal.Create("tailscale-serve", "operator@example.com");
        var forged = TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('b', 64), "local-loopback");

        Assert.Equal(InstallationOperatorMembershipStatus.Member, (await membership.EvaluateAsync(remote)).Status);
        Assert.True((await policy.EvaluateAsync(remote, Requirement(), "grant.issue.shared")).Allowed);
        Assert.Equal(InstallationOperatorMembershipStatus.Denied, (await membership.EvaluateAsync(forged)).Status);

        monitor.Set(ValidOptions());
        Assert.Equal(InstallationOperatorMembershipStatus.Denied, (await membership.EvaluateAsync(remote)).Status);
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED",
            (await policy.EvaluateAsync(remote, Requirement(), "grant.issue.shared-removed")).Code);

        monitor.Set(ValidOptions(allowed: ["operator@example.com"], invited: ["Operator@example.com"]));
        Assert.Equal(InstallationOperatorMembershipStatus.Unavailable, (await membership.EvaluateAsync(remote)).Status);
        Assert.Equal("STANDING_GRANT_ISSUER_UNAVAILABLE",
            (await policy.EvaluateAsync(remote, Requirement(), "grant.issue.shared-invalid")).Code);
    }

    [Fact]
    public async Task Default_disabled_remote_access_keeps_only_explicit_local_operators()
    {
        var policy = new PlatformStandingGrantIssuerPolicy(
            new MutableOptionsMonitor(new WebRemoteAccessOptions()));
        var local = PrivateOperatorPrincipal.Create("local-loopback", "local-operator");
        var remote = PrivateOperatorPrincipal.Create("tailscale-serve", "operator@example.com");

        Assert.True((await policy.EvaluateAsync(local, Requirement(), "grant.issue.default-local")).Allowed);
        var remoteDecision = await policy.EvaluateAsync(remote, Requirement(), "grant.issue.default-remote");
        Assert.False(remoteDecision.Allowed);
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", remoteDecision.Code);
    }

    [Fact]
    public async Task Invited_anonymous_unverified_and_forged_authentication_method_are_not_issuers()
    {
        var monitor = new MutableOptionsMonitor(ValidOptions(
            allowed: ["operator@example.com"], invited: ["guest@example.com"]));
        var policy = new PlatformStandingGrantIssuerPolicy(monitor);
        var invited = PrivateOperatorPrincipal.Create("tailscale-invited-web", "guest@example.com");
        var anonymous = PrivateOperatorPrincipal.Create("anonymous-public-web", "anonymous-public-operator");
        var unverified = TrustedPrincipalContext.Unauthenticated("MISSING_IDENTITY");
        var forged = TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('b', 64), "local-loopback");

        foreach (var issuer in new[] { invited, anonymous, unverified, forged })
        {
            var decision = await policy.EvaluateAsync(issuer, Requirement(), "grant.issue.denied");
            Assert.False(decision.Allowed);
        }

        var invitedDecision = await policy.EvaluateAsync(invited, Requirement(), "grant.issue.invited");
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", invitedDecision.Code);
        var anonymousDecision = await policy.EvaluateAsync(anonymous, Requirement(), "grant.issue.anonymous");
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", anonymousDecision.Code);
        var unverifiedDecision = await policy.EvaluateAsync(unverified, Requirement(), "grant.issue.unverified");
        Assert.Equal("STANDING_GRANT_ISSUER_UNAUTHENTICATED", unverifiedDecision.Code);
        var forgedDecision = await policy.EvaluateAsync(forged, Requirement(), "grant.issue.forged");
        Assert.Equal("STANDING_GRANT_ISSUER_DENIED", forgedDecision.Code);
    }

    [Fact]
    public async Task Invalid_current_configuration_fails_closed_without_disclosing_logins()
    {
        var monitor = new MutableOptionsMonitor(ValidOptions(
            allowed: ["operator@example.com"], invited: ["Operator@example.com"]));
        var issuer = PrivateOperatorPrincipal.Create("local-loopback", "local-operator");

        var decision = await new PlatformStandingGrantIssuerPolicy(monitor)
            .EvaluateAsync(issuer, Requirement(), "grant.issue.invalid-config");

        Assert.False(decision.Allowed);
        Assert.Equal("STANDING_GRANT_ISSUER_UNAVAILABLE", decision.Code);
        Assert.DoesNotContain("operator@example.com", decision.Evidence.ReasonCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_transition_and_command_id_fail_with_safe_audit_evidence()
    {
        var monitor = new MutableOptionsMonitor(ValidOptions());
        var policy = new PlatformStandingGrantIssuerPolicy(monitor);
        var issuer = PrivateOperatorPrincipal.Create("local-loopback", "local-operator");

        var transition = await policy.EvaluateAsync(issuer,
            Requirement() with { ExpectedCurrentRevision = 1 }, "grant.issue.invalid-transition");
        Assert.False(transition.Allowed);
        Assert.Equal("INVALID_STANDING_GRANT_TRANSITION", transition.Code);

        var command = await policy.EvaluateAsync(issuer, Requirement(), "invalid command");
        Assert.False(command.Allowed);
        Assert.Equal("INVALID_STANDING_GRANT_COMMAND", command.Code);
        Assert.Equal("invalid", command.Evidence.CorrelationId);
        Assert.Equal(issuer.PrincipalId, command.Evidence.PrincipalReference);
        Assert.Equal("standing-grant.issue", command.Evidence.Capability);
        Assert.Equal("system.private-host", command.Evidence.Scope);
    }

    private static WebRemoteAccessOptions ValidOptions(
        string[]? allowed = null,
        string[]? invited = null,
        bool enabled = true) => new()
        {
            Enabled = enabled,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = allowed ?? [],
            InvitedLogins = invited ?? []
        };

    private static StandingGrantIssuerRequirement Requirement()
    {
        var application = ApplicationIdentifier.Parse("demo");
        var revision = new StandingGrantRevision(
            "grant@1", "grant", 1, Hash, "principal.subject", application,
            StandingGrantScope.Application, null, [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [], []), [], 1,
            DateTime.UtcNow.AddMinutes(5), false, "operation.issue");
        return new(StandingGrantIssuerMutation.Issue, 0, revision);
    }

    private sealed class MutableOptionsMonitor(WebRemoteAccessOptions current) : IOptionsMonitor<WebRemoteAccessOptions>
    {
        public WebRemoteAccessOptions CurrentValue { get; private set; } = current;

        public WebRemoteAccessOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<WebRemoteAccessOptions, string?> listener) => null;

        public void Set(WebRemoteAccessOptions value) => CurrentValue = value;
    }
}
