using System.Net;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.Tests;

public sealed class LocalKnowledgeSeatLifecycleTests
{
    [Fact]
    public void Running_host_rechecks_seat_configuration_on_each_request()
    {
        var values = Values();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var running = new ConfigurationLocalKnowledgeSeatProvider(configuration);
        var initial = running.Current();

        configuration["Knowledge:LocalPlayer:CampaignId"] = "campaign.changed";
        configuration["Knowledge:LocalPlayer:Role"] = "Actor";
        configuration["Knowledge:LocalPlayer:ActorId"] = "actor.changed";

        var changed = running.Current();
        Assert.NotEqual(initial, changed);
        Assert.Equal("campaign.fixture", initial.CampaignId);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, initial.Role);
        Assert.Null(initial.ActorId);
        Assert.Equal(["dnd2024-core"], initial.SourceIds);
        Assert.Equal("campaign.changed", changed.CampaignId);
        Assert.Equal(KnowledgeAudienceRole.Actor, changed.Role);
        Assert.Equal("actor.changed", changed.ActorId);

        var restarted = new ConfigurationLocalKnowledgeSeatProvider(
            new ConfigurationBuilder().AddInMemoryCollection(Values()).Build());
        var afterRestart = restarted.Current();
        Assert.Equal(initial.Enabled, afterRestart.Enabled);
        Assert.Equal(initial.PrincipalId, afterRestart.PrincipalId);
        Assert.Equal(initial.ApplicationId, afterRestart.ApplicationId);
        Assert.Equal(initial.CampaignId, afterRestart.CampaignId);
        Assert.Equal(initial.ActorId, afterRestart.ActorId);
        Assert.Equal(initial.Role, afterRestart.Role);
        Assert.Equal(initial.SourceIds, afterRestart.SourceIds);
    }

    [Fact]
    public async Task Game_master_policy_requires_loopback_and_application_but_allows_selected_campaign()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var accessor = new HttpContextAccessor { HttpContext = context };
        var seat = new LocalKnowledgeSeatSnapshot(true, "principal.fixture", "dnd2024",
            "campaign.fixture", null, KnowledgeAudienceRole.GameMaster, ["dnd2024-core"]);
        var allowed = new LocalKnowledgeAudiencePolicy(accessor, new Seats(seat),
            new KnowledgeApplicationSelection("dnd2024"));

        var resolution = await allowed.ResolveAsync("campaign.fixture");
        Assert.NotNull(resolution.Grant);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, resolution.Grant!.Role);
        Assert.Null(resolution.Grant.ActorId);

        var selected = await allowed.ResolveAsync("campaign.other");
        Assert.Equal("campaign.other", selected.Grant!.CampaignId);
        Assert.Equal(KnowledgeAudienceRole.GameMaster, selected.Grant.Role);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        Assert.Null((await allowed.ResolveAsync("campaign.fixture")).Grant);
    }

    private static Dictionary<string, string?> Values() => new(StringComparer.Ordinal)
    {
        ["Knowledge:LocalPlayer:Enabled"] = "true",
        ["Knowledge:LocalPlayer:PrincipalId"] = "principal.fixture",
        ["Knowledge:LocalPlayer:ApplicationId"] = "dnd2024",
        ["Knowledge:LocalPlayer:SourceIds:0"] = "dnd2024-core",
        ["Knowledge:LocalPlayer:CampaignId"] = "campaign.fixture",
        ["Knowledge:LocalPlayer:Role"] = "GameMaster"
    };

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }
}
