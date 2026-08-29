using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.Sources;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Authorization-first adapter for the two immutable registry administration writes.</summary>
internal sealed class SystemRegistryCommitTools
{
    public Task<ToolEnvelope> RegisterApplicationAsync(
        IRegistryAdministrationService? administration,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken) => ExecuteAsync(
            "system.application.register", administration, authorization, log, payload, intent,
            procedures, dryRun, cancellationToken, application: true);

    public Task<ToolEnvelope> RegisterSourceAsync(
        IRegistryAdministrationService? administration,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken) => ExecuteAsync(
            "system.source.register", administration, authorization, log, payload, intent,
            procedures, dryRun, cancellationToken, application: false);

    private static async Task<ToolEnvelope> ExecuteAsync(
        string kind,
        IRegistryAdministrationService? administration,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken,
        bool application)
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return await RecordedFailureAsync(log, decision, kind, decision.Code,
                "Private-operator authentication is required before administrative registration.",
                "query(kind: \"capabilities\")", intent, procedures,
                $"Denied unauthorized {kind} before payload parsing.");
        if (administration is null)
            return await RecordedFailureAsync(log, decision, kind, "REGISTRY_UNAVAILABLE",
                "Registry administration is not configured.", "query(kind: \"capabilities\")",
                intent, procedures, "Registry administration was unavailable.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("payload must be a JSON object.");

            return application
                ? await ApplicationAsync(administration, decision, document.RootElement, payload,
                    intent, procedures, dryRun, cancellationToken)
                : await SourceAsync(administration, decision, document.RootElement, payload,
                    intent, procedures, dryRun, cancellationToken);
        }
        catch (JsonException exception)
        {
            return await RecordedFailureAsync(log, decision, kind, "INVALID_PAYLOAD", exception.Message,
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                $"Rejected malformed {kind} payload.");
        }
        catch (RegistryAdministrationException exception)
        {
            var fix = exception.Code == "DRY_RUN_REQUIRED"
                ? CommitCall(kind, payload, dryRun: true)
                : VerbSurface.CommitCall(kind, dryRun: true);
            return await RecordedFailureAsync(log, decision, kind, exception.Code, exception.Message,
                fix, intent, procedures,
                $"Rejected {kind}: {exception.Code}.");
        }
        catch (ArgumentException exception)
        {
            var code = application ? "INVALID_APPLICATION" : "INVALID_SOURCE";
            return await RecordedFailureAsync(log, decision, kind, code, exception.Message,
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                $"Rejected invalid {kind} input.");
        }
        catch (Exception exception)
        {
            return await RecordedFailureAsync(log, decision, kind, "REGISTRY_WRITE_FAILED",
                "The registration transaction failed without changing registry state.",
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                $"Registration transaction failed: {exception.GetType().Name}.");
        }
    }

    private static async Task<ToolEnvelope> ApplicationAsync(
        IRegistryAdministrationService administration,
        PrivateOperatorAuthorizationDecision decision,
        JsonElement payload,
        string rawPayload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        RequireProperties(payload,
            "requestToken", "applicationId", "displayName", "description", "baseApplications", "expectedFingerprint");
        var requestToken = String(payload, "requestToken", 32, allowEmpty: false);
        var applicationId = ApplicationIdentifier.Parse(String(payload, "applicationId", 63, allowEmpty: false));
        var displayName = String(payload, "displayName", 200, allowEmpty: false);
        var description = String(payload, "description", 2000, allowEmpty: true);
        var bases = StringArray(payload, "baseApplications", 32, 63)
            .Select(ApplicationIdentifier.Parse).ToArray();
        var expected = NullableFingerprint(payload, "expectedFingerprint");
        var registration = new ApplicationRegistration(applicationId, displayName, description, bases);
        var context = Context(requestToken, expected, intent, procedures, decision);

        if (dryRun)
        {
            var preview = await administration.PreviewApplicationAsync(registration, context, cancellationToken);
            return ToolEnvelope.Success(
                Data(true, requestToken, preview.Outcome, registration, preview.Registration),
                preview.OperationId,
                CommitCall("system.application.register", rawPayload));
        }

        var receipt = await administration.RegisterApplicationAsync(registration, context, cancellationToken);
        return ToolEnvelope.Success(
            Data(false, requestToken, receipt.Outcome, registration, receipt.Registration),
            receipt.OperationId,
            $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(applicationId.Value)})");
    }

    private static async Task<ToolEnvelope> SourceAsync(
        IRegistryAdministrationService administration,
        PrivateOperatorAuthorizationDecision decision,
        JsonElement payload,
        string rawPayload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        RequireProperties(payload,
            "requestToken", "applicationId", "sourceId", "allowedRootId", "relativePathOrGlob",
            "trust", "precedence", "logicalIdentity", "expectedFingerprint");
        var requestToken = String(payload, "requestToken", 32, allowEmpty: false);
        var applicationId = ApplicationIdentifier.Parse(String(payload, "applicationId", 63, allowEmpty: false));
        var sourceId = String(payload, "sourceId", 200, allowEmpty: false);
        var allowedRootId = String(payload, "allowedRootId", 200, allowEmpty: false);
        var relativePath = String(payload, "relativePathOrGlob", 1000, allowEmpty: false);
        var trustText = String(payload, "trust", 9, allowEmpty: false);
        var trust = trustText switch
        {
            "trusted" => SourceTrust.Trusted,
            "untrusted" => SourceTrust.Untrusted,
            _ => throw Invalid("trust must be 'trusted' or 'untrusted'.")
        };
        var precedenceElement = payload.GetProperty("precedence");
        if (precedenceElement.ValueKind != JsonValueKind.Number
            || !precedenceElement.TryGetInt32(out var precedence)
            || precedence is < -1_000_000 or > 1_000_000)
            throw Invalid("precedence must be an integer from -1000000 through 1000000.");
        var logicalIdentity = String(payload, "logicalIdentity", 200, allowEmpty: false);
        var expected = NullableFingerprint(payload, "expectedFingerprint");
        var registration = new SourceRegistration(applicationId, sourceId, allowedRootId, relativePath,
            trust, precedence, logicalIdentity);
        var context = Context(requestToken, expected, intent, procedures, decision);

        if (dryRun)
        {
            var preview = await administration.PreviewSourceAsync(registration, context, cancellationToken);
            return ToolEnvelope.Success(
                Data(true, requestToken, preview.Outcome, preview.Registration, preview.Fingerprint),
                preview.OperationId,
                CommitCall("system.source.register", rawPayload));
        }

        var receipt = await administration.RegisterSourceAsync(registration, context, cancellationToken);
        return ToolEnvelope.Success(
            Data(false, requestToken, receipt.Outcome, receipt.Registration, receipt.Fingerprint),
            receipt.OperationId,
            $"query(kind: \"system.sources\", applicationId: {JsonSerializer.Serialize(applicationId.Value)}, id: {JsonSerializer.Serialize(sourceId)})");
    }

    private static object Data(
        bool dryRun,
        string requestToken,
        string outcome,
        ApplicationRegistration registration,
        ApplicationRevision revision) => new
        {
            DryRun = dryRun,
            RequestToken = requestToken,
            Outcome = outcome,
            Application = new
            {
                Id = registration.Id.Value,
                registration.DisplayName,
                registration.Description,
                Revision = revision.Revision,
                revision.Fingerprint,
                BaseApplications = registration.BaseApplications.Select(value => value.Value).ToArray()
            }
        };

    private static object Data(
        bool dryRun,
        string requestToken,
        string outcome,
        SourceRegistration registration,
        string fingerprint) => new
        {
            DryRun = dryRun,
            RequestToken = requestToken,
            Outcome = outcome,
            Source = new
            {
                ApplicationId = registration.ApplicationId.Value,
                registration.SourceId,
                registration.AllowedRootId,
                registration.RelativePathOrGlob,
                Trust = registration.Trust.ToString().ToLowerInvariant(),
                registration.Precedence,
                registration.LogicalIdentity,
                Fingerprint = fingerprint
            }
        };

    private static RegistryAdministrationContext Context(
        string token,
        string? expected,
        string intent,
        string[]? procedures,
        PrivateOperatorAuthorizationDecision decision) => new(
            token, expected, intent, Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);

    private static void RequireProperties(JsonElement payload, params string[] required)
    {
        var names = payload.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Length != required.Length
            || names.Except(required, StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"payload must contain exactly: {string.Join(", ", required)}.");
    }

    private static string String(JsonElement payload, string name, int maximum, bool allowEmpty)
    {
        var element = payload.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String)
            throw Invalid($"{name} must be a string.");
        var value = element.GetString() ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximum)
            throw Invalid($"{name} must be {(allowEmpty ? "at most" : "from 1 through")} {maximum} characters.");
        return value;
    }

    private static IReadOnlyList<string> StringArray(JsonElement payload, string name, int maximumItems, int maximumLength)
    {
        var element = payload.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maximumItems)
            throw Invalid($"{name} must be an array with at most {maximumItems} items.");
        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())
                || item.GetString()!.Length > maximumLength)
                throw Invalid($"Every {name} item must be a nonblank string of at most {maximumLength} characters.");
            values.Add(item.GetString()!);
        }
        return values;
    }

    private static string? NullableFingerprint(JsonElement payload, string name)
    {
        var element = payload.GetProperty(name);
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String) throw Invalid($"{name} must be null or a string.");
        return element.GetString();
    }

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Modify)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Modify,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static Task<ToolEnvelope> RecordedFailureAsync(
        IOperationLog log,
        PrivateOperatorAuthorizationDecision decision,
        string kind,
        string code,
        string why,
        string fix,
        string intent,
        string[]? procedures,
        string summary) => ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures,
            () => Task.FromResult(new ToolOutcome(null, summary, [fix], new(code, why, fix),
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence))),
            consumesReadEvidence: false);

    private static RegistryAdministrationException Invalid(string message) => new("INVALID_PAYLOAD", message);
    private static string CommitCall(string kind, string payload) =>
        $"commit(kind: {JsonSerializer.Serialize(kind)}, payload: {JsonSerializer.Serialize(payload)})";
    private static string CommitCall(string kind, string payload, bool dryRun) => dryRun
        ? $"commit(kind: {JsonSerializer.Serialize(kind)}, payload: {JsonSerializer.Serialize(payload)}, dryRun: true)"
        : CommitCall(kind, payload);
}
