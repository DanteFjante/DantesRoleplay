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
/// </summary>
[McpServerToolType]
public sealed class QueryTool
{
    private static readonly IReadOnlyDictionary<string, string> QueryKinds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["capabilities"] = "Protocol kinds, payload shapes, and supported parameters.",
            ["procedures"] = "Procedure summaries, or one full procedure by id and version.",
            ["world"] = "Component definitions and example entities.",
            ["entities"] = "Entities by id or search criteria.",
            ["mechanics"] = "Mechanic summaries, or one full mechanic by id and version.",
            ["history"] = "Recent operation audit records."
        };

    [McpServerTool(Name = "query")]
    [Description(
        "Read the system. Start with kind capabilities or procedures when you need to understand " +
        "an operation. The kind is one of capabilities, procedures, world, entities, mechanics, " +
        "or history. Use id for a full record where supported; irrelevant filters are ignored.")]
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
        [Description("Maximum entity or history results.")] int? limit = null,
        [Description("Number of example entities for the world query.")] int? sample = null,
        [Description("Only failed history records.")] bool failuresOnly = false,
        [Description("History tool filter.")] string? tool = null,
        [Description("History subject filter.")] string? subject = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!QueryKinds.ContainsKey(normalizedKind))
        {
            return await ToolRunner.RunAsync(log, "query", async () =>
                ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown query kind '{kind}'. Valid kinds: {string.Join(", ", QueryKinds.Keys)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected query kind '{kind}'."));
        }

        if (normalizedKind == "capabilities")
        {
            return await ToolRunner.RunAsync(log, "query", async () =>
                ToolOutcome.Ok(
                    Capabilities(),
                    "Returned the query and commit protocol capabilities.",
                    "query(kind: \"procedures\") — read the operating manual before changing anything."));
        }

        using var dispatch = ToolRunner.EnterProtocol("query", normalizedKind);

        return normalizedKind switch
        {
            "procedures" when string.IsNullOrWhiteSpace(id) =>
                await new ProcedureTools().FindProceduresAsync(
                    procedures, log, query, category, includeInactive, cancellationToken),
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
                    mechanics, log, id, version, query, category, scope, includeInactive, cancellationToken),
            "history" =>
                await new HistoryTool().HistoryAsync(
                    log, limit ?? 20, failuresOnly, tool, subject, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled query kind '{kind}'.")
        };
    }

    private static object Capabilities() =>
        new
        {
            QueryKinds,
            CommitKinds = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["procedure"] = new { SupportsDryRun = true, Contract = "procedure.system.create-feature" },
                ["component"] = new { SupportsDryRun = false, Contract = "procedure.world.change" },
                ["effects"] = new { SupportsDryRun = true, Contract = "procedure.world.change" },
                ["mechanic"] = new { SupportsDryRun = true, Contract = "procedure.mechanic.create" },
                ["action"] = new { SupportsDryRun = false, Contract = "procedure.mechanic.run" }
            },
            QueryParameters = new
            {
                id = "procedures, mechanics, or one entity",
                ids = "entities",
                version = "procedures and mechanics with id",
                query = "procedures and mechanics",
                nameQuery = "entities",
                withDefinitionId = "entities",
                category = "procedures and mechanics",
                scope = "mechanics",
                includeInactive = "procedures and mechanics",
                limit = "entities and history",
                sample = "world",
                failuresOnly = "history",
                tool = "history",
                subject = "history"
            },
            CommitPayloads = new
            {
                procedure = "{id, category, name, description, instructions, governs?, constraints?, status?, changeNote?}",
                component = "{id, name, description, schema?}",
                effects = "{effects: [{type, entityId?, definitionId?, toEntityId?, kind?, slot?, name?, data?}, ...]}",
                mechanic = "{id, category, name, description?, matches?, requirements?, source, scope?, status?, changeNote?}",
                action = "{intent, roleEntityIds?, input?, scope?, seed?}"
            }
        };
}
