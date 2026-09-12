using System.Net;
using System.Security.Claims;
using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Tests;

public sealed class WebAnonymousPublicAccessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_network_access_requires_the_explicit_anonymous_public_setting(bool enabled)
    {
        var context = PublicRequest();
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "spoofed@example.com";
        var access = Policy(enabled).Evaluate(context);
        Assert.Equal(enabled, access.Allowed);
        if (!enabled)
        {
            Assert.Equal("LOCAL_ACCESS_REQUIRED", access.ErrorCode);
            return;
        }
        Assert.Equal(WebAccessMode.AnonymousPublic, access.Mode);
        Assert.Null(access.Login);
        var principal = WebAccessPolicy.CreatePrincipal(access);
        Assert.Equal(WebAccessPolicy.AnonymousPublicAuthenticationType, principal.Identity!.AuthenticationType);
        Assert.Equal("anonymous-public", ControlCenterStatus.Create(principal).Access.Mode);
        var trusted = WebTrustedPrincipalContextFactory.Create(access);
        Assert.Equal("anonymous-public-web", trusted.AuthenticationMethod);
        Assert.Equal(trusted, WebTrustedPrincipalContextFactory.FromPrincipal(principal));
        Assert.NotEqual(WebTrustedPrincipalContextFactory.Create(new(true, WebAccessMode.Local)).PrincipalId,
            trusted.PrincipalId);
    }

    [Theory]
    [InlineData(PrivateOperatorCapability.ControlRead, false)]
    [InlineData(PrivateOperatorCapability.ControlPagesWrite, true)]
    [InlineData(PrivateOperatorCapability.ControlSettingsWrite, true)]
    [InlineData(PrivateOperatorCapability.ControlAiMessage, true)]
    public void Public_identity_cannot_use_control_routes_at_the_external_http_origin(
        PrivateOperatorCapability capability, bool mutation)
    {
        var context = PublicRequest();
        context.Request.Method = mutation ? "POST" : "GET";
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = "http://198.51.100.10";
        var guard = new WebControlRequestGuard(new(Policy(true), new PrivateOperatorAuthorizationPolicy()));
        var decision = guard.Evaluate(context, capability, mutation);
        Assert.False(decision.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_DENIED", decision.ErrorCode);
        Assert.Equal("anonymous-public-web", decision.Evidence.AuthenticationMethod);
    }

    [Fact]
    public void Public_setting_preserves_local_access_and_does_not_accept_a_missing_transport_identity()
    {
        var context = PublicRequest();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new HostString("localhost");
        Assert.Equal(WebAccessMode.Local, Policy(true).Evaluate(context).Mode);
        context.Connection.RemoteIpAddress = null;
        Assert.False(Policy(true).Evaluate(context).Allowed);
        Assert.False(WebTrustedPrincipalContextFactory.FromPrincipal(new ClaimsPrincipal()).Verified);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Loopback_reverse_proxy_with_a_public_host_never_becomes_local_owner(bool anonymousEnabled,
        bool allowed)
    {
        var context = PublicRequest();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new HostString("public.example");

        var decision = Policy(anonymousEnabled).Evaluate(context);

        Assert.Equal(allowed, decision.Allowed);
        if (allowed) Assert.Equal(WebAccessMode.AnonymousPublic, decision.Mode);
        else Assert.Equal("LOCAL_ACCESS_REQUIRED", decision.ErrorCode);
    }

    private static WebAccessPolicy Policy(bool enabled) => new(Options.Create(new WebRemoteAccessOptions
        { AllowAnonymousPublicAccess = enabled }));

    [Theory]
    [InlineData("http://198.51.100.10", true)]
    [InlineData("http://other.example", false)]
    [InlineData("null", false)]
    [InlineData(null, false)]
    public async Task Public_upload_and_mapped_edit_filter_requires_same_origin(string? origin, bool allowed)
    {
        var context = PublicRequest();
        context.User = WebAccessPolicy.CreatePrincipal(Policy(true).Evaluate(context));
        if (origin is not null) context.Request.Headers.Origin = origin;
        var invoked = false;
        await new WebUploadOriginFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context), _ =>
        {
            invoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });
        Assert.Equal(allowed, invoked);
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_call_raw_routes_but_can_call_player_projection_routes()
    {
        var raw = PublicRequest();
        var projection = PublicRequest();
        var policy = Policy(true);
        var filter = new WebInterfaceSecurityFilter(
            new(policy, new PrivateOperatorAuthorizationPolicy()), policy);
        var rawInvoked = false;
        var projectionInvoked = false;
        raw.Request.Path = "/api/data/entity/secret";
        projection.Request.Path = "/api/applications/dnd2024/state-spaces/main/entities/hero/read-models/dnd2024.query.party";

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(raw), _ =>
        {
            rawInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(projection), _ =>
        {
            projectionInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(rawInvoked);
        Assert.True(projectionInvoked);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/ui/dnd2024", true)]
    [InlineData("/components/system-workspace.js", true)]
    [InlineData("/api/web/applications", true)]
    [InlineData("/api/readiness/applications/dnd2024", true)]
    [InlineData("/api/audience-context", true)]
    [InlineData("/api/applications/dnd2024/content", true)]
    [InlineData("/api/applications/dnd2024/rules", true)]
    [InlineData("/api/applications/dnd2024/state-spaces/main/entities/hero/read-models/query", true)]
    [InlineData("/api/applications/dnd2024/state-spaces/main/media-batch", false)]
    [InlineData("/api/applications/dnd2024/state-spaces/main/entities/hero/media", false)]
    [InlineData("/api/applications/dnd2024/state-spaces/foreign/entities/undiscovered/media/player-visible/content", false)]
    [InlineData("/api/data/entity/secret", false)]
    [InlineData("/api/changes", false)]
    [InlineData("/api/applications/dnd2024/catalog/browse", false)]
    [InlineData("/api/applications/dnd2024/state-spaces/main/entities/hero", false)]
    [InlineData("/api/applications/dnd2024/state-spaces/main/entities/hero/components", false)]
    [InlineData("/api/blobs/secret", false)]
    [InlineData("/ui/control-center", false)]
    public void Anonymous_route_surface_contains_the_player_site_without_raw_authority(
        string path, bool expected)
    {
        Assert.Equal(expected, WebAccessPolicy.IsPlayerSafePublicPath(path));
    }

    [Fact]
    public async Task Anonymous_player_routes_are_read_only_even_when_the_path_has_an_action_shape()
    {
        var context = PublicRequest();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/ui/dnd2024/actions/change-premise";
        var policy = Policy(true);
        var filter = new WebInterfaceSecurityFilter(
            new(policy, new PrivateOperatorAuthorizationPolicy()), policy);
        var invoked = false;

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context), _ =>
        {
            invoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(invoked);
    }

    private static DefaultHttpContext PublicRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.4");
        context.Connection.LocalPort = 6217;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("198.51.100.10");
        return context;
    }
}
