using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemApplicationReadinessHandler
{
    public Task<ToolEnvelope> ReadAsync(
        ApplicationReadinessService? readiness,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(log, "query", async () =>
    {
        var decision = authorization?.Authorize(PrivateOperatorCapability.Read)
            ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
                PrivateOperatorCapability.Read,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "mcp-request"));
        if (!decision.Allowed)
            return Fail(decision, decision.Code,
                "Private-operator authentication is required before application readiness inspection.",
                "query(kind: \"capabilities\")",
                "Denied readiness inspection before reading application state.");
        if (readiness is null)
            return Fail(decision, "APPLICATION_READINESS_UNAVAILABLE",
                "Application readiness inspection is not configured in this host.",
                "query(kind: \"capabilities\")",
                "Application readiness inspection was unavailable.");

        try
        {
            var report = await readiness.ReadAsync(applicationId ?? string.Empty, cancellationToken);
            return new(report,
                $"Application '{report.ApplicationId}' readiness is {report.Status}; " +
                $"{report.Checks.Count(value => value.Status == "ready")} of {report.Checks.Count} checks are ready.",
                [Call(report.ApplicationId)],
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        }
        catch (ApplicationReadinessException exception)
        {
            return Fail(decision, exception.Code, exception.Message,
                "query(kind: \"system.applications\")", "Rejected an invalid readiness target.");
        }
    });

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string why,
        string fix,
        string summary) => new(null, summary, [fix], new(code, why, fix),
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static string Call(string applicationId) =>
        $"query(kind: \"system.application-readiness\", applicationId: {JsonSerializer.Serialize(applicationId)})";
}
