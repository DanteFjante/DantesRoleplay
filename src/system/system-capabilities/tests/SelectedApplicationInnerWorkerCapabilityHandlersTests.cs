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
    public void Registrations_are_the_five_selected_application_worker_transport_contracts()
    {
        var owner = new RecordingOwner();
        var submit = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId, owner).Registration;
        var read = new SelectedApplicationInnerWorkerReadCapabilityHandler(owner).Registration;
        var list = new SelectedApplicationInnerWorkerReadCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.ListCapabilityId, owner).Registration;
        var wait = new SelectedApplicationInnerWorkerReadCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.WaitCapabilityId, owner).Registration;
        var cancel = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.CancelCapabilityId, owner).Registration;

        Assert.Equal("system-task-orchestration", submit.Owner);
        Assert.Equal(SystemCapabilityMode.Write, submit.Mode);
        Assert.Equal(SystemCapabilityMode.Read, read.Mode);
        Assert.Equal(SystemCapabilityMode.Read, list.Mode);
        Assert.Equal(SystemCapabilityMode.Read, wait.Mode);
        Assert.Equal(SystemCapabilityMode.Write, cancel.Mode);
        Assert.Equal(PrivateOperatorCapability.Read, read.RequiredCapability);
        Assert.Equal(PrivateOperatorCapability.Modify, submit.RequiredCapability);
        Assert.True(submit.RequiresConfirmation);
        Assert.True(submit.RequiresIdempotencyKey);
        Assert.Equal(["procedure.system.inner-worker.submit"], submit.ProcedureIds);
        Assert.Equal(["procedure.system.inner-worker.read"], read.ProcedureIds);
        Assert.Equal(["procedure.system.inner-worker.list"], list.ProcedureIds);
        Assert.Equal(["procedure.system.inner-worker.wait"], wait.ProcedureIds);
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
        Assert.Empty(submitted.DependencyInputs);
        Assert.Equal("{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Inspect the bounded assignment.\"}", submitted.AssignmentJson);
        Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(owner.SubmitCommands));
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
        var input = Element("{\"stateSpaceId\":\"state.1\",\"taskId\":\"task.2\",\"commandId\":\"command.2\"}");

        var readResult = await read.ReadAsync(input, Context());
        var preflight = await cancel.PreflightAsync(input, []);
        var cancelResult = await cancel.ExecuteAsync(input, Execution(preflight));

        Assert.True(readResult.Ok);
        Assert.Equal(owner.ReadResult!.ToJson(), readResult.Data!.Value.GetRawText());
        Assert.True(cancelResult.Ok);
        Assert.Equal(owner.CancelResult!.ToJson(), cancelResult.Data!.Value.GetRawText());
        Assert.Equal(new("state.1", handle), Assert.Single(owner.Reads));
        Assert.Equal(new("state.1", handle), Assert.Single(owner.Cancellations));
    }

    [Fact]
    public async Task Submit_forwards_only_named_pointer_mappings_for_declared_dependencies()
    {
        var owner = new RecordingOwner { SubmitResult = InteractionInvocationResult.Pending(new("task.1", "command.1")) };
        var handler = new SelectedApplicationInnerWorkerWriteCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId, owner);
        var input = Element("{\"stateSpaceId\":\"state.1\",\"procedure\":{\"definitionId\":\"procedure.fixture\",\"revision\":1,\"contentFingerprint\":\"" + Hash + "\"},\"instruction\":\"Inspect\",\"resultSchema\":\"{}\",\"dependencyHandles\":[{\"taskId\":\"task.prerequisite\",\"commandId\":\"command.prerequisite\"}],\"dependencyInputs\":[{\"name\":\"priorAnswer\",\"handle\":{\"taskId\":\"task.prerequisite\",\"commandId\":\"command.prerequisite\"},\"jsonPointer\":\"/answer\"}]}");
        var preflight = await handler.PreflightAsync(input, []);

        var result = await handler.ExecuteAsync(input, Execution(preflight));

        Assert.True(result.Ok);
        var mapping = Assert.Single(Assert.Single(owner.Submissions).DependencyInputs);
        Assert.Equal("priorAnswer", mapping.Name);
        Assert.Equal(new("task.prerequisite", "command.prerequisite"), mapping.Handle);
        Assert.Equal("/answer", mapping.JsonPointer);
    }

    [Fact]
    public async Task List_and_wait_forward_bounded_readback_requests_and_preserve_owner_results()
    {
        var owner = new RecordingOwner
        {
            ListResult = InteractionInvocationResult.CompletedComputation("{\"items\":[]}", "list.1"),
            WaitResult = InteractionInvocationResult.Pending(new("task.2", "command.2"))
        };
        var list = new SelectedApplicationInnerWorkerReadCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.ListCapabilityId, owner);
        var wait = new SelectedApplicationInnerWorkerReadCapabilityHandler(
            SelectedApplicationInnerWorkerSchemas.WaitCapabilityId, owner);

        var listed = await list.ReadAsync(Element(
            "{\"stateSpaceId\":\"state.1\",\"pageSize\":16,\"cursor\":null}"), Context());
        var waited = await wait.ReadAsync(Element(
            "{\"stateSpaceId\":\"state.1\",\"taskId\":\"task.2\",\"commandId\":\"command.2\",\"waitMilliseconds\":25000}"), Context());

        Assert.True(listed.Ok);
        Assert.Equal(owner.ListResult!.ToJson(), listed.Data!.Value.GetRawText());
        Assert.True(waited.Ok);
        Assert.Equal(owner.WaitResult!.ToJson(), waited.Data!.Value.GetRawText());
        Assert.Equal(new("state.1", 16, null), Assert.Single(owner.Lists));
        Assert.Equal(new(new("state.1", new("task.2", "command.2")), 25_000), Assert.Single(owner.Waits));
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
        var large = await read.ReadAsync(Element("{\"stateSpaceId\":\"state.1\",\"taskId\":\"task.1\",\"commandId\":\"command.1\",\"padding\":\"" + new string('x', 66_000) + "\"}"), Context());

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
        public InteractionInvocationResult? ListResult { get; init; }
        public InteractionInvocationResult? WaitResult { get; init; }
        public List<SelectedApplicationInnerWorkerSubmission> Submissions { get; } = [];
        public List<string> SubmitCommands { get; } = [];
        public List<SelectedApplicationInnerWorkerHandle> Reads { get; } = [];
        public List<SelectedApplicationInnerWorkerHandle> Cancellations { get; } = [];
        public List<SelectedApplicationInnerWorkerList> Lists { get; } = [];
        public List<SelectedApplicationInnerWorkerWait> Waits { get; } = [];

        public Task<InteractionInvocationResult> SubmitAsync(SystemCapabilityInvocationContext context, string commandId,
            SelectedApplicationInnerWorkerSubmission request, CancellationToken cancellationToken = default)
        {
            SubmitCommands.Add(commandId);
            Submissions.Add(request);
            return Task.FromResult(SubmitResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> ReadAsync(SystemCapabilityInvocationContext context,
            SelectedApplicationInnerWorkerHandle request, CancellationToken cancellationToken = default)
        {
            Reads.Add(request);
            return Task.FromResult(ReadResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> CancelAsync(SystemCapabilityInvocationContext context, string commandId,
            SelectedApplicationInnerWorkerHandle request, CancellationToken cancellationToken = default)
        {
            Cancellations.Add(request);
            return Task.FromResult(CancelResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> ListAsync(SystemCapabilityInvocationContext context,
            SelectedApplicationInnerWorkerList request, CancellationToken cancellationToken = default)
        {
            Lists.Add(request);
            return Task.FromResult(ListResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }

        public Task<InteractionInvocationResult> WaitAsync(SystemCapabilityInvocationContext context,
            SelectedApplicationInnerWorkerWait request, CancellationToken cancellationToken = default)
        {
            Waits.Add(request);
            return Task.FromResult(WaitResult ?? InteractionInvocationResult.Unavailable("INNER_WORKER_UNAVAILABLE", "Unavailable."));
        }
    }
}
