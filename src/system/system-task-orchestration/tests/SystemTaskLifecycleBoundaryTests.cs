using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleBoundaryTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(InteractionExecutionProfile.Atomic)]
    [InlineData(InteractionExecutionProfile.ReadOnly)]
    public async Task Non_workflow_submission_is_rejected_without_a_row(InteractionExecutionProfile profile)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var result = await fixture.CreateStore().EnqueueAsync(Request(fixture, profile: profile));
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, result.Disposition);
        Assert.Null(result.Handle);
        Assert.Null(await fixture.CreateStore().ClaimNextAsync("worker.1", TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("application")]
    [InlineData("state")]
    public async Task Dependency_with_wrong_scope_is_rejected(string changed)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture, "parent.command"))).Handle!;
        var child = Request(fixture, "child.command",
            principal: changed == "principal" ? "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" : Principal,
            application: changed == "application" ? "another-app" : "fixture-app",
            state: changed == "state" ? "state.2" : "state.1");
        var mismatched = new SystemTaskDurableSubmissionRequest(child.InvocationHost,
            child.SelectedDefinition, child.InputJson, dependencyHandles: [parent]);
        var result = await store.EnqueueAsync(mismatched);
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, result.Disposition);
    }

    [Fact]
    public async Task Child_of_cancelled_parent_is_rejected()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture, "parent.command"))).Handle!;
        await store.RequestCancellationAsync(parent, propagate: true);
        var result = await store.EnqueueAsync(Request(fixture, "child.command", parentCommandId: parent.CommandId), propagateCancellation: true);
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, result.Disposition);
    }

    [Fact]
    public async Task Child_allowance_is_local_and_cannot_use_root_remaining_budget()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture, "parent.command"))).Handle!;
        var parentLease = await store.ClaimNextAsync("parent.worker", TimeSpan.FromMinutes(1));
        Assert.Equal(parent, parentLease!.Request.Handle);
        var child = (await store.EnqueueAsync(Request(fixture, "child.command", parentCommandId: parent.CommandId,
            remainingOperations: 1))).Handle!;
        var lease = await store.ClaimNextAsync("worker", TimeSpan.FromMinutes(1));
        Assert.NotNull(lease);
        var call = await store.BeginHostCallAsync(lease!, "operation.1", "{}");
        // Claiming the child already consumed its one-operation allowance.
        Assert.Equal(SystemTaskHostCallDisposition.BudgetExhausted, call.Disposition);
        Assert.Equal(child.TaskId, lease!.Request.Handle.TaskId);
        Assert.Equal(SystemTaskHostCallDisposition.NewPending,
            (await store.BeginHostCallAsync(parentLease, "operation.root", "{}")).Disposition);
    }

    [Fact]
    public async Task Maximum_input_and_checkpoint_json_are_retained()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var input = "{\"value\":\"" + new string('x', 65520) + "\"}";
        var checkpoint = new SystemTaskCheckpoint("step.1", "handler.1", "wake.1",
            "{\"value\":\"" + new string('y', 65520) + "\"}");
        var result = await fixture.CreateStore().EnqueueAsync(Request(fixture, "large.command", input, checkpoint));
        Assert.Equal(SystemTaskEnqueueDisposition.Created, result.Disposition);
        var retained = await fixture.CreateStore().ReadAsync(result.Handle!);
        Assert.Equal(input, retained!.Request.InputJson);
        Assert.Equal(checkpoint, retained.Checkpoint);
    }

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command = "command.1", string? input = null, SystemTaskCheckpoint? checkpoint = null,
        InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow, string? parentCommandId = null,
        int remainingOperations = 16, string principal = Principal, string application = "fixture-app", string state = "state.1") => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse(application), 1, Hash, []),
            state, "grant.1", command, "revision.1", profile,
            new InteractionInvocationBudget(remainingOperations,
                fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime), parentCommandId),
        new("procedure.fixture", 1, Hash), input ?? "{}", checkpoint);
}
