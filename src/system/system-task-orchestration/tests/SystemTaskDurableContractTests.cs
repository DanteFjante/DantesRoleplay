using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskDurableContractTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static IEnumerable<object[]> DurableValues()
    {
        yield return [new SystemTaskDurableHandle("task.1", "command.1"), typeof(SystemTaskDurableHandle)];
        yield return [new SystemTaskCheckpoint("await-response", "resume-handler", "correlation.1", "{\"b\":2,\"a\":1}"), typeof(SystemTaskCheckpoint)];
        yield return [new SystemTaskAttemptIdentity("command.1", "attempt.1", "lease.1", 1, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc)), typeof(SystemTaskAttemptIdentity)];
    }

    [Theory]
    [MemberData(nameof(DurableValues))]
    public void Durable_values_round_trip_through_json(object value, Type type)
    {
        var json = JsonSerializer.Serialize(value, type);
        var restored = JsonSerializer.Deserialize(json, type);

        Assert.NotNull(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored, type));
    }

    [Theory]
    [InlineData("{\"checkpoint\":\"a\",\"checkpoint\":\"b\"}")]
    [InlineData("[]")]
    public void Checkpoint_rejects_invalid_or_duplicate_state_json(string stateJson)
    {
        Assert.Throws<InteractionContractException>(() => new SystemTaskCheckpoint("checkpoint.1", "handler.1", "correlation.1", stateJson));
    }

    [Fact]
    public void Durable_values_reject_oversize_state_invalid_utc_and_empty_identities()
    {
        Assert.Equal("JSON_TOO_LARGE", Assert.Throws<InteractionContractException>(() =>
            new SystemTaskCheckpoint("checkpoint.1", "handler.1", "correlation.1", "{\"value\":\"" + new string('x', InteractionContractLimits.JsonBytes) + "\"}" )).Code);
        Assert.Equal("INVALID_LEASE_EXPIRY", Assert.Throws<InteractionContractException>(() =>
            new SystemTaskAttemptIdentity("command.1", "attempt.1", "lease.1", 1, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local))).Code);
        Assert.Throws<InteractionContractException>(() => new SystemTaskDurableHandle("", "command.1"));
        Assert.Throws<InteractionContractException>(() => new SystemTaskAttemptIdentity("command.1", "", "lease.1", 1, DateTime.UtcNow));
        Assert.Throws<InteractionContractException>(() => new SystemTaskAttemptIdentity("command.1", "command.1", "lease.1", 1, DateTime.UtcNow));
    }

    [Fact]
    public void Retry_attempt_changes_without_changing_the_stable_command()
    {
        var first = new SystemTaskAttemptIdentity("command.1", "attempt.1", "lease.1", 1, DateTime.UtcNow);
        var retry = new SystemTaskAttemptIdentity(first.StableCommandId, "attempt.2", "lease.2", 2, DateTime.UtcNow);

        Assert.Equal(first.StableCommandId, retry.StableCommandId);
        Assert.NotEqual(first.AttemptId, retry.AttemptId);
    }

    [Fact]
    public async Task Unavailable_durable_service_never_claims_pending_or_committed_work()
    {
        var service = new UnavailableSystemTaskDurableService();
        var result = await service.SubmitAsync(new SystemTaskDurableSubmissionRequest(Host(), Definition(), "{\"input\":true}"));

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Null(result.TaskHandle);
        Assert.Null(result.Receipt);
        Assert.NotEqual(InteractionInvocationResultTag.Pending, result.Tag);
    }

    [Fact]
    public async Task Cancelled_token_is_reported_without_creating_a_durable_handle()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new UnavailableSystemTaskDurableService().SubmitAsync(
            new SystemTaskDurableSubmissionRequest(Host(), Definition(), "{}"), cancellation.Token);

        Assert.Equal(InteractionInvocationResultTag.Cancelled, result.Tag);
        Assert.Null(result.TaskHandle);
    }

    [Fact]
    public async Task Unavailable_get_and_cancel_never_claim_a_task_result()
    {
        var service = new UnavailableSystemTaskDurableService();
        var handle = new SystemTaskDurableHandle("task.1", "command.1");

        var results = await Task.WhenAll(service.GetAsync(Host(), handle), service.CancelAsync(Host(), handle));

        Assert.All(results, result =>
        {
            Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
            Assert.Null(result.TaskHandle);
            Assert.Null(result.Receipt);
        });
    }

    [Fact]
    public void Submission_rejects_duplicate_dependencies_and_json_cannot_supply_its_host()
    {
        var dependency = new SystemTaskDurableHandle("task.1", "command.1");
        Assert.Equal("INVALID_TASK_DEPENDENCIES", Assert.Throws<InteractionContractException>(() =>
            new SystemTaskDurableSubmissionRequest(Host(), Definition(), "{}", dependencyHandles: [dependency, dependency])).Code);
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<SystemTaskDurableSubmissionRequest>(
            "{\"invocationHost\":{},\"selectedDefinition\":{\"exactDefinitionId\":\"procedure.fixture\",\"version\":1,\"fingerprint\":\"" + Hash + "\"},\"inputJson\":\"{}\"}"));
    }

    [Fact]
    public void Invocation_host_authority_cannot_enter_a_storage_contract_from_json()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InteractionInvocationHost>("{}"));
    }

    private static InteractionInvocationHost Host() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
        "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.Workflow,
        new InteractionInvocationBudget(2, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc)));

    private static SystemTaskSelectedDefinition Definition() => new("procedure.fixture", 1, Hash);
}
