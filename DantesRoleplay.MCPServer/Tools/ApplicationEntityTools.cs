using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// Read adapter for an application's isolated ECS state.  The older generic world store is kept
/// for legacy world queries; applicationId plus stateSpaceId deliberately selects this adapter.
/// </summary>
public sealed class ApplicationEntityTools
{
    public async Task<ToolEnvelope> GetEntitiesAsync(
        IApplicationRegistry applications,
        IStateSpaceRegistry stateSpaces,
        IEntityComponentStore entities,
        IOperationLog log,
        string applicationId,
        string stateSpaceId,
        string[]? ids,
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

            if (ids is not { Length: > 0 })
            {
                return ToolOutcome.Fail(
                    "ENTITY_ID_REQUIRED",
                    "Application entity reads require id or ids.",
                    "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
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
}
