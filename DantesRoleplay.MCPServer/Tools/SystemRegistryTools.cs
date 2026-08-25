using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Authenticated read-only protocol adapter over immutable application/source registries.</summary>
internal sealed class SystemRegistryTools
{
    public Task<ToolEnvelope> ApplicationsAsync(
        IApplicationRegistry? applications,
        IApplicationActivationReader? activations,
        IStateSpaceAdministrationReader? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        int? limit) => ToolRunner.RunAsync(log, "query", () => Task.FromResult(
            ExecuteApplications(applications, activations, stateSpaces, authorization, applicationId, limit)));

    private static ToolOutcome ExecuteApplications(
        IApplicationRegistry? applications,
        IApplicationActivationReader? activations,
        IStateSpaceAdministrationReader? stateSpaces,
        IPrivateOperatorRequestAuthorizer? authorization,
        string? applicationId,
        int? limit)
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed) return Denied(decision, "system.applications");
        if (applications is null)
            return Fail(decision, "REGISTRY_UNAVAILABLE", "Application registry is not configured.",
                Capabilities, "Application registry was unavailable.");
        var size = limit ?? 50;
        if (size is < 1 or > 100) return Invalid(decision, "limit must be from 1 through 100.");
        try
        {
            if (!string.IsNullOrWhiteSpace(applicationId))
            {
                var id = ApplicationIdentifier.Parse(applicationId);
                var registration = applications.Describe(id);
                var revision = applications.Get(id);
                if (registration is null || revision is null)
                    return Fail(decision, "APPLICATION_UNKNOWN", "The requested application is not registered.",
                        ApplicationsCall(null, size), "No registered application matched the exact identifier.");
                return Ok(decision, new
                {
                    Application = Describe(registration, revision, activations?.Current(id)),
                    StateSpaces = stateSpaces?.List(id, size).Select(SystemStateSpaceTools.Summary).ToArray()
                },
                    $"Returned registered application '{id.Value}'.", SourcesCall(id.Value, null, 50));
            }

            var registrations = applications.List(size);
            var values = registrations
                .Select(registration => Describe(registration, applications.Get(registration.Id)!,
                    activations?.Current(registration.Id))).ToArray();
            var next = values.Length == 0 ? Capabilities : ApplicationsCall(registrations[0].Id.Value, size);
            return Ok(decision, new { Applications = values, Limit = size },
                $"Returned {values.Length} registered application(s).", next);
        }
        catch (ArgumentException)
        {
            return Fail(decision, "INVALID_APPLICATION", "applicationId is not a valid non-system application identifier.",
                ApplicationsCall(null, size), "Rejected an invalid application identifier.");
        }
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

    private static object Describe(
        ApplicationRegistration registration,
        ApplicationRevision revision,
        ActiveApplicationManifest? active) => new
    {
        Id = registration.Id.Value,
        registration.DisplayName,
        registration.Description,
        Revision = revision.Revision,
        revision.Fingerprint,
        BaseApplications = registration.BaseApplications.Select(value => value.Value).ToArray(),
        Active = active is null ? null : SystemApplicationActivationTools.Summary(active)
    };

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
