using System.ComponentModel;
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
        IOperationLog log,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "orient", async () =>
        {
            var categories = await procedures.GetCategoriesAsync(cancellationToken);
            var definitions = await world.GetDefinitionsAsync(cancellationToken);

            var procedureCount = categories.Sum(c => c.Count);

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
                        "The agent that operates and extends it. Today you can read everything " +
                        "listed under capabilities, create and revise procedure contracts, and " +
                        "change world state through apply_effects. Game rules as JavaScript are " +
                        "not runnable yet — see notYetBuilt.",
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
                        "Reads go through describe_world and get_entities. EVERY change goes " +
                        "through apply_effects, which validates the whole list and then applies " +
                        "it in one transaction — there is no partial write. A component cannot " +
                        "be attached until define_component has declared it."
                },
                Capabilities = Capabilities(),
                NotYetBuilt = NotYetBuilt()
            };

            var nextSteps = new List<string>();

            if (procedureCount == 0)
            {
                nextSteps.Add(
                    "No procedures exist, which is unexpected — the system seeds its own. " +
                    "Call history(failuresOnly: true) to see whether seeding failed.");
            }
            else
            {
                nextSteps.Add("find_procedures() — list the operating manual. Read this before doing anything else.");
                nextSteps.Add("get_procedure(id: \"procedure.system.inspect\") — how to look around before changing anything.");
                nextSteps.Add("describe_world() — what the world currently holds, and which component definitions exist.");
                nextSteps.Add("write_procedure(..., dryRun: true) — if your task is to add or revise a procedure.");
                nextSteps.Add("apply_effects(effects: [...], dryRun: true) — if your task is to change the world.");
            }

            nextSteps.Add("history() — see what was done recently, optionally filtered by tool or subject.");

            return ToolOutcome.Ok(
                data,
                $"Oriented: {procedureCount} procedures, {definitions.Count} component definitions, {entityCount} entities.",
                [.. nextSteps]);
        });

    /// <summary>
    /// What this system can do TODAY. A guard test asserts this matches the declared MCP tools in
    /// both directions — every tool appears here, and nothing appears here that is not a tool.
    /// </summary>
    private static IReadOnlyList<string> Capabilities() =>
    [
        "orient — you are here",
        "find_procedures — list or search the operating manual",
        "get_procedure — read one procedure in full, optionally at an older version",
        "write_procedure — create a procedure, or revise one (revising appends a version)",
        "describe_world — what the world holds: component definitions, usage, example entities",
        "get_entities — fetch entities in full by id, or search by name or component",
        "define_component — declare a kind of data entities can carry",
        "apply_effects — the only way world state changes; validated and applied as one transaction",
        "history — recent operations, including failures"
    ];

    /// <summary>
    /// Stated explicitly so a cold model does not infer these from the architecture's ambitions
    /// and then invent a way to do them. Knowing what is absent is as useful as knowing what is
    /// present, and much cheaper than discovering it by failing.
    /// </summary>
    private static IReadOnlyList<string> NotYetBuilt() =>
    [
        "Storing or running JavaScript game mechanics — not started. There is no way to define " +
        "what an action DOES; you can only record its consequences yourself via apply_effects.",
        "Rolling anything, resolving anything, or any rule at all — the system contains no game.",
        "Events and subscriptions — not started.",
        "Inspecting this application's own source or tool registration — not available, so a " +
        "procedure describing how the code works cannot currently be verified against the code."
    ];
}
