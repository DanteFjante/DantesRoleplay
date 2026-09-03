using System.ComponentModel;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// The read-only legacy-mechanic handler behind <c>query(kind: "mechanics")</c>.
/// Current mechanics are discovered through application catalogs, executed through
/// <c>application.action.execute</c>, and authored through the governed mechanic sandbox.
/// Not registered as an MCP tool (VERB_MIGRATION.md D5).
///
/// There is no separate "get" path. Reading one in full is <c>query(kind: "mechanics", id: ...)</c>
/// — the same kind, one argument different — because an unbounded collection needs exactly two
/// layers, summary list and full record, and nothing more.
///
/// </summary>
public sealed class MechanicHandler
{
    [Description(
        "Find game rules, or read one in full. Mechanics are JavaScript written while playing — " +
        "they are what makes this a game rather than a database, and none of them are built in. " +
        "Call with a query to search by what a player would say (\"shove him\"), or with an exact " +
        "id to get that mechanic INCLUDING its source and the data it declares it needs. Read a " +
        "mechanic before revising it, and before assuming what it does.")]
    public async Task<ToolEnvelope> FindMechanicsAsync(
        IMechanicStore mechanics,
        IOperationLog log,
        [Description("Exact mechanic id. Returns the full mechanic with its source. Overrides query.")]
        string? id = null,
        [Description("Version to read, with id. Omit for the live one — older versions are kept forever.")]
        int? version = null,
        [Description("What a player might say, or words from the rule's name. Omit to list everything.")]
        string? query = null,
        [Description("Restrict to one category branch and its descendants. Browse paths with query(kind: \"categories\", catalog: \"mechanics\").")] string? category = null,
        [Description("Ruleset. Rules in this scope rank first; shared rules are always included.")]
        string? scope = null,
        [Description("Include deprecated and archived rules. Default false.")]
        bool includeInactive = false,
        [Description("Maximum results for a search. Omit for the store's default of 50.")]
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "find_mechanics", async () =>
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                var mechanic = await mechanics.GetAsync(id, version, cancellationToken);

                if (mechanic is null)
                {
                    var exists = await mechanics.ExistsAsync(id, cancellationToken);

                    return exists
                        ? ToolOutcome.Fail(
                            "UNKNOWN_VERSION",
                            $"Mechanic '{id}' exists but has no version {version}.",
                            $"query(kind: \"mechanics\", id: \"{id}\") — omit the version for the live one.",
                            $"Version {version} of '{id}' not found.")
                        : ToolOutcome.Fail(
                            "UNKNOWN_MECHANIC",
                            $"There is no mechanic with id '{id}'.",
                            "query(kind: \"mechanics\") — list what does exist, then retry with a real id.",
                            $"Mechanic '{id}' not found.");
                }

                var steps = new List<string>();

                if (mechanic.Version < mechanic.LatestVersion)
                {
                    steps.Add($"This is version {mechanic.Version} of {mechanic.LatestVersion}. query(kind: \"mechanics\", id: \"{id}\") returns the live one.");
                }

                steps.Add("query(kind: \"system.feature-search\", applicationId: \"...\", query: \"...\") — find the current application mechanic that owns this intent.");
                steps.Add($"{McpVerbCatalog.CommitCall("application.action.execute")} — execute an exact current application mechanic after filling its registered contract.");

                return ToolOutcome.OkAbout(id, mechanic, $"Read {id} v{mechanic.Version}.", [.. steps]);
            }

            var results = limit is null
                ? await mechanics.FindAsync(
                    query, category, scope, includeInactive, cancellationToken: cancellationToken)
                : await mechanics.FindAsync(
                    query, category, scope, includeInactive, limit.Value, cancellationToken);

            if (results.Count == 0)
            {
                var all = await mechanics.FindAsync(cancellationToken: cancellationToken);

                // §7.4: an empty search is exactly where an agent concludes a thing is impossible.
                // Here that conclusion is usually right AND the fix is to write the rule.
                return ToolOutcome.Ok(
                    new { Mechanics = results, TotalWithoutFilters = all.Count },
                    $"No mechanics matched (query: '{query}').",
                    all.Count > 0
                        ? "query(kind: \"mechanics\") — clear the filters; rules exist, just not matching this."
                        : "No legacy rules exist. orient() lists the active draft-authoring capability; system.mechanic-sandbox.draft creates only a governed inert draft.");
            }

            return ToolOutcome.Ok(
                new { Mechanics = results },
                $"Found {results.Count} mechanic(s).",
                $"query(kind: \"mechanics\", id: \"{results[0].Id}\") — read one in full, source included.");
        });
}
