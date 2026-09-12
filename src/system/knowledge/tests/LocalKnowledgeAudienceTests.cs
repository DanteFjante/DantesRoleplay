using System.Net;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Tests;

public sealed class LocalKnowledgeAudienceTests
{
    [Fact]
    public void Application_binding_is_selected_per_request_instead_of_at_startup()
    {
        using var app = Host();
        using var first = app.Services.CreateScope();
        var original = first.ServiceProvider.GetRequiredService<KnowledgeApplicationSelection>();
        Assert.Equal("fixture", original.ApplicationId);
        app.Configuration["Knowledge:LocalPlayer:ApplicationId"] = "another-application";
        using var second = app.Services.CreateScope();
        Assert.Equal("another-application", second.ServiceProvider
            .GetRequiredService<KnowledgeApplicationSelection>().ApplicationId);
        Assert.Equal("fixture", original.ApplicationId);
    }

    [Fact]
    public async Task Loopback_exact_campaign_grants_fixed_actor_and_revokes_on_next_request()
    {
        using var app = Host();
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = Context(IPAddress.Loopback);
        var policy = app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();

        var granted = await policy.ResolveAsync("campaign.fixture");
        var wrongCampaign = await policy.ResolveAsync("campaign.other");
        app.Configuration["Knowledge:LocalPlayer:Enabled"] = "false";
        var revoked = await policy.ResolveAsync("campaign.fixture");

        Assert.True(granted.Granted);
        Assert.Equal(KnowledgeAudienceRole.Actor, granted.Grant!.Role);
        Assert.Equal("actor.fixture", granted.Grant.ActorId);
        Assert.Equal("principal.fixture", granted.Grant.PrincipalId);
        Assert.Equal(64, granted.Grant.PolicyRevision.Length);
        Assert.False(wrongCampaign.Granted);
        Assert.False(revoked.Granted);
    }

    [Fact]
    public async Task Remote_or_missing_http_peer_never_grants_and_host_composition_resolves()
    {
        using var app = Host();
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        var policy = app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();

        accessor.HttpContext = Context(IPAddress.Parse("100.64.0.8"));
        var remote = await policy.ResolveAsync("campaign.fixture");
        accessor.HttpContext = null;
        var missing = await policy.ResolveAsync("campaign.fixture");

        Assert.False(remote.Granted);
        Assert.False(missing.Granted);
        using var scope = app.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKnowledgeApplicationBindingResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKnowledgeActorParticipationVerifier>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeCandidateResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeCoordinator>());
    }

    [Fact]
    public async Task Loopback_exact_campaign_grants_fixed_game_master_without_an_actor()
    {
        using var app = Host(gameMaster: true);
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = Context(IPAddress.Loopback);
        var policy = app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();

        var granted = await policy.ResolveAsync("campaign.fixture");

        Assert.True(granted.Granted);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, granted.Grant!.Role);
        Assert.Null(granted.Grant.ActorId);
        Assert.Equal("principal.fixture", granted.Grant.PrincipalId);
    }

    [Fact]
    public async Task Loopback_game_master_may_select_another_campaign_but_actor_may_not()
    {
        using var gameMasterApp = Host(gameMaster: true);
        var gameMasterAccessor = gameMasterApp.Services.GetRequiredService<IHttpContextAccessor>();
        gameMasterAccessor.HttpContext = Context(IPAddress.Loopback);
        var gameMasterPolicy = gameMasterApp.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();

        using var actorApp = Host();
        var actorAccessor = actorApp.Services.GetRequiredService<IHttpContextAccessor>();
        actorAccessor.HttpContext = Context(IPAddress.Loopback);
        var actorPolicy = actorApp.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();

        var gameMaster = await gameMasterPolicy.ResolveAsync("campaign.other");
        var actor = await actorPolicy.ResolveAsync("campaign.other");

        Assert.True(gameMaster.Granted);
        Assert.Equal("campaign.other", gameMaster.Grant!.CampaignId);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, gameMaster.Grant.Role);
        Assert.Null(gameMaster.Grant.ActorId);
        Assert.False(actor.Granted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Anonymous_website_never_receives_shared_game_master_authority(
        bool enabled, bool gameMaster)
    {
        using var app = Host(gameMaster);
        var context = Context(IPAddress.Parse("203.0.113.4"));
        context.Request.Path = "/api/audience-context";
        context.Request.Host = new HostString("198.51.100.10");
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "spoofed@example.com";
        app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var policy = app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();
        Assert.False((await policy.ResolveAsync("campaign.fixture")).Granted);

        var webAccess = new WebAccessPolicy(Options.Create(new WebRemoteAccessOptions
            { AllowAnonymousPublicAccess = enabled }));
        var filter = new WebInterfaceSecurityFilter(
            new(webAccess, new PrivateOperatorAuthorizationPolicy()), webAccess);
        KnowledgeAudienceResolution? audience = null;
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context), async _ =>
        {
            audience = await policy.ResolveAsync("campaign.fixture");
            return Results.Ok();
        });

        if (enabled)
        {
            Assert.True(audience?.Granted);
            Assert.Equal("campaign.fixture", audience!.Grant!.CampaignId);
            Assert.Equal("public-website", audience.Grant.PrincipalId);
            Assert.Equal(KnowledgeAudienceRole.PlayerGroup, audience.Grant.Role);
            Assert.Null(audience.Grant.ActorId);
        }
        else Assert.False(audience?.Granted == true);
        Assert.False((await policy.ResolveAsync("campaign.other")).Granted);
        context.Request.Path = "/mcp";
        Assert.False((await policy.ResolveAsync("campaign.fixture")).Granted);
        context.Request.Path = "/api/audience-context";
        context.Connection.RemoteIpAddress = null;
        Assert.False((await policy.ResolveAsync("campaign.fixture")).Granted);
    }

    [Theory]
    [InlineData(WebAccessMode.Local)]
    [InlineData(WebAccessMode.Tailscale)]
    public async Task Website_reads_media_and_rules_share_authority_without_an_enabled_MCP_actor(WebAccessMode mode)
    {
        using var app = Host();
        app.Configuration["Knowledge:LocalPlayer:Enabled"] = "false";
        app.Configuration["Knowledge:LocalPlayer:ActorId"] = null;
        var context = Context(mode == WebAccessMode.Local ? IPAddress.Loopback : IPAddress.Parse("203.0.113.4"));
        context.Request.Path = "/api/audience-context";
        context.User = WebAccessPolicy.CreatePrincipal(new(true, mode, "fixture@example.com"));
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = context;
        var policy = app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>();
        var grant = await policy.ResolveAsync("campaign.other");
        Assert.True(grant.Granted);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, grant.Grant!.Role);
        Assert.Null(grant.Grant.ActorId);
        Assert.Equal(DantesRoleplay.CatalogNavigation.ReadableRuleAudience.Dm,
            app.Services.GetRequiredService<DantesRoleplay.Web.Hosting.IWebReadableRulesAudienceProvider>().Current());
        Assert.Equal(DantesRoleplay.Media.EntityMediaAudience.GameMaster,
            app.Services.GetRequiredService<DantesRoleplay.Media.IEntityMediaAudienceResolver>()
                .Resolve(DantesRoleplay.Applications.ApplicationIdentifier.Parse("fixture"))!.Audience);
        context.Request.Path = "/mcp";
        Assert.False((await policy.ResolveAsync("campaign.fixture")).Granted);
        context.Request.Path = "/api/audience-context";
        context.User = new();
        Assert.False((await policy.ResolveAsync("campaign.fixture")).Granted);
    }

    [Fact]
    public async Task Configured_game_master_mcp_retains_game_master_media_authority()
    {
        using var app = Host(gameMaster: true);
        var context = Context(IPAddress.Loopback);
        context.Request.Path = ServerConfiguration.McpEndpoint;
        app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var grant = await app.Services.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>()
            .ResolveAsync("campaign.fixture");

        Assert.True(grant.Granted);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, grant.Grant!.Role);
        Assert.Equal(DantesRoleplay.Media.EntityMediaAudience.GameMaster,
            app.Services.GetRequiredService<DantesRoleplay.Media.IEntityMediaAudienceResolver>()
                .Resolve(DantesRoleplay.Applications.ApplicationIdentifier.Parse("fixture"))!.Audience);
    }

    [Fact]
    public void Anonymous_website_forces_rules_and_media_to_player_audience()
    {
        using var app = Host(gameMaster: true);
        var context = Context(IPAddress.Parse("203.0.113.4"));
        context.Request.Path = "/api/audience-context";
        context.User = WebAccessPolicy.CreatePrincipal(new(true, WebAccessMode.AnonymousPublic));
        app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        Assert.Equal(DantesRoleplay.CatalogNavigation.ReadableRuleAudience.Public,
            app.Services.GetRequiredService<DantesRoleplay.Web.Hosting.IWebReadableRulesAudienceProvider>().Current());
        Assert.Equal(DantesRoleplay.Media.EntityMediaAudience.Player,
            app.Services.GetRequiredService<DantesRoleplay.Media.IEntityMediaAudienceResolver>()
                .Resolve(DantesRoleplay.Applications.ApplicationIdentifier.Parse("fixture"))!.Audience);
    }

    private static WebApplication Host(bool gameMaster = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder.Configuration["Knowledge:LocalPlayer:Enabled"] = "true";
        builder.Configuration["Knowledge:LocalPlayer:PrincipalId"] = "principal.fixture";
        builder.Configuration["Knowledge:LocalPlayer:ApplicationId"] = "fixture";
        builder.Configuration["Knowledge:LocalPlayer:CampaignId"] = "campaign.fixture";
        builder.Configuration["Knowledge:LocalPlayer:Role"] = gameMaster ? "GameMaster" : "Actor";
        if (!gameMaster) builder.Configuration["Knowledge:LocalPlayer:ActorId"] = "actor.fixture";
        builder.Services.AddSingleton<ILocalStructuredCompletionProvider, Completion>();
        builder.Services.AddDantesRoleplayMcpServer(
            "Data Source=:memory:", DatabaseProvider.Sqlite,
            hostConfiguration: builder.Configuration);
        return builder.Build();
    }

    private static DefaultHttpContext Context(IPAddress address)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = address;
        context.Request.Path = ServerConfiguration.McpEndpoint;
        return context;
    }

    private sealed class Completion : ILocalStructuredCompletionProvider
    {
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalModelStatus.Unavailable("fixture", "fixture"));

        public Task<StructuredCompletionResult> CompleteAsync(
            StructuredCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StructuredCompletionResult.Failure("fixture", "fixture"));
    }
}
