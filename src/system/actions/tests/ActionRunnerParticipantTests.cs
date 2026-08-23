using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.Story;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Slice 6: receipt staging shares the action transaction and cannot leave partial state.</summary>
public sealed class ActionRunnerParticipantTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_staged_participant_receipt_commits_with_the_effect_and_success_audit()
    {
        await using var db = _fixture.CreateContext();
        var (runner, world) = await RunnerAsync(db);
        var participant = new ReceiptParticipant(db);

        var result = await ((IStoryPlanActionRunner)runner).RunWithParticipantAsync(Request(), participant);

        Assert.True(result.Ok, result.Error?.Why);
        Assert.True(participant.Staged);
        Assert.Contains("\"vigour\":6", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
        Assert.Contains(await db.Operations.ToListAsync(), operation => operation.Id == ReceiptParticipant.ReceiptOperationId && operation.Success);
        Assert.Contains(await db.Operations.ToListAsync(), operation => operation.Id == result.OperationId && operation.Success);
    }

    [Fact]
    public async Task A_failure_after_receipt_staging_rolls_back_effect_success_audit_and_receipt_together()
    {
        await using var db = _fixture.CreateContext();
        var (runner, world) = await RunnerAsync(db);
        var participant = new ReceiptParticipant(db, throwAfterStage: true);

        var result = await ((IStoryPlanActionRunner)runner).RunWithParticipantAsync(Request(), participant);

        Assert.False(result.Ok);
        Assert.Equal("UNHANDLED", result.Error?.Code);
        Assert.Contains("\"vigour\":10", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
        var operations = await db.Operations.ToListAsync();
        Assert.DoesNotContain(operations, operation => operation.Id == ReceiptParticipant.ReceiptOperationId);
        Assert.Single(operations);
        Assert.False(operations[0].Success);
    }

    [Fact]
    public async Task Story_action_executor_commits_the_action_and_durable_story_receipt_together()
    {
        await using var db = _fixture.CreateContext();
        var (runner, world) = await RunnerAsync(db);
        var store = new StoryPlanStore(db);
        var run = await store.CreateAsync(ActionPlan());
        var lease = (await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!;
        var executor = new StoryPlanActionExecutor(db, (IStoryPlanActionRunner)runner);

        var result = await executor.ExecuteAsync(run, Assert.Single(run.Steps), Preparation(), lease, CancellationToken.None);

        var completed = (await store.GetAsync(run.Id))!;
        var step = Assert.Single(completed.Steps);
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal(StoryPlanStatus.Completed, completed.Status);
        Assert.Equal(StoryPlanStepStatus.Completed, step.Status);
        Assert.Equal(result.OperationId, step.ActionOperationId);
        Assert.Contains("mechanic.receipt.vigour", step.MechanicId, StringComparison.Ordinal);
        Assert.Contains("\"vigour\":6", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
        Assert.Contains(await db.Operations.ToListAsync(), operation => operation.Id == result.OperationId && operation.Success);
    }

    [Fact]
    public async Task Story_action_executor_rolls_back_when_the_lease_or_cancellation_guard_is_stale()
    {
        await using var db = _fixture.CreateContext();
        var (runner, world) = await RunnerAsync(db);
        var store = new StoryPlanStore(db);
        var run = await store.CreateAsync(ActionPlan());
        var lease = (await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!;
        var current = await db.StoryPlanRuns.SingleAsync();
        current.CancelRequested = true;
        current.Revision++;
        await db.SaveChangesAsync();
        var executor = new StoryPlanActionExecutor(db, (IStoryPlanActionRunner)runner);

        var result = await executor.ExecuteAsync(run, Assert.Single(run.Steps), Preparation(), lease, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("CANCELLED", result.Error?.Code);
        Assert.Contains("\"vigour\":10", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
        var persisted = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Running, persisted.Status);
        Assert.Equal(StoryPlanStepStatus.Running, Assert.Single(persisted.Steps).Status);
    }

    [Fact]
    public async Task An_oversized_story_receipt_rolls_back_the_action_with_the_internal_failure_code()
    {
        await using var db = _fixture.CreateContext();
        var (runner, world) = await RunnerAsync(db);

        var result = await ((IStoryPlanActionRunner)runner).RunWithParticipantAsync(Request(), new OversizedReceiptParticipant());

        Assert.False(result.Ok);
        Assert.Equal("STORY_INTERNAL_FAILURE", result.Error?.Code);
        Assert.Contains("\"vigour\":10", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
    }

    private static async Task<(ActionRunner Runner, WorldStore World)> RunnerAsync(DantesRoleplayDbContext db)
    {
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", "{\"vigour\":10}");
        await mechanics.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.receipt.vigour",
            Category = "test",
            Name = "Spend vigour with receipt",
            Matches = "spend vigour with receipt",
            Requirements = "{\"roles\":{\"subject\":{\"components\":[\"stats\"]}}}",
            Status = MechanicStatus.Active,
            Source = """
                var stats = JSON.parse(ctx.roles.subject.components.stats);
                return { effects: [{ type: 'component.merge', entityId: ctx.roles.subject.id,
                  definitionId: 'stats', data: JSON.stringify({ vigour: stats.vigour - ctx.input.cost }) }] };
                """
        });
        return (new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine())), world);
    }

    private static ActionRequest Request() => new()
    {
        Intent = "spend vigour with receipt",
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = "orban" },
        Input = "{\"cost\":4}"
    };

    private static StoryPlanRun ActionPlan() => new()
    {
        Id = "story-plan.0123456789abcdef0123456789abcdef",
        RequestToken = "story-plan.receipt-test",
        CampaignId = "campaign.test.story",
        Objective = "Spend vigour safely.",
        PlanJson = "{}",
        PrincipalId = "development.test",
        PolicyRevision = "development-static-v1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Steps =
        [
            new StoryPlanStepRun
            {
                StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef",
                StepIndex = 0,
                StepId = "act",
                Kind = StoryPlanStepKind.Action,
                Intent = "spend vigour with receipt",
                RoleEntityIdsJson = "{\"subject\":\"orban\"}",
                InputJson = "{\"cost\":4}"
            }
        ]
    };

    private static StoryActionPreparation Preparation() => new(
        new LocalActionProposal("action", "mechanic.receipt.vigour", "spend vigour with receipt",
            new Dictionary<string, string> { ["subject"] = "orban" }, "{\"cost\":4}", null, ["procedure.test.action"]),
        [new("procedure.test.action", 1, "0123456789abcdef")], "mechanic.receipt.vigour", 1);

    private sealed class ReceiptParticipant(DantesRoleplayDbContext db, bool throwAfterStage = false) : IActionCommitParticipant
    {
        public const string ReceiptOperationId = "11111111111111111111111111111111";
        public bool Staged { get; private set; }

        public async Task StageAsync(ActionRunResult result, CancellationToken cancellationToken)
        {
            Staged = true;
            db.Operations.Add(new Operation
            {
                Id = ReceiptOperationId,
                Timestamp = DateTime.UtcNow,
                Tool = "commit",
                Summary = "Staged story receipt.",
                Success = true
            });
            await db.SaveChangesAsync(cancellationToken);
            if (throwAfterStage) throw new InvalidOperationException("Receipt staging failed after it was written.");
        }
    }

    private sealed class OversizedReceiptParticipant : IActionCommitParticipant
    {
        public Task StageAsync(ActionRunResult result, CancellationToken cancellationToken) =>
            Task.FromException(new StoryPlanResultLimitException());
    }
}
