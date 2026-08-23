using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.Security;
using DantesRoleplay.Story;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Slice 7: serial story orchestration retains completed history across every step kind.</summary>
public sealed class StoryPlanActionOrchestrationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Context_knowledge_and_action_complete_in_order_and_form_one_safe_handoff()
    {
        await using var db = _fixture.CreateContext();
        var (processor, store, world) = await ProcessorAsync(db, new ReadyRoutes());
        var run = await store.CreateAsync(Run(
            ("context", StoryPlanStepKind.CampaignContext, "Recall the campaign.", null, null),
            ("knowledge", StoryPlanStepKind.Knowledge, "What is known about the lens?", null, null),
            ("action", StoryPlanStepKind.Action, "spend vigour", "{\"subject\":\"orban\"}", "{\"cost\":4}")));

        for (var index = 0; index < 3; index++)
        {
            var lease = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);
            Assert.NotNull(lease);
            Assert.Equal(index, lease!.StepIndex);
            await processor.ProcessAsync(lease, CancellationToken.None);
        }

        var completed = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Completed, completed.Status);
        Assert.Equal(3, completed.CompletedStepCount);
        Assert.All(completed.Steps, step => Assert.Equal(StoryPlanStepStatus.Completed, step.Status));
        Assert.Contains("\"vigour\":6", (await world.GetEntityAsync("orban"))!.Components.Single().Data);
        Assert.Contains("The lens opens at moonrise.", completed.HandoffJson, StringComparison.Ordinal);
        Assert.Contains("Vigour spent.", completed.HandoffJson, StringComparison.Ordinal);
        Assert.Contains("procedure.play.storytelling", completed.HandoffJson, StringComparison.Ordinal);
        Assert.DoesNotContain("procedure.game.core.world.knowledge\",\"version", completed.HandoffJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blocked_action_preserves_prior_results_skips_later_work_and_returns_a_partial_handoff()
    {
        await using var db = _fixture.CreateContext();
        var (processor, store, _) = await ProcessorAsync(db, new BlockedRoutes());
        var run = await store.CreateAsync(Run(
            ("knowledge", StoryPlanStepKind.Knowledge, "What is known about the lens?", null, null),
            ("action", StoryPlanStepKind.Action, "spend vigour", "{\"subject\":\"orban\"}", "{\"cost\":4}"),
            ("later", StoryPlanStepKind.Knowledge, "What happens next?", null, null)));

        for (var index = 0; index < 2; index++)
        {
            var lease = (await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!;
            await processor.ProcessAsync(lease, CancellationToken.None);
        }

        var blocked = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Blocked, blocked.Status);
        Assert.Equal(1, blocked.CompletedStepCount);
        Assert.Equal("STORY_ROUTE_NEEDS_INPUT", blocked.StopCode);
        Assert.Equal([StoryPlanStepStatus.Completed, StoryPlanStepStatus.Blocked, StoryPlanStepStatus.Skipped], blocked.Steps.OrderBy(step => step.StepIndex).Select(step => step.Status));
        Assert.Contains("The lens opens at moonrise.", blocked.HandoffJson, StringComparison.Ordinal);
        Assert.Contains("Choose the subject.", blocked.HandoffJson, StringComparison.Ordinal);
    }

    private static async Task<(IStoryPlanStepProcessor Processor, StoryPlanStore Store, WorldStore World)> ProcessorAsync(
        DantesRoleplayDbContext db, ILocalRouteProposalCoordinator routes)
    {
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", "{\"vigour\":10}");
        await mechanics.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.story.vigour", Category = "test", Name = "Spend vigour", Matches = "spend vigour",
            Requirements = "{\"roles\":{\"subject\":{\"components\":[\"stats\"]}}}", Status = MechanicStatus.Active,
            Source = """
                var stats = JSON.parse(ctx.roles.subject.components.stats);
                return { narration: 'Vigour spent.', effects: [{ type: 'component.merge', entityId: ctx.roles.subject.id,
                  definitionId: 'stats', data: JSON.stringify({ vigour: stats.vigour - ctx.input.cost }) }] };
                """
        });
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.campaign.chapter", "query(kind: \"campaign-resume\")");
        await WriteProcedureAsync(procedures, "procedure.game.core.world.knowledge", "query(kind: \"knowledge-answer\")");
        await WriteProcedureAsync(procedures, "procedure.test.action", "commit(kind: \"action\")");
        var store = new StoryPlanStore(db);
        var projections = new ProjectionResolver(db);
        var runner = new ActionRunner(db, mechanics, projections, new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, projections, new JintMechanicEngine()));
        var preparer = new StoryActionStepPreparer(routes, procedures, mechanics, new OperationLog(db), new ReadyVerifier());
        var executor = new StoryPlanActionExecutor(db, (IStoryPlanActionRunner)runner);
        var resume = new ResumeReader(new("campaign.test.story", "The Observatory", "Find the vanished astronomer.", ["Reach the lens"], [], "world.test", null, null, [], [], "trusted-host-only"));
        var knowledge = new KnowledgeCoordinator(new("answered", [new("The lens opens at moonrise.", "true", "fact")], []));
        return (new StoryPlanStepProcessor(db, store, new GameMasterAudience(), resume, knowledge, procedures, new OperationLog(db), preparer, executor), store, world);
    }

    private static async Task WriteProcedureAsync(IProcedureStore procedures, string id, string governs) =>
        await procedures.WriteAsync(new WriteProcedureRequest { Id = id, Category = "test.story", Name = id,
            Description = "A test story-plan procedure.", Instructions = "Use the typed bounded path.", Governs = governs, CreatedBy = "test" });

    private static StoryPlanRun Run(params (string Id, string Kind, string Intent, string? Roles, string? Input)[] steps) => new()
    {
        Id = "story-plan.0123456789abcdef0123456789abcdef", RequestToken = "story-plan.action-orchestration", CampaignId = "campaign.test.story",
        Objective = "Resolve the observatory signal.", PlanJson = "{}", PrincipalId = "development.test", PolicyRevision = "development-static-v1",
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        Steps = steps.Select((step, index) => new StoryPlanStepRun { StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef", StepIndex = index,
            StepId = step.Id, Kind = step.Kind, Intent = step.Intent, RoleEntityIdsJson = step.Roles ?? "{}", InputJson = step.Input ?? "{}" }).ToList()
    };

    private sealed class GameMasterAudience : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedCampaignAudienceResolution(new("development.test", campaignId, CampaignAudienceRoles.GameMaster, null, "development-static-v1")));
    }

    private sealed class ResumeReader(CampaignResume answer) : ICampaignResumeReader
    {
        public Task<CampaignResume?> GetAsync(string campaignId, CancellationToken cancellationToken = default) => Task.FromResult<CampaignResume?>(answer);
    }

    private sealed class KnowledgeCoordinator(AuthorizedKnowledgeAnswerResult answer) : IAuthorizedKnowledgeAnswerCoordinator
    {
        public Task<AuthorizedKnowledgeAnswerResult> AnswerAsync(AuthorizedKnowledgeAnswerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(answer);
    }

    private sealed class ReadyVerifier : IProcedureBoundActionVerifier
    {
        public Task<ProcedureBoundActionVerification> VerifyAsync(string objective, LocalActionProposal proposal, int mechanicVersion, IReadOnlyList<ProcedureDetail> procedures, IReadOnlyList<string> priorSummaries, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcedureBoundActionVerification("ready", "Ready.", []));
    }

    private sealed class ReadyRoutes : ILocalRouteProposalCoordinator
    {
        public Task<LocalRouteProposalResult> ProposeAsync(LocalRouteProposalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRouteProposalResult("proposed", "high", "Exact route.",
                [new("mechanic.story.vigour", "test", "Spend vigour", "", "spend vigour", "", MechanicStatus.Active, 1)],
                [new("procedure.test.action", "test.story", "Action", "", "commit(kind: \"action\")", ProcedureStatus.Active, 1)],
                new("action", "mechanic.story.vigour", request.Intent, request.RoleEntityIds!, request.Input, request.Scope, ["procedure.test.action"]), []));
    }

    private sealed class BlockedRoutes : ILocalRouteProposalCoordinator
    {
        public Task<LocalRouteProposalResult> ProposeAsync(LocalRouteProposalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRouteProposalResult("blocked", "none", "Choose the subject.", [], [], null, ["Choose the subject."]));
    }
}
