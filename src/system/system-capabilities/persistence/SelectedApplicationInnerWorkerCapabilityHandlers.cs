using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Narrow seam for the selected-application worker transport.  The owner, rather than this
/// transport, resolves the procedure, profile, model, tools, grants, and durable lifecycle.
/// </summary>
internal interface ISelectedApplicationInnerWorkerOwner
{
    Task<InteractionInvocationResult> SubmitAsync(SystemCapabilityInvocationContext context, string commandId,
        SelectedApplicationInnerWorkerSubmission request, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> ReadAsync(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerHandle request, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> ListAsync(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerList request, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> WaitAsync(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerWait request, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> CancelAsync(SystemCapabilityInvocationContext context, string commandId,
        SelectedApplicationInnerWorkerHandle request, CancellationToken cancellationToken = default);
}

internal sealed record SelectedApplicationInnerWorkerSubmission(ApplicationIdentifier ApplicationId,
    string StateSpaceId, SystemTaskSelectedDefinition Procedure, string AssignmentJson,
    string ResultSchemaJson, IReadOnlyList<SystemTaskDurableHandle> DependencyHandles);

internal sealed record SelectedApplicationInnerWorkerHandle(string StateSpaceId,
    SystemTaskDurableHandle Handle);

internal sealed record SelectedApplicationInnerWorkerList(string StateSpaceId, int PageSize, string? Cursor);

internal sealed record SelectedApplicationInnerWorkerWait(SelectedApplicationInnerWorkerHandle Target,
    int WaitMilliseconds);

internal sealed class SelectedApplicationInnerWorkerReadCapabilityHandler(
    string capabilityId, ISelectedApplicationInnerWorkerOwner owner) : ISystemReadCapabilityHandler
{
    internal SelectedApplicationInnerWorkerReadCapabilityHandler(ISelectedApplicationInnerWorkerOwner owner)
        : this(SelectedApplicationInnerWorkerSchemas.ReadCapabilityId, owner) { }

    public SystemCapabilityRegistration Registration { get; } = SelectedApplicationInnerWorkerSchemas.Registration(
        capabilityId, SystemCapabilityMode.Read, capabilityId switch
        {
            SelectedApplicationInnerWorkerSchemas.ListCapabilityId => SelectedApplicationInnerWorkerSchemas.ListInput,
            SelectedApplicationInnerWorkerSchemas.WaitCapabilityId => SelectedApplicationInnerWorkerSchemas.WaitInput,
            _ => SelectedApplicationInnerWorkerSchemas.HandleInput
        });

    public Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
        CancellationToken cancellationToken = default) => Task.FromResult(SelectedApplicationInnerWorkerSchemas.Failure(
            "APPLICATION_CONTEXT_REQUIRED", "A current selected application is required."));

    public async Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
        SystemCapabilityInvocationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!SelectedApplicationInnerWorkerSchemas.TryApplication(context, out _))
                return SelectedApplicationInnerWorkerSchemas.Failure("APPLICATION_CONTEXT_REQUIRED",
                    "A current selected application is required.");
            var result = capabilityId switch
            {
                SelectedApplicationInnerWorkerSchemas.ReadCapabilityId =>
                    await owner.ReadAsync(context, SelectedApplicationInnerWorkerSchemas.Handle(input), cancellationToken),
                SelectedApplicationInnerWorkerSchemas.ListCapabilityId =>
                    await owner.ListAsync(context, SelectedApplicationInnerWorkerSchemas.List(input), cancellationToken),
                SelectedApplicationInnerWorkerSchemas.WaitCapabilityId =>
                    await owner.WaitAsync(context, SelectedApplicationInnerWorkerSchemas.Wait(input), cancellationToken),
                _ => throw new ArgumentException("Unknown inner worker read capability.")
            };
            return SelectedApplicationInnerWorkerSchemas.Success(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        {
            return SelectedApplicationInnerWorkerSchemas.Failure("SYSTEM_INNER_WORKER_INPUT_INVALID",
                "The worker read request is invalid.");
        }
    }
}

internal sealed class SelectedApplicationInnerWorkerWriteCapabilityHandler(
    string capabilityId, ISelectedApplicationInnerWorkerOwner owner) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = SelectedApplicationInnerWorkerSchemas.Registration(
        capabilityId, SystemCapabilityMode.Write, capabilityId == SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId
            ? SelectedApplicationInnerWorkerSchemas.SubmitInput : SelectedApplicationInnerWorkerSchemas.HandleInput);

    public Task<SystemCapabilityWritePreflight> PreflightAsync(JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (capabilityId == SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId)
                _ = SelectedApplicationInnerWorkerSchemas.Assignment(input);
            else
                _ = SelectedApplicationInnerWorkerSchemas.Handle(input);
            var fingerprint = SelectedApplicationInnerWorkerSchemas.Fingerprint(input);
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(fingerprint,
                capabilityId == SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId
                    ? "Submit one bounded assignment to the selected application's worker owner."
                    : "Request cancellation of one selected-application worker handle.",
                capabilityId == SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId
                    ? ["selected-application"]
                    : [$"system-task:{SelectedApplicationInnerWorkerSchemas.Handle(input).Handle.TaskId}"],
                JsonSerializer.Serialize(new { inputFingerprint = fingerprint })));
        }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure("SYSTEM_INNER_WORKER_INPUT_INVALID",
                "The worker write request is invalid.", SelectedApplicationInnerWorkerSchemas.Recovery));
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(JsonElement input,
        SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var fingerprint = SelectedApplicationInnerWorkerSchemas.Fingerprint(input);
            if (!SelectedApplicationInnerWorkerSchemas.MatchesEvidence(context.ExecutionEvidenceJson, fingerprint))
                return SelectedApplicationInnerWorkerSchemas.WriteFailure("SYSTEM_CAPABILITY_PREFLIGHT_STALE");
            if (!SelectedApplicationInnerWorkerSchemas.TryApplication(context.Invocation, out var applicationId))
                return SelectedApplicationInnerWorkerSchemas.WriteFailure("APPLICATION_CONTEXT_REQUIRED",
                    "A current selected application is required.");

            InteractionInvocationResult result;
            if (capabilityId == SelectedApplicationInnerWorkerSchemas.SubmitCapabilityId)
            {
                var assignment = SelectedApplicationInnerWorkerSchemas.Assignment(input);
                result = await owner.SubmitAsync(context.Invocation, context.RequestToken,
                    new(applicationId, assignment.StateSpaceId, assignment.Procedure, assignment.AssignmentJson,
                        assignment.ResultSchemaJson, assignment.DependencyHandles), cancellationToken);
            }
            else
            {
                result = await owner.CancelAsync(context.Invocation, context.RequestToken,
                    SelectedApplicationInnerWorkerSchemas.Handle(input), cancellationToken);
            }
            return SelectedApplicationInnerWorkerSchemas.WriteSuccess(result, context.RequestToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        {
            return SelectedApplicationInnerWorkerSchemas.WriteFailure("SYSTEM_INNER_WORKER_INPUT_INVALID",
                "The worker write request is invalid.");
        }
    }
}

internal static class SelectedApplicationInnerWorkerSchemas
{
    internal const string SubmitCapabilityId = SystemCapabilityIds.InnerWorkerSubmit;
    internal const string ReadCapabilityId = SystemCapabilityIds.InnerWorkerRead;
    internal const string ListCapabilityId = SystemCapabilityIds.InnerWorkerList;
    internal const string WaitCapabilityId = SystemCapabilityIds.InnerWorkerWait;
    internal const string CancelCapabilityId = SystemCapabilityIds.InnerWorkerCancel;
    internal const string Recovery = "Retain the worker handle, select the current application, and retry through the worker owner.";

    // Assignment is deliberately closed: profile/model/provider/tool/grant selection is host-owned.
    internal const string SubmitInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["stateSpaceId","procedure","instruction","resultSchema","dependencyHandles"],"properties":{
        "stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"procedure":{"type":"object","additionalProperties":false,
        "required":["definitionId","revision","contentFingerprint"],"properties":{"definitionId":{"type":"string","minLength":1,"maxLength":200},
        "revision":{"type":"integer","minimum":1},"contentFingerprint":{"type":"string","minLength":64,"maxLength":64}}},
        "instruction":{"type":"string","minLength":1,"maxLength":8000},"resultSchema":{"type":"string","minLength":2,"maxLength":16000},
        "dependencyHandles":{"type":"array","maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["taskId","commandId"],
        "properties":{"taskId":{"type":"string","minLength":1,"maxLength":200},"commandId":{"type":"string","minLength":1,"maxLength":128}}}}}}
        """;
    internal const string HandleInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["stateSpaceId","taskId","commandId"],"properties":{
        "stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"taskId":{"type":"string","minLength":1,"maxLength":200},
        "commandId":{"type":"string","minLength":1,"maxLength":128}}}
        """;
    internal const string ListInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["stateSpaceId","pageSize"],"properties":{
        "stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"pageSize":{"type":"integer","minimum":1,"maximum":16},
        "cursor":{"type":["string","null"],"maxLength":1024}}}
        """;
    internal const string WaitInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["stateSpaceId","taskId","commandId","waitMilliseconds"],"properties":{
        "stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"taskId":{"type":"string","minLength":1,"maxLength":200},
        "commandId":{"type":"string","minLength":1,"maxLength":128},"waitMilliseconds":{"type":"integer","minimum":0,"maximum":25000}}}
        """;
    internal const string ResultOutput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["tag","code","message","dataJson","readEvidence","receipt","proposal","pending","completionEvidenceReference","previousCommits","recoveryIdentity"],
        "properties":{"tag":{"type":"string","minLength":1,"maxLength":32},"code":{"type":"string","minLength":1,"maxLength":200},
        "message":{"type":"string","minLength":1,"maxLength":1000},"dataJson":{"type":["string","null"],"maxLength":65536},
        "readEvidence":{"type":["object","null"]},"receipt":{"type":["object","null"]},"proposal":{"type":["object","null"]},
        "pending":{"type":["object","null"]},"completionEvidenceReference":{"type":["string","null"],"maxLength":1024},
        "previousCommits":{"type":"array","maxItems":64},"recoveryIdentity":{"type":["object","null"]}}}
        """;

    internal static SystemCapabilityRegistration Registration(string id, SystemCapabilityMode mode, string input) => new(
        id, 1, "system-task-orchestration",
        mode == SystemCapabilityMode.Read ? id switch
            {
                ListCapabilityId => "List authorized durable focused-worker results for the current selected application and state space.",
                WaitCapabilityId => "Wait briefly for one durable focused-worker result for the current selected application.",
                _ => "Read one durable focused-worker result for the current selected application."
            }
            : id == SubmitCapabilityId ? "Submit one bounded focused-worker assignment for the current selected application."
            : "Request cancellation of one focused worker for the current selected application.",
        mode, input, ResultOutput,
        [id switch
        {
            SubmitCapabilityId => "procedure.system.inner-worker.submit",
            ReadCapabilityId => "procedure.system.inner-worker.read",
            ListCapabilityId => "procedure.system.inner-worker.list",
            WaitCapabilityId => "procedure.system.inner-worker.wait",
            CancelCapabilityId => "procedure.system.inner-worker.cancel",
            _ => throw new ArgumentException("Unknown inner worker capability.", nameof(id))
        }], mode == SystemCapabilityMode.Read ? PrivateOperatorCapability.Read : PrivateOperatorCapability.Modify,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, RequiresConfirmation: mode == SystemCapabilityMode.Write,
        RequiresIdempotencyKey: mode == SystemCapabilityMode.Write);

    internal static SelectedApplicationInnerWorkerSubmitWire Assignment(JsonElement input)
    {
        BoundedObject(input);
        var wire = Deserialize<AssignmentWire>(input);
        if (string.IsNullOrWhiteSpace(wire.Instruction) || wire.Instruction.Length > 8_000 || wire.Instruction.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(wire.StateSpaceId) || wire.StateSpaceId.Length > InteractionContractLimits.Identifier || wire.StateSpaceId.Any(char.IsControl))
            throw new InteractionContractException("INVALID_WORKER_ASSIGNMENT", "The worker instruction is outside its bounded transport contract.");
        if (wire.Procedure is null || wire.DependencyHandles is null)
            throw new JsonException("The worker request is missing a required object or collection.");
        var procedure = new SystemTaskSelectedDefinition(wire.Procedure.DefinitionId, wire.Procedure.Revision,
            wire.Procedure.ContentFingerprint);
        var resultSchema = InteractionCanonicalJson.CanonicalizeObject(wire.ResultSchema);
        if (Encoding.UTF8.GetByteCount(resultSchema) > 16_000)
            throw new InteractionContractException("INVALID_WORKER_RESULT_SCHEMA", "The worker result schema exceeds its transport bound.");
        var dependencies = wire.DependencyHandles.Select(value => new SystemTaskDurableHandle(value.TaskId, value.CommandId)).ToArray();
        if (dependencies.Select(value => value.TaskId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new InteractionContractException("INVALID_WORKER_DEPENDENCIES", "Worker dependency handles must be distinct.");
        return new(wire.StateSpaceId, procedure,
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                format = SystemInnerWorkerAssignmentV1.Format,
                instruction = wire.Instruction
            })),
            resultSchema, Array.AsReadOnly(dependencies));
    }

    internal static SelectedApplicationInnerWorkerHandle Handle(JsonElement input)
    {
        BoundedObject(input);
        var wire = Deserialize<HandleWire>(input);
        if (string.IsNullOrWhiteSpace(wire.StateSpaceId) || wire.StateSpaceId.Length > InteractionContractLimits.Identifier
            || wire.StateSpaceId.Any(char.IsControl))
            throw new InteractionContractException("INVALID_WORKER_STATE_SPACE", "The worker state-space identity is invalid.");
        return new(wire.StateSpaceId, new(wire.TaskId, wire.CommandId));
    }

    internal static SelectedApplicationInnerWorkerList List(JsonElement input)
    {
        BoundedObject(input);
        var wire = Deserialize<ListWire>(input);
        StateSpace(wire.StateSpaceId);
        if (wire.PageSize is < 1 or > 16 || wire.Cursor is { Length: > 1_024 })
            throw new InteractionContractException("INVALID_WORKER_LIST", "The worker list bounds are invalid.");
        return new(wire.StateSpaceId, wire.PageSize, wire.Cursor);
    }

    internal static SelectedApplicationInnerWorkerWait Wait(JsonElement input)
    {
        BoundedObject(input);
        var wire = Deserialize<WaitWire>(input);
        StateSpace(wire.StateSpaceId);
        if (wire.WaitMilliseconds is < 0 or > 25_000)
            throw new InteractionContractException("INVALID_WORKER_WAIT", "The worker wait bound is invalid.");
        return new(new(wire.StateSpaceId, new(wire.TaskId, wire.CommandId)), wire.WaitMilliseconds);
    }

    private static void StateSpace(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > InteractionContractLimits.Identifier
            || value.Any(char.IsControl))
            throw new InteractionContractException("INVALID_WORKER_STATE_SPACE", "The worker state-space identity is invalid.");
    }

    internal static bool TryApplication(SystemCapabilityInvocationContext? context, out ApplicationIdentifier applicationId)
    {
        if (context is { Principal.Verified: true, ApplicationId: { IsSystem: false } selected })
        {
            applicationId = selected;
            return true;
        }
        applicationId = default!;
        return false;
    }

    internal static SystemCapabilityHandlerResult Success(InteractionInvocationResult result) =>
        SystemCapabilityHandlerResult.Success(ResultElement(result));

    internal static SystemCapabilityHandlerResult Failure(string code, string message) =>
        SystemCapabilityHandlerResult.Failure(code, message, Recovery);

    internal static SystemCapabilityWriteHandlerResult WriteSuccess(InteractionInvocationResult result, string operationId) =>
        SystemCapabilityWriteHandlerResult.Success(ResultElement(result), operationId, Fingerprint(ResultElement(result)));

    internal static SystemCapabilityWriteHandlerResult WriteFailure(string code, string? message = null) =>
        SystemCapabilityWriteHandlerResult.Failure(code, message ?? "The worker operation was rejected.", Recovery);

    internal static string Fingerprint(JsonElement input) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(InteractionCanonicalJson.CanonicalizeObject(input.GetRawText()))));

    internal static bool MatchesEvidence(string json, string fingerprint)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject().Count() == 1
                && document.RootElement.TryGetProperty("inputFingerprint", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == fingerprint;
        }
        catch (JsonException) { return false; }
    }

    private static T Deserialize<T>(JsonElement input) where T : class =>
        JsonSerializer.Deserialize<T>(input.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new JsonException("The worker request is absent.");

    private static void BoundedObject(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object
            || Encoding.UTF8.GetByteCount(input.GetRawText()) > 65_536)
            throw new JsonException("The worker request must be a bounded JSON object.");
    }

    private static JsonElement ResultElement(InteractionInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record AssignmentWire(
        [property: JsonRequired] string StateSpaceId,
        [property: JsonRequired] ProcedureWire Procedure,
        [property: JsonRequired] string Instruction,
        [property: JsonRequired] string ResultSchema,
        [property: JsonRequired] IReadOnlyList<TaskHandleWire> DependencyHandles);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProcedureWire([property: JsonRequired] string DefinitionId,
        [property: JsonRequired] int Revision, [property: JsonRequired] string ContentFingerprint);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record HandleWire([property: JsonRequired] string StateSpaceId,
        [property: JsonRequired] string TaskId, [property: JsonRequired] string CommandId);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ListWire([property: JsonRequired] string StateSpaceId,
        [property: JsonRequired] int PageSize, string? Cursor);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record WaitWire([property: JsonRequired] string StateSpaceId,
        [property: JsonRequired] string TaskId, [property: JsonRequired] string CommandId,
        [property: JsonRequired] int WaitMilliseconds);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record TaskHandleWire([property: JsonRequired] string TaskId,
        [property: JsonRequired] string CommandId);

    internal sealed record SelectedApplicationInnerWorkerSubmitWire(string StateSpaceId,
        SystemTaskSelectedDefinition Procedure, string AssignmentJson, string ResultSchemaJson,
        IReadOnlyList<SystemTaskDurableHandle> DependencyHandles);
}
