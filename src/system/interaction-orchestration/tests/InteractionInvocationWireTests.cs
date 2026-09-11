using System.Text.Json;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionInvocationWireTests
{
    private const string Operation = "0123456789abcdef0123456789abcdef";
    private static readonly string Hash = new('A', 64);

    public static IEnumerable<object[]> Outcomes()
    {
        yield return ["completed", InteractionInvocationResult.Completed("{\"value\":1}", new(Hash, Hash, Hash, Hash, Hash))];
        yield return ["completed", InteractionInvocationResult.CompletedComputation("[1,2]", "runner.result.1")];
        yield return ["proposed", InteractionInvocationResult.Proposed(new("apply", [new("step.1", "action", "fixture.action", 1,
            Hash, [], new Dictionary<string, string>(), JsonSerializer.SerializeToElement(new { }), [])]))];
        yield return ["committed", InteractionInvocationResult.Committed(new(Operation, Hash, []))];
        yield return ["pending", InteractionInvocationResult.Pending(new SystemTaskDurableHandle("task.1", "command.1"))];
        yield return ["failed", InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The action is not authorized.")];
        yield return ["failed", InteractionInvocationResult.Failed("IDEMPOTENCY_CONFLICT", "The command payload changed.")];
        yield return ["cancelled", InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "The request was cancelled.")];
        yield return ["unavailable", InteractionInvocationResult.Unavailable("SYSTEM_TASK_DURABILITY_UNAVAILABLE", "Durability is unavailable.")];
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void Every_outcome_uses_the_same_closed_wire_envelope(string tag, InteractionInvocationResult result)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var wire = document.RootElement;
        Assert.Equal(tag, wire.GetProperty("tag").GetString());
        Assert.Equal(new[] { "code", "completionEvidenceReference", "dataJson", "message", "pending", "previousCommits",
            "proposal", "readEvidence", "receipt", "recoveryIdentity", "tag" },
            wire.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(tag == "committed", wire.GetProperty("receipt").ValueKind == JsonValueKind.Object);
        Assert.Equal(tag == "pending", wire.GetProperty("pending").ValueKind == JsonValueKind.Object);
        Assert.Equal(tag == "proposed", wire.GetProperty("proposal").ValueKind == JsonValueKind.Object);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InteractionInvocationResult>(wire.GetRawText()));
    }

    [Fact]
    public void Uncertain_outcome_retains_a_typed_recovery_identity_without_changing_the_error_code()
    {
        var result = InteractionInvocationResult.Failed("ACTION_OUTCOME_UNKNOWN", "Reconcile this operation.",
            recoveryIdentity: new ApplicationEcsExecutionIdentity(Operation, Hash));
        using var document = JsonDocument.Parse(result.ToJson());
        Assert.Equal("ACTION_OUTCOME_UNKNOWN", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(Operation, document.RootElement.GetProperty("recoveryIdentity").GetProperty("operationId").GetString());
        Assert.Null(result.Receipt);
    }

    [Fact]
    public void Cancellation_preserves_prior_commits_and_freezes_the_supplied_effect_list()
    {
        var effects = new List<ApplicationEcsEffectReceipt> { new(0, "component.set", "entity.1", "fixture.value", 2) };
        var result = InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "Later work was cancelled.",
            [new(Operation, Hash, effects)]);
        effects.Clear();
        Assert.Single(result.PreviousCommits.Single().Effects);
        Assert.Null(result.Receipt);
        Assert.Null(result.TaskHandle);
    }

    [Fact]
    public void Completed_computation_retains_prior_commits_without_changing_its_evidence_contract()
    {
        var effects = new List<ApplicationEcsEffectReceipt> { new(0, "component.set", "entity.1", "fixture.value", 2) };
        var result = InteractionInvocationResult.CompletedComputation("{\"answer\":42}", "runner.result.1",
            [new(Operation, Hash, effects)]);
        effects.Clear();

        Assert.Equal("runner.result.1", result.CompletionEvidenceReference);
        Assert.Null(result.ReadEvidence);
        Assert.Single(result.PreviousCommits.Single().Effects);
        using var document = JsonDocument.Parse(result.ToJson());
        Assert.Equal(Operation, document.RootElement.GetProperty("previousCommits")[0].GetProperty("operationId").GetString());
    }

    [Fact]
    public void Pending_workflow_retains_prior_commits_without_claiming_completion()
    {
        var result = InteractionInvocationResult.Pending(new SystemTaskDurableHandle("task.1", "command.1"),
            [new(Operation, Hash, [])]);

        Assert.NotNull(result.TaskHandle);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Single(result.PreviousCommits);
        Assert.Equal("pending", result.WireTag);
    }

    [Fact]
    public void Workflow_prior_commits_remain_bounded()
    {
        var commits = Enumerable.Range(0, InteractionContractLimits.EvidenceItems + 1)
            .Select(_ => new InteractionInvocationCommitReceipt(Operation, Hash, []))
            .ToArray();

        Assert.Throws<InteractionContractException>(() =>
            InteractionInvocationResult.CompletedComputation("{\"answer\":42}", "runner.result.1", commits));
        Assert.Throws<InteractionContractException>(() =>
            InteractionInvocationResult.Pending(new SystemTaskDurableHandle("task.1", "command.1"), commits));
    }

    [Fact]
    public void A_recovered_commit_distinguishes_unavailable_effect_details_from_an_empty_effect_batch()
    {
        var recovered = InteractionInvocationResult.Committed(new(Operation, Hash, [], EffectDetailsAvailable: false));
        var noEffects = InteractionInvocationResult.Committed(new(Operation, Hash, []));
        Assert.False(recovered.Receipt!.EffectDetailsAvailable);
        Assert.True(noEffects.Receipt!.EffectDetailsAvailable);
    }
}
