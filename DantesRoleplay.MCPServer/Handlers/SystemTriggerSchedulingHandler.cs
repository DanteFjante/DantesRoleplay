using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Operations;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Private, authorization-first adapters over the shared trigger administration service.</summary>
internal sealed class SystemTriggerSchedulingHandler
{
    public Task<ToolEnvelope> QueryAsync(
        ITriggerSchedulingAdministrationService? administration,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string? resource,
        string? id,
        int? limit,
        CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "query", async () =>
        {
            var decision = Authorize(authorization, PrivateOperatorCapability.TriggerAdministrationRead);
            if (!decision.Allowed) return Denied(decision);
            if (administration is null) return Unavailable();
            try
            {
                var application = string.IsNullOrWhiteSpace(applicationId)
                    ? null : ApplicationIdentifier.Parse(applicationId);
                var result = await administration.QueryAsync(TriggerSchedulingAdministrationQuery.Create(
                    application, resource, id, limit ?? 50), cancellationToken);
                return ToolOutcome.Ok(result, "Returned a bounded safe trigger-scheduling projection.");
            }
            catch (Exception exception) when (RequestFailure(exception))
            { return Failure(exception); }
        });

    public async Task<ToolEnvelope> CommitAsync(
        ITriggerSchedulingAdministrationService? administration,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var decision = Authorize(authorization, PrivateOperatorCapability.TriggerAdministrationWrite);
        if (!decision.Allowed)
            return await RecordFailureAsync(log, decision, decision.Code,
                "A verified private operator is required for trigger administration.", intent, procedures);
        if (administration is null)
            return await RecordFailureAsync(log, decision, "TRIGGER_ADMIN_UNAVAILABLE",
                "Trigger scheduling administration is not configured.", intent, procedures);
        try
        {
            var command = TriggerSchedulingAdministrationCommand.Parse(payload);
            var context = new TriggerSchedulingAdministrationContext(intent,
                Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);
            var result = dryRun
                ? await administration.PreviewAsync(command, context, cancellationToken)
                : await administration.CommitAsync(command, context, cancellationToken);
            var fix = dryRun
                ? $"commit(kind: \"system.trigger-scheduling\", payload: {JsonSerializer.Serialize(payload)})"
                : $"query(kind: \"system.trigger-scheduling\", applicationId: {JsonSerializer.Serialize(command.ApplicationId.Value)}, resource: \"overview\")";
            return ToolEnvelope.Success(new { DryRun = dryRun, Result = result }, result.OperationId, fix);
        }
        catch (Exception exception) when (RequestFailure(exception))
        {
            var code = exception switch
            {
                TriggerSchedulingAdministrationException administrationException => administrationException.Code,
                TriggerSchedulingContractException contractException => contractException.Code,
                _ => "TRIGGER_ADMIN_PAYLOAD"
            };
            return await RecordFailureAsync(log, decision, code, exception.Message, intent, procedures);
        }
    }

    private static PrivateOperatorAuthorizationDecision Authorize(
        IPrivateOperatorRequestAuthorizer? authorization, PrivateOperatorCapability capability) =>
        authorization?.Authorize(capability) ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"), capability,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "mcp-request"));

    private static ToolOutcome Denied(PrivateOperatorAuthorizationDecision decision) => new(
        null, "Rejected unauthorized trigger administration.",
        ["Call this operation through the local private host."],
        new(decision.Code, "A verified private operator is required.",
            "Call this operation through the local private host."),
        GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static ToolOutcome Unavailable() => ToolOutcome.Fail("TRIGGER_ADMIN_UNAVAILABLE",
        "Trigger scheduling administration is not configured.", "query(kind: \"capabilities\")",
        "Trigger scheduling administration was unavailable.");

    private static bool RequestFailure(Exception exception) => exception is
        TriggerSchedulingAdministrationException or TriggerSchedulingContractException or
        ArgumentException or JsonException or InvalidOperationException;

    private static ToolOutcome Failure(Exception exception) => ToolOutcome.Fail(
        exception is TriggerSchedulingAdministrationException administration ? administration.Code
            : exception is TriggerSchedulingContractException contract ? contract.Code : "TRIGGER_ADMIN_QUERY",
        exception.Message, "query(kind: \"capabilities\")", "Rejected invalid trigger administration query.");

    private static Task<ToolEnvelope> RecordFailureAsync(IOperationLog log,
        PrivateOperatorAuthorizationDecision decision, string code, string message,
        string intent, string[]? procedures) => ToolRunner.RunAsync(log, "commit", intent,
            "commit:system.trigger-scheduling", procedures,
            () => Task.FromResult(new ToolOutcome(null, "Trigger administration was not applied.",
                ["query(kind: \"capabilities\")"],
                new(code, message, "query(kind: \"capabilities\")"),
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence))),
            consumesReadEvidence: false);
}
