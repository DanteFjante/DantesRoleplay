using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

internal sealed class SystemApplicationActivationTools
{
    public async Task<ToolEnvelope> ActivateAsync(
        IApplicationActivationService? activations,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = "system.application.activate";
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return await FailureAsync(log, decision, kind, decision.Code,
                "Private-operator authentication is required before application activation.",
                "query(kind: \"capabilities\")", intent, procedures,
                "Denied application activation before payload parsing or preview access.");
        if (activations is null)
            return await FailureAsync(log, decision, kind, "ACTIVATION_UNAVAILABLE",
                "Application activation is not configured.", "query(kind: \"capabilities\")",
                intent, procedures, "Application activation was unavailable.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("payload must be a JSON object.");
            var root = document.RootElement;
            RequireProperties(root,
                ["requestToken", "applicationId", "previewFingerprint", "expectedActiveFingerprint"],
                ["sourceIds"]);
            var token = String(root, "requestToken", 32);
            var app = ApplicationIdentifier.Parse(String(root, "applicationId", 63));
            var previewFingerprint = String(root, "previewFingerprint", 64);
            var expected = NullableString(root, "expectedActiveFingerprint", 64);
            var sourceIds = root.TryGetProperty("sourceIds", out var selected)
                ? SourceIds(selected) : null;
            var request = new ApplicationActivationRequest(app, previewFingerprint, expected, sourceIds);
            var context = new ApplicationActivationContext(token, intent,
                Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);

            if (dryRun)
            {
                var preview = await activations.PreviewAsync(request, context, cancellationToken);
                return ToolEnvelope.Success(Data(true, token, preview.Outcome, preview.Activation),
                    preview.OperationId, CommitCall(payload));
            }

            var receipt = await activations.ActivateAsync(request, context, cancellationToken);
            return ToolEnvelope.Success(Data(false, token, receipt.Outcome, receipt.Activation),
                receipt.OperationId,
                $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(app.Value)})");
        }
        catch (JsonException)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD",
                "payload must be valid JSON with the documented closed activation shape.",
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected malformed application activation payload.");
        }
        catch (ApplicationActivationException exception)
        {
            var fix = exception.Code switch
            {
                "DRY_RUN_REQUIRED" or "DRY_RUN_STALE" => CommitCall(payload, dryRun: true),
                "PREVIEW_STALE" or "PREVIEW_INVALID" => "query(kind: \"system.application-preview\", applicationId: \"...\")",
                "ACTIVATION_STALE" => "query(kind: \"system.applications\", applicationId: \"...\")",
                _ => VerbSurface.CommitCall(kind, dryRun: true)
            };
            return await FailureAsync(log, decision, kind, exception.Code, exception.Message,
                fix, intent, procedures, $"Rejected application activation: {exception.Code}.");
        }
        catch (ArgumentException exception)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD", exception.Message,
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected invalid application activation input.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailureAsync(log, decision, kind, "ACTIVATION_FAILED",
                "The activation transaction failed without changing active application state.",
                VerbSurface.CommitCall(kind, dryRun: true), intent, procedures,
                "Application activation failed without disclosing internal details.");
        }
    }

    private static object Data(bool dryRun, string requestToken, string outcome, ActiveApplicationManifest activation) => new
    {
        DryRun = dryRun,
        RequestToken = requestToken,
        Outcome = outcome,
        Activation = Summary(activation)
    };

    internal static object Summary(ActiveApplicationManifest activation) => new
    {
        ApplicationId = activation.ApplicationId.Value,
        activation.ActivationRevision,
        activation.ApplicationRevision,
        activation.ApplicationFingerprint,
        activation.PreviewFingerprint,
        activation.ScannedDocumentsFingerprint,
        activation.CandidateManifestFingerprint,
        activation.DependencyGraphFingerprint,
        activation.ActivationFingerprint,
        activation.DependencyCoverageVersion,
        activation.DependencyCoverageComplete,
        SourceCount = activation.Sources.Count,
        SourceIds = activation.Sources.Select(value => value.SourceId).ToArray(),
        WinnerCount = activation.Winners.Count,
        activation.ActivatedAtUtc
    };

    private static void RequireProperties(
        JsonElement payload,
        IReadOnlyList<string> required,
        IReadOnlyList<string> optional)
    {
        var names = payload.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Except(required.Concat(optional), StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"payload must contain {string.Join(", ", required)} and only optional fields: {string.Join(", ", optional)}.");
    }

    private static IReadOnlyList<string> SourceIds(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw Invalid("sourceIds must be an array.");
        var values = element.EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())
                || value.GetString()!.Length > 200)
                throw Invalid("Every sourceIds entry must be a nonblank string of at most 200 characters.");
            return value.GetString()!;
        }).ToArray();
        if (values.Length is < 1 or > 100
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid("sourceIds must contain 1 through 100 unique source IDs.");
        return Array.AsReadOnly(values);
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

    private static ApplicationActivationException Invalid(string message) => new("INVALID_PAYLOAD", message);
    private static string CommitCall(string payload, bool dryRun = false) =>
        $"commit(kind: \"system.application.activate\", payload: {JsonSerializer.Serialize(payload)}"
        + (dryRun ? ", dryRun: true)" : ")");
}
