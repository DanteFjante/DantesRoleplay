using DantesRoleplay.Authorization;

namespace DantesRoleplay.Tests;

public sealed class PrivateOperatorAuthorizationTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(PrivateOperatorCapability.Read, "read")]
    [InlineData(PrivateOperatorCapability.Modify, "modify")]
    [InlineData(PrivateOperatorCapability.ControlRead, "control.read")]
    [InlineData(PrivateOperatorCapability.ControlPagesWrite, "control.pages.write")]
    [InlineData(PrivateOperatorCapability.ControlSettingsWrite, "control.settings.write")]
    [InlineData(PrivateOperatorCapability.TriggerObservationSubmit, "trigger.observation.submit")]
    [InlineData(PrivateOperatorCapability.ControlAiMessage, "control.ai.message")]
    [InlineData(PrivateOperatorCapability.ControlCodexApprove, "control.codex.approve")]
    [InlineData(PrivateOperatorCapability.TriggerAdministrationRead, "trigger.admin.read")]
    [InlineData(PrivateOperatorCapability.TriggerAdministrationWrite, "trigger.admin.write")]
    public void Verified_private_operator_is_allowed_for_the_closed_capabilities(
        PrivateOperatorCapability capability,
        string evidenceName)
    {
        var policy = new PrivateOperatorAuthorizationPolicy();
        var decision = policy.Evaluate(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "tailscale-serve"),
            capability,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "request-01"));

        Assert.True(decision.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_ALLOWED", decision.Code);
        Assert.Equal(Principal, decision.Evidence.PrincipalReference);
        Assert.Equal(evidenceName, decision.Evidence.Capability);
        Assert.DoesNotContain("@", decision.Evidence.PrincipalReference, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_private_operator_capability_fails_closed()
    {
        var decision = new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"),
            (PrivateOperatorCapability)999,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "request-invalid-capability"));

        Assert.False(decision.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_UNSUPPORTED_CAPABILITY", decision.Code);
        Assert.Equal("invalid", decision.Evidence.Capability);
    }

    [Fact]
    public void Missing_identity_and_wrong_scope_fail_closed_with_safe_evidence()
    {
        var policy = new PrivateOperatorAuthorizationPolicy();
        var anonymous = policy.Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("REMOTE_IDENTITY_REQUIRED"),
            PrivateOperatorCapability.Read,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "request-02"));
        var wrongScope = policy.Evaluate(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"),
            PrivateOperatorCapability.Modify,
            "application.other",
            "request-03"));

        Assert.False(anonymous.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", anonymous.Code);
        Assert.Empty(anonymous.Evidence.PrincipalReference);
        Assert.False(wrongScope.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_WRONG_SCOPE", wrongScope.Code);
        Assert.Equal("application.other", wrongScope.Evidence.Scope);
    }

    [Fact]
    public void Forged_or_non_opaque_principal_references_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            TrustedPrincipalContext.VerifiedPrincipal("operator@example.com", "tailscale-serve"));
        Assert.Throws<ArgumentException>(() =>
            TrustedPrincipalContext.VerifiedPrincipal("principal.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "tailscale-serve"));
        Assert.Throws<ArgumentException>(() => TrustedPrincipalContext.Unauthenticated(""));
    }
}
