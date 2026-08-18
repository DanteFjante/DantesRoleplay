using System.ComponentModel;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The last three tools this system will ever have (§7.1): find, write, run.
///
/// Note that there is no get_mechanic. Reading one in full is <c>find_mechanics(id: "...")</c> —
/// the same tool, one argument different — because the twelfth slot was the last one and reading
/// source is not worth spending a permanent tool on when an argument does it.
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
        [Description("Restrict to one category.")] string? category = null,
        [Description("Ruleset. Rules in this scope rank first; shared rules are always included.")]
        string? scope = null,
        [Description("Include deprecated and archived rules. Default false.")]
        bool includeInactive = false,
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
                            $"find_mechanics(id: \"{id}\") — omit the version for the live one.",
                            $"Version {version} of '{id}' not found.")
                        : ToolOutcome.Fail(
                            "UNKNOWN_MECHANIC",
                            $"There is no mechanic with id '{id}'.",
                            "find_mechanics() — list what does exist, then retry with a real id.",
                            $"Mechanic '{id}' not found.");
                }

                var steps = new List<string>();

                if (mechanic.Version < mechanic.LatestVersion)
                {
                    steps.Add($"This is version {mechanic.Version} of {mechanic.LatestVersion}. find_mechanics(id: \"{id}\") returns the live one.");
                }

                steps.Add($"run_action(mechanicId: \"{id}\", roles: {{...}}, dryRun: true) — try it without applying anything.");
                steps.Add($"write_mechanic(id: \"{id}\", ..., dryRun: true) — only if running it showed it is wrong.");

                return ToolOutcome.OkAbout(id, mechanic, $"Read {id} v{mechanic.Version}.", [.. steps]);
            }

            var results = await mechanics.FindAsync(
                query, category, scope, includeInactive, cancellationToken: cancellationToken);

            if (results.Count == 0)
            {
                var all = await mechanics.FindAsync(cancellationToken: cancellationToken);

                // §7.4: an empty search is exactly where an agent concludes a thing is impossible.
                // Here that conclusion is usually right AND the fix is to write the rule.
                return ToolOutcome.Ok(
                    new { Mechanics = results, TotalWithoutFilters = all.Count },
                    $"No mechanics matched (query: '{query}').",
                    all.Count > 0
                        ? "find_mechanics() — clear the filters; rules exist, just not matching this."
                        : "No rules exist at all. This system ships without a game — write_mechanic(..., dryRun: true) creates the first one.");
            }

            return ToolOutcome.Ok(
                new { Mechanics = results },
                $"Found {results.Count} mechanic(s).",
                $"find_mechanics(id: \"{results[0].Id}\") — read one in full, source included.");
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

          return { narration: "what happened", effects: [ ...same shape as apply_effects... ] }

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
        [Description("What players might say to invoke this, ONE PER LINE. Without these, run_action will rarely find it.")]
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
                        $"write_mechanic(id: \"{id}\", ..., status: \"active\") — retry with status omitted, or one of: draft, active, deprecated, archived.",
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
                        ? "Address the failing checks above, then call again with dryRun omitted."
                        : $"write_mechanic(id: \"{id}\", ...) with dryRun omitted to commit this.");
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
                    $"write_mechanic(id: \"{id}\", ..., dryRun: true) — correct the blocking checks before committing.",
                    $"Rejected '{id}': blocking mechanic checks failed.");
            }

            var result = await mechanics.WriteAsync(request, cancellationToken);
            var verb = result.Created ? "Created" : "Revised";

            return ToolOutcome.Ok(
                new { result.Mechanic, result.Created, Checks = checks, ProceduresYouDemonstrablyRead = read },
                $"{verb} {id} v{result.Mechanic.Version}.",
                $"run_action(mechanicId: \"{id}\", roles: {{...}}, dryRun: true) — a rule that has never been run is a guess.",
                "find_mechanics() — check you have not created a near-duplicate.");
        }, consumesReadEvidence: !dryRun);

    [McpServerTool(Name = "run_action")]
    [Description(
        """
        Resolve something a player is trying to do, by running a game rule.

        Call with `intent` alone first — it returns candidate mechanics without running anything,
        and you choose. Then call again with `mechanicId`, the `roles` it needs, and any `input`.

        This is the whole chain in one call: fetch exactly the data the rule declared, run it in a
        sandbox that can reach nothing else, validate what it proposes, and apply all of it in one
        transaction — or none of it. Use dryRun to see the narration and the proposed effects
        without changing anything.

        `roles` maps the rule's OWN role names to entity ids, e.g. {"subject": "orban"}. Read the
        mechanic with find_mechanics(id: ...) to see what it takes. Chance is seeded and the seed
        is returned: pass the same seed back to replay a run exactly.
        """)]
    public async Task<ToolEnvelope> RunActionAsync(
        IMechanicStore mechanics,
        IProjectionResolver resolver,
        IMechanicEngine engine,
        IEffectApplier applier,
        IOperationLog log,
        [Description("What the player is trying to do, in their words. Use alone to get candidates.")]
        string intent = "",
        [Description("Which rule to run. Omit to search by intent instead of running anything.")]
        string? mechanicId = null,
        [Description("The rule's role names to entity ids, e.g. {\"subject\": \"orban\"}.")]
        Dictionary<string, string>? roles = null,
        [Description("Arguments for this action as a JSON object, e.g. {\"cost\": 3}.")]
        string input = "{}",
        [Description("Ruleset to prefer when searching. Shared rules are always included.")]
        string scope = "",
        [Description("Reuse a seed from an earlier run to reproduce it exactly. Omit for a new one.")]
        long? seed = null,
        [Description("Run the rule and show what it would do, without applying anything.")]
        bool dryRun = false,
        [Description("Ids of procedures you consulted.")] string[]? proceduresUsed = null,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "run_action", intent, mechanicId ?? string.Empty, proceduresUsed, async () =>
        {
            // No mechanic named: this is the matching step, and it deliberately runs nothing. The
            // premise of the system is supervising code an AI wrote, and picking which rule to
            // execute is the moment where that supervision is cheapest.
            if (string.IsNullOrWhiteSpace(mechanicId))
            {
                if (string.IsNullOrWhiteSpace(intent))
                {
                    return ToolOutcome.Fail(
                        "NO_INTENT",
                        "Neither intent nor mechanicId was given, so there is nothing to match or run.",
                        "run_action(intent: \"what the player is trying to do\") — returns candidate rules.",
                        "Rejected run_action: nothing to do.");
                }

                var candidates = await mechanics.FindAsync(intent, scope: scope, cancellationToken: cancellationToken);

                if (candidates.Count == 0)
                {
                    return ToolOutcome.Ok(
                        new { Candidates = candidates, Intent = intent },
                        $"No rule matches '{intent}'.",
                        "This system ships without a game, so an unmatched action usually means the rule has not been written yet.",
                        "find_mechanics() — see everything that does exist, in case it is worded differently.",
                        "write_mechanic(..., dryRun: true) — author the rule, then run it.");
                }

                return ToolOutcome.Ok(
                    new { Candidates = candidates, Intent = intent },
                    $"{candidates.Count} rule(s) could resolve '{intent}'. Nothing was run.",
                    $"find_mechanics(id: \"{candidates[0].Id}\") — read it, including which roles it needs.",
                    $"run_action(mechanicId: \"{candidates[0].Id}\", roles: {{...}}, dryRun: true) — once you know the roles.");
            }

            var mechanic = await mechanics.GetAsync(mechanicId, cancellationToken: cancellationToken);

            if (mechanic is null)
            {
                return ToolOutcome.Fail(
                    "UNKNOWN_MECHANIC",
                    $"There is no mechanic with id '{mechanicId}'.",
                    $"run_action(intent: \"{intent}\") — find one by what the player is doing.",
                    $"Mechanic '{mechanicId}' not found.");
            }

            MechanicRequirements requirements;

            try
            {
                requirements = MechanicRequirements.Parse(mechanic.Requirements);
            }
            catch (System.Text.Json.JsonException ex)
            {
                return ToolOutcome.Fail(
                    "BROKEN_REQUIREMENTS",
                    $"Mechanic '{mechanicId}' has requirements that are not valid JSON: {ex.Message}",
                    $"write_mechanic(id: \"{mechanicId}\", ..., dryRun: true) — fix its requirements.",
                    $"Mechanic '{mechanicId}' has unparseable requirements.");
            }

            // A new seed unless one was handed back for replay. Recorded either way, because a rule
            // that decides by chance is unreviewable if the chance cannot be reproduced.
            var actualSeed = seed ?? Random.Shared.NextInt64(1, long.MaxValue);

            var resolved = await resolver.ResolveAsync(
                requirements,
                roles ?? [],
                input,
                actualSeed,
                cancellationToken);

            if (!resolved.Ok)
            {
                return ToolOutcome.Fail(
                    "UNRESOLVED_ROLES",
                    string.Join(" ", resolved.Problems),
                    $"find_mechanics(id: \"{mechanicId}\") — see exactly which roles it declares, then retry.",
                    $"Could not materialise roles for '{mechanicId}': {resolved.Problems.Count} problem(s).");
            }

            var run = await engine.RunAsync(
                mechanic.Source, resolved.Projection!, ExecutionLimits.Default, cancellationToken);

            if (!run.Ok)
            {
                // The rule is broken, which is ordinary — it was written by an LLM mid-session.
                // The log is returned because it is usually where the reason is.
                return ToolOutcome.Fail(
                    string.IsNullOrEmpty(run.LimitHit) ? "MECHANIC_FAILED" : "MECHANIC_STOPPED",
                    $"{run.Error}{(run.Log.Count > 0 ? " Logged: " + string.Join(" | ", run.Log) : "")}",
                    $"write_mechanic(id: \"{mechanicId}\", ..., changeNote: \"...\", dryRun: true) — revise it; the old version is kept.",
                    $"Mechanic '{mechanicId}' v{mechanic.Version} failed: {run.Error}");
            }

            var validation = await applier.ApplyAsync(run.Output.Effects, dryRun, cancellationToken);

            if (!validation.Valid)
            {
                // The rule ran but proposed something incoherent. Nothing was applied, and the rule
                // is what needs fixing — not the caller's arguments.
                return ToolOutcome.Fail(
                    "INVALID_EFFECTS",
                    $"'{mechanicId}' ran but proposed changes that do not hold together; nothing was applied. " +
                    string.Join(" ", validation.Problems.Select(p => $"[{p.Index}] {p.Effect}: {p.Problem}")),
                    $"write_mechanic(id: \"{mechanicId}\", ..., dryRun: true) — the rule needs correcting, not your arguments.",
                    $"Mechanic '{mechanicId}' proposed {validation.Problems.Count} invalid effect(s).");
            }

            var touched = run.Output.Effects
                .Select(e => e.EntityId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var data = new
            {
                run.Output.Narration,
                Mechanic = $"{mechanic.Id} v{mechanic.Version}",
                Applied = validation.Applied,
                EffectsProposed = run.Output.Effects.Count,
                Effects = dryRun ? run.Output.Effects : [],
                Entities = touched,
                run.Log,
                Seed = actualSeed,
                run.ElapsedMilliseconds,
                Data = run.Output.Data
            };

            // Subject is the mechanic AND everyone involved, comma separated, because both
            // questions get asked: "which rules have been run" and "what has happened to Orban".
            // history matches on comma boundaries, so one field answers both.
            //
            // PARTICIPANTS, not just the entities that changed. A check that Orban made and passed
            // belongs in Orban's history even though it altered nothing — "what happened to this
            // character" is a question about events, and an outcome is an event.
            var involved = (resolved.Projection!.Roles.Values.Select(r => r.Id))
                .Concat(touched)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var subject = Truncate(string.Join(",", new[] { mechanicId }.Concat(involved)), 200);

            return dryRun
                ? ToolOutcome.OkAbout(
                    subject,
                    data,
                    $"Dry run of {mechanic.Id} v{mechanic.Version}: {run.Output.Effects.Count} effect(s) valid, nothing applied.",
                    $"run_action(mechanicId: \"{mechanicId}\", roles: {{...}}, seed: {actualSeed}) — commit exactly this outcome.")
                : ToolOutcome.OkAbout(
                    subject,
                    data,
                    $"{mechanic.Id} v{mechanic.Version}: {run.Output.Narration} ({validation.Count} effect(s) applied, seed {actualSeed})",
                    touched.Count > 0
                        ? $"get_entities(ids: [\"{touched[0]}\"]) — confirm the result reads the way you intended."
                        : "history() — see the operation this produced.");
        }, consumesReadEvidence: !dryRun);

    /// <summary>Cuts on a comma boundary, so a truncated subject list is still a list of whole ids.</summary>
    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var cut = value.LastIndexOf(',', max - 1);
        return cut > 0 ? value[..cut] : value[..max];
    }
}
