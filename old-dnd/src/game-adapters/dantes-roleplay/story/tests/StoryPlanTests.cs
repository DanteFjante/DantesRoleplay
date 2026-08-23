using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Security;
using DantesRoleplay.Story;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class StoryPlanTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Exact_parser_accepts_the_closed_start_shape_and_rejects_unknown_fields()
    {
        using var valid = JsonDocument.Parse("""
            {"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Learn what changed.","steps":[{"id":"context","kind":"campaign-context","intent":"Recall current context."},{"id":"fact","kind":"knowledge","intent":"What is known?"}]}
            """);
        var parsed = StoryPlanJsonParser.TryParseStart(valid.RootElement, out var request);
        Assert.True(parsed.Valid, parsed.Problem?.Message);
        Assert.NotNull(request);
        Assert.Equal(StoryPlanStepKind.CampaignContext, request!.Steps[0].Kind);

        using var unknown = JsonDocument.Parse("""
            {"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Learn what changed.","steps":[{"id":"context","kind":"campaign-context","intent":"Recall current context."}],"mechanicId":"forbidden"}
            """);
        Assert.False(StoryPlanJsonParser.TryParseStart(unknown.RootElement, out _).Valid);
    }

    [Fact]
    public void Validator_requires_context_to_be_first_and_limits_actions()
    {
        var late = new StoryPlanStartRequest("start", "story-plan.test-02", "campaign.test.story", "Test.",
        [new("fact", "knowledge", "What is known?"), new("context", "campaign-context", "Recall context.")]);
        Assert.False(StoryPlanValidator.Validate(late).Valid);

        var manyActions = new StoryPlanStartRequest("start", "story-plan.test-03", "campaign.test.story", "Test.",
            Enumerable.Range(0, 5).Select(index => new StoryPlanStepRequest($"action-{index}", "action", "do thing", new Dictionary<string, string>(), "{}")).ToArray());
        Assert.False(StoryPlanValidator.Validate(manyActions).Valid);
    }

    [Fact]
    public async Task Start_is_durable_idempotent_and_cancellable_for_the_development_gm()
    {
        await using var db = _fixture.CreateContext();
        var coordinator = new StoryPlanCoordinator(new StoryPlanStore(db), new FixedAudience("campaign.test.story"), new StoryPlanWakeQueue());
        var request = new StoryPlanStartRequest("start", "story-plan.test-04", "campaign.test.story", "Learn what changed.",
            [new("fact", "knowledge", "What is known?")]);
        var started = await coordinator.StartAsync(request);
        Assert.Equal(StoryPlanStatus.Pending, started.Status);
        Assert.True(StoryPlanValidator.StoryPlanId(started.StoryPlanId));
        Assert.Single(db.StoryPlanRuns);
        Assert.Single(db.StoryPlanStepRuns);

        var replay = await coordinator.StartAsync(request);
        Assert.Equal(started.StoryPlanId, replay.StoryPlanId);
        Assert.Single(db.StoryPlanRuns);

        var cancelled = await coordinator.CancelAsync(new("cancel", started.StoryPlanId, started.Revision));
        Assert.True(cancelled.Status is StoryPlanStatus.Pending or StoryPlanStatus.Running);
        Assert.True((await db.StoryPlanRuns.SingleAsync()).CancelRequested);
    }

    [Fact]
    public async Task Start_normalizes_missing_and_empty_roles_for_idempotent_replay()
    {
        await using var db = _fixture.CreateContext();
        var coordinator = new StoryPlanCoordinator(new StoryPlanStore(db), new FixedAudience("campaign.test.story"), new StoryPlanWakeQueue());
        var missingRoles = new StoryPlanStartRequest("start", "story-plan.test-05", "campaign.test.story", "Learn what changed.",
            [new("act", "action", "Do it.")]);
        var explicitEmptyRoles = missingRoles with { Steps = [new("act", "action", "Do it.", new Dictionary<string, string>(), "{}")] };

        var first = await coordinator.StartAsync(missingRoles);
        var replay = await coordinator.StartAsync(explicitEmptyRoles);

        Assert.Equal(first.StoryPlanId, replay.StoryPlanId);
        Assert.Single(db.StoryPlanRuns);
    }

    private sealed class FixedAudience(string campaignId) : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string requestedCampaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(requestedCampaignId == campaignId
                ? new AuthenticatedCampaignAudienceResolution(new("development.test", campaignId, CampaignAudienceRoles.GameMaster, null, "development-static-v1"))
                : AuthenticatedCampaignAudienceResolution.Denied());
    }
}
