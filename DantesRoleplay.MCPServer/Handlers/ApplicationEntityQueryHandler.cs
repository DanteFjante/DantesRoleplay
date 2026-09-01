using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// Read adapter for an application's isolated ECS state.  The older generic world store is kept
/// for legacy world queries; applicationId plus stateSpaceId deliberately selects this adapter.
/// </summary>
public sealed class ApplicationEntityQueryHandler
{
    /// <summary>
    /// Resolves exact IDs across the bounded registered application state spaces. This is the
    /// compatibility path for callers that previously treated <c>entities</c> as one global
    /// namespace; name and component searches remain explicitly scoped.
    /// </summary>
    public async Task<ToolEnvelope> FindExactEntitiesAsync(
        IApplicationRegistry applications,
        IStateSpaceRegistry stateSpaces,
        IEntityComponentStore entities,
        IOperationLog log,
        string[] ids,
        int limit,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "find_application_entities", async () =>
        {
            var requested = ids.Distinct(StringComparer.Ordinal).Take(limit).ToArray();
            var matches = new List<(ApplicationIdentifier Application, StateSpaceView StateSpace, EcsEntityView Entity)>();
            foreach (var application in applications.List(100))
            {
                string? after = null;
                do
                {
                    var page = stateSpaces.ListPage(application.Id, after, 100);
                    foreach (var stateSpace in page.StateSpaces)
                    foreach (var entityId in requested)
                    {
                        var entity = await entities.GetEntityAsync(stateSpace.StateSpaceId, entityId, cancellationToken);
                        if (entity is not null) matches.Add((application.Id, stateSpace, entity));
                    }
                    after = page.NextStateSpaceId;
                } while (after is not null);
            }

            if (matches.Count == 0)
            {
                return ToolOutcome.Fail(
                    "UNKNOWN_ENTITY",
                    $"None of these entity ids exist in the live application state spaces: {string.Join(", ", requested)}.",
                    "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
                    $"No application entities found for {requested.Length} id(s).");
            }

            var ambiguous = matches.GroupBy(value => value.Entity.EntityId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (ambiguous.Length > 0)
            {
                return ToolOutcome.Fail(
                    "ENTITY_AMBIGUOUS",
                    $"These entity ids occur in more than one application state space: {string.Join(", ", ambiguous)}.",
                    "Repeat the query with applicationId and stateSpaceId.",
                    "Rejected an ambiguous unscoped application entity query.");
            }

            var detailed = new List<object>();
            foreach (var match in matches)
            {
                var components = await entities.ListComponentsAsync(
                    match.StateSpace.StateSpaceId, match.Entity.EntityId, null, 100, cancellationToken);
                detailed.Add(new
                {
                    ApplicationId = match.Application.Value,
                    StateSpaceId = match.StateSpace.StateSpaceId,
                    match.Entity.EntityId,
                    match.Entity.Name,
                    match.Entity.Revision,
                    Components = components.Components.Select(component => new
                    {
                        component.Type.QualifiedTypeId,
                        component.Type.TypeVersion,
                        component.Type.SchemaHash,
                        Value = JsonSerializer.Deserialize<JsonElement>(component.ValueJson),
                        component.Revision
                    }).ToArray()
                });
            }

            var missing = requested.Except(matches.Select(value => value.Entity.EntityId), StringComparer.Ordinal).ToArray();
            return ToolOutcome.Ok(
                new { Entities = detailed, Missing = missing },
                missing.Length == 0
                    ? $"Fetched {detailed.Count} live application entity(ies) in full."
                    : $"Fetched {detailed.Count}; {missing.Length} id(s) were not found.");
        });

    public async Task<ToolEnvelope> GetEntitiesAsync(
        IApplicationRegistry applications,
        IStateSpaceRegistry stateSpaces,
        IEntityComponentStore entities,
        IOperationLog log,
        string applicationId,
        string stateSpaceId,
        string[]? ids,
        string? nameQuery,
        string? withDefinitionId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "get_application_entities", async () =>
        {
            ApplicationIdentifier application;
            try
            {
                application = ApplicationIdentifier.Parse(applicationId);
            }
            catch (ArgumentException)
            {
                return ToolOutcome.Fail(
                    "APPLICATION_UNKNOWN",
                    "The application is unknown.",
                    "query(kind: \"system.applications\")",
                    "Rejected an invalid application entity query.");
            }

            var stateSpace = stateSpaces.Get(stateSpaceId);
            if (applications.Get(application) is null || stateSpace is null
                || stateSpace.ApplicationRevision.ApplicationId != application)
            {
                return ToolOutcome.Fail(
                    "STATE_SPACE_WRONG_APPLICATION",
                    "The requested state space is unavailable for this application.",
                    "query(kind: \"system.applications\")",
                    "Rejected an application/state-space mismatch.");
            }

            var search = Trim(nameQuery);
            var componentFilter = Trim(withDefinitionId);
            if (ids is not { Length: > 0 } && (search is not null || componentFilter is not null))
            {
                if (entities is not IEntityComponentSearchStore searchable)
                {
                    return ToolOutcome.Fail(
                        "ENTITY_SEARCH_UNAVAILABLE",
                        "This application entity reader cannot search; it resolves exact IDs only.",
                        "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
                        "Rejected an application entity search against a reader without search support.");
                }

                if (search is { Length: > 200 } || componentFilter is { Length: > 200 })
                {
                    return ToolOutcome.Fail(
                        "ENTITY_SEARCH_INVALID",
                        "nameQuery and withDefinitionId may not exceed 200 characters.",
                        "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", nameQuery: \"...\")",
                        "Rejected an overlong application entity search term.");
                }

                var found = await searchable.SearchEntitiesAsync(
                    stateSpaceId,
                    new EcsEntitySearch(search, componentFilter, null, Math.Clamp(limit, 1, 100)),
                    cancellationToken);
                var summaries = new List<object>();
                foreach (var match in found.Entities)
                {
                    var types = await entities.ListComponentsAsync(
                        stateSpaceId, match.EntityId, null, 100, cancellationToken);
                    summaries.Add(new
                    {
                        match.EntityId,
                        match.Name,
                        match.Revision,
                        ComponentTypeIds = types.Components
                            .Select(component => component.Type.QualifiedTypeId).ToArray()
                    });
                }

                return ToolOutcome.Ok(
                    new
                    {
                        ApplicationId = application.Value,
                        StateSpaceId = stateSpaceId,
                        Entities = summaries,
                        NextEntityId = found.NextEntityId
                    },
                    summaries.Count == 0
                        ? "No application entity matched this search."
                        : $"Matched {summaries.Count} application entity(ies); read one in full with id.");
            }

            if (ids is not { Length: > 0 })
            {
                return ToolOutcome.Fail(
                    "ENTITY_ID_REQUIRED",
                    "Application entity reads require id, ids, nameQuery, or withDefinitionId.",
                    "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", nameQuery: \"...\")",
                    "Rejected an unbounded application entity read.");
            }

            var requested = ids.Distinct(StringComparer.Ordinal).Take(limit).ToArray();
            var detailed = new List<object>();
            var foundIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entityId in requested)
            {
                var entity = await entities.GetEntityAsync(stateSpaceId, entityId, cancellationToken);
                if (entity is null) continue;
                foundIds.Add(entity.EntityId);

                var components = await entities.ListComponentsAsync(
                    stateSpaceId, entity.EntityId, null, 100, cancellationToken);
                detailed.Add(new
                {
                    entity.EntityId,
                    entity.Name,
                    entity.Revision,
                    Components = components.Components.Select(component => new
                    {
                        component.Type.QualifiedTypeId,
                        component.Type.TypeVersion,
                        component.Type.SchemaHash,
                        Value = JsonSerializer.Deserialize<JsonElement>(component.ValueJson),
                        component.Revision
                    }).ToArray()
                });
            }

            var missing = requested.Except(foundIds, StringComparer.Ordinal).ToArray();
            if (detailed.Count == 0)
            {
                return ToolOutcome.Fail(
                    "UNKNOWN_ENTITY",
                    $"None of these entity ids exist in '{stateSpaceId}': {string.Join(", ", requested)}.",
                    "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
                    $"No application entities found for {requested.Length} id(s).");
            }

            return ToolOutcome.Ok(
                new { ApplicationId = application.Value, StateSpaceId = stateSpaceId, Entities = detailed, Missing = missing },
                missing.Length == 0
                    ? $"Fetched {detailed.Count} application entity(ies) in full."
                    : $"Fetched {detailed.Count}; {missing.Length} id(s) were not found.");
        });

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
