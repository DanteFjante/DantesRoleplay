using System.ComponentModel;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The mechanic handlers behind <c>query(kind: "mechanics")</c> and
/// <c>commit(kind: "mechanic")</c>. Not registered as MCP tools (VERB_MIGRATION.md D5).
///
/// There is no separate "get" path. Reading one in full is <c>query(kind: "mechanics", id: ...)</c>
/// — the same kind, one argument different — because an unbounded collection needs exactly two
/// layers, summary list and full record, and nothing more.
///
/// This class also held a second, older <c>run_action</c> implementation taking a mechanicId, a
/// roles map and its own dryRun. It was superseded by <see cref="ActionTools"/> over
/// <c>IActionRunner</c> and had been unreachable and untested ever since — but its next-step
/// strings still advertised choosing a mechanic by id and dry-running an action, neither of which
/// the live path supports. It was removed rather than repaired: a dead second implementation that
/// describes capabilities the real one lacks is worse than no implementation at all.
/// </summary>
[McpServerToolType]
public sealed class MechanicTools
{
    [McpServerTool(Name = "find_mechanics")]
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

                steps.Add($"{VerbSurface.CommitCall("action")} — resolve an action through it. A rule is selected by intent, not by id, so use words this one matches, and fill roleEntityIds with the role names it declares above.");
                steps.Add($"{VerbSurface.CommitCall("mechanic", id, dryRun: true)} — only if running it showed it is wrong.");

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
                        : $"No rules exist at all. This system ships without a game — {VerbSurface.CommitCall("mechanic", dryRun: true)} creates the first one.");
            }

            return ToolOutcome.Ok(
                new { Mechanics = results },
                $"Found {results.Count} mechanic(s).",
                $"query(kind: \"mechanics\", id: \"{results[0].Id}\") — read one in full, source included.");
        });

    [McpServerTool(Name = "write_mechanic")]
    [Description(
        """
        Create a game rule, or revise one. Writing an existing id APPENDS a version; the old source
        is kept forever, because an operation recorded last week ran against it. ALWAYS dryRun
        first — it reports named checks including whether the components you named exist and
        whether another rule already answers the same phrases.

        The source is a JavaScript function body. It receives `ctx` and returns an object:

          ctx.roles.<name>            an entity you declared: .id, .name, .components, .containerId
          ctx.roles.<name>.components component data as JSON STRINGS — JSON.parse them
          ctx.input                   the caller's arguments for this action
          ctx.randomInt(min, max)     seeded and reproducible, inclusive both ends
          ctx.random()                seeded, 0 to 1
          ctx.log(message)            shows up in the run result and in history

          return { narration: "what happened", effects: [ ...same shape as commit(kind: "effects")... ] }

        The rule CANNOT read the database, call out, or reach anything not in ctx — it gets exactly
        what its requirements declared and nothing else. That is what makes it reviewable, so
        declare honestly rather than minimally.

        requirements is JSON:
          {"roles": {"subject": {"components": ["stats"], "description": "who acts",
                                 "optional": false, "includeContents": false}}}
        """)]
    public async Task<ToolEnvelope> WriteMechanicAsync(
        IMechanicStore mechanics,
        IOperationLog log,
        [Description("Dotted id, e.g. \"mechanic.check.ability\". PERMANENT — no rename, no delete.")]
        string id,
        [Description("Category for browsing, e.g. \"check\", \"movement\". Reuse an existing one where you can.")]
        string category,
        [Description("Short human title.")] string name,
        [Description("One or two sentences describing what this rule resolves.")]
        string description,
        [Description("What players might say to invoke this, ONE PER LINE. Without these, an action will rarely find it.")]
        string matches,
        [Description("JSON projection spec — the roles and components this rule reads. Declare honestly.")]
        string requirements,
        [Description("The JavaScript function body. See this tool's description for what ctx holds.")]
        string source,
        [Description("Ruleset this belongs to. Omit for a rule shared by every campaign.")]
        string scope = "",
        [Description("One of: draft, active, deprecated, archived. Defaults to active on create.")]
        string? status = null,
        [Description("Why this revision exists. Expected when revising.")] string changeNote = "",
        [Description("What you were trying to achieve, in your own words. Goes in the audit log.")]
        string intent = "",
        [Description("Ids of procedures you consulted.")] string[]? proceduresUsed = null,
        [Description("Validate and report named checks without writing. Do this first.")]
        bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        // Same reasoning as write_procedure and apply_effects: a dry run validates rather than
        // acts, so it must not spend the read evidence the real call is judged against.
        await ToolRunner.RunAsync(log, "write_mechanic", intent, id, proceduresUsed, async () =>
        {
            MechanicStatus? parsedStatus = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<MechanicStatus>(status, ignoreCase: true, out var value))
                {
                    return ToolOutcome.Fail(
                        "INVALID_STATUS",
                        $"'{status}' is not a status.",
                        $"{VerbSurface.CommitCall("mechanic", id)} — retry with status omitted, or one of: draft, active, deprecated, archived.",
                        $"Rejected write to '{id}': bad status.");
                }

                parsedStatus = value;
            }

            var request = new WriteMechanicRequest
            {
                Id = id,
                Category = category,
                Name = name,
                Description = description,
                Matches = matches,
                Requirements = string.IsNullOrWhiteSpace(requirements) ? "{}" : requirements,
                Source = source,
                Scope = scope,
                Status = parsedStatus,
                CreatedBy = "llm",
                ChangeNote = changeNote
            };

            var checks = await mechanics.CheckAsync(request, cancellationToken);
            var read = await log.RecentlyReadProceduresAsync(cancellationToken);

            if (dryRun)
            {
                return ToolOutcome.Ok(
                    new
                    {
                        Checks = checks,
                        ProceduresYouDemonstrablyRead = read,
                        Committed = false
                    },
                    $"Dry run for '{id}': {checks.Count(c => c.Passed)}/{checks.Count} checks passed, nothing written.",
                    checks.Any(c => !c.Passed)
                        ? "Address the failing checks above, then send the identical payload again with dryRun omitted."
                        : $"{VerbSurface.CommitCall("mechanic", id)} — the identical payload with dryRun omitted commits it.");
            }

            var blocking = checks
                .Where(c => c.Blocking && !c.Passed)
                .ToList();

            if (blocking.Count > 0)
            {
                return ToolOutcome.Fail(
                    "INVALID_MECHANIC",
                    $"The mechanic failed {blocking.Count} blocking check(s): " +
                    string.Join(" ", blocking.Select(c => c.Detail)),
                    $"{VerbSurface.CommitCall("mechanic", id, dryRun: true)} — correct the blocking checks before committing.",
                    $"Rejected '{id}': blocking mechanic checks failed.");
            }

            var result = await mechanics.WriteAsync(request, cancellationToken);
            var verb = result.Created ? "Created" : "Revised";

            return ToolOutcome.Ok(
                new { result.Mechanic, result.Created, Checks = checks, ProceduresYouDemonstrablyRead = read },
                $"{verb} {id} v{result.Mechanic.Version}.",
                $"{VerbSurface.CommitCall("action")} — run it; a rule that has never been run is a guess. Use words from its match phrases as the intent.",
                "query(kind: \"mechanics\") — check you have not created a near-duplicate.");
        }, consumesReadEvidence: !dryRun);
}
