using System.Collections.Concurrent;
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
    public void Transfer_reserves_allowance_and_returns_an_independent_bounded_ledger()
    {
        var deadline = DateTime.UtcNow.AddMinutes(1);
        var source = new InteractionInvocationBudget(5, deadline);

        Assert.True(source.TryTransferOperations(3, out var transferred));
        Assert.NotNull(transferred);
        Assert.Equal(2, source.RemainingOperations);
        Assert.Equal(3, transferred.MaximumOperations);
        Assert.Equal(deadline, transferred.DeadlineUtc);
        Assert.True(source.TryConsumeOperation());
        Assert.True(source.TryConsumeOperation());
        Assert.False(source.TryConsumeOperation());
        Assert.True(transferred.TryConsumeOperation());
        Assert.True(transferred.TryConsumeOperation());
        Assert.True(transferred.TryConsumeOperation());
        Assert.False(transferred.TryConsumeOperation());
    }

    [Fact]
    public void Transfer_observes_every_nested_parent_limit()
    {
        var root = new InteractionInvocationBudget(10, DateTime.UtcNow.AddMinutes(1));
        var parent = root.Child(5, root.DeadlineUtc);
        var nested = parent.Child(5, parent.DeadlineUtc);
        Assert.True(parent.TryConsumeOperation());
        Assert.True(parent.TryConsumeOperation());

        Assert.False(nested.TryTransferOperations(4, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(8, root.RemainingOperations);
        Assert.Equal(3, parent.RemainingOperations);
        Assert.Equal(3, nested.RemainingOperations);
        Assert.True(nested.TryTransferOperations(3, out var transferred));
        Assert.NotNull(transferred);
        Assert.Equal(5, root.RemainingOperations);
        Assert.Equal(0, parent.RemainingOperations);
        Assert.Equal(0, nested.RemainingOperations);
    }

    [Fact]
    public void Failed_transfer_is_atomic_and_does_not_debit_any_ledger()
    {
        var root = new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(1));
        var parent = root.Child(1, root.DeadlineUtc);
        var nested = parent.Child(1, parent.DeadlineUtc);

        Assert.False(nested.TryTransferOperations(2, out var transferred));
        Assert.Null(transferred);
        Assert.Equal(4, root.RemainingOperations);
        Assert.Equal(1, parent.RemainingOperations);
        Assert.Equal(1, nested.RemainingOperations);
    }

    [Fact]
    public void Concurrent_consumption_and_transfer_conserve_the_root_allowance()
    {
        var root = new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(1));
        var transferred = new ConcurrentBag<InteractionInvocationBudget>();
        Assert.True(root.TryConsumeOperation());
        Assert.True(root.TryTransferOperations(1, out var initialTransfer));
        transferred.Add(initialTransfer);
        var accepted = 2;

        Parallel.For(0, 62, index =>
        {
            if (index % 2 == 0)
            {
                if (root.TryConsumeOperation()) Interlocked.Increment(ref accepted);
            }
            else if (root.TryTransferOperations(1, out var budget))
            {
                transferred.Add(budget);
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.Equal(16, accepted);
        Assert.Equal(0, root.RemainingOperations);
        Assert.All(transferred, budget => Assert.True(budget.TryConsumeOperation()));
        Assert.Equal(0, root.RemainingOperations);
    }

    [Fact]
    public void Transferred_allowance_is_not_refunded_or_linked_to_downstream_spending()
    {
        var root = new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(1));

        Assert.True(root.TryTransferOperations(2, out var transferred));
        Assert.Equal(2, root.RemainingOperations);
        Assert.True(transferred.TryConsumeOperation());
        Assert.Equal(2, root.RemainingOperations);
    }

    [Fact]
    public void Transfer_rejects_invalid_counts_and_preserves_the_callers_deadline()
    {
        var deadline = DateTime.UtcNow.AddSeconds(-1);
        var budget = new InteractionInvocationBudget(2, deadline);

        var negative = Assert.Throws<InteractionContractException>(() => budget.TryTransferOperations(-1, out _));
        var zero = Assert.Throws<InteractionContractException>(() => budget.TryTransferOperations(0, out _));
        var overLimit = Assert.Throws<InteractionContractException>(() =>
            budget.TryTransferOperations(InteractionContractLimits.ProposalSteps + 1, out _));
        Assert.Equal("INVALID_INVOCATION_BUDGET", negative.Code);
        Assert.Equal("INVALID_INVOCATION_BUDGET", zero.Code);
        Assert.Equal("INVALID_INVOCATION_BUDGET", overLimit.Code);
        Assert.Equal(2, budget.RemainingOperations);
        Assert.True(budget.TryTransferOperations(1, out var transferred));
        Assert.Equal(deadline, transferred.DeadlineUtc);
        Assert.Equal("INVALID_INVOCATION_DEADLINE", Assert.Throws<InteractionContractException>(() =>
            new InteractionInvocationBudget(1, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local))).Code);
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

    [Fact]
    public void Application_host_has_an_atomic_null_state_pair()
    {
        var host = InteractionInvocationHost.ForApplication(
            Principal(), Revision(), "grant.1", "command.1", InteractionExecutionProfile.ReadOnly,
            new(1, DateTime.UtcNow.AddMinutes(1)));

        Assert.Null(host.StateSpaceId);
        Assert.Null(host.StateRevision);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(host));
    }

    [Theory]
    [InlineData(null, "revision.1")]
    [InlineData("", "revision.1")]
    [InlineData("state.1", null)]
    [InlineData("state.1", "")]
    public void State_host_constructor_rejects_a_missing_scope_pair(
        string? stateSpaceId,
        string? stateRevision) =>
        Assert.Throws<InteractionContractException>(() => new InteractionInvocationHost(
            Principal(), Revision(), stateSpaceId!, "grant.1", "command.1", stateRevision!,
            InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1))));

    [Fact]
    public void Existing_state_host_fingerprint_is_compatible()
    {
        var host = new InteractionInvocationHost(
            Principal(), Revision(), "state.1", "grant.1", "command.1", "revision.1",
            InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));

        var fingerprint = InteractionInvocationIdentity.Fingerprint(
            host, "sample-app.mechanic.one", 2, new string('B', 64),
            new Dictionary<string, string> { ["subject"] = "entity.1" }, "{\"value\":1}");

        Assert.Equal("D5D111F42B6EE001101AF6C9ED6C3812483D32B97A34CE38ED9C1F47C8E50F54", fingerprint);
    }

    private static TrustedPrincipalContext Principal() => TrustedPrincipalContext.VerifiedPrincipal(
        "principal." + new string('a', 64), "test");

    private static ApplicationRevision Revision() => new(
        ApplicationIdentifier.Parse("sample-app"), 1, Hash, [ApplicationIdentifier.Parse("base-app")]);
}
