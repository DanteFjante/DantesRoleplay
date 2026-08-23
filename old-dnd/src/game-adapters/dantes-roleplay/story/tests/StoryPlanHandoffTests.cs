using System.Reflection;
using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Story;

namespace DantesRoleplay.Tests;

/// <summary>Slice 7: terminal handoffs expose only bounded completed-story evidence.</summary>
public sealed class StoryPlanHandoffTests
{
    [Fact]
    public void Terminal_handoff_contains_completed_results_and_only_the_storytelling_contract()
    {
        var run = new StoryPlanRun
        {
            Id = "story-plan.0123456789abcdef0123456789abcdef", RequestToken = "story-plan.handoff-01",
            CampaignId = "campaign.test.story", Objective = "Resolve the signal.", PlanJson = "{}",
            PrincipalId = "development.test", PolicyRevision = "development-static-v1", Status = StoryPlanStatus.Blocked,
            CompletedStepCount = 2, StopCode = "STORY_ROUTE_NEEDS_INPUT", StopMessage = "Choose a witness.",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            Steps =
            [
                Step(0, "context", StoryPlanStepKind.CampaignContext, StoryPlanStepStatus.Completed,
                    new("context", StoryPlanStepKind.CampaignContext, StoryPlanStepStatus.Completed, "", ["Campaign: Observatory."], "", [], [])),
                Step(1, "fact", StoryPlanStepKind.Knowledge, StoryPlanStepStatus.Completed,
                    new("fact", StoryPlanStepKind.Knowledge, StoryPlanStepStatus.Completed, "", ["[true/fact] The signal repeats."], "", [], ["world.test"])),
                Step(2, "action", StoryPlanStepKind.Action, StoryPlanStepStatus.Blocked,
                    new("action", StoryPlanStepKind.Action, StoryPlanStepStatus.Blocked, "", [], "", ["Choose a witness."], [])),
                Step(3, "later", StoryPlanStepKind.Action, StoryPlanStepStatus.Skipped,
                    new("later", StoryPlanStepKind.Action, StoryPlanStepStatus.Skipped, "", [], "raw effect must not appear", [], []))
            ]
        };

        var handoff = Build(run);

        Assert.Equal(["Campaign: Observatory."], handoff.ContextSummaries);
        Assert.Equal(["[true/fact] The signal repeats."], handoff.FactsLearned);
        Assert.Empty(handoff.ActionNarrations);
        Assert.Equal(["world.test"], handoff.AffectedEntityIds);
        Assert.Equal(["procedure.play.storytelling"], handoff.ProcedureIdsForNextTurn);
        Assert.DoesNotContain(handoff.ActionNarrations, value => value.Contains("raw effect", StringComparison.Ordinal));
    }

    [Fact]
    public void Terminal_handoff_rejects_an_oversized_public_value_instead_of_serializing_it()
    {
        var run = new StoryPlanRun
        {
            Id = "story-plan.0123456789abcdef0123456789abcdef", RequestToken = "story-plan.handoff-02",
            CampaignId = "campaign.test.story", Objective = "Resolve the signal.", PlanJson = "{}",
            PrincipalId = "development.test", PolicyRevision = "development-static-v1", Status = StoryPlanStatus.Completed,
            CompletedStepCount = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            Steps = [Step(0, "action", StoryPlanStepKind.Action, StoryPlanStepStatus.Completed,
                new("action", StoryPlanStepKind.Action, StoryPlanStepStatus.Completed, "done", [], new string('x', 1001), [], []))]
        };

        Assert.False(StoryPlanHandoffBuilder.TryBuild(run, out _));
    }

    private static StoryPlanStepRun Step(int index, string id, string kind, string status, StoryPlanStepResult result) => new()
    {
        StoryPlanId = "story-plan.0123456789abcdef0123456789abcdef", StepIndex = index, StepId = id,
        Kind = kind, Intent = id, RoleEntityIdsJson = "{}", InputJson = "{}", Status = status,
        ResultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    private static StoryHandoff Build(StoryPlanRun run)
    {
        var type = typeof(StoryPlanStore).Assembly.GetType("DantesRoleplay.DataAccess.StoryPlanHandoffBuilder")!;
        var method = type.GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return Assert.IsType<StoryHandoff>(method.Invoke(null, [run]));
    }
}
