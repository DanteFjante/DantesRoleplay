using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Supplies pure reducers with exact registered application objects. It interprets only generic
/// object declarations and opaque role bindings; application vocabulary remains in the catalog.
/// </summary>
public sealed class ApplicationMechanicObjectProjectionResolver(
    IProjectionDefinitionRegistry definitions,
    IProjectionMaterializer materializer,
    IProjectionCollectionMaterializer collections,
    IEntityComponentStore entities) : IApplicationMechanicObjectProjectionResolver
{
    public async Task<ProjectionResult> ResolveAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        MechanicRequirements requirements,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyDictionary<string, string> roleAssignments,
        string inputJson,
        long seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(mapping);
        roleAssignments ??= new Dictionary<string, string>();
        var problems = new List<string>();
        if (!MechanicInput.TryValidateObject(inputJson, out var inputProblem))
            problems.Add($"INVALID_INPUT: {inputProblem}");
        problems.AddRange(requirements.ProjectionProblems()
            .Select(value => $"INVALID_PROJECTION_REQUIREMENTS: {value}"));
        foreach (var supplied in roleAssignments.Keys.Where(value => !requirements.Roles.ContainsKey(value)))
            problems.Add($"UNKNOWN_ROLE: This mechanic does not declare role '{supplied}'.");
        foreach (var (role, requirement) in requirements.Roles)
            if (!requirement.Optional && (!roleAssignments.TryGetValue(role, out var value) ||
                                          string.IsNullOrWhiteSpace(value)))
                problems.Add($"MISSING_REQUIRED_ROLE: Role '{role}' is required.");
        foreach (var (name, declaration) in requirements.ObjectRoles)
            foreach (var mechanicRole in declaration.RoleBindings.Values)
                if (!roleAssignments.TryGetValue(mechanicRole, out var value) ||
                    string.IsNullOrWhiteSpace(value))
                    problems.Add($"MISSING_OBJECT_ROLE_BINDING: Object '{name}' requires role '{mechanicRole}'.");
        if (requirements.ObjectRoles.Count == 0)
            problems.Add("OBJECT_ROLE_REQUIRED: An object-based reducer must declare an exact object role.");
        if (problems.Count > 0) return new(null, problems);

        var projected = new Dictionary<string, MechanicObjectProjection>(StringComparer.Ordinal);
        var roleEntities = new Dictionary<string, EntityProjection>(StringComparer.Ordinal);
        var observed = new Dictionary<(string EntityId, string TypeId), MechanicComponentRevision>();
        var componentRevisions = new Dictionary<string, Dictionary<string, int?>>(StringComparer.Ordinal);
        var relationshipCollections = new List<MechanicRelationshipCollectionSnapshot>();
        try
        {
            foreach (var (name, declaration) in requirements.ObjectRoles.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var definition = definitions.Get(declaration.QualifiedId, declaration.Version);
                if (definition is null || definition.Owner != applicationId ||
                    definition.ContentHash != declaration.ContentFingerprint || definition.ObjectContract is null)
                    return ProjectionResult.Failed("OBJECT_ROLE_STALE: An exact registered reducer object is unavailable.");
                var objectBindings = declaration.RoleBindings.ToDictionary(
                    value => value.Key,
                    value => roleAssignments[value.Value],
                    StringComparer.Ordinal);
                var reference = definition.Reference;
                ProjectionMaterializationResult root;
                ProjectionCollectionMaterializationResult? collection = null;
                if (declaration.CollectionId is null)
                {
                    root = await materializer.MaterializeAsync(new(
                        stateSpaceId, reference, objectBindings), cancellationToken);
                }
                else
                {
                    collection = await collections.MaterializeAsync(new(
                        stateSpaceId, reference, objectBindings, declaration.CollectionId,
                        declaration.Perspective), cancellationToken);
                    if (!collection.Complete)
                        return ProjectionResult.Failed(
                            "OBJECT_ROLE_INCOMPLETE: A reducer cannot decide from a partial object collection.");
                    root = new(collection.Projection, collection.OutputJson, collection.SourceRevisions);
                }

                var identities = new Dictionary<string, MechanicObjectEntity>(StringComparer.Ordinal);
                foreach (var (objectRole, entityId) in objectBindings)
                {
                    var entity = await entities.GetEntityAsync(stateSpaceId, entityId, cancellationToken);
                    if (entity is null)
                        return ProjectionResult.Failed("OBJECT_ROLE_ENTITY_STALE: A reducer object role is unavailable.");
                    identities[objectRole] = new(entity.EntityId, entity.Name);
                    if (declaration.RoleBindings.TryGetValue(objectRole, out var mechanicRole))
                    {
                        if (roleEntities.TryGetValue(mechanicRole, out var prior) && prior.Id != entity.EntityId)
                            return ProjectionResult.Failed(
                                "OBJECT_ROLE_BINDING_CONFLICT: Reducer objects disagree about a mechanic role.");
                        roleEntities[mechanicRole] = new(entity.EntityId, entity.Name,
                            new Dictionary<string, string>(StringComparer.Ordinal));
                    }
                }

                using var value = JsonDocument.Parse(root.OutputJson);
                projected[name] = new(definition.QualifiedId, definition.Version,
                    definition.ContentHash, identities, value.RootElement.Clone());
                foreach (var source in root.SourceRevisions)
                {
                    var revision = new MechanicComponentRevision(source.EntityId,
                        source.Type.QualifiedTypeId, source.Type.TypeVersion, source.Type.SchemaHash,
                        source.Revision);
                    var key = (revision.EntityId, revision.QualifiedTypeId);
                    if (observed.TryGetValue(key, out var prior) && prior != revision)
                        return ProjectionResult.Failed(
                            "OBJECT_ROLE_SNAPSHOT_CONFLICT: Object inputs observed conflicting component revisions.");
                    observed[key] = revision;
                }

                var sourceRequired = definition.ObjectContract.Sources.ToDictionary(
                    value => value.InputId, value => value.Required, StringComparer.Ordinal);
                foreach (var input in definition.ComponentInputs)
                {
                    if (!objectBindings.TryGetValue(input.EntityRole, out var entityId)) continue;
                    var local = mapping.Components.FirstOrDefault(value => value.Value == input.Type).Key;
                    if (string.IsNullOrWhiteSpace(local)) continue;
                    var source = root.SourceRevisions.SingleOrDefault(value =>
                        value.EntityId == entityId && value.Type == input.Type);
                    if (source is null && sourceRequired[input.InputId])
                        return ProjectionResult.Failed(
                            "OBJECT_ROLE_COMPONENT_STALE: A required reducer object source is unavailable.");
                    if (!componentRevisions.TryGetValue(entityId, out var revisions))
                        componentRevisions[entityId] = revisions = new(StringComparer.Ordinal);
                    revisions[local] = source?.Revision;
                }

                if (collection is not null)
                {
                    var declaredCollection = definition.ObjectContract.Collections.Single(value =>
                        value.CollectionId == declaration.CollectionId);
                    var relationship = definition.ObjectContract.Relationships.Single(value =>
                        value.RelationshipId == declaredCollection.SourceId);
                    var incoming = relationship.Direction == "incoming";
                    var sourceRole = incoming ? relationship.ToRole : relationship.FromRole;
                    relationshipCollections.Add(new(relationship.QualifiedKind,
                        objectBindings[sourceRole], incoming, collection.RelationshipRevisions.Select(value =>
                            new MechanicRelationshipRevision(value.FromEntityId, value.ToEntityId,
                                value.QualifiedKind, value.Revision)).ToArray()));
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                          JsonException or UnauthorizedAccessException)
        {
            return ProjectionResult.Failed(
                "OBJECT_ROLE_MATERIALIZATION_FAILED: Exact reducer objects could not be materialized.");
        }

        return new(new MechanicProjection
        {
            StateSpaceId = stateSpaceId,
            Input = inputJson,
            Seed = seed,
            Roles = roleEntities,
            Objects = projected,
            ObservedComponents = observed.Values.OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .ThenBy(value => value.QualifiedTypeId, StringComparer.Ordinal).ToArray(),
            ComponentRevisions = componentRevisions,
            RelationshipCollections = relationshipCollections
        }, []);
    }
}
