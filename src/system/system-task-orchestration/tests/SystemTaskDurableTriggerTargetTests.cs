using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskDurableTriggerTargetTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Occurrence_identity_is_repeatable_and_separates_bindings_scopes_and_occurrences()
    {
        var first = Identity();
        Assert.Equal(first, Identity());
        Assert.NotEqual(first, Identity(version: 2));
        Assert.NotEqual(first, Identity(occurrence: "event.2"));
        Assert.NotEqual(first, Identity(scope: "state.2"));
    }

    [Fact]
    public void Target_pins_definition_and_canonical_input_without_authorizing_or_executing_it()
    {
        var target = Target(Host(), new("procedure.fixture", 1, Hash), "{\"z\":1,\"a\":2}");
        Assert.Equal("{\"a\":2,\"z\":1}", target.Submission.InputJson);
        Assert.Equal(1, target.Submission.SelectedDefinition.Version);
        Assert.Equal(Identity().CorrelationId, target.CorrelationId);
        Assert.Equal("grant.1", target.Submission.InvocationHost.GrantReference);
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Serialize(target.Submission));
    }

    [Fact]
    public void Redelivery_with_changed_definition_or_grant_keeps_command_identity_for_conflict_detection()
    {
        var first = Target(Host(), new("procedure.fixture", 1, Hash), "{}");
        var changed = Target(Host(grant: "grant.2"), new("procedure.fixture", 2, Hash), "{\"changed\":true}");
        Assert.Equal(first.Submission.InvocationHost.CommandId, changed.Submission.InvocationHost.CommandId);
        Assert.NotEqual(first.Submission.SelectedDefinition, changed.Submission.SelectedDefinition);
    }

    [Theory]
    [InlineData(InteractionExecutionProfile.Atomic, "SYSTEM_TASK_PROFILE_UNSUPPORTED")]
    [InlineData(InteractionExecutionProfile.ReadOnly, "SYSTEM_TASK_PROFILE_UNSUPPORTED")]
    public void Trigger_target_rejects_profiles_that_cannot_enqueue(InteractionExecutionProfile profile, string code) =>
        Assert.Equal(code, Assert.Throws<InteractionContractException>(() =>
            Target(Host(profile: profile), new("procedure.fixture", 1, Hash), "{}")).Code);

    [Fact]
    public void Delivery_attempt_cannot_supply_a_fresh_command_identity() =>
        Assert.Equal("SYSTEM_TASK_TRIGGER_IDENTITY_MISMATCH", Assert.Throws<InteractionContractException>(() =>
            Target(Host(command: "another-command"), new("procedure.fixture", 1, Hash), "{}")).Code);

    private static (string CommandId, string CorrelationId) Identity(int version = 1,
        string occurrence = "event.1", string scope = "state.1") =>
        SystemTaskDurableTriggerTarget.Identity("trigger.binding", version, occurrence, Principal, "fixture-app", scope);

    private static InteractionInvocationHost Host(string? command = null, string grant = "grant.1",
        InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow) => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
        "state.1", grant, command ?? Identity().CommandId, "revision.1", profile,
        new InteractionInvocationBudget(16, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    private static SystemTaskDurableTriggerTarget Target(InteractionInvocationHost host,
        SystemTaskSelectedDefinition selected, string input) =>
        new("trigger.binding", 1, Hash, "event.1", host, selected, input);
}
