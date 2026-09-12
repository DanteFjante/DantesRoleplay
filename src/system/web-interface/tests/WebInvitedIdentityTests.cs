using System.Net;
using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DantesRoleplay.Tests;

public sealed class WebInvitedIdentityTests
{
    [Fact]
    public void Invited_login_is_a_distinct_stable_authenticated_identity()
    {
        var policy = Policy(invited: ["guest@example.com"]);
        var first = policy.Evaluate(Request("Guest@Example.com"));
        var second = policy.Evaluate(Request("guest@example.com"));

        Assert.True(first.Allowed);
        Assert.Equal(WebAccessMode.InvitedTailscale, first.Mode);
        var claims = WebAccessPolicy.CreatePrincipal(first);
        Assert.Equal(WebAccessPolicy.InvitedTailscaleAuthenticationType, claims.Identity!.AuthenticationType);
        Assert.Equal("invitedtailscale", claims.FindFirst("dantesroleplay:access-mode")?.Value);

        var firstTrusted = WebTrustedPrincipalContextFactory.Create(first);
        var secondTrusted = WebTrustedPrincipalContextFactory.Create(second);
        Assert.True(firstTrusted.Verified);
        Assert.Equal("tailscale-invited-web", firstTrusted.AuthenticationMethod);
        Assert.Equal(firstTrusted.PrincipalId, secondTrusted.PrincipalId);
        Assert.Equal(firstTrusted, WebTrustedPrincipalContextFactory.FromPrincipal(claims));
        Assert.NotEqual(
            PrivateOperatorPrincipal.Create("tailscale-serve", "guest@example.com").PrincipalId,
            firstTrusted.PrincipalId);
    }

    [Fact]
    public void Normalized_operator_and_invited_login_overlap_is_invalid_configuration()
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Policy(
            allowed: [" Operator@Example.com "],
            invited: ["operator@example.COM"]));

        Assert.Equal(WebRemoteAccessOptions.SectionName, exception.OptionsName);
        Assert.Contains(exception.Failures, failure =>
            failure.Contains("must not contain the same normalized login", StringComparison.Ordinal));
    }

    [Fact]
    public void Spoofed_network_header_and_wrong_host_are_never_trusted_as_tailscale_identity()
    {
        var options = new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            InvitedLogins = ["guest@example.com"]
        };
        var network = Request("guest@example.com", IPAddress.Parse("203.0.113.10"));
        var wrongHost = Request("guest@example.com", host: "other.example.ts.net");

        Assert.Equal("LOCAL_ACCESS_REQUIRED", new WebAccessPolicy(Options.Create(options)).Evaluate(network).ErrorCode);
        Assert.Equal("REMOTE_ACCESS_DENIED", new WebAccessPolicy(Options.Create(options)).Evaluate(wrongHost).ErrorCode);
    }

    [Fact]
    public void Multiple_or_unbounded_tailscale_identity_headers_are_rejected()
    {
        var policy = Policy(invited: ["guest@example.com"]);
        var duplicate = Request();
        duplicate.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] =
            new StringValues(["guest@example.com", "other@example.com"]);
        var unbounded = Request(new string('a', 321));

        Assert.Equal("REMOTE_IDENTITY_AMBIGUOUS", policy.Evaluate(duplicate).ErrorCode);
        Assert.Equal("REMOTE_IDENTITY_INVALID", policy.Evaluate(unbounded).ErrorCode);
    }

    [Fact]
    public void Removing_an_invite_immediately_stops_authenticating_that_login()
    {
        var request = Request("guest@example.com");

        Assert.Equal(WebAccessMode.InvitedTailscale,
            Policy(invited: ["guest@example.com"]).Evaluate(request).Mode);
        var removed = Policy().Evaluate(request);
        Assert.False(removed.Allowed);
        Assert.Equal("REMOTE_ACCESS_DENIED", removed.ErrorCode);
    }

    [Fact]
    public void Invited_identity_is_denied_before_the_private_operator_policy_runs()
    {
        var context = Request("guest@example.com");
        var guard = new WebPrivateOperatorGuard(
            Policy(invited: ["guest@example.com"]),
            new MustNotRunPrivateOperatorPolicy());

        var decision = guard.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Null(decision.Principal);
        Assert.Equal("PRIVATE_OPERATOR_DENIED", decision.ErrorCode);
        Assert.Equal("PRIVATE_OPERATOR_UNPRIVILEGED_IDENTITY", decision.Evidence.ReasonCode);
        Assert.Equal("tailscale-invited-web", decision.Evidence.AuthenticationMethod);
    }

    [Fact]
    public void Anonymous_public_legacy_access_is_rejected_by_the_platform_authentication_guard()
    {
        var context = Request(remoteAddress: IPAddress.Parse("203.0.113.10"));
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "guest@example.com";
        var policy = Policy(invited: ["guest@example.com"], anonymous: true);

        Assert.Equal(WebAccessMode.AnonymousPublic, policy.Evaluate(context).Mode);
        var decision = new WebPlatformAccessGuard(policy).Evaluate(context);

        Assert.False(decision.Authenticated);
        Assert.Null(decision.Principal);
        Assert.Null(decision.TrustedPrincipal);
        Assert.Equal("PLATFORM_AUTHENTICATION_REQUIRED", decision.ErrorCode);
    }

    [Fact]
    public void Platform_guard_authenticates_invited_identity_without_authorizing_an_operation()
    {
        var decision = new WebPlatformAccessGuard(Policy(invited: ["guest@example.com"]))
            .Evaluate(Request("guest@example.com"));

        Assert.True(decision.Authenticated);
        Assert.NotNull(decision.Principal);
        Assert.True(decision.TrustedPrincipal!.Verified);
        Assert.Equal("tailscale-invited-web", decision.TrustedPrincipal.AuthenticationMethod);
    }

    [Fact]
    public void Legacy_operator_access_and_principal_derivation_are_unchanged()
    {
        var access = Policy(allowed: ["operator@example.com"])
            .Evaluate(Request("Operator@Example.com"));

        Assert.True(access.Allowed);
        Assert.Equal(WebAccessMode.Tailscale, access.Mode);
        Assert.Equal(WebAccessPolicy.TailscaleAuthenticationType,
            WebAccessPolicy.CreatePrincipal(access).Identity!.AuthenticationType);
        var trusted = WebTrustedPrincipalContextFactory.Create(access);
        Assert.Equal("tailscale-serve", trusted.AuthenticationMethod);
        Assert.Equal(
            PrivateOperatorPrincipal.Create("tailscale-serve", "operator@example.com").PrincipalId,
            trusted.PrincipalId);
        var platform = new WebPlatformAccessGuard(Policy(allowed: ["operator@example.com"]))
            .Evaluate(Request("operator@example.com"));
        Assert.True(platform.Authenticated);
        Assert.Equal(trusted, platform.TrustedPrincipal);

        var decision = new WebPrivateOperatorGuard(
            Policy(allowed: ["operator@example.com"]),
            new PrivateOperatorAuthorizationPolicy())
            .Evaluate(Request("operator@example.com"));
        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    private static WebAccessPolicy Policy(
        string[]? allowed = null,
        string[]? invited = null,
        bool anonymous = false) =>
        new(Options.Create(new WebRemoteAccessOptions
        {
            Enabled = true,
            AllowAnonymousPublicAccess = anonymous,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = allowed ?? [],
            InvitedLogins = invited ?? []
        }));

    private static DefaultHttpContext Request(
        string? login = null,
        IPAddress? remoteAddress = null,
        string host = "roleplay.example.ts.net")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress ?? IPAddress.Loopback;
        context.Request.Host = new HostString(host);
        if (login is not null)
            context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = login;
        return context;
    }

    private sealed class MustNotRunPrivateOperatorPolicy : IPrivateOperatorAuthorizationPolicy
    {
        public PrivateOperatorAuthorizationDecision Evaluate(PrivateOperatorAuthorizationRequest request) =>
            throw new InvalidOperationException("Invited identities must be rejected before this policy runs.");
    }
}
