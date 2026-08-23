using DantesRoleplay.DataAccess;
using DantesRoleplay.Security;
using DantesRoleplay.Story;

namespace DantesRoleplay.Tests;

/// <summary>Slice 4: authorization occurs before durable state is examined for a start.</summary>
public sealed class StoryPlanCoordinatorPolicyTests
{
    [Fact]
    public async Task Denied_start_does_not_read_or_write_the_store()
    {
        var coordinator = new StoryPlanCoordinator(new ThrowingStore(), new DeniedAudience(), new StoryPlanWakeQueue());
        var result = await coordinator.StartAsync(new("start", "story-plan.test-06", "campaign.test.story", "Test.",
            [new("fact", StoryPlanStepKind.Knowledge, "What is known?")]));

        Assert.Equal("STORY_AUDIENCE_DENIED", result.StopCode);
    }

    [Fact]
    public async Task Actor_grant_is_denied_before_a_start_touches_the_store()
    {
        var coordinator = new StoryPlanCoordinator(new ThrowingStore(), new ActorAudience(), new StoryPlanWakeQueue());
        var result = await coordinator.StartAsync(new("start", "story-plan.test-07", "campaign.test.story", "Test.",
            [new("fact", StoryPlanStepKind.Knowledge, "What is known?")]));

        Assert.Equal("STORY_AUDIENCE_DENIED", result.StopCode);
    }

    [Fact]
    public async Task Query_waits_only_until_the_revision_changes()
    {
        var coordinator = new StoryPlanCoordinator(new RevisionChangingStore(), new GameMasterAudience(), new StoryPlanWakeQueue());

        var result = await coordinator.GetAsync(new("story-plan.0123456789abcdef0123456789abcdef", AfterRevision: 1, WaitSeconds: 1));

        Assert.Equal(2, result.Revision);
    }

    private sealed class DeniedAudience : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticatedCampaignAudienceResolution.Denied());
    }

    private sealed class ActorAudience : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedCampaignAudienceResolution(new("development.actor", campaignId, CampaignAudienceRoles.Actor, "actor.test", "development-static-v1")));
    }

    private sealed class GameMasterAudience : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedCampaignAudienceResolution(new("development.gm", campaignId, CampaignAudienceRoles.GameMaster, null, "development-static-v1")));
    }

    private sealed class RevisionChangingStore : IStoryPlanStore
    {
        private int _reads;
        public Task<StoryPlanRun?> GetAsync(string storyPlanId, bool tracked = false, CancellationToken cancellationToken = default)
        {
            var revision = Interlocked.Increment(ref _reads) == 1 ? 1 : 2;
            return Task.FromResult<StoryPlanRun?>(new()
            {
                Id = storyPlanId, RequestToken = "story-plan.wait-01", CampaignId = "campaign.test.story", Objective = "Test.", PlanJson = "{}",
                PrincipalId = "development.gm", PolicyRevision = "development-static-v1", Revision = revision,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                Steps = [new() { StoryPlanId = storyPlanId, StepIndex = 0, StepId = "fact", Kind = StoryPlanStepKind.Knowledge, Intent = "What is known?", RoleEntityIdsJson = "{}", InputJson = "{}" }]
            });
        }
        public Task<StoryPlanRun?> FindByRequestTokenAsync(string requestToken, bool tracked = false, CancellationToken cancellationToken = default) => throw ThrowingStore.Used();
        public Task<StoryPlanRun> CreateAsync(StoryPlanRun run, CancellationToken cancellationToken = default) => throw ThrowingStore.Used();
        public Task<bool> RequestCancelAsync(string storyPlanId, int expectedRevision, CancellationToken cancellationToken = default) => throw ThrowingStore.Used();
        public Task<StoryPlanLease?> ClaimNextAsync(string leaseOwner, DateTime nowUtc, CancellationToken cancellationToken = default) => throw ThrowingStore.Used();
        public Task<bool> RenewLeaseAsync(StoryPlanLease lease, DateTime nowUtc, CancellationToken cancellationToken = default) => throw ThrowingStore.Used();
    }

    private sealed class ThrowingStore : IStoryPlanStore
    {
        internal static Exception Used() => new("Store must not be touched for a denied start.");
        public Task<StoryPlanRun?> GetAsync(string storyPlanId, bool tracked = false, CancellationToken cancellationToken = default) => throw Used();
        public Task<StoryPlanRun?> FindByRequestTokenAsync(string requestToken, bool tracked = false, CancellationToken cancellationToken = default) => throw Used();
        public Task<StoryPlanRun> CreateAsync(StoryPlanRun run, CancellationToken cancellationToken = default) => throw Used();
        public Task<bool> RequestCancelAsync(string storyPlanId, int expectedRevision, CancellationToken cancellationToken = default) => throw Used();
        public Task<StoryPlanLease?> ClaimNextAsync(string leaseOwner, DateTime nowUtc, CancellationToken cancellationToken = default) => throw Used();
        public Task<bool> RenewLeaseAsync(StoryPlanLease lease, DateTime nowUtc, CancellationToken cancellationToken = default) => throw Used();
    }
}
