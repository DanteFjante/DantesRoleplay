using System.Reflection;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tests;

/// <summary>Slice 5: the fixed campaign-context projection is bounded and excludes routing metadata.</summary>
public sealed class StoryPlanReadStepBoundaryTests
{
    [Fact]
    public void Context_projection_caps_combined_goals_and_boundaries_milestones_and_private_reference_metadata()
    {
        var resume = new CampaignResume(
            "campaign.test.story", "The Observatory", "A premise.",
            Enumerable.Range(1, 5).Select(index => $"goal {index}").ToArray(),
            Enumerable.Range(1, 5).Select(index => $"boundary {index}").ToArray(),
            "world.test", null, null,
            [new("entity.hidden", "gm-only", "secret", "Visible name", "Safe summary.", "hidden")],
            Enumerable.Range(1, 6).Select(index => new CampaignClosedChapterMilestone($"chapter.{index}", $"Milestone {index}", "A safe closure.", DateTime.UtcNow, index, $"event.{index}")).ToArray(),
            "trusted-host-only");

        var findings = ContextFindings(resume);

        Assert.Equal(8, findings.Count(value => value.StartsWith("Goal:", StringComparison.Ordinal) || value.StartsWith("Boundary:", StringComparison.Ordinal)));
        Assert.Equal(5, findings.Count(value => value.StartsWith("Milestone:", StringComparison.Ordinal)));
        Assert.DoesNotContain(findings, value => value.Contains("entity.hidden", StringComparison.Ordinal) || value.Contains("gm-only", StringComparison.Ordinal) || value.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Context_projection_blocks_instead_of_truncating_an_oversized_fact()
    {
        var resume = new CampaignResume("campaign.test.story", new string('x', 501), "Premise.", [], [], "world.test", null, null, [], [], "trusted-host-only");

        var method = ProcessorType().GetMethod("TryContextFindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameters = new object?[] { resume, null };
        var valid = Assert.IsType<bool>(method.Invoke(null, parameters));

        Assert.False(valid);
    }

    private static IReadOnlyList<string> ContextFindings(CampaignResume resume)
    {
        var method = ProcessorType().GetMethod("TryContextFindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameters = new object?[] { resume, null };
        Assert.True(Assert.IsType<bool>(method.Invoke(null, parameters)));
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(parameters[1]);
    }

    private static Type ProcessorType() => typeof(StoryPlanStore).Assembly.GetType("DantesRoleplay.DataAccess.StoryPlanStepProcessor")!;
}
