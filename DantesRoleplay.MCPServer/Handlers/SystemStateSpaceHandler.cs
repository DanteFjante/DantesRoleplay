using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.StateSpaceAdministration;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemStateSpaceHandler
{
    public async Task<ToolEnvelope> CreateAsync(
        IStateSpaceAdministrationService? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = "system.state-space.create";
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return await FailureAsync(log, decision, kind, decision.Code,
                "Private-operator authentication is required before state-space creation.",
                "query(kind: \"capabilities\")", intent, procedures,
                "Denied state-space creation before payload parsing or state access.");
        if (stateSpaces is null)
            return await FailureAsync(log, decision, kind, "STATE_SPACE_ADMINISTRATION_UNAVAILABLE",
                "State-space administration is not configured.", "query(kind: \"capabilities\")",
                intent, procedures, "State-space administration was unavailable.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("payload must be a JSON object.");
            var root = document.RootElement;
            RequireProperties(root, "requestToken", "stateSpaceId", "applicationId", "activeFingerprint", "expectedFingerprint");
            var token = String(root, "requestToken", 32);
            var stateSpaceId = String(root, "stateSpaceId", 200);
            var application = ApplicationIdentifier.Parse(String(root, "applicationId", 63));
            var activeFingerprint = String(root, "activeFingerprint", 64);
            var expected = NullableString(root, "expectedFingerprint", 64);
            var request = new StateSpaceCreationRequest(
                stateSpaceId, application, activeFingerprint, expected);
            var context = new StateSpaceCreationContext(token, intent,
                Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);

            if (dryRun)
            {
                var preview = await stateSpaces.PreviewCreateAsync(request, context, cancellationToken);
                return ToolEnvelope.Success(Data(true, token, preview.Outcome, preview.Binding),
                    preview.OperationId, CommitCall(payload));
            }

            var receipt = await stateSpaces.CreateAsync(request, context, cancellationToken);
            return ToolEnvelope.Success(Data(false, token, receipt.Outcome, receipt.Binding),
                receipt.OperationId,
                $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(application.Value)})");
        }
        catch (JsonException)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD",
                "payload must be valid JSON with the documented closed state-space creation shape.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected malformed state-space creation payload.");
        }
        catch (StateSpaceAdministrationException exception)
        {
            var fix = exception.Code switch
            {
                "DRY_RUN_REQUIRED" or "DRY_RUN_STALE" => CommitCall(payload, dryRun: true),
                "ACTIVATION_REQUIRED" or "ACTIVATION_STALE" or "APPLICATION_STALE" =>
                    "query(kind: \"system.applications\", applicationId: \"...\")",
                "STATE_SPACE_EXISTS" => "query(kind: \"system.applications\", applicationId: \"...\")",
                _ => McpVerbCatalog.CommitCall(kind, dryRun: true)
            };
            return await FailureAsync(log, decision, kind, exception.Code, exception.Message,
                fix, intent, procedures, $"Rejected state-space creation: {exception.Code}.");
        }
        catch (ArgumentException exception)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD", exception.Message,
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected invalid state-space creation input.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailureAsync(log, decision, kind, "STATE_SPACE_CREATE_FAILED",
                "The state-space transaction failed without changing runtime state.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "State-space creation failed without disclosing internal details.");
        }
    }

    public async Task<ToolEnvelope> UpgradeAsync(
        IStateSpaceAdministrationService? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = "system.state-space.upgrade";
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return await FailureAsync(log, decision, kind, decision.Code,
                "Private-operator authentication is required before state-space upgrade.",
                "query(kind: \"capabilities\")", intent, procedures,
                "Denied state-space upgrade before payload parsing or state access.");
        if (stateSpaces is null)
            return await FailureAsync(log, decision, kind, "STATE_SPACE_ADMINISTRATION_UNAVAILABLE",
                "State-space administration is not configured.", "query(kind: \"capabilities\")",
                intent, procedures, "State-space administration was unavailable.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("payload must be a JSON object.");
            var root = document.RootElement;
            RequireProperties(root, "requestToken", "stateSpaceId", "applicationId", "activeFingerprint", "expectedBindingFingerprint");
            var token = String(root, "requestToken", 32);
            var stateSpaceId = String(root, "stateSpaceId", 200);
            var application = ApplicationIdentifier.Parse(String(root, "applicationId", 63));
            var request = new StateSpaceUpgradeRequest(stateSpaceId, application,
                String(root, "activeFingerprint", 64), String(root, "expectedBindingFingerprint", 64));
            var context = new StateSpaceUpgradeContext(token, intent,
                Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);

            if (dryRun)
            {
                var preview = await stateSpaces.PreviewUpgradeAsync(request, context, cancellationToken);
                return ToolEnvelope.Success(UpgradeData(true, token, preview.Outcome,
                        preview.PreviousBinding, preview.TargetBinding, preview.Compatibility),
                    preview.OperationId, UpgradeCall(payload));
            }

            var receipt = await stateSpaces.UpgradeAsync(request, context, cancellationToken);
            return ToolEnvelope.Success(UpgradeData(false, token, receipt.Outcome,
                    receipt.PreviousBinding, receipt.Binding, receipt.Compatibility),
                receipt.OperationId,
                $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(application.Value)})");
        }
        catch (JsonException)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD",
                "payload must be valid JSON with the documented closed state-space upgrade shape.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected malformed state-space upgrade payload.");
        }
        catch (StateSpaceAdministrationException exception)
        {
            var fix = exception.Code switch
            {
                "DRY_RUN_REQUIRED" or "DRY_RUN_STALE" => UpgradeCall(payload, dryRun: true),
                "ACTIVATION_REQUIRED" or "ACTIVATION_STALE" or "APPLICATION_STALE"
                    or "BINDING_STALE" or "STATE_SPACE_ALREADY_CURRENT" =>
                    "query(kind: \"system.applications\", applicationId: \"...\")",
                "MIGRATION_REQUIRED" => "query(kind: \"capabilities\")",
                _ => McpVerbCatalog.CommitCall(kind, dryRun: true)
            };
            return await FailureAsync(log, decision, kind, exception.Code, exception.Message,
                fix, intent, procedures, $"Rejected state-space upgrade: {exception.Code}.");
        }
        catch (ArgumentException exception)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD", exception.Message,
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected invalid state-space upgrade input.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailureAsync(log, decision, kind, "STATE_SPACE_UPGRADE_FAILED",
                "The state-space upgrade transaction failed without changing runtime state.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "State-space upgrade failed without disclosing internal details.");
        }
    }

    private static object Data(
        bool dryRun,
        string requestToken,
        string outcome,
        StateSpaceBindingSummary binding) => new
    {
        DryRun = dryRun,
        RequestToken = requestToken,
        Outcome = outcome,
        Binding = Summary(binding)
    };

    private static object UpgradeData(
        bool dryRun,
        string requestToken,
        string outcome,
        StateSpaceBindingSummary previous,
        StateSpaceBindingSummary binding,
        StateSpaceCompatibilityEvidence compatibility) => new
    {
        DryRun = dryRun,
        RequestToken = requestToken,
        Outcome = outcome,
        PreviousBinding = Summary(previous),
        Binding = Summary(binding),
        Compatibility = compatibility
    };

    internal static object Summary(StateSpaceBindingSummary binding) => new
    {
        binding.StateSpaceId,
        ApplicationId = binding.ApplicationId.Value,
        binding.ApplicationRevision,
        binding.ApplicationFingerprint,
        binding.ActiveFingerprint,
        binding.BindingRevision,
        binding.BindingFingerprint,
        binding.CreatedAtUtc,
        binding.UpdatedAtUtc
    };

    private static void RequireProperties(JsonElement payload, params string[] required)
    {
        var names = payload.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Length != required.Length
            || names.Except(required, StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"payload must contain exactly: {string.Join(", ", required)}.");
    }

    private static string String(JsonElement payload, string name, int maximum)
    {
        var element = payload.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())
            || element.GetString()!.Length > maximum)
            throw Invalid($"{name} must be a nonblank string of at most {maximum} characters.");
        return element.GetString()!;
    }

    private static string? NullableString(JsonElement payload, string name, int maximum)
    {
        var element = payload.GetProperty(name);
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())
            || element.GetString()!.Length > maximum)
            throw Invalid($"{name} must be null or a nonblank string of at most {maximum} characters.");
        return element.GetString();
    }

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Modify)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Modify,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static Task<ToolEnvelope> FailureAsync(
        IOperationLog log, PrivateOperatorAuthorizationDecision decision, string kind, string code,
        string why, string fix, string intent, string[]? procedures, string summary) =>
        ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures,
            () => Task.FromResult(new ToolOutcome(null, summary, [fix], new(code, why, fix),
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence))),
            consumesReadEvidence: false);

    private static StateSpaceAdministrationException Invalid(string message) => new("INVALID_PAYLOAD", message);
    private static string CommitCall(string payload, bool dryRun = false) =>
        $"commit(kind: \"system.state-space.create\", payload: {JsonSerializer.Serialize(payload)}"
        + (dryRun ? ", dryRun: true)" : ")");
    private static string UpgradeCall(string payload, bool dryRun = false) =>
        $"commit(kind: \"system.state-space.upgrade\", payload: {JsonSerializer.Serialize(payload)}"
        + (dryRun ? ", dryRun: true)" : ")");
}
