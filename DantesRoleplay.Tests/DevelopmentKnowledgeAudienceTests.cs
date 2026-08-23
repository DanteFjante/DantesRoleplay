using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Security;
using DantesRoleplay.World;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.Tests;

public sealed class DevelopmentKnowledgeAudienceTests
{
    [Fact]
    public async Task Fixed_actor_seat_grants_only_its_configured_campaign()
    {
        var policy = new DevelopmentCampaignAudiencePolicy(new DevelopmentKnowledgeAudienceOptions
        {
            Enabled = true,
            PrincipalId = "development.alice",
            CampaignId = "campaign.local",
            Role = "actor",
            ActorId = "actor.alice"
        });

        var granted = await policy.ResolveAsync("campaign.local");
        var denied = await policy.ResolveAsync("campaign.other");

        Assert.True(granted.Granted);
        Assert.Equal("actor.alice", granted.Grant!.ActorId);
        Assert.False(denied.Granted);
    }

    [Fact]
    public void Disabled_host_uses_the_safe_unavailable_placeholder()
    {
        using var app = Host("Data Source=:memory:");
        using var scope = app.Services.CreateScope();

        Assert.IsType<UnavailableKnowledgeAnswerCoordinator>(
            scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeAnswerCoordinator>());
        Assert.Null(scope.ServiceProvider.GetService<IAuthenticatedCampaignAudiencePolicy>());
    }

    [Fact]
    public void Enabled_host_registers_the_fixed_policy_and_real_answer_coordinator()
    {
        using var app = Host("Data Source=:memory:", new DevelopmentKnowledgeAudienceOptions
        {
            Enabled = true,
            CampaignId = "campaign.local",
            Role = "gm"
        });
        using var scope = app.Services.CreateScope();

        Assert.IsType<DevelopmentCampaignAudiencePolicy>(
            scope.ServiceProvider.GetRequiredService<IAuthenticatedCampaignAudiencePolicy>());
        Assert.IsNotType<UnavailableKnowledgeAnswerCoordinator>(
            scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeAnswerCoordinator>());
    }

    private static WebApplication Host(string connectionString, DevelopmentKnowledgeAudienceOptions? audience = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder.Services.AddDantesRoleplayMcpServer(connectionString, DatabaseProvider.Sqlite,
            developmentKnowledgeAudience: audience);
        return builder.Build();
    }
}
