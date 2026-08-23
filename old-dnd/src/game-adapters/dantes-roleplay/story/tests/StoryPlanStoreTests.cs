using DantesRoleplay.DataAccess;
using DantesRoleplay.Story;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Slice 3: durable plan state can be claimed, leased, and cancelled without execution.</summary>
public sealed class StoryPlanStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Store_creates_reads_and_claims_a_durable_pending_plan_in_step_order()
    {
        await using var db = _fixture.CreateContext();
        var store = new StoryPlanStore(db);
        var created = await store.CreateAsync(Run());

        var found = await store.GetAsync(created.Id);
        var lease = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);

        Assert.NotNull(found);
        Assert.Equal(["first", "second"], found!.Steps.OrderBy(step => step.StepIndex).Select(step => step.StepId));
        Assert.NotNull(lease);
        Assert.Equal(0, lease!.StepIndex);
        Assert.Equal(StoryPlanStatus.Running, (await db.StoryPlanRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Store_claims_a_cancelled_pending_plan_so_the_worker_can_finalize_it()
    {
        await using var db = _fixture.CreateContext();
        var store = new StoryPlanStore(db);
        var run = await store.CreateAsync(Run());
        Assert.True(await store.RequestCancelAsync(run.Id, 1));

        var lease = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);

        Assert.NotNull(lease);
        Assert.Equal(run.Id, lease!.StoryPlanId);
    }

    [Fact]
    public async Task Lease_renewal_rejects_revision_loss_and_expired_running_work_is_reclaimed()
    {
        await using var db = _fixture.CreateContext();
        var store = new StoryPlanStore(db);
        var run = await store.CreateAsync(Run());
        var first = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);
        Assert.NotNull(first);
        Assert.True(await store.RenewLeaseAsync(first!, DateTime.UtcNow));
        Assert.False(await store.RenewLeaseAsync(first! with { Revision = first!.Revision - 1 }, DateTime.UtcNow));

        var tracked = await db.StoryPlanRuns.SingleAsync();
        tracked.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-3);
        await db.SaveChangesAsync();
        var reclaimed = await store.ClaimNextAsync("worker-b", DateTime.UtcNow);

        Assert.NotNull(reclaimed);
        Assert.Equal("worker-b", reclaimed!.LeaseOwner);
        Assert.Equal(0, reclaimed.StepIndex);
    }

    [Fact]
    public async Task Lease_renewal_is_bound_to_the_claimed_running_step()
    {
        await using var db = _fixture.CreateContext();
        var store = new StoryPlanStore(db);
        await store.CreateAsync(Run());
        var lease = (await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!;

        Assert.False(await store.RenewLeaseAsync(lease with { StepIndex = 1 }, DateTime.UtcNow));

        var run = await db.StoryPlanRuns.SingleAsync();
        run.Status = StoryPlanStatus.Pending;
        await db.SaveChangesAsync();
        Assert.False(await store.RenewLeaseAsync(lease, DateTime.UtcNow));
    }

    [Fact]
    public async Task Durable_steps_cannot_outlive_their_plan_row()
    {
        await using var db = _fixture.CreateContext();
        var run = await new StoryPlanStore(db).CreateAsync(Run());

        Assert.Throws<InvalidOperationException>(() => db.StoryPlanRuns.Remove(run));
        await Task.CompletedTask;
    }

    private static StoryPlanRun Run() => new()
    {
        Id = "story-plan.0123456789abcdef0123456789abcdef",
        RequestToken = "story-plan.store-01",
        CampaignId = "campaign.test.story",
        Objective = "Test durable storage.",
        PlanJson = "{}",
        PrincipalId = "development.test",
        PolicyRevision = "development-static-v1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Steps =
        [
            new() { StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef", StepIndex = 0, StepId = "first", Kind = StoryPlanStepKind.Knowledge, Intent = "What is known?", RoleEntityIdsJson = "{}", InputJson = "{}" },
            new() { StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef", StepIndex = 1, StepId = "second", Kind = StoryPlanStepKind.Knowledge, Intent = "What changed?", RoleEntityIdsJson = "{}", InputJson = "{}" }
        ]
    };
}
