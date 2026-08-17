using System.ComponentModel;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.World;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The world surface: two reads and two writes.
///
/// Four tools for an entire data model is only possible because the model has five structures and
/// no game concepts in it (§3.11). A surface with add_character, set_stat and move_item would need
/// a new tool every time the game grew; this one never does, because growth happens in component
/// definitions and JavaScript rather than in C#.
///
/// Every structural change goes through <c>apply_effects</c>, so there is exactly one place where
/// world state changes and exactly one place that has to be atomic (§3.8).
/// </summary>
[McpServerToolType]
public sealed class WorldTools
{
    [McpServerTool(Name = "describe_world")]
    [Description(
        "Overview of what exists in the world right now: which component definitions have been " +
        "declared, how heavily each is used, and how many entities there are. Read-only and " +
        "cheap. Call this before define_component so you reuse an existing definition instead of " +
        "declaring a near-duplicate, and before apply_effects so you use definition ids that " +
        "actually exist.")]
    public async Task<ToolEnvelope> DescribeWorldAsync(
        IWorldStore world,
        IOperationLog log,
        [Description("How many example entities to include. Default 10, use 0 for none.")]
        int sample = 10,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "describe_world", async () =>
        {
            var definitions = await world.GetDefinitionsAsync(cancellationToken);

            var examples = sample > 0
                ? await world.FindEntitiesAsync(limit: sample, cancellationToken: cancellationToken)
                : [];

            var data = new
            {
                HowItWorks =
                    "Everything is an entity. Data lives in components attached to entities, and " +
                    "each component names a definition that must exist first. Entities can " +
                    "contain other entities (one container each, with a slot) and can be related " +
                    "to each other by a named kind. That is the whole model — a new game concept " +
                    "is a definition plus data, never a schema change.",
                ComponentDefinitions = definitions,
                ExampleEntities = examples,
                Note = sample > 0 && examples.Count == sample
                    ? $"Showing the first {sample} entities; there may be more. get_entities(nameQuery: ...) to look for specific ones."
                    : string.Empty
            };

            var nextSteps = new List<string>();

            if (definitions.Count == 0)
            {
                nextSteps.Add(
                    "No component definitions exist yet, so nothing can hold data. " +
                    "define_component(id: ..., name: ..., description: ...) declares the first one.");
            }
            else
            {
                nextSteps.Add($"get_entities(withDefinitionId: \"{definitions[0].Id}\") — see what carries that component.");
                nextSteps.Add("apply_effects(effects: [...], dryRun: true) — validate a change before making it.");
            }

            return ToolOutcome.Ok(
                data,
                $"World: {definitions.Count} component definition(s), {examples.Count} entity example(s) shown.",
                [.. nextSteps]);
        });

    [McpServerTool(Name = "get_entities")]
    [Description(
        "Fetch entities. Give ids to get them in full — every component, what contains them, " +
        "what they contain and their relationships — which is what you want before changing " +
        "anything. Give nameQuery or withDefinitionId instead to search, which returns summaries " +
        "only. Read-only.")]
    public async Task<ToolEnvelope> GetEntitiesAsync(
        IWorldStore world,
        IOperationLog log,
        [Description("Exact entity ids. Returns full detail for each. Takes precedence over the search arguments.")]
        string[]? ids = null,
        [Description("Substring of the entity name. Used only when ids is omitted.")]
        string? nameQuery = null,
        [Description("Return only entities carrying this component definition. Used only when ids is omitted.")]
        string? withDefinitionId = null,
        [Description("Maximum results for a search. Default 50.")] int limit = 50,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "get_entities", async () =>
        {
            if (ids is { Length: > 0 })
            {
                var found = await world.GetEntitiesAsync(ids, cancellationToken);
                var missing = ids.Except(found.Select(e => e.Id), StringComparer.Ordinal).ToArray();

                if (found.Count == 0)
                {
                    return ToolOutcome.Fail(
                        "UNKNOWN_ENTITY",
                        $"None of these entity ids exist or all are deleted: {string.Join(", ", ids)}.",
                        "get_entities(nameQuery: \"...\") — search by name, or describe_world() to see what exists.",
                        $"No entities found for {ids.Length} id(s).");
                }

                var detailed = new List<object>();

                foreach (var entity in found)
                {
                    detailed.Add(new
                    {
                        entity.Id,
                        entity.Name,
                        entity.Components,
                        entity.ContainerId,
                        entity.ContainerSlot,
                        Contains = await world.GetContentsAsync(entity.Id, cancellationToken),
                        Relationships = await world.GetRelationshipsAsync(entity.Id, true, cancellationToken)
                    });
                }

                return ToolOutcome.Ok(
                    new { Entities = detailed, Missing = missing },
                    missing.Length == 0
                        ? $"Fetched {found.Count} entity(ies) in full."
                        : $"Fetched {found.Count}; {missing.Length} id(s) not found: {string.Join(", ", missing)}.",
                    "apply_effects(effects: [...], dryRun: true) — validate a change against what you just read.");
            }

            var results = await world.FindEntitiesAsync(nameQuery, withDefinitionId, limit, cancellationToken);

            if (results.Count == 0)
            {
                // §7.4: an empty search is where an agent wrongly concludes a thing does not exist.
                var any = await world.FindEntitiesAsync(limit: 1, cancellationToken: cancellationToken);

                return ToolOutcome.Ok(
                    new { Entities = results },
                    $"No entities matched (nameQuery: '{nameQuery}', withDefinitionId: '{withDefinitionId}').",
                    any.Count > 0
                        ? "get_entities() — clear the filters; entities exist, just not matching this."
                        : "The world is empty. apply_effects with an entity.create effect makes the first one.");
            }

            return ToolOutcome.Ok(
                new { Entities = results },
                $"Found {results.Count} entity(ies).",
                $"get_entities(ids: [\"{results[0].Id}\"]) — read one in full before changing it.");
        });

    [McpServerTool(Name = "define_component")]
    [Description(
        "Declare a kind of data an entity can carry, e.g. \"stats\" or \"description\". Nothing " +
        "can be attached to an entity until its definition exists, which is deliberate: an " +
        "undeclared component is nearly always a typo, and a silently created one is invisible " +
        "forever after. Call describe_world() first and REUSE an existing definition where one " +
        "fits — two definitions meaning the same thing is the failure mode this system is built " +
        "to avoid. Writing an id that already exists updates it rather than creating a second.")]
    public async Task<ToolEnvelope> DefineComponentAsync(
        IWorldStore world,
        IOperationLog log,
        [Description("Short lower-case id, e.g. \"stats\". Permanent — there is no rename.")] string id,
        [Description("Short human title.")] string name,
        [Description("What this component holds and when to attach it. One or two sentences.")]
        string description,
        [Description(
            "Optional JSON Schema for the component data. Documentation for the next agent " +
            "rather than an enforced constraint — the store does not validate against it yet.")]
        string schema = "",
        [Description("What you were trying to achieve, in your own words. Goes in the audit log.")]
        string intent = "",
        [Description("Ids of procedures you consulted.")] string[]? proceduresUsed = null,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "define_component", intent, id, proceduresUsed, async () =>
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                return ToolOutcome.Fail(
                    "MISSING_ARGUMENT",
                    "Both id and name are required.",
                    "define_component(id: \"...\", name: \"...\", description: \"...\") — retry with all three.",
                    "Rejected define_component: missing id or name.");
            }

            var before = await world.GetDefinitionsAsync(cancellationToken);
            var existed = before.Any(d => d.Id == id);

            var definition = await world.DefineComponentAsync(id, name, description, schema, cancellationToken);

            var neighbours = before
                .Where(d => d.Id != id)
                .Select(d => new { d.Id, d.Name, d.UsageCount })
                .ToList();

            return ToolOutcome.Ok(
                new { Definition = definition, Created = !existed, OtherDefinitions = neighbours },
                existed
                    ? $"Updated existing component definition '{id}'."
                    : $"Created component definition '{id}'. {neighbours.Count} other(s) already existed.",
                existed
                    ? "describe_world() — confirm the change reads the way you intended."
                    : "Check OtherDefinitions above: if one of them already meant this, you have just created a duplicate.",
                $"apply_effects(effects: [{{type: \"component.set\", entityId: \"...\", definitionId: \"{id}\", data: \"{{}}\"}}])");
        });

    [McpServerTool(Name = "apply_effects")]
    [Description(
        """
        The ONLY way world state changes. Submit a list of effects; the whole list is validated
        first and then applied in one transaction, so it either all happens or none of it does.
        ALWAYS call with dryRun: true first — validation reports every fault at once, each naming
        the effect's position and how to fix it.

        Each effect is an object with a "type" plus the fields that type needs:

          entity.create        entityId, name                     (entityId is required and permanent)
          entity.delete        entityId
          component.add        entityId, definitionId, data       (fails if already present)
          component.set        entityId, definitionId, data       (replaces the data wholesale)
          component.merge      entityId, definitionId, data       (patches top-level keys only)
          component.remove     entityId, definitionId
          containment.move     entityId, toEntityId, slot         (omit toEntityId to remove from its container)
          relationship.create  entityId, toEntityId, kind, data
          relationship.remove  entityId, toEntityId, kind

        "data" is a JSON object as a string, e.g. "{\"strength\":12}". A later effect may rely on
        an earlier one in the same list, so create an entity and populate it in a single call.
        There are no game-specific verbs and there never will be: a status, a score or an
        inventory is a component definition plus data, not a new kind of effect.
        """)]
    public async Task<ToolEnvelope> ApplyEffectsAsync(
        IEffectApplier applier,
        IOperationLog log,
        [Description("The effects, applied in order. All or nothing.")] Effect[] effects,
        [Description("What you were trying to achieve, in your own words. Goes in the audit log.")]
        string intent = "",
        [Description("Ids of procedures you consulted.")] string[]? proceduresUsed = null,
        [Description("Validate and report without writing. Do this first.")] bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        // Same reasoning as write_procedure: a dry run validates rather than acts, so it must not
        // spend the read evidence that the real call is then judged against.
        await ToolRunner.RunAsync(log, "apply_effects", intent, string.Empty, proceduresUsed, async () =>
        {
            effects ??= [];

            if (effects.Length == 0)
            {
                return ToolOutcome.Fail(
                    "NO_EFFECTS",
                    "The effects list was empty, so there was nothing to validate or apply.",
                    "apply_effects(effects: [{type: \"entity.create\", entityId: \"...\", name: \"...\"}], dryRun: true)",
                    "Rejected apply_effects: empty list.");
            }

            var result = await applier.ApplyAsync(effects, dryRun, cancellationToken);

            if (!result.Valid)
            {
                return ToolOutcome.Fail(
                    "INVALID_EFFECTS",
                    $"{result.Problems.Count} problem(s); nothing was applied. " +
                    string.Join(" ", result.Problems.Select(p => $"[{p.Index}] {p.Effect}: {p.Problem}")),
                    "Correct the effects listed above — the index is the position in the list you sent — then call again with dryRun: true.",
                    $"Rejected {effects.Length} effect(s): {result.Problems.Count} problem(s).");
            }

            if (dryRun)
            {
                return ToolOutcome.Ok(
                    new { Validated = effects.Length, Problems = result.Problems, Applied = false },
                    $"Dry run: all {effects.Length} effect(s) valid, nothing written.",
                    "apply_effects(effects: [...]) with dryRun omitted to commit exactly this list.");
            }

            // Recording the entity as the subject is what makes history(subject: "...") answer
            // "what has been done to this thing", which is the question a supervisor asks (§3.12).
            var touched = effects
                .Select(e => e.EntityId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return ToolOutcome.OkAbout(
                // Capped: the column holds 200 characters, and a subject that silently overflowed
                // would make history lie about a batch rather than merely abbreviate it.
                Truncate(string.Join(",", touched), 200),
                new { result.Applied, result.Count, Entities = touched },
                $"Applied {result.Count} effect(s) in one transaction" +
                (touched.Count == 0 ? "." : $": {string.Join(", ", touched.Take(5))}{(touched.Count > 5 ? ", …" : "")}."),
                "get_entities(ids: [...]) — confirm the result reads the way you intended.",
                "history() — see the operation this produced.");
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
