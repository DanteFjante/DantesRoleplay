using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleReplayTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Exact_replay_after_deadline_returns_original_handle_but_new_expired_work_is_rejected()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var request = Request(fixture);
        var created = await fixture.CreateStore().EnqueueAsync(request);
        fixture.TimeProvider.Advance(TimeSpan.FromHours(2));
        var replay = await fixture.CreateStore().EnqueueAsync(request);
        Assert.Equal(SystemTaskEnqueueDisposition.Existing, replay.Disposition);
        Assert.Equal(created.Handle, replay.Handle);
        var expired = await fixture.CreateStore().EnqueueAsync(Request(fixture, "command.new", budget: request.InvocationHost.Budget));
        Assert.Equal("SYSTEM_TASK_DEADLINE_EXPIRED", expired.Code);
        Assert.Null(expired.Handle);
        Assert.Equal(1, await CountAsync(fixture));
        Assert.Null(await fixture.CreateStore().ClaimNextAsync("worker", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Exact_replay_after_host_budget_consumption_retains_handle_and_original_admitted_limit()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var request = Request(fixture);
        // Admission is allowed to reserve less than the immutable requested ceiling.
        for (var used = 0; used < 4; used++) Assert.True(request.InvocationHost.Budget.TryConsumeOperation());
        var created = await fixture.CreateStore().EnqueueAsync(request);
        Assert.Equal(12, (await fixture.CreateStore().ReadAsync(created.Handle!))!.Request.Invocation.AdmittedOperations);
        while (request.InvocationHost.Budget.TryConsumeOperation()) { }
        var replay = await fixture.CreateStore().EnqueueAsync(request);
        Assert.Equal(SystemTaskEnqueueDisposition.Existing, replay.Disposition);
        Assert.Equal(created.Handle, replay.Handle);
        Assert.Equal(12, (await fixture.CreateStore().ReadAsync(created.Handle!))!.Request.Invocation.AdmittedOperations);
        var rejected = await fixture.CreateStore().EnqueueAsync(Request(fixture, "command.new", budget: request.InvocationHost.Budget));
        Assert.Equal("SYSTEM_TASK_BUDGET_EXHAUSTED", rejected.Code);
        Assert.Null(rejected.Handle);
        Assert.Equal(1, await CountAsync(fixture));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("principal")]
    [InlineData("grant")]
    [InlineData("budget")]
    [InlineData("profile")]
    public async Task Changed_stable_payload_conflicts_even_after_admission_limits_expire(string field)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var request = Request(fixture);
        var created = await fixture.CreateStore().EnqueueAsync(request);
        while (request.InvocationHost.Budget.TryConsumeOperation()) { }
        fixture.TimeProvider.Advance(TimeSpan.FromHours(2));
        var changed = Request(fixture,
            input: field == "input" ? "{\"changed\":true}" : "{}",
            principal: field == "principal" ? "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" : Principal,
            grant: field == "grant" ? "grant.changed" : "grant.1",
            profile: field == "profile" ? InteractionExecutionProfile.Atomic : InteractionExecutionProfile.Workflow,
            budget: field == "budget" ? new InteractionInvocationBudget(8, request.InvocationHost.Budget.DeadlineUtc) : request.InvocationHost.Budget);
        var conflict = await fixture.CreateStore().EnqueueAsync(changed);
        Assert.Equal(SystemTaskEnqueueDisposition.Conflict, conflict.Disposition);
        Assert.Null(conflict.Handle);
        Assert.Equal("{}", (await fixture.CreateStore().ReadAsync(created.Handle!))!.Request.InputJson);
        Assert.Equal(1, await CountAsync(fixture));
    }

    private static async Task<long> CountAsync(SystemTaskLifecycleSchemaFixture fixture)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM system_task_lifecycle";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command = "command.replay", string input = "{}", string principal = Principal, string grant = "grant.1",
        InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow, InteractionInvocationBudget? budget = null) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", grant, command, "revision.1", profile,
            budget ?? new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime)),
        new("procedure.fixture", 1, Hash), input);
}
