using System.ComponentModel;
using DantesRoleplay.Actions;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The handler behind <c>commit(kind: "action")</c>. Not registered as an MCP tool
/// (VERB_MIGRATION.md D5).
///
/// All selection, projection, sandbox execution, effect handling and audit work belongs to
/// <see cref="IActionRunner"/>; this class only translates the payload and the result into the
/// standard envelope.
/// </summary>
[McpServerToolType]
public sealed class ActionTools
{
    [McpServerTool(Name = "run_action")]
    [Description(
        "Run an action through the active JavaScript mechanics. Give the player's free-text " +
        "intent and an explicit roleEntityIds map from the mechanic's role names to entity ids. " +
        "The first ranked active mechanic is selected, its declared projection is materialised, " +
        "the sandbox runs without CLR access, and its effects are dry-run validated and applied " +
        "atomically. Read procedure.mechanic.run first. On success, call " +
        "query(kind: \"entities\", ids: [...]) with the returned affected ids to confirm the " +
        "resulting world state.")]
    public async Task<ToolEnvelope> RunActionAsync(
        IActionRunner runner,
        [Description("What the actor is trying to do, in the player's words.")] string intent,
        [Description("Explicit mechanic role names to permanent entity ids. Do not infer role names.")]
        Dictionary<string, string>? roleEntityIds = null,
        [Description("JSON object handed unchanged to the mechanic as ctx.input. Defaults to {}.")]
        string input = "{}",
        [Description("Optional ruleset scope. Shared mechanics remain eligible.")] string? scope = null,
        [Description("Optional replay seed. Omit to generate one and return it.")] long? seed = null,
        [Description("Ids of procedures you consulted before this action.")] string[]? proceduresUsed = null,
        CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(
            new ActionRequest
            {
                Intent = intent,
                RoleEntityIds = roleEntityIds ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Input = input,
                Scope = scope,
                Seed = seed,
                ProceduresUsed = proceduresUsed ?? []
            },
            cancellationToken);

        if (result.Error is not null)
        {
            return ToolEnvelope.Failure(
                result.Error.Code,
                result.Error.Why,
                result.Error.Fix,
                result.OperationId);
        }

        return ToolEnvelope.Success(
            new
            {
                result.Candidates,
                result.Mechanic,
                result.Projection,
                result.Seed,
                result.Output.Narration,
                result.Output.Data,
                result.Output.Effects,
                result.AppliedCount,
                result.AffectedEntityIds,
                result.Log,
                result.LimitHit,
                result.ElapsedMilliseconds
            },
            result.OperationId,
            [.. result.NextSteps]);
    }
}
