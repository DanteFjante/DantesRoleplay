using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class SelectedApplicationInnerWorkerCapabilityHandlersTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("fixture-app");
    private static readonly string Principal = "principal." + new string('a', 64);
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void Registrations_are_the_three_selected_application_worker_transport_contracts()
    {
        var owner = new RecordingOwner();
        var submit = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId, owner).Registration;
        var read = new SelectedApplicationInnerWorkerReadCapabilityHandler(owner).Registration;
        var cancel = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.CancelCapabilityId, owner).Registration;

        Assert.Equal("system-task-orchestration", submit.Owner);
        Assert.Equal(SystemCapabilityMode.Write, submit.Mode);
        Assert.Equal(SystemCapabilityMode.Read, read.Mode);
        Assert.Equal(SystemCapabilityMode.Write, cancel.Mode);
        Assert.Equal(PrivateOperatorCapability.Read, read.RequiredCapability);
        Assert.Equal(PrivateOperatorCapability.Modify, submit.RequiredCapability);
        Assert.True(submit.RequiresConfirmation);
        Assert.True(submit.RequiresIdempotencyKey);
        Assert.Equal(["procedure.system.inner-worker.submit"], submit.ProcedureIds);
        Assert.Equal(["procedure.system.inner-worker.read"], read.ProcedureIds);
        Assert.Equal(["procedure.system.inner-worker.cancel"], cancel.ProcedureIds);
        Assert.DoesNotContain("provider", submit.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", submit.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool", submit.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant", submit.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_forwards_only_the_canonical_bounded_assignment_and_preserves_pending_result_json()
    {
        var owner = new RecordingOwner { SubmitResult = InteractionInvocationResult.Pending(new("task.1", "command.1")) };
        var handler = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId, owner);
        var input = SubmitInput("Inspect the bounded assignment.", dependency: true);
        var preflight = await handler.PreflightAsync(input, []);

        Assert.True(preflight.Ok);
        var result = await handler.ExecuteAsync(input, Execution(preflight));

        Assert.True(result.Ok);
        Assert.Equal("0123456789abcdef0123456789abcdef", result.OperationId);
        var submitted = Assert.Single(owner.Submissions);
        Assert.Equal(Application, submitted.ApplicationId);
        Assert.Equal("state.1", submitted.StateSpaceId);
        Assert.Equal("procedure.fixture", submitted.Procedure.ExactDefinitionId);
        Assert.Equal("{\"summary\":{\"type\":\"string\"},\"type\":\"object\"}", submitted.ResultSchemaJson);
        Assert.Equal(new SystemTaskDurableHandle("task.prerequisite", "command.prerequisite"),
            Assert.Single(submitted.DependencyHandles));
        Assert.Equal("{\"instruction\":\"Inspect the bounded assignment.\"}", submitted.AssignmentJson);
        Assert.Equal(owner.SubmitResult!.ToJson(), result.Data!.Value.GetRawText());
    }

    [Fact]
    public async Task Read_and_cancel_forward_exact_handle_and_preserve_owner_result_json()
    {
        var handle = new SystemTaskDurableHandle("task.2", "command.2");
        var owner = new RecordingOwner
        {
            ReadResult = InteractionInvocationResult.CompletedComputation("{\"summary\":\"done\"}", "result.2"),
            CancelResult = InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED", "The task acknowledged cancellation.")
        };
        var read = new SelectedApplicationInnerWorkerReadCapabilityHandler(owner);
        var cancel = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.CancelCapabilityId, owner);
        var input = Element("{\"taskId\":\"task.2\",\"commandId\":\"command.2\"}");

        var readResult = await read.ReadAsync(input, Context());
        var preflight = await cancel.PreflightAsync(input, []);
        var cancelResult = await cancel.ExecuteAsync(input, Execution(preflight));

        Assert.True(readResult.Ok);
        Assert.Equal(owner.ReadResult!.ToJson(), readResult.Data!.Value.GetRawText());
        Assert.True(cancelResult.Ok);
        Assert.Equal(owner.CancelResult!.ToJson(), cancelResult.Data!.Value.GetRawText());
        Assert.Equal(handle, Assert.Single(owner.Reads));
        Assert.Equal(handle, Assert.Single(owner.Cancellations));
    }

    [Fact]
    public async Task Handlers_require_a_current_selected_application_and_reject_unbounded_or_open_input()
    {
        var owner = new RecordingOwner();
        var submit = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId, owner);
        var read = new SelectedApplicationInnerWorkerReadCapabilityHandler(owner);
        var valid = SubmitInput("Inspect");
        var preflight = await submit.PreflightAsync(valid, []);

        var noSelection = await submit.ExecuteAsync(valid, Execution(preflight, selected: false));
        var disallowed = new[] { "provider", "model", "tools", "grant" };
        var invalid = new List<SystemCapabilityWritePreflight>();
        foreach (var field in disallowed)
            invalid.Add(await submit.PreflightAsync(Element("{\"stateSpaceId\":\"state.1\",\"procedure\":{\"definitionId\":\"procedure.fixture\",\"revision\":1,\"contentFingerprint\":\"" + Hash + "\"},\"instruction\":\"Inspect\",\"resultSchema\":\"{}\",\"dependencyHandles\":[],\"" + field + "\":\"untrusted\"}"), []));
        var large = await read.ReadAsync(Element("{\"taskId\":\"task.1\",\"commandId\":\"command.1\",\"padding\":\"" + new string('x', 17_000) + "\"}"), Context());

        Assert.False(noSelection.Ok);
        Assert.Equal("APPLICATION_CONTEXT_REQUIRED", noSelection.Error!.Code);
        Assert.All(invalid, result =>
        {
            Assert.False(result.Ok);
            Assert.Equal("SYSTEM_INNER_WORKER_INPUT_INVALID", result.Error!.Code);
        });
        Assert.False(large.Ok);
        Assert.Equal("SYSTEM_INNER_WORKER_INPUT_INVALID", large.Error!.Code);
        Assert.Empty(owner.Submissions);
        Assert.Empty(owner.Reads);
    }

    private static SystemCapabilityInvocationContext Context(ApplicationIdentifier? application = null, bool selected = true) => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"), "application.authoring", "fixture-correlation")
    {
        ApplicationId = selected ? application ?? Application : null
    };

    private static SystemCapabilityWriteExecutionContext Execution(SystemCapabilityWritePreflight preflight,
        ApplicationIdentifier? application = null, bool selected = true) => new(Context(application, selected), "0123456789abcdef0123456789abcdef",
        "Submit focused work.", ["procedure.system.inner-worker.submit"],
        new(Principal, "fixture", "Modify", "application.authoring", "fixture-correlation", true, "ALLOWED"),
        preflight.ExecutionEvidenceJson);

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement SubmitInput(string instruction, bool dependency = false) => Element("{\"stateSpaceId\":\"state.1\",\"procedure\":{\"definitionId\":\"procedure.fixture\",\"revision\":1,\"contentFingerprint\":\"" + Hash + "\"},\"instruction\":\"" + instruction + "\",\"resultSchema\":\"{\\\"type\\\":\\\"object\\\",\\\"summary\\\":{\\\"type\\\":\\\"string\\\"}}\",\"dependencyHandles\":" + (dependency ? "[{\"taskId\":\"task.prerequisite\",\"commandId\":\"command.prerequisite\"}]" : "[]") + "}");

    private sealed class RecordingOwner : ISelectedApplicationInnerWorkerOwner
    {
        public InteractionInvocationResult? SubmitResult { get; init; }
        public InteractionInvocationResult? ReadResult { get; init; }
        public InteractionInvocationResult? CancelResult { get; init; }
        public List<SelectedApplicationInnerWorkerSubmission> Submissions { get; } = [];
        public List<SystemTaskDurableHandle> Reads { get; } = [];
        public List<SystemTaskDurableHandle> Cancellations { get; } = [];

        public Task<InteractionInvocationResult> SubmitAsync(SystemCapabilityInvocationContext context,
            SelectedApplicationInnerWorkerSubmission request, CancellationToken cancellationToken = default)
        {
            Submissions.Add(request);
            return Task.FromResult(SubmitResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> ReadAsync(SystemCapabilityInvocationContext context,
            SystemTaskDurableHandle handle, CancellationToken cancellationToken = default)
        {
            Reads.Add(handle);
            return Task.FromResult(ReadResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> CancelAsync(SystemCapabilityInvocationContext context,
            SystemTaskDurableHandle handle, CancellationToken cancellationToken = default)
        {
            Cancellations.Add(handle);
            return Task.FromResult(CancelResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }
    }
}
