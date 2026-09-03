using System.ComponentModel;
using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// The read-only legacy-world handlers behind <c>query(kind: "world")</c> and
/// <c>query(kind: "entities")</c>. Current application state is written only through the
/// application-scoped component-type and world-state capabilities.
///
/// Not registered as MCP tools (VERB_MIGRATION.md D5); the dispatchers call these directly. Every
/// literal call in a NextStep or a fix is written in the public call form, because that string is
/// the recovery path a low-context session actually follows.
/// </summary>
public sealed class WorldHandler
{
    [Description(
        "Overview of what exists in the world right now: which component definitions have been " +
        "declared, how heavily each is used, and how many entities there are. Read-only and " +
        "cheap. This view remains available for migration and inspection of records that have not " +
        "yet been adopted into an application state space.")]
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
                    ? $"Showing the first {sample} entities; there may be more. query(kind: \"entities\", nameQuery: \"...\") to look for specific ones."
                    : string.Empty
            };

            var nextSteps = new List<string>();

            if (definitions.Count == 0)
            {
                nextSteps.Add(
                    "No legacy component definitions exist. query(kind: \"system.applications\") " +
                    "lists current application catalogs and query(kind: \"capabilities\") gives their write contracts.");
            }
            else
            {
                nextSteps.Add($"query(kind: \"entities\", withDefinitionId: \"{definitions[0].Id}\") — see what carries that component.");
                nextSteps.Add($"{McpVerbCatalog.CommitCall("system.world-state.sync", dryRun: true)} — preview an application-scoped manifest before applying it.");
            }

            return ToolOutcome.Ok(
                data,
                $"World: {definitions.Count} component definition(s), {examples.Count} entity example(s) shown.",
                [.. nextSteps]);
        });
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
                        "query(kind: \"entities\", nameQuery: \"...\") — search by name, or query(kind: \"world\") to see what exists.",
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
                    "query(kind: \"capabilities\") — inspect the current application-scoped write contracts before changing state.");
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
                        ? "query(kind: \"entities\") — clear the filters; entities exist, just not matching this."
                        : "The legacy world is empty. query(kind: \"system.applications\") — select a current application before creating state.");
            }

            return ToolOutcome.Ok(
                new { Entities = results },
                $"Found {results.Count} entity(ies).",
                $"query(kind: \"entities\", id: \"{results[0].Id}\") — read one in full before changing it.");
        });
}
