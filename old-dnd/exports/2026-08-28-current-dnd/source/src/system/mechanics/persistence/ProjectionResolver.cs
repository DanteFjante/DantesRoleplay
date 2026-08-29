using DantesRoleplay.Actions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Materialises a mechanic's declared requirements in one pass.
///
/// "One query" in §3.6a is the intent rather than a literal count — it is two here, and both are
/// batched across every role at once. What matters is that the number of round trips does not grow
/// with the number of participants, because the alternative is the N+1 pattern that made
/// TravelRoleplay's rules slow in exactly the situations that had the most going on.
/// </summary>
public sealed class ProjectionResolver(DantesRoleplayDbContext db) : IProjectionResolver
{
    private readonly DantesRoleplayDbContext _db = db;

    private sealed record ContainmentNode(string ContainerId, string Id, string Name, string Slot);

    public async Task<ProjectionResult> ResolveAsync(
        MechanicRequirements requirements,
        IReadOnlyDictionary<string, string> roleAssignments,
        string input = "{}",
        long seed = 0,
        CancellationToken cancellationToken = default)
    {
        requirements ??= new MechanicRequirements();
        roleAssignments ??= new Dictionary<string, string>();

        var problems = new List<string>();

        if (!ActionInput.TryValidateObject(input, out var inputProblem))
        {
            problems.Add($"INVALID_INPUT: {inputProblem}");
        }

        foreach (var problem in requirements.ProjectionProblems())
            problems.Add($"INVALID_PROJECTION_REQUIREMENTS: {problem}");

        // A role the mechanic does not declare is a caller misunderstanding, not a harmless extra.
        // Passing "target" to a rule that never mentions one usually means the wrong mechanic was
        // chosen, and silently dropping it would turn that into a puzzling result instead.
        foreach (var supplied in roleAssignments.Keys)
        {
            if (!requirements.Roles.ContainsKey(supplied))
            {
                problems.Add(
                    $"UNKNOWN_ROLE: This mechanic does not have a role called '{supplied}'. It takes: " +
                    $"{(requirements.Roles.Count == 0 ? "(none)" : string.Join(", ", requirements.Roles.Keys))}.");
            }
        }

        var needed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (role, requirement) in requirements.Roles)
        {
            if (roleAssignments.TryGetValue(role, out var entityId) && !string.IsNullOrWhiteSpace(entityId))
            {
                needed[role] = entityId.Trim();
                continue;
            }

            if (!requirement.Optional)
            {
                var wants = requirement.Components.Count == 0
                    ? "no components"
                    : string.Join(", ", requirement.Components);

                problems.Add(
                    $"MISSING_REQUIRED_ROLE: Role '{role}' is required and was not supplied. " +
                    $"{(requirement.Description.Length > 0 ? requirement.Description + " " : "")}" +
                    $"It reads: {wants}. Pass roles: {{\"{role}\": \"<entityId>\"}}.");
            }
        }

        if (problems.Count > 0)
        {
            return new ProjectionResult(null, problems);
        }

        if (needed.Count == 0)
        {
            return new ProjectionResult(
                new MechanicProjection { Input = input, Seed = seed },
                []);
        }

        var wantedIds = needed.Values.Distinct(StringComparer.Ordinal).ToList();

        // Every component of every wanted entity comes back, and the filtering to what each ROLE
        // declared happens below. Filtering in SQL would need one query per role, and the whole
        // point of a declared projection is that materialising it is a fixed cost.
        var entities = await _db.Entities
            .AsNoTracking()
            .Where(e => wantedIds.Contains(e.Id) && e.DeletedAt == null)
            .Select(e => new
            {
                e.Id,
                e.Name,
                Components = e.Components.Select(c => new { c.DefinitionId, c.Data }).ToList()
            })
            .ToListAsync(cancellationToken);

        var byId = entities.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var (role, entityId) in needed)
        {
            if (!byId.ContainsKey(entityId))
            {
                problems.Add(
                    $"UNKNOWN_ENTITY: Role '{role}' names entity '{entityId}', which does not exist or was deleted. " +
                    "Check it with get_entities.");
            }
        }

        if (problems.Count > 0)
        {
            return new ProjectionResult(null, problems);
        }

        var containers = await _db.Containments
            .AsNoTracking()
            .Where(c => wantedIds.Contains(c.ContainedId))
            .Select(c => new { c.ContainedId, c.ContainerId, c.Slot })
            .ToListAsync(cancellationToken);

        var containerOf = containers.ToDictionary(c => c.ContainedId, StringComparer.Ordinal);

        var requestedContentsDepth = needed
            .Where(pair => requirements.Roles[pair.Key].IncludeContents)
            .Select(pair => new { EntityId = pair.Value, Depth = requirements.Roles[pair.Key].ContentsDepth ?? 1 })
            .GroupBy(request => request.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(request => request.Depth), StringComparer.Ordinal);

        var contentsByContainer = new Dictionary<string, List<ContainmentNode>>(StringComparer.Ordinal);
        var descendantIds = new HashSet<string>(StringComparer.Ordinal);
        var frontier = requestedContentsDepth;

        // Each level is a shared set query. The fixed, generic traversal bound keeps mechanics
        // from receiving an unbounded or lazy view of the world.
        for (var depth = 1; depth <= ProjectionLimits.MaxContentsDepth && frontier.Count > 0; depth++)
        {
            var containerIds = frontier.Keys.ToList();
            var rows = await _db.Containments
                .AsNoTracking()
                .Where(containment => containerIds.Contains(containment.ContainerId))
                .Join(
                    _db.Entities.Where(entity => entity.DeletedAt == null),
                    containment => containment.ContainedId,
                    entity => entity.Id,
                    (containment, entity) => new ContainmentNode(containment.ContainerId, entity.Id, entity.Name, containment.Slot))
                .ToListAsync(cancellationToken);

            var next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!contentsByContainer.TryGetValue(row.ContainerId, out var children))
                {
                    children = [];
                    contentsByContainer[row.ContainerId] = children;
                }
                children.Add(row);
                descendantIds.Add(row.Id);

                if (frontier[row.ContainerId] > 1)
                {
                    var remaining = frontier[row.ContainerId] - 1;
                    if (!next.TryGetValue(row.Id, out var known) || remaining > known)
                        next[row.Id] = remaining;
                }
            }
            frontier = next;
        }

        var contentComponentIds = requirements.Roles.Values
            .SelectMany(requirement => requirement.ContentComponentIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var contentComponents = descendantIds.Count == 0 || contentComponentIds.Count == 0
            ? []
            : await _db.Components
                .AsNoTracking()
                .Where(component => descendantIds.Contains(component.EntityId) && contentComponentIds.Contains(component.DefinitionId))
                .Select(component => new { component.EntityId, component.DefinitionId, component.Data })
                .ToListAsync(cancellationToken);

        var contentComponentsByEntity = contentComponents
            .GroupBy(component => component.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal),
                StringComparer.Ordinal);

        // Component references are declared data access, not an implicit graph walk. A role names
        // the component field that holds an entity id and the precise components visible on that
        // target. This lets rules follow durable definition references without copying static
        // facts onto campaign entities or granting general store access to JavaScript.
        var referenceTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void CollectReference(IReadOnlyDictionary<string, string> components, ComponentReferenceRequirement reference, string role, string entityId)
        {
            if (!components.TryGetValue(reference.SourceComponentId, out var raw)) return;
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(reference.Field, out var value))
                {
                    problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' entity '{entityId}' component '{reference.SourceComponentId}' lacks reference field '{reference.Field}'.");
                    return;
                }

                var targetId = value.ValueKind == JsonValueKind.String ? value.GetString() :
                    value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 &&
                    value.TryGetProperty("entityId", out var referencedId) && referencedId.ValueKind == JsonValueKind.String
                        ? referencedId.GetString() : null;
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' entity '{entityId}' component '{reference.SourceComponentId}' field '{reference.Field}' is not an entity reference.");
                    return;
                }
                targetId = targetId.Trim();
                if (!referenceTargets.TryGetValue(targetId, out var targetComponents))
                {
                    targetComponents = new HashSet<string>(StringComparer.Ordinal);
                    referenceTargets[targetId] = targetComponents;
                }
                targetComponents.UnionWith(reference.TargetComponentIds);
            }
            catch (JsonException)
            {
                problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' entity '{entityId}' component '{reference.SourceComponentId}' is not JSON object data.");
            }
        }

        foreach (var (role, entityId) in needed)
        {
            var requirement = requirements.Roles[role];
            foreach (var reference in requirement.ComponentReferences ?? [])
            {
                var rootComponents = byId[entityId].Components
                    .Where(component => component.DefinitionId == reference.SourceComponentId)
                    .ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);
                CollectReference(rootComponents, reference, role, entityId);

                foreach (var (containedId, components) in contentComponentsByEntity)
                    CollectReference(components, reference, role, containedId);
            }
        }

        var referenceComponentRows = referenceTargets.Count == 0
            ? []
            : await _db.Components
                .AsNoTracking()
                .Where(component => referenceTargets.Keys.Contains(component.EntityId) &&
                    component.Entity != null && component.Entity.DeletedAt == null)
                .Select(component => new { component.EntityId, component.DefinitionId, component.Data })
                .ToListAsync(cancellationToken);

        referenceComponentRows = referenceComponentRows
            .Where(component => referenceTargets[component.EntityId].Contains(component.DefinitionId))
            .ToList();

        var referenced = referenceComponentRows
            .GroupBy(component => component.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new ReferencedEntityProjection(group.Key,
                    group.ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal)),
                StringComparer.Ordinal);

        foreach (var (targetId, expectedComponents) in referenceTargets)
        {
            if (!referenced.TryGetValue(targetId, out var target) || expectedComponents.Any(component => !target.Components.ContainsKey(component)))
                problems.Add($"COMPONENT_REFERENCE_TARGET_MISSING: Declared reference target '{targetId}' is missing or lacks its required components.");
        }

        var relationshipsWanted = needed
            .Where(pair => requirements.Roles[pair.Key].IncludeRelationships)
            .Select(pair => pair.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Relationship projection is deliberately opt-in just like contained entities. The joins
        // exclude soft-deleted endpoints, so a rule never receives a dangling edge to something
        // it could not itself name as a role.
        var relationships = relationshipsWanted.Count == 0
            ? []
            : await _db.Relationships
                .AsNoTracking()
                .Where(r => relationshipsWanted.Contains(r.FromEntityId) || relationshipsWanted.Contains(r.ToEntityId))
                .Join(
                    _db.Entities.Where(e => e.DeletedAt == null),
                    relationship => relationship.FromEntityId,
                    entity => entity.Id,
                    (relationship, _) => relationship)
                .Join(
                    _db.Entities.Where(e => e.DeletedAt == null),
                    relationship => relationship.ToEntityId,
                    entity => entity.Id,
                    (relationship, _) => new { relationship.FromEntityId, relationship.ToEntityId, relationship.Kind, relationship.Data })
                .ToListAsync(cancellationToken);

        var relatedTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (role, entityId) in needed)
        foreach (var declaration in requirements.Roles[role].RelationshipComponents ?? [])
        foreach (var relationship in relationships.Where(value => value.Kind == declaration.Kind &&
                     (declaration.Direction == "either" &&
                         (value.FromEntityId == entityId || value.ToEntityId == entityId) ||
                      declaration.Direction == "outgoing" && value.FromEntityId == entityId ||
                      declaration.Direction == "incoming" && value.ToEntityId == entityId)))
        {
            var endpointId = relationship.FromEntityId == entityId
                ? relationship.ToEntityId : relationship.FromEntityId;
            if (!relatedTargets.TryGetValue(endpointId, out var componentIds))
                relatedTargets[endpointId] = componentIds = new(StringComparer.Ordinal);
            componentIds.UnionWith(declaration.TargetComponentIds);
        }
        var relatedEntities = relatedTargets.Count == 0
            ? []
            : await _db.Entities.AsNoTracking()
                .Where(entity => relatedTargets.Keys.Contains(entity.Id) && entity.DeletedAt == null)
                .Select(entity => new
                {
                    entity.Id,
                    entity.Name,
                    Components = entity.Components
                        .Select(component => new { component.DefinitionId, component.Data }).ToList()
                }).ToListAsync(cancellationToken);
        var relatedById = relatedEntities.ToDictionary(value => value.Id, StringComparer.Ordinal);

        var projection = new MechanicProjection
        {
            Input = input,
            Seed = seed,
            References = referenced
        };

        foreach (var (role, entityId) in needed)
        {
            var requirement = requirements.Roles[role];
            var entity = byId[entityId];

            // THE filter. A mechanic sees the components it declared and nothing else — including
            // when the entity happens to carry a dozen others. Requirements that understate what a
            // rule reads would make the supervision view a lie, so they are also what is enforced.
            var declared = entity.Components
                .Where(c => requirement.Components.Contains(c.DefinitionId, StringComparer.Ordinal))
                .ToDictionary(c => c.DefinitionId, c => c.Data, StringComparer.Ordinal);

            containerOf.TryGetValue(entityId, out var containment);

            projection.Roles[role] = new EntityProjection(
                entity.Id,
                entity.Name,
                declared,
                containment?.ContainerId,
                containment?.Slot ?? string.Empty,
                requirement.IncludeContents
                    ? BuildContainedProjection(entityId, requirement.ContentsDepth ?? 1,
                        requirement.ContentComponentIds ?? [], requirement.ContentsDepth is not null || (requirement.ContentComponentIds?.Count ?? 0) > 0,
                        contentsByContainer, contentComponentsByEntity, role, problems)
                    : null,
                requirement.IncludeRelationships
                    ? relationships
                        .Where(r => r.FromEntityId == entityId || r.ToEntityId == entityId)
                        .OrderBy(r => r.Kind, StringComparer.Ordinal)
                        .ThenBy(r => r.FromEntityId, StringComparer.Ordinal)
                        .ThenBy(r => r.ToEntityId, StringComparer.Ordinal)
                        .Select(r => new RelationshipProjection(r.FromEntityId, r.ToEntityId, r.Kind, r.Data))
                        .ToList()
                    : null,
                BuildRelated(role, entityId, requirement));
        }

        return problems.Count == 0
            ? new ProjectionResult(projection, [])
            : new ProjectionResult(null, problems);

        IReadOnlyList<RelatedEntityProjection>? BuildRelated(
            string role, string entityId, RoleRequirement requirement)
        {
            var declarations = requirement.RelationshipComponents ?? [];
            if (declarations.Count == 0) return null;
            var result = new List<RelatedEntityProjection>();
            foreach (var declaration in declarations)
            foreach (var relationship in relationships.Where(value => value.Kind == declaration.Kind &&
                         (declaration.Direction == "either" &&
                             (value.FromEntityId == entityId || value.ToEntityId == entityId) ||
                          declaration.Direction == "outgoing" && value.FromEntityId == entityId ||
                          declaration.Direction == "incoming" && value.ToEntityId == entityId)))
            {
                var endpointId = relationship.FromEntityId == entityId
                    ? relationship.ToEntityId : relationship.FromEntityId;
                if (!relatedById.TryGetValue(endpointId, out var endpoint))
                {
                    problems.Add($"RELATIONSHIP_COMPONENT_TARGET_MISSING: Related endpoint '{endpointId}' is unavailable.");
                    continue;
                }
                var components = endpoint.Components.ToDictionary(value => value.DefinitionId,
                    value => value.Data, StringComparer.Ordinal);
                if (declaration.TargetComponentIds.Any(value => !components.ContainsKey(value)))
                {
                    problems.Add($"RELATIONSHIP_COMPONENT_TARGET_MISSING: Related endpoint '{endpointId}' is incomplete.");
                    continue;
                }
                result.Add(new(endpointId, endpoint.Name, relationship.FromEntityId,
                    relationship.ToEntityId, relationship.Kind, relationship.Data,
                    components.Where(value => declaration.TargetComponentIds.Contains(value.Key,
                        StringComparer.Ordinal)).ToDictionary(StringComparer.Ordinal)));
            }
            if (result.Count > ProjectionLimits.MaxRelatedNodes)
            {
                problems.Add($"RELATIONSHIP_COMPONENT_LIMIT_EXCEEDED: Role '{role}' exceeds the related-node limit.");
                return null;
            }
            return result.OrderBy(value => value.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.FromEntityId, StringComparer.Ordinal)
                .ThenBy(value => value.ToEntityId, StringComparer.Ordinal)
                .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
        }
    }

    private static IReadOnlyList<ContainedProjection> BuildContainedProjection(
        string rootId,
        int depth,
        IReadOnlyList<string> allowedComponentIds,
        bool enforceNodeLimit,
        IReadOnlyDictionary<string, List<ContainmentNode>> contentsByContainer,
        IReadOnlyDictionary<string, Dictionary<string, string>> componentsByEntity,
        string role,
        List<string> problems)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var count = 0;

        IReadOnlyList<ContainedProjection> Build(string containerId, int remainingDepth)
        {
            if (!contentsByContainer.TryGetValue(containerId, out var children)) return [];
            var projection = new List<ContainedProjection>(children.Count);
            foreach (var child in children
                         .OrderBy(node => node.Name, StringComparer.Ordinal)
                         .ThenBy(node => node.Slot, StringComparer.Ordinal)
                         .ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                if (!visited.Add(child.Id))
                {
                    problems.Add($"CONTAINMENT_PROJECTION_CYCLE: Role '{role}' reaches containment cycle at '{child.Id}'.");
                    return [];
                }

                count++;
                if (enforceNodeLimit && count > ProjectionLimits.MaxContainedNodes)
                {
                    problems.Add($"CONTAINMENT_PROJECTION_LIMIT: Role '{role}' projects more than {ProjectionLimits.MaxContainedNodes} contained entities.");
                    return [];
                }

                IReadOnlyDictionary<string, string>? declaredComponents = null;
                if (allowedComponentIds.Count > 0)
                {
                    declaredComponents = componentsByEntity.TryGetValue(child.Id, out var componentData)
                        ? componentData
                            .Where(component => allowedComponentIds.Contains(component.Key, StringComparer.Ordinal))
                            .ToDictionary(component => component.Key, component => component.Value, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                }

                var nested = remainingDepth > 1 ? Build(child.Id, remainingDepth - 1) : null;
                visited.Remove(child.Id);
                if (problems.Count > 0) return [];
                projection.Add(new ContainedProjection(child.Id, child.Name, child.Slot, declaredComponents, nested));
            }
            return projection;
        }

        return Build(rootId, depth);
    }
}
