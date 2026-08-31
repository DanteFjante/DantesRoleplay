using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemDependencyHandler
{
    public Task<ToolEnvelope> InspectAsync(
        IProjectionImpactService? impacts,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string? id,
        bool? transitive,
        int? limit) => ToolRunner.RunAsync(log, "query", () => Task.FromResult(
            Inspect(impacts, authorization, applicationId, id, transitive, limit)));

    private static ToolOutcome Inspect(
        IProjectionImpactService? impacts,
        IPrivateOperatorRequestAuthorizer? authorization,
        string? applicationId,
        string? id,
        bool? transitive,
        int? limit)
    {
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return Fail(decision, decision.Code,
                "Private-operator authentication is required before dependency inspection.",
                "query(kind: \"capabilities\")",
                "Denied dependency inspection before identifier parsing or registry access.");
        if (impacts is null)
            return Fail(decision, "DEPENDENCIES_UNAVAILABLE", "Dependency inspection is not configured.",
                "query(kind: \"capabilities\")", "Dependency inspection was unavailable.");
        var size = limit ?? 100;
        if (size is < 1 or > 250)
            return Fail(decision, "INVALID_PAYLOAD", "limit must be from 1 through 250.",
                Call(applicationId, id, transitive ?? true, 100), "Rejected an invalid dependency limit.");

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
            var report = impacts.Analyze(app, id, transitive ?? true);
            return Ok(decision, Describe(report, size),
                id is null
                    ? $"Returned the declared dependency inventory for '{app.Value}'."
                    : $"Returned declared dependents of '{id}'.",
                Call(app.Value, null, true, size));
        }
        catch (ProjectionImpactException exception)
        {
            return Fail(decision, exception.Code, exception.Message,
                Call(app.Value, null, true, size), "The dependency target could not be inspected.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail(decision, "DEPENDENCIES_FAILED",
                "Dependency inspection failed without changing application state.",
                Call(app.Value, id, transitive ?? true, size),
                "Dependency inspection failed without disclosing internal details.");
        }
    }

    private static object Describe(ProjectionImpactReport report, int limit)
    {
        var inventory = report.Root is null;
        return new
        {
            ApplicationId = report.ApplicationId.Value,
            report.GraphFingerprint,
            report.Root,
            report.Transitive,
            Coverage = new
            {
                Indexed = new[] { "component-field", "projection" },
                Deferred = new[] { "mechanic", "procedure", "event", "subscription", "catalog" },
                Complete = false
            },
            Counts = new
            {
                Nodes = report.Nodes.Count,
                Edges = report.Edges.Count,
                Dependents = report.Dependents.Count
            },
            Nodes = inventory ? report.Nodes.Take(limit).ToArray() : [],
            Edges = inventory ? report.Edges.Take(limit).ToArray() : [],
            Dependents = report.Dependents.Take(limit).ToArray(),
            Limit = limit,
            Truncated = inventory
                ? report.Nodes.Count > limit || report.Edges.Count > limit
                : report.Dependents.Count > limit
        };
    }

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

    private static string Call(string? applicationId, string? id, bool transitive, int limit) =>
        $"query(kind: \"system.dependencies\", applicationId: {JsonSerializer.Serialize(applicationId ?? "example")}, "
        + $"id: {(id is null ? "null" : JsonSerializer.Serialize(id))}, transitive: {transitive.ToString().ToLowerInvariant()}, limit: {limit})";
}
