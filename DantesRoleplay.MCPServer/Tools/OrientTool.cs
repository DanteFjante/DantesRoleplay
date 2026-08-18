using System.ComponentModel;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The single entry point (ARCHITECTURE.md §7.2).
///
/// A session that has never seen this system should be able to call exactly one tool and know
/// what this is, what state it is in, and what to do next.
///
/// **This response describes what EXISTS, never what is planned.** An earlier version said the
/// agent "may add component definitions and world data" when no tool did either — a cold-model
/// test caught it immediately. That is the same failure that crippled TravelRoleplay
/// (ARCHITECTURE.md §1), just inverted: over-promising is as misleading as going stale, and this
/// file is the one place where being wrong poisons everything downstream. When adding a
/// capability, update <see cref="Capabilities"/> in the same change.
/// </summary>
[McpServerToolType]
public sealed class OrientTool
{
    [McpServerTool(Name = "orient")]
    [Description(
        "START HERE. Explains what this system is, reports what currently exists in it, states " +
        "what is NOT built yet, and tells you which call to make next. Cheap, read-only, and " +
        "safe to call again at any point if you lose track of what you were doing.")]
    public async Task<ToolEnvelope> OrientAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IMechanicStore mechanics,
        IOperationLog log,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "orient", async () =>
        {
            var categories = await procedures.GetCategoriesAsync(cancellationToken);
            var definitions = await world.GetDefinitionsAsync(cancellationToken);
            var ruleCategories = await mechanics.GetCategoriesAsync(cancellationToken);

            var procedureCount = categories.Sum(c => c.Count);
            var ruleCount = ruleCategories.Sum(c => c.Count);

            // Capped rather than counted: this is an orientation number, and the exact total is
            // get_entities' job. A cheap query here keeps orient cheap enough to call freely.
            var sampled = await world.FindEntitiesAsync(limit: 500, cancellationToken: cancellationToken);
            var entityCount = sampled.Count == 500 ? "500+" : sampled.Count.ToString();

            var data = new
            {
                System = new
                {
                    Is =
                        "A persistent roleplaying-game kernel. It stores world state, runs game " +
                        "rules, and records what was done. It contains no game itself: playable " +
                        "features are meant to be data and JavaScript added at runtime.",
                    YouAre =
                        "The agent that operates and extends it. You can read everything listed " +
                        "under capabilities, write procedure contracts, change world state " +
                        "through commit(kind: \"effects\"), and WRITE AND RUN GAME RULES as JavaScript. A " +
                        "rule you write during play is stored, versioned and reusable next " +
                        "session — that is the point of this system, not a side feature.",
                    TheOneRule =
                        "Before performing an operation, retrieve and follow the relevant " +
                        "procedure contracts. Each contract states what it governs, so match " +
                        "that against what you are about to do rather than guessing."
                },
                Procedures = new
                {
                    Total = procedureCount,
                    ByCategory = categories.ToDictionary(c => c.Category, c => c.Count),
                    KnownCategories = categories.Select(c => c.Category).ToArray()
                },
                World = new
                {
                    ComponentDefinitions = definitions.Count,
                    Entities = entityCount,
                    HowItWorks =
                        "Everything in the world is an entity with components attached. There " +
                        "are only five structures: entity, component, component definition, " +
                        "containment, relationship. A new game concept is a row, never a schema " +
                        "change.",
                    Reachable = true,
                    Note =
                        "Reads go through query(kind: \"world\") and query(kind: \"entities\"). EVERY " +
                        "change goes through commit(kind: \"effects\"), which validates the whole list and then applies " +
                        "it in one transaction — there is no partial write. A component cannot " +
                        "be attached until define_component has declared it."
                },
                Rules = new
                {
                    Total = ruleCount,
                    ByCategory = ruleCategories.ToDictionary(c => c.Category, c => c.Count),
                    HowItWorks =
                        "Game rules are JavaScript stored in this system and written during play. " +
                        "A rule declares which participants and which components it reads, is " +
                        "handed exactly that and nothing else, and returns proposed effects plus " +
                        "narration. It cannot query, cannot reach the host, and is stopped by " +
                        "execution limits. Chance is seeded, and the seed is recorded.",
                    Note = ruleCount == 0
                        ? "NO RULES EXIST YET. Nothing can be resolved until one is written — that " +
                          "is the intended empty state, not a fault. write_mechanic creates the first."
                        : "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"...\\\"}\") lists candidates without running anything, so " +
                          "you choose the rule before it executes."
                },
                Capabilities = Capabilities(),
                NotYetBuilt = NotYetBuilt()
            };

            var nextSteps = new List<string>();

            if (procedureCount == 0)
            {
                nextSteps.Add(
                    "No procedures exist, which is unexpected — the system seeds its own. " +
                    "Call query(kind: \"history\", failuresOnly: true) to see whether seeding failed.");
            }
            else
            {
                nextSteps.Add("query(kind: \"procedures\") — list the operating manual. Read this before doing anything else.");
                nextSteps.Add("query(kind: \"procedures\", id: \"procedure.system.inspect\") — how to look around before changing anything.");
                nextSteps.Add("query(kind: \"world\") — what the world currently holds, and which component definitions exist.");
                nextSteps.Add("commit(kind: \"procedure\", payload: \"{\\\"id\\\":\\\"...\\\",\\\"category\\\":\\\"...\\\",\\\"name\\\":\\\"...\\\",\\\"description\\\":\\\"...\\\",\\\"instructions\\\":\\\"...\\\"}\", dryRun: true) — if your task is to add or revise a procedure.");
                nextSteps.Add("commit(kind: \"effects\", payload: \"{\\\"effects\\\":[...]}\", dryRun: true) — if your task is to change the world.");

                nextSteps.Add(ruleCount == 0
                    ? "No game rules exist yet, so nothing can be resolved. commit(kind: \"mechanic\", payload: \"{\\\"id\\\":\\\"...\\\",\\\"category\\\":\\\"...\\\",\\\"name\\\":\\\"...\\\",\\\"source\\\":\\\"...\\\"}\", dryRun: true) writes the first one."
                    : "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"what the player is trying to do\\\"}\") — lists candidate rules without running any.");
            }

            nextSteps.Add("query(kind: \"history\") — see what was done recently, optionally filtered by tool or subject.");

            return ToolOutcome.Ok(
                data,
                $"Oriented: {procedureCount} procedures, {definitions.Count} component definitions, " +
                $"{entityCount} entities, {ruleCount} game rule(s).",
                [.. nextSteps]);
        });

    /// <summary>
    /// What this system can do TODAY. A guard test asserts this matches the declared MCP tools in
    /// both directions — every tool appears here, and nothing appears here that is not a tool.
    /// </summary>
    private static IReadOnlyList<string> Capabilities() =>
    [
        "orient — you are here",
        "query — read capabilities, procedures, world data, entities, mechanics, or history",
        "commit — validate or apply a procedure, component, effect list, mechanic, or action"
    ];

    /// <summary>
    /// Stated explicitly so a cold model does not infer these from the architecture's ambitions
    /// and then invent a way to do them. Knowing what is absent is as useful as knowing what is
    /// present, and much cheaper than discovering it by failing.
    /// </summary>
    private static IReadOnlyList<string> NotYetBuilt() =>
    [
        "Any actual game. The machinery to write and run rules exists; the rules do not. Whether " +
        "this system can resolve a given action depends entirely on whether someone has written " +
        "that rule yet — check with query(kind: \"mechanics\") rather than assuming either way.",
        "Reactive rules — nothing happens on its own between actions. There are no events, no " +
        "subscriptions, and no passage of time.",
        "Composing one rule from another — a mechanic cannot call another mechanic.",
        "Events and subscriptions — not started.",
        "Inspecting this application's own source or tool registration — not available, so a " +
        "procedure describing how the code works cannot currently be verified against the code."
    ];
}
