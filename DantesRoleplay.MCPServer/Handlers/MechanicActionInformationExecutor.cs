using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Information;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Adapter from a declared neutral action contract to the existing ruleset-neutral action runner.</summary>
public sealed class MechanicActionInformationExecutor(IActionRunner actions) : IInformationActionExecutor
{
    public const string ExecutorId = "kernel.mechanic-action";
    public string Id => ExecutorId;

    public async Task<InformationActionExecutionResult> ExecuteAsync(InformationActionContract contract, string inputJson, CancellationToken cancellationToken = default)
    {
        ActionPayload? input;
        try { input = JsonSerializer.Deserialize<ActionPayload>(inputJson); }
        catch (JsonException) { return InformationActionExecutionResult.Rejected("INFORMATION_ACTION_INPUT_INVALID", "The action input could not be read."); }
        if (input is null || string.IsNullOrWhiteSpace(input.Intent)) return InformationActionExecutionResult.Rejected("INFORMATION_ACTION_INPUT_INVALID", "The action contract requires an intent.");
        var result = await actions.RunAsync(new ActionRequest
        {
            Intent = input.Intent,
            RoleEntityIds = input.RoleEntityIds ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Input = input.Input ?? "{}",
            Scope = input.Scope,
            Seed = input.Seed,
            ProceduresUsed = ["procedure.information.action"]
        }, cancellationToken);
        return result.Error is not null
            ? InformationActionExecutionResult.Rejected(result.Error.Code, result.Error.Why)
            : new InformationActionExecutionResult("executed", new { contract.Id, result.Mechanic, result.Output.Narration, result.Output.Data, result.Output.Effects, result.AppliedCount, result.AffectedEntityIds, result.OperationId, result.Seed });
    }

    private sealed class ActionPayload
    {
        public string? Intent { get; init; }
        public Dictionary<string, string>? RoleEntityIds { get; init; }
        public string? Input { get; init; }
        public string? Scope { get; init; }
        public long? Seed { get; init; }
    }
}
