using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Security;
using DantesRoleplay.Story;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;

namespace DantesRoleplay.Tests;

/// <summary>Slice 5: fixed read steps complete serially with procedure evidence and bounded handoff data.</summary>
public sealed class StoryPlanReadStepProcessorTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Context_and_knowledge_steps_complete_in_order_with_fixed_procedure_evidence()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.campaign.chapter", "query(kind: \"campaign-resume\")");
        await WriteProcedureAsync(procedures, "procedure.game.core.world.knowledge", "query(kind: \"knowledge-answer\")");
        var store = new StoryPlanStore(db);
        var resume = new ResumeReader(new("campaign.test.story", "The Observatory", "Find the vanished astronomer.", ["Reach the lens"], ["No torture"], "world.test", null, null, [], [], "trusted-host-only"));
        var knowledge = new KnowledgeCoordinator(new("answered", [new("The lens opens at moonrise.", "true", "fact")], []));
        var processor = Processor(db, store, resume, knowledge, procedures);
        var run = await store.CreateAsync(Run(
            ("context", StoryPlanStepKind.CampaignContext, "Load campaign context."),
            ("knowledge", StoryPlanStepKind.Knowledge, "When does the lens open?")));

        var first = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);
        Assert.NotNull(first);
        Assert.Equal(0, first!.StepIndex);
        await processor.ProcessAsync(first, CancellationToken.None);

        var second = await store.ClaimNextAsync("worker-a", DateTime.UtcNow);
        Assert.NotNull(second);
        Assert.Equal(1, second!.StepIndex);
        await processor.ProcessAsync(second, CancellationToken.None);

        var completed = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Completed, completed.Status);
        Assert.Equal(2, completed.CompletedStepCount);
        Assert.Equal("campaign.test.story", resume.LastCampaignId);
        Assert.Equal("campaign.test.story", knowledge.LastRequest!.CampaignId);
        Assert.Equal(12, knowledge.LastRequest.CandidateLimit);
        Assert.All(completed.Steps, step => Assert.Contains("procedure.", step.ProcedureEvidenceJson, StringComparison.Ordinal));
        Assert.Equal(2, await db.Operations.CountAsync(operation => operation.Tool == "query" && operation.Subject.StartsWith("procedure.")));
        Assert.Contains("The lens opens at moonrise.", completed.HandoffJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_knowledge_is_a_completed_step_with_unresolved_information()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.game.core.world.knowledge", "query(kind: \"knowledge-answer\")");
        var store = new StoryPlanStore(db);
        var processor = Processor(db, store, new ResumeReader(null), new KnowledgeCoordinator(AuthorizedKnowledgeAnswerResult.Unknown()), procedures);
        var run = await store.CreateAsync(Run(("knowledge", StoryPlanStepKind.Knowledge, "Who built the lens?")));

        var lease = (await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!;
        await processor.ProcessAsync(lease, CancellationToken.None);

        var completed = (await store.GetAsync(run.Id))!;
        using var result = JsonDocument.Parse(Assert.Single(completed.Steps).ResultJson);
        Assert.Equal(StoryPlanStatus.Completed, completed.Status);
        Assert.Equal("No definite knowledge was found.", result.RootElement.GetProperty("summary").GetString());
        Assert.Empty(result.RootElement.GetProperty("findings").EnumerateArray());
        Assert.NotEmpty(result.RootElement.GetProperty("missingInformation").EnumerateArray());
    }

    [Fact]
    public async Task Revoked_access_stops_a_context_step_before_campaign_data_is_read()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.campaign.chapter", "query(kind: \"campaign-resume\")");
        var store = new StoryPlanStore(db);
        var resume = new ResumeReader(new("campaign.test.story", "The Observatory", "Find the vanished astronomer.", [], [], "world.test", null, null, [], [], "trusted-host-only"));
        var processor = Processor(db, store, resume, new KnowledgeCoordinator(AuthorizedKnowledgeAnswerResult.Unknown()), procedures,
            new SequenceAudience(Granted(), AuthenticatedCampaignAudienceResolution.Denied()));
        var run = await store.CreateAsync(Run(("context", StoryPlanStepKind.CampaignContext, "Load campaign context.")));

        await processor.ProcessAsync((await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!, CancellationToken.None);

        var blocked = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Blocked, blocked.Status);
        Assert.Equal("STORY_AUDIENCE_DENIED", blocked.StopCode);
        Assert.Null(resume.LastCampaignId);
    }

    [Fact]
    public async Task Missing_context_blocks_with_its_public_error_code()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.campaign.chapter", "query(kind: \"campaign-resume\")");
        var store = new StoryPlanStore(db);
        var processor = Processor(db, store, new ResumeReader(null), new KnowledgeCoordinator(AuthorizedKnowledgeAnswerResult.Unknown()), procedures);
        var run = await store.CreateAsync(Run(("context", StoryPlanStepKind.CampaignContext, "Load campaign context.")));

        await processor.ProcessAsync((await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!, CancellationToken.None);

        var blocked = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Blocked, blocked.Status);
        Assert.Equal("STORY_CONTEXT_UNAVAILABLE", blocked.StopCode);
    }

    [Fact]
    public async Task Unavailable_knowledge_blocks_with_its_public_error_code()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await WriteProcedureAsync(procedures, "procedure.game.core.world.knowledge", "query(kind: \"knowledge-answer\")");
        var store = new StoryPlanStore(db);
        var unavailable = new AuthorizedKnowledgeAnswerResult("unavailable", [], ["Try again later."], "KNOWLEDGE_UNAVAILABLE");
        var processor = Processor(db, store, new ResumeReader(null), new KnowledgeCoordinator(unavailable), procedures);
        var run = await store.CreateAsync(Run(("knowledge", StoryPlanStepKind.Knowledge, "Who built the lens?")));

        await processor.ProcessAsync((await store.ClaimNextAsync("worker-a", DateTime.UtcNow))!, CancellationToken.None);

        var blocked = (await store.GetAsync(run.Id))!;
        Assert.Equal(StoryPlanStatus.Blocked, blocked.Status);
        Assert.Equal("STORY_KNOWLEDGE_UNAVAILABLE", blocked.StopCode);
    }

    private static async Task WriteProcedureAsync(IProcedureStore procedures, string id, string governs) =>
        await procedures.WriteAsync(new WriteProcedureRequest
        {
            Id = id,
            Category = "test.story",
            Name = id,
            Description = "A bounded fixed story-plan read procedure.",
            Instructions = "Read the authorized backend source and return only its bounded result.",
            Governs = governs,
            CreatedBy = "test"
        });

    private static StoryPlanRun Run(params (string Id, string Kind, string Intent)[] steps) => new()
    {
        Id = "story-plan.0123456789abcdef0123456789abcdef",
        RequestToken = "story-plan.read-step-test",
        CampaignId = "campaign.test.story",
        Objective = "Answer the party safely.",
        PlanJson = "{}",
        PrincipalId = "development.test",
        PolicyRevision = "development-static-v1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        Steps = steps.Select((step, index) => new StoryPlanStepRun
        {
            StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef",
            StepIndex = index,
            StepId = step.Id,
            Kind = step.Kind,
            Intent = step.Intent,
            RoleEntityIdsJson = "{}",
            InputJson = "{}"
        }).ToList()
    };

    private static IStoryPlanStepProcessor Processor(
        DantesRoleplayDbContext db,
        IStoryPlanStore store,
        ICampaignResumeReader resume,
        IAuthorizedKnowledgeAnswerCoordinator knowledge,
        IProcedureStore procedures,
        IAuthenticatedCampaignAudiencePolicy? audience = null)
    {
        var type = typeof(StoryPlanStore).Assembly.GetType("DantesRoleplay.DataAccess.StoryPlanStepProcessor")!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (IStoryPlanStepProcessor)constructor.Invoke([db, store, audience ?? new GameMasterAudience(), resume, knowledge, procedures, new OperationLog(db), null!, null!]);
    }

    private static AuthenticatedCampaignAudienceResolution Granted() =>
        new(new("development.test", "campaign.test.story", CampaignAudienceRoles.GameMaster, null, "development-static-v1"));

    private sealed class GameMasterAudience : IAuthenticatedCampaignAudiencePolicy
    {
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted() with { Grant = new("development.test", campaignId, CampaignAudienceRoles.GameMaster, null, "development-static-v1") });
    }

    private sealed class SequenceAudience(params AuthenticatedCampaignAudienceResolution[] responses) : IAuthenticatedCampaignAudiencePolicy
    {
        private int _index;
        public Task<AuthenticatedCampaignAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);
    }

    private sealed class ResumeReader(CampaignResume? answer) : ICampaignResumeReader
    {
        public string? LastCampaignId { get; private set; }
        public Task<CampaignResume?> GetAsync(string campaignId, CancellationToken cancellationToken = default)
        {
            LastCampaignId = campaignId;
            return Task.FromResult(answer);
        }
    }

    private sealed class KnowledgeCoordinator(AuthorizedKnowledgeAnswerResult answer) : IAuthorizedKnowledgeAnswerCoordinator
    {
        public AuthorizedKnowledgeAnswerRequest? LastRequest { get; private set; }
        public Task<AuthorizedKnowledgeAnswerResult> AnswerAsync(AuthorizedKnowledgeAnswerRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(answer);
        }
    }
}
