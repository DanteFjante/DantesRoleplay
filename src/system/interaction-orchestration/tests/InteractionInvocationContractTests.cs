using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionInvocationContractTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void Host_authority_cannot_be_deserialized_from_authored_json()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InteractionInvocationHost>("{}"));
    }

    [Fact]
    public void Profiles_have_closed_lowercase_wire_names()
    {
        Assert.Equal("\"read-only\"", JsonSerializer.Serialize(InteractionExecutionProfile.ReadOnly));
        Assert.Equal(InteractionExecutionProfile.Atomic,
            JsonSerializer.Deserialize<InteractionExecutionProfile>("\"atomic\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InteractionExecutionProfile>("\"readOnly\""));
    }

    [Fact]
    public void Shared_budget_prevents_siblings_from_exceeding_the_root_cap()
    {
        var root = new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1));
        var first = root.Child(2, root.DeadlineUtc);
        var second = root.Child(2, root.DeadlineUtc);

        Assert.True(first.TryConsumeOperation());
        Assert.True(second.TryConsumeOperation());
        Assert.False(first.TryConsumeOperation());
        Assert.Equal(0, root.RemainingOperations);
    }

    [Fact]
    public void Nested_child_cannot_bypass_its_parent_cap()
    {
        var root = new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(1));
        var parent = root.Child(1, root.DeadlineUtc);
        var grandchild = parent.Child(1, parent.DeadlineUtc);

        Assert.True(grandchild.TryConsumeOperation());
        Assert.False(parent.TryConsumeOperation());
    }

    [Fact]
    public void Completed_computation_requires_explicit_non_read_evidence()
    {
        var result = InteractionInvocationResult.CompletedComputation("{\"answer\":42}", "runner.result.1");

        Assert.Equal("completed", result.WireTag);
        Assert.Null(result.ReadEvidence);
        Assert.Equal("runner.result.1", result.CompletionEvidenceReference);
        Assert.Contains("\"readEvidence\":null", result.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Host_defensively_copies_selected_base_revisions()
    {
        var bases = new[] { ApplicationIdentifier.Parse("base-app") };
        var revision = new ApplicationRevision(ApplicationIdentifier.Parse("sample-app"), 1, Hash, bases);
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('a', 64), "test"), revision, "state.1", "grant.1", "command.1",
            "revision.1", InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));

        bases[0] = ApplicationIdentifier.Parse("changed-base");

        Assert.Equal("base-app", host.ApplicationRevision.BaseApplications.Single().Value);
    }
}
