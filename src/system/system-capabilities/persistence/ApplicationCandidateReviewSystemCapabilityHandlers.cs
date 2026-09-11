using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.SystemCapabilities;

internal sealed class ApplicationCandidateReviewReadCapabilityHandler(
    DantesRoleplayDbContext db, IApplicationRegistry applications,
    SystemTaskApplicationValidationService reviews) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ApplicationCandidateReviewRead, 1, "system-task-orchestration",
        "Read current durable reuse-review status and its available bounded result for one exact candidate-review handle.",
        SystemCapabilityMode.Read, ReviewSchemas.HandleInput, ReviewSchemas.ReadOutput,
        ["procedure.system.application-candidate.review"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input, CancellationToken cancellationToken = default) =>
        Task.FromResult(Failure("APPLICATION_CONTEXT_REQUIRED"));

    public async Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
        SystemCapabilityInvocationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ReviewSchemas.Deserialize<ReviewHandleWire>(input);
            if (context.ApplicationId is not { IsSystem: false } applicationId)
                return Failure("APPLICATION_CONTEXT_REQUIRED");
            var selection = await ApplicationCandidateCapabilityHost.CreateAsync(db, applications,
                context, applicationId, [StandingGrantCapability.Read], ReviewSchemas.ReadCommand(context, input),
                InteractionExecutionProfile.ReadOnly, 1, cancellationToken, TimeSpan.FromSeconds(10));
            if (selection.Hosts.Count == 0) return Failure(selection.Code);
            InteractionInvocationResult? result = null;
            foreach (var host in selection.Hosts)
            {
                result = await reviews.ReadAsync(host, request.Handle, cancellationToken);
                if (result.Tag == InteractionInvocationResultTag.Completed && result.DataJson is not null
                    && result.CompletionEvidenceReference is not null)
                    return SystemCapabilityHandlerResult.Success(JsonSerializer.SerializeToElement(new
                    {
                        status = result.WireTag,
                        code = result.Code,
                        message = result.SafeMessage,
                        data = JsonSerializer.Deserialize<JsonElement>(result.DataJson),
                        evidenceReference = result.CompletionEvidenceReference
                    }));
                if (!ReviewSchemas.RetryableAuthorizationDenial(result.Code)) break;
            }
            return Failure(result?.Code ?? selection.Code, result?.SafeMessage);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        { return Failure("APPLICATION_CANDIDATE_REVIEW_INPUT_INVALID"); }
    }

    private static SystemCapabilityHandlerResult Failure(string code, string? message = null) =>
        SystemCapabilityHandlerResult.Failure(code, message ?? "The candidate review could not be read.",
            ReviewSchemas.Recovery);
}

internal sealed class ApplicationCandidateReviewWriteCapabilityHandler(
    string id, DantesRoleplayDbContext db, IApplicationRegistry applications,
    SystemTaskApplicationValidationService reviews) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        id, 1, "system-task-orchestration",
        id == SystemCapabilityIds.ApplicationCandidateReviewSubmit
            ? "Submit one exact retained application candidate for durable reuse review."
            : "Request cancellation of one exact durable application-candidate review handle.",
        SystemCapabilityMode.Write,
        id == SystemCapabilityIds.ApplicationCandidateReviewSubmit ? ReviewSchemas.SubmitInput : ReviewSchemas.HandleInput,
        ReviewSchemas.WriteOutput, ["procedure.system.application-candidate.review"],
        PrivateOperatorCapability.Modify, SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true);

    public Task<SystemCapabilityWritePreflight> PreflightAsync(JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var affected = id == SystemCapabilityIds.ApplicationCandidateReviewSubmit
                ? SubmitReferences(ReviewSchemas.Deserialize<ReviewSubmitWire>(input))
                : HandleReferences(ReviewSchemas.Deserialize<ReviewHandleWire>(input));
            var fingerprint = ReviewSchemas.Fingerprint(input);
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(fingerprint,
                id == SystemCapabilityIds.ApplicationCandidateReviewSubmit
                    ? "Submit the exact retained candidate for durable reuse review."
                    : "Request cancellation of the exact durable candidate-review handle.",
                affected, JsonSerializer.Serialize(new { inputFingerprint = fingerprint })));
        }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                "APPLICATION_CANDIDATE_REVIEW_INPUT_INVALID",
                "The candidate review request is invalid.", ReviewSchemas.Recovery));
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(JsonElement input,
        SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ReviewSchemas.MatchesEvidence(context.ExecutionEvidenceJson, ReviewSchemas.Fingerprint(input)))
                return Failure("SYSTEM_CAPABILITY_PREFLIGHT_STALE");
            return id == SystemCapabilityIds.ApplicationCandidateReviewSubmit
                ? await SubmitAsync(ReviewSchemas.Deserialize<ReviewSubmitWire>(input), context, cancellationToken)
                : await CancelAsync(ReviewSchemas.Deserialize<ReviewHandleWire>(input), context, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InteractionContractException or ArgumentException)
        { return Failure("APPLICATION_CANDIDATE_REVIEW_INPUT_INVALID"); }
    }

    private async Task<SystemCapabilityWriteHandlerResult> SubmitAsync(ReviewSubmitWire request,
        SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var applicationId = ApplicationIdentifier.Parse(request.ApplicationId);
        var candidate = new ApplicationCandidateReference(applicationId, request.CandidateId,
            request.Revision, request.ContentFingerprint);
        var selection = await ApplicationCandidateCapabilityHost.CreateAsync(db, applications,
            context.Invocation, applicationId, [StandingGrantCapability.Read, StandingGrantCapability.Validate],
            context.RequestToken, InteractionExecutionProfile.ReadOnly,
            StandingGrantLimits.MaximumOperations, cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await reviews.SubmitAsync(host, candidate, request.AuthoringOperationId,
                request.AuthoringCommandId, cancellationToken);
            if (result.Tag == InteractionInvocationResultTag.Pending && result.TaskHandle is not null)
                return Success(result, result.TaskHandle);
            if (!ReviewSchemas.RetryableAuthorizationDenial(result.Code)) break;
        }
        return Failure(result?.Code ?? selection.Code, result?.SafeMessage);
    }

    private async Task<SystemCapabilityWriteHandlerResult> CancelAsync(ReviewHandleWire request,
        SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Invocation.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await ApplicationCandidateCapabilityHost.CreateAsync(db, applications,
            context.Invocation, applicationId, [StandingGrantCapability.Read, StandingGrantCapability.Validate],
            context.RequestToken, InteractionExecutionProfile.ReadOnly, 1, cancellationToken,
            TimeSpan.FromSeconds(10));
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await reviews.CancelAsync(host, request.Handle, cancellationToken);
            if (result.Tag is InteractionInvocationResultTag.Cancelled or InteractionInvocationResultTag.Pending)
                return Success(result, result.TaskHandle ?? request.Handle);
            if (!ReviewSchemas.RetryableAuthorizationDenial(result.Code)) break;
        }
        return Failure(result?.Code ?? selection.Code, result?.SafeMessage);
    }

    private static SystemCapabilityWriteHandlerResult Success(
        InteractionInvocationResult result, SystemTaskDurableHandle handle)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            status = result.WireTag, code = result.Code, message = result.SafeMessage,
            task = new { taskId = handle.TaskId, commandId = handle.CommandId }
        });
        return SystemCapabilityWriteHandlerResult.Success(data, handle.TaskId,
            ReviewSchemas.Fingerprint(data));
    }

    private static IReadOnlyList<string> SubmitReferences(ReviewSubmitWire request) =>
        [$"application:{request.ApplicationId}", $"candidate:{request.CandidateId}@{request.Revision}",
            $"operation:{request.AuthoringOperationId}"];

    private static IReadOnlyList<string> HandleReferences(ReviewHandleWire request) =>
        [$"system-task:{request.TaskId}"];

    private static SystemCapabilityWriteHandlerResult Failure(string code, string? message = null) =>
        SystemCapabilityWriteHandlerResult.Failure(code,
            message ?? "The candidate review operation was rejected.", ReviewSchemas.Recovery);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReviewSubmitWire(
    [property: JsonRequired] string ApplicationId,
    [property: JsonRequired] string CandidateId,
    [property: JsonRequired] int Revision,
    [property: JsonRequired] string ContentFingerprint,
    [property: JsonRequired] string AuthoringOperationId,
    [property: JsonRequired] string AuthoringCommandId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReviewHandleWire(
    [property: JsonRequired] string TaskId,
    [property: JsonRequired] string CommandId)
{
    internal SystemTaskDurableHandle Handle => new(TaskId, CommandId);
}

internal static class ReviewSchemas
{
    internal const string Recovery =
        "Retain the exact candidate, authoring receipt, task handle, and command identities; recheck current application grants before retrying.";

    internal const string SubmitInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["applicationId","candidateId","revision","contentFingerprint","authoringOperationId","authoringCommandId"],
        "properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},
        "candidateId":{"type":"string","minLength":32,"maxLength":32},"revision":{"type":"integer","minimum":1},
        "contentFingerprint":{"type":"string","minLength":64,"maxLength":64},
        "authoringOperationId":{"type":"string","minLength":32,"maxLength":32},
        "authoringCommandId":{"type":"string","minLength":1,"maxLength":128}}}
        """;

    internal const string HandleInput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["taskId","commandId"],"properties":{
        "taskId":{"type":"string","minLength":1,"maxLength":128},
        "commandId":{"type":"string","minLength":1,"maxLength":128}}}
        """;

    internal const string WriteOutput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["status","code","message","task"],"properties":{
        "status":{"type":"string","enum":["pending","cancelled"]},
        "code":{"type":"string","minLength":1,"maxLength":200},"message":{"type":"string","minLength":1,"maxLength":1000},
        "task":{"type":"object","additionalProperties":false,"required":["taskId","commandId"],"properties":{
        "taskId":{"type":"string","minLength":1,"maxLength":128},"commandId":{"type":"string","minLength":1,"maxLength":128}}}}}
        """;

    internal const string ReadOutput = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["status","code","message","data","evidenceReference"],"properties":{
        "status":{"type":"string","enum":["completed"]},"code":{"type":"string","minLength":1,"maxLength":200},
        "message":{"type":"string","minLength":1,"maxLength":1000},"data":{"type":"object"},
        "evidenceReference":{"type":"string","minLength":1,"maxLength":200}}}
        """;

    internal static T Deserialize<T>(JsonElement input) where T : class =>
        JsonSerializer.Deserialize<T>(input.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new JsonException("The candidate review request is absent.");

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

    internal static bool RetryableAuthorizationDenial(string code) =>
        ApplicationCandidateCapabilityHost.RetryableGrantDenial(code)
        || code == "INNER_VALIDATION_NOT_AUTHORIZED";

    internal static string ReadCommand(SystemCapabilityInvocationContext context, JsonElement input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "system.application-candidate.review-read\n" + context.Principal.PrincipalId + "\n"
            + context.CorrelationId + "\n" + Fingerprint(input))))[..32];
}
