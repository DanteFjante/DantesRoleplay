using System.Text.Json;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

internal sealed class SystemApplicationPreviewTools
{
    public Task<ToolEnvelope> PreviewAsync(
        IApplicationPreviewService? previews,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string[]? sourceIds,
        int? limit,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(log, "query", async () =>
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return Fail(decision, decision.Code,
                "Private-operator authentication is required before application preview.",
                "query(kind: \"capabilities\")",
                "Denied application preview before identifier parsing or scanning.");
        if (previews is null)
            return Fail(decision, "PREVIEW_UNAVAILABLE", "Application preview is not configured.",
                "query(kind: \"capabilities\")", "Application preview was unavailable.");
        var size = limit ?? 100;
        if (size is < 1 or > 250)
            return Fail(decision, "INVALID_PAYLOAD", "limit must be from 1 through 250.",
                Call(applicationId, sourceIds, 100), "Rejected an invalid application-preview limit.");

        ApplicationIdentifier app;
        try { app = ApplicationIdentifier.Parse(applicationId ?? string.Empty); }
        catch (ArgumentException)
        {
            return Fail(decision, "INVALID_APPLICATION",
                "applicationId is required and must be a valid non-system application identifier.",
                "query(kind: \"system.applications\")", "Rejected an invalid application identifier.");
        }

        try
        {
            var preview = sourceIds is null
                ? await previews.PreviewAsync(app, cancellationToken)
                : await previews.PreviewAsync(app, sourceIds, cancellationToken);
            return Ok(decision, Describe(preview, size),
                $"Previewed application '{app.Value}' from its registered sources.",
                $"query(kind: \"system.sources\", applicationId: {JsonSerializer.Serialize(app.Value)})");
        }
        catch (ApplicationPreviewException exception)
        {
            return Fail(decision, exception.Code, exception.Message,
                "query(kind: \"system.applications\")", "Application preview target was unavailable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail(decision, "PREVIEW_FAILED",
                "Application preview failed without changing application state.",
                Call(app.Value, sourceIds, size), $"Application preview failed: {exception.GetType().Name}.");
        }
    });

    private static object Describe(ApplicationPreviewResult preview, int limit) => new
    {
        ApplicationId = preview.ApplicationId.Value,
        preview.ApplicationRevision,
        preview.ApplicationFingerprint,
        preview.ScannedDocumentsFingerprint,
        preview.CandidateManifestFingerprint,
        preview.PreviewFingerprint,
        preview.IsValid,
        Counts = new
        {
            Sources = preview.Sources.Count,
            Winners = preview.Winners.Count,
            Shadows = preview.Shadows.Count,
            Problems = preview.Problems.Count
        },
        Sources = preview.Sources.Take(limit).ToArray(),
        Winners = preview.Winners.Take(limit).ToArray(),
        Shadows = preview.Shadows.Take(limit).ToArray(),
        Problems = preview.Problems.Take(limit).ToArray(),
        Limit = limit,
        Truncated = preview.Sources.Count > limit || preview.Winners.Count > limit
            || preview.Shadows.Count > limit || preview.Problems.Count > limit
    };

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Read)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Read,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static ToolOutcome Ok(
        PrivateOperatorAuthorizationDecision decision,
        object data,
        string summary,
        params string[] nextSteps) =>
        new(data, summary, nextSteps, GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string why,
        string fix,
        string summary) =>
        new(null, summary, [fix], new(code, why, fix),
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static string Call(string? applicationId, string[]? sourceIds, int limit) =>
        $"query(kind: \"system.application-preview\", applicationId: {JsonSerializer.Serialize(applicationId ?? "example")}, sourceIds: {JsonSerializer.Serialize(sourceIds)}, limit: {limit})";
}
