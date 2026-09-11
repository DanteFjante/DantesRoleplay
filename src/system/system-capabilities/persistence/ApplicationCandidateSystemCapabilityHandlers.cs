using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.SystemCapabilities;

internal sealed class ApplicationCandidateInspectCapabilityHandler(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationAuthoringService authoring) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ApplicationCandidateInspect, 1, "application-activation",
        "Inspect one exact retained application candidate using the selected current application and standing Read grant.",
        SystemCapabilityMode.Read, ApplicationCandidateCapabilitySchemas.InspectInput,
        ApplicationCandidateCapabilitySchemas.InspectOutput,
        ["procedure.system.application-candidate.inspect"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input, CancellationToken cancellationToken = default) =>
        Task.FromResult(SystemCapabilityHandlerResult.Failure(
            "APPLICATION_CONTEXT_REQUIRED", "A trusted selected application is required.",
            ApplicationCandidateCapabilitySchemas.Recovery));

    public async Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input, SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ApplicationCandidateCapabilitySchemas.Deserialize<InspectWire>(input);
            var applicationId = ApplicationIdentifier.Parse(request.ApplicationId);
            var selection = await ApplicationCandidateCapabilityHost.CreateAsync(
                db, applications, context, applicationId, StandingGrantCapability.Read,
                ApplicationCandidateCapabilityHost.ReadCommand(context, input),
                InteractionExecutionProfile.ReadOnly, cancellationToken);
            if (selection.Hosts.Count == 0)
                return ApplicationCandidateCapabilitySchemas.ReadFailure(selection.Code);
            InteractionInvocationResult? result = null;
            foreach (var host in selection.Hosts)
            {
                result = await authoring.InspectAsync(host,
                    new(request.CandidateId, request.Revision, request.SourceOperationId), cancellationToken);
                if (result.Tag == InteractionInvocationResultTag.Completed && result.DataJson is not null
                    && result.CompletionEvidenceReference is not null)
                    return SystemCapabilityHandlerResult.Success(JsonSerializer.SerializeToElement(new
                    {
                        status = result.WireTag,
                        code = result.Code,
                        message = result.SafeMessage,
                        dataJson = result.DataJson,
                        evidenceReference = result.CompletionEvidenceReference
                    }));
                if (!ApplicationCandidateCapabilityHost.RetryableGrantDenial(result.Code)) break;
            }
            return ApplicationCandidateCapabilitySchemas.ReadFailure(
                result?.Code ?? selection.Code, result?.SafeMessage);
        }
        catch (OperationCanceledException) { throw; }
        catch (InteractionContractException exception)
        {
            return ApplicationCandidateCapabilitySchemas.ReadFailure(exception.Code,
                "The application candidate request is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return ApplicationCandidateCapabilitySchemas.ReadFailure("INVALID_PAYLOAD");
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record InspectWire(
        [property: JsonRequired] string ApplicationId,
        [property: JsonRequired] string? CandidateId,
        [property: JsonRequired] int Revision,
        [property: JsonRequired] string? SourceOperationId);
}

internal sealed class ApplicationCandidateWriteCapabilityHandler(
    string id,
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationAuthoringService authoring) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = ApplicationCandidateCapabilitySchemas.Registration(id);

    public Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input, IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var parsed = Parse(input);
            var fingerprint = ApplicationCandidateCapabilitySchemas.Fingerprint(input);
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(
                fingerprint,
                $"{ApplicationCandidateCapabilitySchemas.Operation(id)} application candidate data for '{parsed.ApplicationId}'.",
                [$"application:{parsed.ApplicationId}"],
                JsonSerializer.Serialize(new { inputFingerprint = fingerprint })));
        }
        catch (InteractionContractException exception)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                exception.Code, "The application candidate request is invalid or exceeds its bounded operation budget.",
                ApplicationCandidateCapabilitySchemas.Recovery));
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                "INVALID_PAYLOAD", "The application candidate request is invalid.",
                ApplicationCandidateCapabilitySchemas.Recovery));
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = Parse(input);
            if (!ApplicationCandidateCapabilitySchemas.MatchesEvidence(
                    context.ExecutionEvidenceJson, ApplicationCandidateCapabilitySchemas.Fingerprint(input)))
                return Failure("SYSTEM_CAPABILITY_PREFLIGHT_STALE");
            var applicationId = ApplicationIdentifier.Parse(parsed.ApplicationId);
            var capability = id switch
            {
                SystemCapabilityIds.ApplicationCandidateWrite => StandingGrantCapability.Author,
                SystemCapabilityIds.ApplicationCandidateValidate => StandingGrantCapability.Validate,
                SystemCapabilityIds.ApplicationCandidateActivate => StandingGrantCapability.Activate,
                SystemCapabilityIds.ApplicationCandidateRecover => StandingGrantCapability.Author,
                _ => throw new ArgumentException("Unknown application candidate capability.")
            };
            var selection = await ApplicationCandidateCapabilityHost.CreateAsync(
                db, applications, context.Invocation, applicationId, capability,
                context.RequestToken, InteractionExecutionProfile.Atomic, parsed.RequiredOperations,
                cancellationToken);
            if (selection.Hosts.Count == 0) return Failure(selection.Code);

            InteractionInvocationResult? result = null;
            foreach (var host in selection.Hosts)
            {
                result = id switch
                {
                    SystemCapabilityIds.ApplicationCandidateWrite => await authoring.WriteCandidateAsync(
                        host, ((WriteParsed)parsed).Request, cancellationToken),
                    SystemCapabilityIds.ApplicationCandidateValidate => await authoring.ValidateAsync(
                        ((ValidateParsed)parsed).Request, host, cancellationToken),
                    SystemCapabilityIds.ApplicationCandidateActivate => await authoring.ActivateAsync(
                        host, ((ActivateParsed)parsed).Request, cancellationToken),
                    SystemCapabilityIds.ApplicationCandidateRecover => await authoring.RecoverAsync(
                        host, ((RecoverParsed)parsed).ActivationRevision,
                        ((RecoverParsed)parsed).ExpectedActiveFingerprint, cancellationToken),
                    _ => throw new ArgumentException("Unknown application candidate capability.")
                };
                if (result.Tag == InteractionInvocationResultTag.Committed && result.Receipt is not null)
                    return SystemCapabilityWriteHandlerResult.Success(JsonSerializer.SerializeToElement(new
                    {
                        status = result.WireTag,
                        code = result.Code,
                        message = result.SafeMessage,
                        operationId = result.Receipt.OperationId,
                        requestFingerprint = result.Receipt.RequestFingerprint
                    }), result.Receipt.OperationId, result.Receipt.RequestFingerprint);
                if (!ApplicationCandidateCapabilityHost.RetryableGrantDenial(result.Code)) break;
            }
            return Failure(result?.Code ?? selection.Code, result?.SafeMessage);
        }
        catch (OperationCanceledException) { throw; }
        catch (InteractionContractException exception)
        {
            return Failure(exception.Code,
                "The application candidate request is invalid or exceeds its bounded operation budget.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Failure("INVALID_PAYLOAD");
        }
    }

    private Parsed Parse(JsonElement input) => id switch
    {
        SystemCapabilityIds.ApplicationCandidateWrite => Write(input),
        SystemCapabilityIds.ApplicationCandidateValidate => Validate(input),
        SystemCapabilityIds.ApplicationCandidateActivate => Activate(input),
        SystemCapabilityIds.ApplicationCandidateRecover => Recover(input),
        _ => throw new ArgumentException("Unknown application candidate capability.")
    };

    private static WriteParsed Write(JsonElement input)
    {
        var value = ApplicationCandidateCapabilitySchemas.Deserialize<WriteWire>(input);
        var request = new ApplicationCandidateWriteRequest(value.CandidateId, value.ExpectedCandidateRevision,
            value.ExpectedActiveFingerprint, value.Origin, value.SynchronizationEvidenceReference,
            value.NewImplementationReason, value.Documents.Select(document => new ApplicationCandidateDocumentInput(
                document.LogicalIdentity, document.SourceId, document.RelativePath, document.MediaType, document.Text)).ToArray());
        SqliteApplicationAuthoringService.Validate(request);
        return new(value.ApplicationId, request, 1);
    }

    private static ValidateParsed Validate(JsonElement input)
    {
        var value = ApplicationCandidateCapabilitySchemas.Deserialize<ValidateWire>(input);
        var app = ApplicationIdentifier.Parse(value.ApplicationId);
        var candidate = new ApplicationCandidateReference(app, value.CandidateId, value.Revision, value.ContentFingerprint);
        var samples = value.Samples.Select(sample => new ApplicationCandidateValidationSample(
            new(sample.Definition.DefinitionId, sample.Definition.Kind, sample.Definition.Revision,
                sample.Definition.ContentFingerprint), sample.InputJson, sample.ExpectedDataJson)).ToArray();
        _ = SqliteApplicationAuthoringService.NormalizeSamples(samples);
        var requiredOperations = 2 + samples.Length;
        if (requiredOperations > StandingGrantLimits.MaximumOperations)
            throw new InteractionContractException("INVOCATION_BUDGET_EXHAUSTED",
                "Validation samples exceed the available operation budget.");
        return new(value.ApplicationId, new(candidate, samples), requiredOperations);
    }

    private static ActivateParsed Activate(JsonElement input)
    {
        var value = ApplicationCandidateCapabilitySchemas.Deserialize<ActivateWire>(input);
        var candidate = new ApplicationCandidateReference(ApplicationIdentifier.Parse(value.ApplicationId),
            value.CandidateId, value.Revision, value.ContentFingerprint);
        return new(value.ApplicationId, new(candidate, value.ValidationOperationId), 1);
    }

    private static RecoverParsed Recover(JsonElement input)
    {
        var value = ApplicationCandidateCapabilitySchemas.Deserialize<RecoverWire>(input);
        _ = ApplicationIdentifier.Parse(value.ApplicationId);
        return new(value.ApplicationId, value.ActivationRevision, value.ExpectedActiveFingerprint, 1);
    }

    private static SystemCapabilityWriteHandlerResult Failure(string code, string? message = null) =>
        SystemCapabilityWriteHandlerResult.Failure(code,
            message ?? "The application candidate request was rejected.",
            ApplicationCandidateCapabilitySchemas.Recovery);

    private abstract record Parsed(string ApplicationId, int RequiredOperations);
    private sealed record WriteParsed(string Id, ApplicationCandidateWriteRequest Request, int Operations) : Parsed(Id, Operations);
    private sealed record ValidateParsed(string Id, ApplicationCandidateValidationRequest Request, int Operations) : Parsed(Id, Operations);
    private sealed record ActivateParsed(string Id, ApplicationCandidateActivationRequest Request, int Operations) : Parsed(Id, Operations);
    private sealed record RecoverParsed(string Id, int ActivationRevision, string? ExpectedActiveFingerprint, int Operations) : Parsed(Id, Operations);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record WriteWire(string ApplicationId, string? CandidateId, int ExpectedCandidateRevision,
        string? ExpectedActiveFingerprint, string Origin, string? SynchronizationEvidenceReference,
        string NewImplementationReason, IReadOnlyList<DocumentWire> Documents);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record DocumentWire(string LogicalIdentity, string SourceId, string RelativePath, string MediaType, string Text);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ValidateWire(string ApplicationId, string CandidateId, int Revision,
        string ContentFingerprint, IReadOnlyList<SampleWire> Samples);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SampleWire(DefinitionWire Definition, string InputJson, string ExpectedDataJson);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record DefinitionWire(string DefinitionId, string Kind, int Revision, string ContentFingerprint);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ActivateWire(string ApplicationId, string CandidateId, int Revision,
        string ContentFingerprint, string ValidationOperationId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RecoverWire(string ApplicationId, int ActivationRevision, string? ExpectedActiveFingerprint);
}

internal static class ApplicationCandidateCapabilityHost
{
    internal static Task<(IReadOnlyList<InteractionInvocationHost> Hosts, string Code)> CreateAsync(
        DantesRoleplayDbContext db, IApplicationRegistry applications,
        SystemCapabilityInvocationContext context, ApplicationIdentifier applicationId,
        StandingGrantCapability capability, string commandId, InteractionExecutionProfile profile,
        CancellationToken cancellationToken) => CreateAsync(db, applications, context, applicationId,
            capability, commandId, profile, 1, cancellationToken);

    internal static async Task<(IReadOnlyList<InteractionInvocationHost> Hosts, string Code)> CreateAsync(
        DantesRoleplayDbContext db, IApplicationRegistry applications,
        SystemCapabilityInvocationContext context, ApplicationIdentifier applicationId,
        StandingGrantCapability capability, string commandId, InteractionExecutionProfile profile,
        int requiredOperations, CancellationToken cancellationToken)
    {
        if (context is null || !context.Principal.Verified
            || context.ApplicationId is not null && context.ApplicationId != applicationId)
            return ([], "APPLICATION_CONTEXT_REQUIRED");
        var application = applications.Get(applicationId);
        if (application is null) return ([], "APPLICATION_UNKNOWN");
        var grants = await CurrentAsync(db, context.Principal, applicationId, cancellationToken);
        if (grants is null) return ([], "STANDING_GRANT_CANDIDATES_UNAVAILABLE");
        if (requiredOperations is < 1 or > StandingGrantLimits.MaximumOperations)
            return ([], "INVOCATION_BUDGET_EXHAUSTED");
        var capable = grants.Where(value => value.Capabilities.Contains(capability)).ToArray();
        var eligible = capable.Where(value => value.MaximumOperations >= requiredOperations)
            .OrderBy(value => value.Definitions.Mode == StandingGrantDefinitionMode.ExactIds ? 0 : 1)
            .ThenBy(value => value.GrantId, StringComparer.Ordinal).ThenBy(value => value.Revision)
            .ToArray();
        if (eligible.Length == 0) return ([], capable.Length == 0
            ? "STANDING_GRANT_DENIED" : "STANDING_GRANT_BUDGET_DENIED");
        var now = DateTime.UtcNow;
        var deadline = eligible.Min(value => value.ExpiresAtUtc);
        if (deadline > now.AddSeconds(10)) deadline = now.AddSeconds(10);
        if (deadline <= now) return ([], "STANDING_GRANT_DENIED");
        var budget = new InteractionInvocationBudget(requiredOperations, deadline);
        return (Array.AsReadOnly(eligible.Select(grant => InteractionInvocationHost.ForApplication(
            context.Principal, application, grant.GrantReference, commandId, profile, budget)).ToArray()),
            "STANDING_GRANT_SELECTED");
    }

    internal static bool RetryableGrantDenial(string code) => code is
        "STANDING_GRANT_DENIED" or "STANDING_GRANT_TARGET_DENIED" or
        "STANDING_GRANT_NOT_CURRENT" or "STANDING_GRANT_INVALID";

    internal static string ReadCommand(SystemCapabilityInvocationContext context, JsonElement input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "system.application-candidate.inspect\n" + context.Principal.PrincipalId + "\n"
            + context.CorrelationId + "\n" + ApplicationCandidateCapabilitySchemas.Fingerprint(input))))[..32]
            .ToLowerInvariant();

    private static async Task<IReadOnlyList<StandingGrantRevision>?> CurrentAsync(
        DantesRoleplayDbContext db, TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId, CancellationToken cancellationToken)
    {
        try
        {
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var now = DateTime.UtcNow;
            var keys = await (from record in db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                              join current in db.Set<StandingGrantCurrentRecord>().AsNoTracking()
                                  on new { record.GrantId, record.Revision } equals new { current.GrantId, current.Revision }
                              where record.PrincipalReference == principal.PrincipalId
                                  && record.ApplicationId == applicationId.Value
                                  && record.Scope == "application" && record.StateSpaceId == null
                                  && !record.Revoked && record.ExpiresAtUtc > now
                              orderby record.GrantId, record.Revision
                              select new GrantKey(record.GrantId, record.Revision)).Take(33)
                .ToArrayAsync(cancellationToken);
            if (keys.Length > 32) return null;
            var result = new List<StandingGrantRevision>(keys.Length);
            foreach (var key in keys)
            {
                if (await PermissionBytesAsync(db, key, cancellationToken) is < 0 or > 16000) return null;
                var row = await db.Set<StandingGrantRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
                    value.GrantId == key.GrantId && value.Revision == key.Revision, cancellationToken);
                if (row is null) return null;
                result.Add(SqliteStandingGrantPolicy.Parse(row));
            }
            return Array.AsReadOnly(result.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static async Task<long> PermissionBytesAsync(
        DantesRoleplayDbContext db, GrantKey key, CancellationToken cancellationToken)
    {
        await using var command = ((SqliteConnection)db.Database.GetDbConnection()).CreateCommand();
        command.Transaction = (SqliteTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT length(CAST(\"PermissionsJson\" AS BLOB)) FROM system_standing_grant_revision WHERE \"GrantId\" = $grantId AND \"Revision\" = $revision";
        command.Parameters.AddWithValue("$grantId", key.GrantId);
        command.Parameters.AddWithValue("$revision", key.Revision);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? -1 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record GrantKey(string GrantId, int Revision);
}

internal static class ApplicationCandidateCapabilitySchemas
{
    internal const string Recovery = "Select a current application, obtain the required standing grant, and retry with the capability schema.";

    internal static T Deserialize<T>(JsonElement input) =>
        JsonSerializer.Deserialize<T>(input.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new JsonException("The request is absent.");

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

    internal static string Operation(string id) => id switch
    {
        SystemCapabilityIds.ApplicationCandidateWrite => "Stage",
        SystemCapabilityIds.ApplicationCandidateValidate => "Validate",
        SystemCapabilityIds.ApplicationCandidateActivate => "Activate",
        SystemCapabilityIds.ApplicationCandidateRecover => "Recover",
        _ => "Process"
    };

    internal static SystemCapabilityHandlerResult ReadFailure(string code, string? message = null) =>
        SystemCapabilityHandlerResult.Failure(code,
            message ?? "The application candidate request was rejected.", Recovery);

    internal static SystemCapabilityRegistration Registration(string id)
    {
        var (description, input, procedure) = id switch
        {
            SystemCapabilityIds.ApplicationCandidateWrite => (
                "Stage bounded replacement documents as a new inert application candidate.", WriteInput,
                "procedure.system.application-candidate.write"),
            SystemCapabilityIds.ApplicationCandidateValidate => (
                "Validate an exact retained candidate with bounded definition-pinned samples.", ValidateInput,
                "procedure.system.application-candidate.validate"),
            SystemCapabilityIds.ApplicationCandidateActivate => (
                "Activate an exact validated application candidate.", ActivateInput,
                "procedure.system.application-candidate.activate"),
            SystemCapabilityIds.ApplicationCandidateRecover => (
                "Copy a bounded retained activation into a fresh inert application candidate.", RecoverInput,
                "procedure.system.application-candidate.recover"),
            _ => throw new ArgumentException("Unknown application candidate capability.", nameof(id))
        };
        return new(id, 1, "application-activation", description, SystemCapabilityMode.Write,
            input, WriteOutput, [procedure], PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true);
    }

    private const string NullableHash = "{\"anyOf\":[{\"type\":\"string\",\"minLength\":64,\"maxLength\":64},{\"type\":\"null\"}]}";
    private const string CandidateProperties = "\"applicationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":63},\"candidateId\":{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},\"revision\":{\"type\":\"integer\",\"minimum\":1},\"contentFingerprint\":{\"type\":\"string\",\"minLength\":64,\"maxLength\":64}";

    internal const string InspectInput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"candidateId\",\"revision\",\"sourceOperationId\"],\"properties\":{\"applicationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":63},\"candidateId\":{\"anyOf\":[{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},{\"type\":\"null\"}]},\"revision\":{\"type\":\"integer\",\"minimum\":0},\"sourceOperationId\":{\"anyOf\":[{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},{\"type\":\"null\"}]}}}";
    internal const string InspectOutput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"status\",\"code\",\"message\",\"dataJson\",\"evidenceReference\"],\"properties\":{\"status\":{\"type\":\"string\",\"enum\":[\"completed\"]},\"code\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"message\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1000},\"dataJson\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":65536},\"evidenceReference\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}}}";
    internal static readonly string WriteInput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"candidateId\",\"expectedCandidateRevision\",\"expectedActiveFingerprint\",\"origin\",\"synchronizationEvidenceReference\",\"newImplementationReason\",\"documents\"],\"properties\":{\"applicationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":63},\"candidateId\":{\"anyOf\":[{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},{\"type\":\"null\"}]},\"expectedCandidateRevision\":{\"type\":\"integer\",\"minimum\":0},\"expectedActiveFingerprint\":" + NullableHash + ",\"origin\":{\"type\":\"string\",\"enum\":[\"runtime\"]},\"synchronizationEvidenceReference\":{\"type\":\"null\"},\"newImplementationReason\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":2000},\"documents\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":16,\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"logicalIdentity\",\"sourceId\",\"relativePath\",\"mediaType\",\"text\"],\"properties\":{\"logicalIdentity\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"sourceId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"relativePath\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1000},\"mediaType\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"text\":{\"type\":\"string\",\"maxLength\":65536}}}}}}";
    internal static readonly string ValidateInput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"candidateId\",\"revision\",\"contentFingerprint\",\"samples\"],\"properties\":{" + CandidateProperties + ",\"samples\":{\"type\":\"array\",\"maxItems\":16,\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"definition\",\"inputJson\",\"expectedDataJson\"],\"properties\":{\"definition\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"definitionId\",\"kind\",\"revision\",\"contentFingerprint\"],\"properties\":{\"definitionId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"kind\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80},\"revision\":{\"type\":\"integer\",\"minimum\":1},\"contentFingerprint\":{\"type\":\"string\",\"minLength\":64,\"maxLength\":64}}},\"inputJson\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":65536},\"expectedDataJson\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":65536}}}}}}";
    internal static readonly string ActivateInput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"candidateId\",\"revision\",\"contentFingerprint\",\"validationOperationId\"],\"properties\":{" + CandidateProperties + ",\"validationOperationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":100}}}";
    internal static readonly string RecoverInput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"activationRevision\",\"expectedActiveFingerprint\"],\"properties\":{\"applicationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":63},\"activationRevision\":{\"type\":\"integer\",\"minimum\":1},\"expectedActiveFingerprint\":" + NullableHash + "}}";
    internal const string WriteOutput = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"status\",\"code\",\"message\",\"operationId\",\"requestFingerprint\"],\"properties\":{\"status\":{\"type\":\"string\",\"enum\":[\"committed\"]},\"code\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"message\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1000},\"operationId\":{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},\"requestFingerprint\":{\"type\":\"string\",\"minLength\":64,\"maxLength\":64}}}";
}
