using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Authenticated read-only protocol adapter over immutable application/source registries.</summary>
internal sealed class SystemRegistryQueryHandler
{
    public Task<ToolEnvelope> ApplicationsAsync(
        ISystemCapabilityCatalog? capabilities,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        int? limit,
        CancellationToken cancellationToken = default) => ToolRunner.RunAsync(log, "query", () =>
            ExecuteApplicationsAsync(capabilities, authorization, applicationId, limit, cancellationToken));

    private static async Task<ToolOutcome> ExecuteApplicationsAsync(
        ISystemCapabilityCatalog? capabilities,
        IPrivateOperatorRequestAuthorizer? authorization,
        string? applicationId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed) return Denied(decision, "system.applications");
        if (capabilities is null)
            return Fail(decision, "SYSTEM_CAPABILITY_UNAVAILABLE", "System capability dispatch is not configured.",
                Capabilities, "System application capability dispatch was unavailable.");
        var size = limit ?? 50;
        var normalizedApplicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId;
        var input = JsonSerializer.Serialize(new { applicationId = normalizedApplicationId, limit = size });
        var result = await capabilities.ReadAsync(
            SystemCapabilityIds.Applications,
            input,
            SystemCapabilityInvocationContext.FromAuthorization(decision.Evidence),
            cancellationToken);
        if (!result.Ok || result.Data is null)
        {
            var error = result.Error ?? new SystemCapabilityError(
                "SYSTEM_CAPABILITY_UNAVAILABLE", "System capability dispatch is unavailable.", "", []);
            var code = error.Code == "SYSTEM_CAPABILITY_INPUT_INVALID" ? "INVALID_PAYLOAD" : error.Code;
            var fix = code == "APPLICATION_UNKNOWN" ? ApplicationsCall(null, Math.Clamp(size, 1, 100)) : Capabilities;
            return new ToolOutcome(
                null,
                code == "APPLICATION_UNKNOWN"
                    ? "No registered application matched the exact identifier."
                    : "System application capability dispatch failed.",
                [fix],
                new ToolError(code, error.Message, fix),
                GuardEvidenceJson: JsonSerializer.Serialize(result.AuthorizationEvidence));
        }

        var data = result.Data.Value;
        if (normalizedApplicationId is not null)
        {
            var application = data.GetProperty("application").Clone();
            var stateSpaces = data.GetProperty("stateSpaces").Clone();
            return new ToolOutcome(
                new { Application = application, StateSpaces = stateSpaces },
                $"Returned registered application '{normalizedApplicationId}'.",
                [SourcesCall(normalizedApplicationId, null, 50)],
                GuardEvidenceJson: JsonSerializer.Serialize(result.AuthorizationEvidence));
        }

        var applications = data.GetProperty("applications").Clone();
        var first = applications.ValueKind == JsonValueKind.Array && applications.GetArrayLength() > 0
            ? applications[0].GetProperty("id").GetString()
            : null;
        return new ToolOutcome(
            new { Applications = applications, Limit = size },
            $"Returned {applications.GetArrayLength()} registered application(s).",
            [first is null ? Capabilities : ApplicationsCall(first, size)],
            GuardEvidenceJson: JsonSerializer.Serialize(result.AuthorizationEvidence));
    }

    public Task<ToolEnvelope> SourcesAsync(
        IApplicationRegistry? applications,
        ISourceRegistry? sources,
        ISourceScanReceiptStore? scans,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string? sourceId,
        int? limit) => ToolRunner.RunAsync(log, "query", () => Task.FromResult(
            ExecuteSources(applications, sources, scans, authorization, applicationId, sourceId, limit)));

    private static ToolOutcome ExecuteSources(
        IApplicationRegistry? applications,
        ISourceRegistry? sources,
        ISourceScanReceiptStore? scans,
        IPrivateOperatorRequestAuthorizer? authorization,
        string? applicationId,
        string? sourceId,
        int? limit)
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed) return Denied(decision, "system.sources");
        if (applications is null || sources is null || scans is null)
            return Fail(decision, "REGISTRY_UNAVAILABLE", "Application/source registries are not configured.",
                Capabilities, "Source registry was unavailable.");
        var size = limit ?? 50;
        if (size is < 1 or > 100) return Invalid(decision, "limit must be from 1 through 100.");
        ApplicationIdentifier app;
        try { app = ApplicationIdentifier.Parse(applicationId ?? ""); }
        catch (ArgumentException)
        {
            return Fail(decision, "INVALID_APPLICATION", "applicationId is required and must be a valid non-system application identifier.",
                ApplicationsCall(null, size), "Rejected an invalid application identifier.");
        }
        if (applications.Get(app) is null)
            return Fail(decision, "APPLICATION_UNKNOWN", "The requested application is not registered.",
                ApplicationsCall(null, size), "No registered application matched the exact identifier.");
        if (sourceId is { Length: > 200 } || sourceId is not null && string.IsNullOrWhiteSpace(sourceId))
            return Invalid(decision, "id must be a nonblank source identifier of at most 200 characters.");

        if (sourceId is not null)
        {
            var source = sources.Get(app, sourceId);
            if (source is null)
                return Fail(decision, "SOURCE_UNKNOWN", "The requested source is not registered for this application.",
                    SourcesCall(app.Value, null, size), "No registered source matched the exact identifier.");
            return Ok(decision, new { Source = Describe(source, scans.Latest(app, source.SourceId)) },
                $"Returned registered source '{source.SourceId}'.", SourcesCall(app.Value, null, size));
        }

        var registrations = sources.List(app, size);
        var values = registrations
            .Select(source => Describe(source, scans.Latest(app, source.SourceId))).ToArray();
        var next = values.Length == 0 ? ApplicationsCall(app.Value, size) : SourcesCall(app.Value, registrations[0].SourceId, size);
        return Ok(decision, new { ApplicationId = app.Value, Sources = values, Limit = size },
            $"Returned {values.Length} registered source(s) for '{app.Value}'.", next);
    }

    private static object Describe(SourceRegistration source, SourceScanReceipt? latestScan) => new
    {
        ApplicationId = source.ApplicationId.Value,
        source.SourceId,
        source.AllowedRootId,
        source.RelativePathOrGlob,
        Trust = source.Trust.ToString().ToLowerInvariant(),
        source.Precedence,
        source.LogicalIdentity,
        Fingerprint = SourceRegistrationFingerprint.Compute(source),
        LatestScan = latestScan is null ? null : new
        {
            latestScan.Generation,
            Status = latestScan.Status.ToString().ToLowerInvariant(),
            latestScan.ContentFingerprint,
            latestScan.RecordedAtUtc
        }
    };

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Read)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Read,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static ToolOutcome Denied(PrivateOperatorAuthorizationDecision decision, string kind) =>
        Fail(decision, decision.Code, "Private-operator authentication is required before administrative discovery.",
            Capabilities, $"Denied unauthorized {kind} before registry lookup.");

    private static ToolOutcome Invalid(PrivateOperatorAuthorizationDecision decision, string why) =>
        Fail(decision, "INVALID_PAYLOAD", why, Capabilities, "Rejected malformed administrative discovery input.");

    private static ToolOutcome Ok(
        PrivateOperatorAuthorizationDecision decision,
        object data,
        string summary,
        params string[] nextSteps) =>
        new(data, summary, nextSteps, GuardEvidenceJson: Evidence(decision));

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string why,
        string fix,
        string summary) =>
        new(null, summary, [fix], new(code, why, fix), GuardEvidenceJson: Evidence(decision));

    private static string Evidence(PrivateOperatorAuthorizationDecision decision) =>
        JsonSerializer.Serialize(decision.Evidence);

    private const string Capabilities = "query(kind: \"capabilities\")";
    private static string ApplicationsCall(string? app, int limit) => app is null
        ? $"query(kind: \"system.applications\", limit: {limit})"
        : $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(app)}, limit: {limit})";
    private static string SourcesCall(string app, string? source, int limit) => source is null
        ? $"query(kind: \"system.sources\", applicationId: {JsonSerializer.Serialize(app)}, limit: {limit})"
        : $"query(kind: \"system.sources\", applicationId: {JsonSerializer.Serialize(app)}, id: {JsonSerializer.Serialize(source)}, limit: {limit})";
}
