using System.Net;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Retrieval;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.Tests;

public sealed class LocalKnowledgeAudienceTests
{
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
        context.Connection.RemoteIpAddress = address;
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
