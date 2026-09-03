using System.Collections.ObjectModel;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Closed, authorization-first dispatcher over explicitly registered system read handlers.</summary>
public sealed class SystemCapabilityCatalog : ISystemCapabilityCatalog
{
    private const string Recovery = "Inspect the registered system capabilities and retry with valid input.";
    private readonly IReadOnlyDictionary<string, Entry> _entries;
    private readonly IBoundedJsonSchemaValidator _schemas;
    private readonly IPrivateOperatorAuthorizationPolicy _authorization;

    public SystemCapabilityCatalog(
        IEnumerable<ISystemReadCapabilityHandler> handlers,
        IBoundedJsonSchemaValidator schemas,
        IPrivateOperatorAuthorizationPolicy authorization,
        IEnumerable<ISystemWriteCapabilityHandler>? writeHandlers = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (handler is null) throw Configuration("SYSTEM_CAPABILITY_HANDLER_INVALID", "A system capability handler is null.");
            var descriptor = Compile(handler.Registration, SystemCapabilityMode.Read);
            if (!entries.TryAdd(descriptor.Id, new(descriptor, handler, null)))
                throw Configuration("SYSTEM_CAPABILITY_DUPLICATE", $"System capability '{descriptor.Id}' is registered more than once.");
        }
        foreach (var handler in writeHandlers ?? [])
        {
            if (handler is null) throw Configuration("SYSTEM_CAPABILITY_HANDLER_INVALID", "A system capability handler is null.");
            var descriptor = Compile(handler.Registration, SystemCapabilityMode.Write);
            if (!entries.TryAdd(descriptor.Id, new(descriptor, null, handler)))
                throw Configuration("SYSTEM_CAPABILITY_DUPLICATE", $"System capability '{descriptor.Id}' is registered more than once.");
        }
        _entries = new ReadOnlyDictionary<string, Entry>(entries);
    }

    public SystemCapabilityDiscoveryResult Discover(SystemCapabilityInvocationContext context)
    {
        var decision = Authorize(context, PrivateOperatorCapability.Read);
        if (!decision.Allowed)
            return new(false, [], AuthorizationError(decision), decision.Evidence);
        return new(
            true,
            Array.AsReadOnly(_entries.Values.Select(value => value.Descriptor)
                .OrderBy(value => value.Id, StringComparer.Ordinal).ToArray()),
            null,
            decision.Evidence);
    }

    public async Task<SystemCapabilityReadResult> ReadAsync(
        string capabilityId,
        string inputJson,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        var baseline = Authorize(context, PrivateOperatorCapability.Read);
        if (!baseline.Allowed)
            return Failure(capabilityId, "", AuthorizationError(baseline), baseline.Evidence);

        if (!ValidCapabilityId(capabilityId) || !_entries.TryGetValue(capabilityId, out var entry))
            return Failure(capabilityId, "", Error(
                "SYSTEM_CAPABILITY_UNKNOWN", "The requested system capability is not registered.", Recovery), baseline.Evidence);
        if (entry.ReadHandler is null)
            return Failure(capabilityId, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_MODE_MISMATCH", "The requested system capability is not readable.", Recovery), baseline.Evidence);

        var decision = entry.Descriptor.RequiredCapability == PrivateOperatorCapability.Read
            ? baseline
            : Authorize(context, entry.Descriptor.RequiredCapability);
        if (!decision.Allowed)
            return Failure(capabilityId, entry.Descriptor.Fingerprint, AuthorizationError(decision), decision.Evidence);

        var input = _schemas.Validate(
            entry.Descriptor.InputSchemaProfile,
            entry.Descriptor.InputSchemaJson,
            inputJson);
        if (input.Status != SchemaValueStatus.Valid)
            return Failure(capabilityId, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_INPUT_INVALID",
                "The capability input does not satisfy its closed schema.",
                Recovery,
                input.Diagnostics), decision.Evidence);

        JsonElement inputElement;
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            inputElement = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Failure(capabilityId, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_INPUT_INVALID", "The capability input is not valid JSON.", Recovery), decision.Evidence);
        }

        SystemCapabilityHandlerResult handled;
        try
        {
            handled = await entry.ReadHandler.ReadAsync(inputElement, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(capabilityId, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_UNAVAILABLE", "The system capability is temporarily unavailable.", Recovery), decision.Evidence);
        }

        if (handled is null || !handled.Ok || handled.Data is null)
        {
            var error = SafeHandlerError(handled?.Error);
            return Failure(capabilityId, entry.Descriptor.Fingerprint, error, decision.Evidence);
        }

        var output = _schemas.Validate(
            entry.Descriptor.OutputSchemaProfile,
            entry.Descriptor.OutputSchemaJson,
            handled.Data.Value.GetRawText());
        if (output.Status != SchemaValueStatus.Valid)
            return Failure(capabilityId, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_OUTPUT_INVALID",
                "The capability owner returned data outside its declared schema.",
                Recovery,
                output.Diagnostics), decision.Evidence);

        return new(
            true,
            capabilityId,
            entry.Descriptor.Fingerprint,
            handled.Data.Value.Clone(),
            null,
            decision.Evidence);
    }

    public async Task<SystemCapabilityWritePreflightResult> PreflightWriteAsync(
        string capabilityId,
        string descriptorFingerprint,
        string inputJson,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveWrite(capabilityId, descriptorFingerprint, inputJson, context);
        if (!resolved.Ok)
            return new(false, resolved.Id, resolved.Fingerprint, null, resolved.Error, resolved.Evidence);
        SystemCapabilityWritePreflight handled;
        try
        {
            handled = await resolved.Entry!.WriteHandler!.PreflightAsync(
                resolved.Input!.Value, earlierSteps ?? [], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, resolved.Id, resolved.Fingerprint, null, Error(
                "SYSTEM_CAPABILITY_UNAVAILABLE", "The system capability preflight is temporarily unavailable.", Recovery),
                resolved.Evidence);
        }
        if (!ValidPreflight(handled))
            return new(false, resolved.Id, resolved.Fingerprint, null, Error(
                "SYSTEM_CAPABILITY_PREFLIGHT_INVALID", "The capability owner returned invalid preflight evidence.", Recovery),
                resolved.Evidence);
        if (!handled.Ok)
            return new(false, resolved.Id, resolved.Fingerprint, null, SafeHandlerError(handled.Error), resolved.Evidence);
        return new(true, resolved.Id, resolved.Fingerprint, handled with
        {
            AffectedReferences = Array.AsReadOnly(handled.AffectedReferences.ToArray()),
            DeferredStepIds = Array.AsReadOnly(handled.DeferredStepIds.ToArray())
        }, null, resolved.Evidence);
    }

    public async Task<SystemCapabilityWriteResult> ExecuteWriteAsync(
        string capabilityId,
        string descriptorFingerprint,
        string inputJson,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resolved = ResolveWrite(capabilityId, descriptorFingerprint, inputJson, context.Invocation);
        if (!resolved.Ok)
            return new(false, resolved.Id, resolved.Fingerprint, null, "", "", resolved.Error, resolved.Evidence);
        if (!ValidExecutionContext(context, resolved.Evidence))
            return new(false, resolved.Id, resolved.Fingerprint, null, "", "", Error(
                "SYSTEM_CAPABILITY_EXECUTION_CONTEXT_INVALID", "Trusted system execution context is invalid.", Recovery),
                resolved.Evidence);
        SystemCapabilityWriteHandlerResult handled;
        try
        {
            handled = await resolved.Entry!.WriteHandler!.ExecuteAsync(
                resolved.Input!.Value, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, resolved.Id, resolved.Fingerprint, null, "", "", Error(
                "SYSTEM_CAPABILITY_WRITE_FAILED", "The system capability write failed without a safe result.", Recovery),
                resolved.Evidence);
        }
        if (handled is null || !handled.Ok || handled.Data is null)
            return new(false, resolved.Id, resolved.Fingerprint, null,
                SafeOperationId(handled?.OperationId), "", SafeHandlerError(handled?.Error), resolved.Evidence);
        var output = _schemas.Validate(
            resolved.Entry.Descriptor.OutputSchemaProfile,
            resolved.Entry.Descriptor.OutputSchemaJson,
            handled.Data.Value.GetRawText());
        if (output.Status != SchemaValueStatus.Valid || !UpperSha256(handled.ReadBackFingerprint) ||
            !ValidOperationId(handled.OperationId))
            return new(false, resolved.Id, resolved.Fingerprint, null,
                SafeOperationId(handled.OperationId), "", Error(
                    "SYSTEM_CAPABILITY_OUTPUT_INVALID_AFTER_COMMIT",
                    "The capability owner committed but its safe read-back result was invalid.", Recovery),
                resolved.Evidence);
        return new(true, resolved.Id, resolved.Fingerprint, handled.Data.Value.Clone(),
            handled.OperationId, handled.ReadBackFingerprint, null, resolved.Evidence);
    }

    private SystemCapabilityDescriptor Compile(
        SystemCapabilityRegistration registration,
        SystemCapabilityMode expectedMode)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!ValidCapabilityId(registration.Id))
            throw Configuration("SYSTEM_CAPABILITY_ID_INVALID", "A capability ID must be a bounded lowercase system.* identifier.");
        if (registration.Version < 1)
            throw Configuration("SYSTEM_CAPABILITY_VERSION_INVALID", "A capability version must be positive.");
        if (!ValidOwner(registration.Owner))
            throw Configuration("SYSTEM_CAPABILITY_OWNER_INVALID", "A capability owner must be a bounded component identifier.");
        if (!BoundedText(registration.Description, 500))
            throw Configuration("SYSTEM_CAPABILITY_DESCRIPTION_INVALID", "A capability description must be bounded text.");
        if (!Enum.IsDefined(registration.Mode) || !Enum.IsDefined(registration.Sensitivity) ||
            !PrivateOperatorCapabilityNames.TryGetAuditName(registration.RequiredCapability, out var capabilityName))
            throw Configuration("SYSTEM_CAPABILITY_METADATA_INVALID", "Capability mode, sensitivity, and authorization metadata must be closed values.");
        if (registration.Mode != expectedMode || expectedMode == SystemCapabilityMode.Read &&
            (registration.RequiresConfirmation || registration.RequiresIdempotencyKey) ||
            expectedMode == SystemCapabilityMode.Write &&
            (!registration.RequiresConfirmation || !registration.RequiresIdempotencyKey))
            throw Configuration("SYSTEM_CAPABILITY_MODE_INVALID",
                "Read handlers cannot require confirmation/idempotency; write handlers must require both.");
        var procedures = registration.ProcedureIds?.ToArray() ?? [];
        if (procedures.Length is < 1 or > 16 || procedures.Distinct(StringComparer.Ordinal).Count() != procedures.Length ||
            procedures.Any(value => !ValidProcedure(value)))
            throw Configuration("SYSTEM_CAPABILITY_PROCEDURES_INVALID", "Capability procedure IDs must be distinct bounded system procedures.");

        var input = _schemas.Compile(registration.InputSchemaJson);
        if (!input.IsAccepted)
            throw Configuration("SYSTEM_CAPABILITY_INPUT_SCHEMA_INVALID", "The capability input schema is not accepted by the bounded profile.");
        var output = _schemas.Compile(registration.OutputSchemaJson);
        if (!output.IsAccepted)
            throw Configuration("SYSTEM_CAPABILITY_OUTPUT_SCHEMA_INVALID", "The capability output schema is not accepted by the bounded profile.");

        var withoutFingerprint = new SystemCapabilityDescriptor(
            registration.Id,
            registration.Version,
            "",
            registration.Owner,
            registration.Description,
            registration.Mode,
            registration.Mode.ToString().ToLowerInvariant(),
            input.ProfileId,
            input.NormalizedSchema,
            input.SchemaHash,
            output.ProfileId,
            output.NormalizedSchema,
            output.SchemaHash,
            Array.AsReadOnly(procedures),
            registration.RequiredCapability,
            capabilityName,
            registration.Sensitivity,
            SensitivityName(registration.Sensitivity),
            registration.RequiresConfirmation,
            registration.RequiresIdempotencyKey);
        return withoutFingerprint with
        {
            Fingerprint = SystemCapabilityDescriptorFingerprint.Compute(withoutFingerprint)
        };
    }

    private PrivateOperatorAuthorizationDecision Authorize(
        SystemCapabilityInvocationContext context,
        PrivateOperatorCapability capability)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _authorization.Evaluate(new(
            context.Principal,
            capability,
            context.Scope,
            context.CorrelationId));
    }

    private static SystemCapabilityReadResult Failure(
        string? id,
        string fingerprint,
        SystemCapabilityError error,
        AuthorizationAuditEvidence evidence) =>
        new(false, SafeId(id), fingerprint, null, error, evidence);

    private static SystemCapabilityError AuthorizationError(PrivateOperatorAuthorizationDecision decision) =>
        Error(decision.Code, "Private-operator authorization is required for this system capability.", decision.Recovery);

    private static SystemCapabilityError SafeHandlerError(SystemCapabilityError? error) =>
        error is not null && error.Diagnostics is not null &&
        ValidErrorCode(error.Code) && BoundedText(error.Message, 300) &&
        BoundedText(error.Recovery, 300) && error.Diagnostics.Count <= SystemJsonSchemaProfile.MaximumDiagnostics
            ? error with { Diagnostics = Array.AsReadOnly(error.Diagnostics.ToArray()) }
            : Error("SYSTEM_CAPABILITY_UNAVAILABLE", "The system capability is temporarily unavailable.", Recovery);

    private static SystemCapabilityError Error(
        string code,
        string message,
        string recovery,
        IReadOnlyList<SchemaDiagnostic>? diagnostics = null) =>
        new(code, message, recovery, Array.AsReadOnly((diagnostics ?? []).Take(
            SystemJsonSchemaProfile.MaximumDiagnostics).ToArray()));

    private static bool ValidCapabilityId(string? value) =>
        value is { Length: >= 8 and <= 120 } && value.StartsWith("system.", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');

    private static bool ValidOwner(string? value) =>
        value is { Length: >= 1 and <= 80 } && char.IsAsciiLetterLower(value[0]) &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static bool ValidProcedure(string? value) =>
        value is { Length: >= 18 and <= 160 } && value.StartsWith("procedure.system.", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');

    private static bool ValidErrorCode(string? value) =>
        value is { Length: >= 3 and <= 80 } &&
        value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_');

    private static string SensitivityName(SystemCapabilitySensitivity value) => value switch
    {
        SystemCapabilitySensitivity.PublicMetadata => "public-metadata",
        SystemCapabilitySensitivity.PrivateOperatorMetadata => "private-operator-metadata",
        SystemCapabilitySensitivity.Secret => "secret",
        _ => "invalid"
    };

    private static bool BoundedText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static string SafeId(string? value) => ValidCapabilityId(value) ? value! : "";

    private WriteResolution ResolveWrite(
        string? capabilityId,
        string? descriptorFingerprint,
        string inputJson,
        SystemCapabilityInvocationContext context)
    {
        var baseline = Authorize(context, PrivateOperatorCapability.Modify);
        var id = SafeId(capabilityId);
        if (!baseline.Allowed)
            return WriteResolution.Failure(id, "", AuthorizationError(baseline), baseline.Evidence);
        if (!ValidCapabilityId(capabilityId) || !_entries.TryGetValue(capabilityId!, out var entry))
            return WriteResolution.Failure(id, "", Error(
                "SYSTEM_CAPABILITY_UNKNOWN", "The requested system capability is not registered.", Recovery), baseline.Evidence);
        if (entry.WriteHandler is null)
            return WriteResolution.Failure(id, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_MODE_MISMATCH", "The requested system capability is not writable.", Recovery), baseline.Evidence);
        if (!string.Equals(descriptorFingerprint, entry.Descriptor.Fingerprint, StringComparison.Ordinal))
            return WriteResolution.Failure(id, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_DESCRIPTOR_STALE", "The system capability descriptor changed after planning.", Recovery), baseline.Evidence);
        var decision = entry.Descriptor.RequiredCapability == PrivateOperatorCapability.Modify
            ? baseline : Authorize(context, entry.Descriptor.RequiredCapability);
        if (!decision.Allowed)
            return WriteResolution.Failure(id, entry.Descriptor.Fingerprint, AuthorizationError(decision), decision.Evidence);
        var validation = _schemas.Validate(entry.Descriptor.InputSchemaProfile,
            entry.Descriptor.InputSchemaJson, inputJson);
        if (validation.Status != SchemaValueStatus.Valid)
            return WriteResolution.Failure(id, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_INPUT_INVALID", "The capability input does not satisfy its closed schema.",
                Recovery, validation.Diagnostics), decision.Evidence);
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return WriteResolution.Success(id, entry, document.RootElement.Clone(), decision.Evidence);
        }
        catch (JsonException)
        {
            return WriteResolution.Failure(id, entry.Descriptor.Fingerprint, Error(
                "SYSTEM_CAPABILITY_INPUT_INVALID", "The capability input is not valid JSON.", Recovery), decision.Evidence);
        }
    }

    private static bool ValidPreflight(SystemCapabilityWritePreflight? value) => value is not null &&
        value.AffectedReferences is { Count: <= 32 } && value.DeferredStepIds is { Count: <= 12 } &&
        value.AffectedReferences.All(reference => BoundedText(reference, 320)) &&
        value.DeferredStepIds.All(step => step is { Length: >= 8 and <= 12 } &&
            step.StartsWith("step-", StringComparison.Ordinal)) &&
        ValidExecutionEvidence(value.ExecutionEvidenceJson) &&
        (value.Ok
            ? value.Status is SystemCapabilityPreflightStatuses.Ready or SystemCapabilityPreflightStatuses.Deferred &&
              UpperSha256(value.PreconditionFingerprint) && BoundedText(value.SafeSummary, 1000) && value.Error is null
            : value.Error is not null);

    private static bool ValidExecutionContext(
        SystemCapabilityWriteExecutionContext context,
        AuthorizationAuditEvidence evidence) =>
        context.Invocation is not null && context.AuthorizationEvidence is not null &&
        string.Equals(context.AuthorizationEvidence.PrincipalReference, evidence.PrincipalReference, StringComparison.Ordinal) &&
        context.AuthorizationEvidence.Allowed && context.RequestToken is { Length: 32 } &&
        context.RequestToken.All(character => char.IsAsciiHexDigitLower(character)) &&
        BoundedText(context.Intent, 8000) && context.ProceduresUsed is { Count: >= 1 and <= 16 } &&
        context.ProceduresUsed.All(ValidProcedure) && ValidExecutionEvidence(context.ExecutionEvidenceJson);

    private static bool ValidExecutionEvidence(string? value)
    {
        if (value is null || value.Length is < 2 or > 16_000) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static bool UpperSha256(string? value) => value is { Length: 64 } &&
        value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static bool ValidOperationId(string? value) => value is { Length: >= 1 and <= 100 } &&
        !value.Any(char.IsControl);

    private static string SafeOperationId(string? value) => ValidOperationId(value) ? value! : "";

    private static SystemCapabilityConfigurationException Configuration(string code, string message) =>
        new(code, message);

    private sealed record Entry(
        SystemCapabilityDescriptor Descriptor,
        ISystemReadCapabilityHandler? ReadHandler,
        ISystemWriteCapabilityHandler? WriteHandler);

    private sealed record WriteResolution(
        bool Ok,
        string Id,
        string Fingerprint,
        Entry? Entry,
        JsonElement? Input,
        SystemCapabilityError? Error,
        AuthorizationAuditEvidence Evidence)
    {
        public static WriteResolution Success(
            string id, Entry entry, JsonElement input, AuthorizationAuditEvidence evidence) =>
            new(true, id, entry.Descriptor.Fingerprint, entry, input, null, evidence);
        public static WriteResolution Failure(
            string id, string fingerprint, SystemCapabilityError error, AuthorizationAuditEvidence evidence) =>
            new(false, id, fingerprint, null, null, error, evidence);
    }
}
