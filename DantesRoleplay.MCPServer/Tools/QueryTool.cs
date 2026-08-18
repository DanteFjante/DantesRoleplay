using System.ComponentModel;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The read side of the three-verb MCP surface. The preserved tool classes remain the behaviour
/// implementation; this class supplies the public protocol and dispatches by the closed kind set.
///
/// The kind list is <see cref="VerbSurface.QueryKinds"/> and nothing else — the switch below is
/// asserted against it in both directions by a guard test, so this tool can never accept a kind
/// the catalog hides, nor advertise one it cannot serve.
/// </summary>
[McpServerToolType]
public sealed class QueryTool
{
    [McpServerTool(Name = "query")]
    [Description(
        "Read anything in this system. kind is one of: capabilities, procedures, world, entities, " +
        "mechanics, history. Omit id for a list or search; pass id for one record in full. When " +
        "you are unsure what a kind takes or what a commit payload looks like, call " +
        "query(kind: \"capabilities\") — it is the exact catalog. Irrelevant filters are ignored, " +
        "so exploring costs nothing. Never changes state.")]
    public async Task<ToolEnvelope> QueryAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IMechanicStore mechanics,
        IOperationLog log,
        [Description(
            "Closed kind: capabilities, procedures, world, entities, mechanics, or history.")]
        string kind,
        [Description("Full-record id for procedures, mechanics, or one entity.")] string? id = null,
        [Description("Entity ids for a full batch read.")] string[]? ids = null,
        [Description("Historical version, only when id is supplied for procedures or mechanics.")]
        int? version = null,
        [Description("Search text for procedures or mechanics.")] string? query = null,
        [Description("Entity name substring.")] string? nameQuery = null,
        [Description("Entity component definition filter.")] string? withDefinitionId = null,
        [Description("Category filter for procedures or mechanics.")] string? category = null,
        [Description("Ruleset preference for mechanics.")] string? scope = null,
        [Description("Include deprecated and archived records.")] bool includeInactive = false,
        [Description("Maximum results for procedures, entities, mechanics or history.")]
        int? limit = null,
        [Description("Number of example entities for the world query.")] int? sample = null,
        [Description("Only failed history records.")] bool failuresOnly = false,
        [Description("History tool filter.")] string? tool = null,
        [Description("History subject filter.")] string? subject = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!VerbSurface.IsQueryKind(normalizedKind))
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown query kind '{kind}'. Valid kinds: "
                    + $"{string.Join(", ", VerbSurface.QueryKindNames)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected query kind '{kind}'.")));
        }

        if (normalizedKind == "capabilities")
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Ok(
                    VerbSurface.Catalog(),
                    "Returned the full query and commit catalog.",
                    "query(kind: \"procedures\") — read the contract governing what you are about to do.",
                    "query(kind: \"world\") — see what the world already holds before adding to it.")));
        }

        using var dispatch = ToolRunner.EnterProtocol("query", normalizedKind);

        return normalizedKind switch
        {
            "procedures" when string.IsNullOrWhiteSpace(id) =>
                await new ProcedureTools().FindProceduresAsync(
                    procedures, log, query, category, includeInactive, limit, cancellationToken),
            "procedures" =>
                await new ProcedureTools().GetProcedureAsync(
                    procedures, log, id!, version, cancellationToken),
            "world" =>
                await new WorldTools().DescribeWorldAsync(
                    world, log, sample ?? 10, cancellationToken),
            "entities" =>
                await new WorldTools().GetEntitiesAsync(
                    world, log,
                    string.IsNullOrWhiteSpace(id) ? ids : [id!],
                    nameQuery,
                    withDefinitionId,
                    limit ?? 50,
                    cancellationToken),
            "mechanics" =>
                await new MechanicTools().FindMechanicsAsync(
                    mechanics, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "history" =>
                await new HistoryTool().HistoryAsync(
                    log, limit ?? 20, failuresOnly, tool, subject, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled query kind '{kind}'.")
        };
    }
}
