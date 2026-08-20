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
/// file is the one place where being wrong poisons everything downstream. Hence the capability
/// section is not written here at all — it is <see cref="VerbSurface.Announcement"/>, the same
/// structure the dispatchers switch on, so it cannot drift from the code that serves it.
/// </summary>
[McpServerToolType]
public sealed class OrientTool
{
    /// <summary>
    /// Orientation counts are capped rather than exact. A number here is meant to tell a session
    /// whether a thing exists and roughly how much of it, and orient has to stay cheap enough to
    /// call again whenever someone loses the thread.
    /// </summary>
    private const int CountCap = 500;

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

            // Archived rules included on purpose: this is the "does anything exist" number, and a
            // session that is told zero will not go looking.
            var rules = await mechanics.FindAsync(
                includeInactive: true, limit: CountCap, cancellationToken: cancellationToken);

            var byStatus = rules
                .GroupBy(r => r.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var byScope = rules
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Scope) ? "(shared)" : r.Scope)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var runnableCount = rules.Count(r => r.Status == MechanicStatus.Active);

            var sampled = await world.FindEntitiesAsync(
                limit: CountCap, cancellationToken: cancellationToken);
            var entityCount = sampled.Count == CountCap ? $"{CountCap}+" : sampled.Count.ToString();

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
                        "through commit(kind: \"effects\"), and WRITE AND RUN GAME RULES as " +
                        "JavaScript. A rule you write during play is stored, versioned and " +
                        "reusable next session — that is the point of this system, not a side " +
                        "feature.",
                    TheOneRule =
                        "Before performing an operation, retrieve and follow the relevant " +
                        "procedure contracts. Each contract states what it governs, so match " +
                        "that against what you are about to do rather than guessing.",
                    HowToOperateIt =
                        "query(kind: \"procedures\", id: \"procedure.system.use\") — three verbs, " +
                        "in one contract."
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
                        "Reads go through query(kind: \"world\") and query(kind: \"entities\"). " +
                        "EVERY change goes through commit(kind: \"effects\"), which validates the " +
                        "whole list and then applies it in one transaction — there is no partial " +
                        "write. A component cannot be attached until commit(kind: \"component\") " +
                        "has declared it."
                },
                Rules = new
                {
                    Total = ruleCount,
                    Runnable = runnableCount,
                    ByStatus = byStatus,
                    ByCategory = ruleCategories.ToDictionary(c => c.Category, c => c.Count),
                    ByScope = byScope,
                    HowItWorks =
                        "Game rules are JavaScript stored in this system and written during play. " +
                        "A rule declares which participants and which components it reads, is " +
                        "handed exactly that and nothing else, and returns proposed effects plus " +
                        "narration. It cannot query, cannot reach the host, and is stopped by " +
                        "execution limits. Chance is seeded, and the seed is recorded.",
                    Note = RuleNote(ruleCount, runnableCount)
                },
                Capabilities = VerbSurface.Announcement(),
                NotYetBuilt = NotYetBuilt(ruleCount)
            };

            var nextSteps = new List<string>();

            if (procedureCount == 0)
            {
                nextSteps.Add(
                    "query(kind: \"history\", failuresOnly: true) — no procedures exist, which is " +
                    "unexpected because the system seeds its own; this shows whether seeding failed.");
            }
            else
            {
                nextSteps.Add("query(kind: \"procedures\", id: \"procedure.system.use\") — how to operate these three verbs. Read this first.");
                nextSteps.Add("query(kind: \"procedures\") — list the whole operating manual and match a contract to what you are about to do.");
                nextSteps.Add("query(kind: \"world\") — what the world currently holds, and which component definitions exist.");
                nextSteps.Add("query(kind: \"capabilities\") — every kind, parameter and payload shape, exactly.");
                nextSteps.Add($"{VerbSurface.CommitCall("effects", dryRun: true)} — if your task is to change the world.");

                nextSteps.Add(ruleCount == 0
                    ? $"{VerbSurface.CommitCall("mechanic", dryRun: true)} — no game rules exist yet, so nothing can be resolved until one is written. This writes the first."
                    : "query(kind: \"mechanics\", query: \"what the player is trying to do\") — find the rule and read which roles it needs, before running it.");
                nextSteps.Add($"{VerbSurface.CommitCall("action")} — resolve an action. The rule is chosen by intent; roleEntityIds must name the roles that rule declares.");
            }

            nextSteps.Add("query(kind: \"history\") — see what was done recently, optionally filtered by tool or subject.");

            return ToolOutcome.Ok(
                data,
                $"Oriented: {procedureCount} procedures, {definitions.Count} component definitions, " +
                $"{entityCount} entities, {ruleCount} game rule(s) of which {runnableCount} runnable.",
                [.. nextSteps]);
        });

    /// <summary>
    /// The three states worth telling apart. "Rules exist but none are active" reads as "no rules"
    /// to anyone who only sees a total, and a session that believes there are no rules will invent
    /// outcomes instead of resolving them.
    /// </summary>
    private static string RuleNote(int total, int runnable) =>
        total == 0
            ? "NO RULES EXIST YET. Nothing can be resolved until one is written — that is the "
              + "intended empty state, not a fault. commit(kind: \"mechanic\", dryRun: true) "
              + "creates the first."
            : runnable == 0
                ? $"{total} rule(s) are stored but NONE are active, so nothing can be resolved yet. "
                  + "Read them with query(kind: \"mechanics\", includeInactive: true) and commit a "
                  + "revision with status \"active\" once one is right."
                : "An action selects the best-ranked active rule matching your intent and RUNS "
                  + "it — there is no way to name a rule and no separate dry run. Read the rule "
                  + "first with query(kind: \"mechanics\") to see which roles it declares; the "
                  + "effects it proposes are validated before any of them are applied.";

    /// <summary>
    /// Stated explicitly so a cold model does not infer these from the architecture's ambitions
    /// and then invent a way to do them. Knowing what is absent is as useful as knowing what is
    /// present, and much cheaper than discovering it by failing.
    ///
    /// The first line is conditional because it stops being true the moment a rule is written, and
    /// a session that is told "no game exists" while the database holds the rules it needs will
    /// bypass them and narrate an outcome instead.
    /// </summary>
    private static IReadOnlyList<string> NotYetBuilt(int ruleCount) =>
    [
        ruleCount == 0
            ? "Any actual game. The machinery to write and run rules exists; no rule has been "
              + "written yet. Whether this system can resolve a given action depends entirely on "
              + "whether someone has written that rule — right now, nobody has."
            : "A complete game. Rules exist, but only the ones somebody wrote — check with "
              + "query(kind: \"mechanics\") before assuming an action can be resolved, and write "
              + "the rule if it is missing.",
        // Narrowed twice now, each time a slice landed. It first denied events and subscriptions
        // outright, then denied reaction execution; both stopped being true. A session is told to
        // believe this list over anything else it reads, so an over-broad denial here is worse
        // than silence: it talks sessions out of a capability that works. Narrow it the same day
        // the capability lands.
        "Anything reaching a person on its own. Reactive rules exist in full — a guard can veto a " +
        "change before it commits, an accepted change records events you can read with " +
        "query(kind: \"events\"), a reaction runs on those with its effects in the same " +
        "transaction, and it can declare an event or raise a notification. But a notification is a " +
        "row somebody reads when they ask: nothing pushes, mails, polls or schedules, and time " +
        "does not pass. Nothing in here moves unless somebody calls a verb.",
        // The last false denial in this list, and it was false for a whole feature. Declarative
        // composition has worked since Feature 5: a rule declares children in its requirements,
        // the host runs them first, and their frozen results arrive as ctx.children. What is
        // genuinely missing is the imperative form, and the difference is worth stating precisely
        // rather than denying the whole capability.
        "Calling a rule on demand from inside another. A mechanic CAN compose: declare children in "
        + "`requirements.children` and their frozen results arrive as ctx.children, resolved before "
        + "the parent runs, up to eight deep with cycles refused. What does not exist is deciding "
        + "mid-execution which rule to run — there is no ctx.mechanics.run, and there is no host "
        + "callback a rule could reach.",
        "Choosing which rule runs. An action selects the best-ranked candidate for the intent; " +
        "there is no way to name a specific mechanic, and no separate dry run for an action.",
        "Inspecting this application's own source or tool registration — not available, so a " +
        "procedure describing how the code works cannot currently be verified against the code."
    ];
}
