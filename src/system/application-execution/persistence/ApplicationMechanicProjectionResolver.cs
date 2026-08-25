using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationExecution;

public sealed class ApplicationMechanicProjectionResolver(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces) : IApplicationMechanicProjectionResolver
{
    private sealed record Node(string ContainerId, string Id, string Name, string Slot);

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
        var stateSpace = stateSpaces.Get(stateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != applicationId)
            return ProjectionResult.Failed("STATE_SPACE_APPLICATION_MISMATCH: The state space does not belong to the requested application.");
        if (!ActionInput.TryValidateObject(inputJson, out var inputProblem))
            problems.Add($"INVALID_INPUT: {inputProblem}");
        problems.AddRange(requirements.ProjectionProblems().Select(value => $"INVALID_PROJECTION_REQUIREMENTS: {value}"));
        foreach (var supplied in roleAssignments.Keys.Where(value => !requirements.Roles.ContainsKey(value)))
            problems.Add($"UNKNOWN_ROLE: This mechanic does not declare role '{supplied}'.");

        var needed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (role, requirement) in requirements.Roles)
        {
            if (roleAssignments.TryGetValue(role, out var entityId) && !string.IsNullOrWhiteSpace(entityId))
                needed[role] = entityId.Trim();
            else if (!requirement.Optional)
                problems.Add($"MISSING_REQUIRED_ROLE: Role '{role}' is required.");
        }
        var requiredLocalIds = requirements.Roles.Values
            .SelectMany(value => value.Components.Concat(value.ContentComponentIds ?? [])
                .Concat((value.ComponentReferences ?? []).SelectMany(reference =>
                    new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds))))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var localId in requiredLocalIds)
        {
            if (!mapping.Components.TryGetValue(localId, out var reference))
            {
                problems.Add($"COMPONENT_MAPPING_MISSING: No exact application component maps '{localId}'.");
                continue;
            }
            try
            {
                reference.Validate();
                var owners = stateSpace!.ApplicationRevision.BaseApplications.Prepend(applicationId).ToArray();
                var owner = owners.FirstOrDefault(candidate =>
                    reference.QualifiedTypeId.StartsWith(candidate.Value + ".", StringComparison.Ordinal));
                if (owner is null) throw new ArgumentException("The component type owner is outside the state-space application/base set.");
                ComponentTypeIdentifier.Validate(owner, reference.QualifiedTypeId);
            }
            catch (ArgumentException exception) { problems.Add($"COMPONENT_MAPPING_INVALID: {exception.Message}"); }
        }
        if (problems.Count > 0) return new(null, problems);
        if (needed.Count == 0)
            return new(new MechanicProjection { Input = inputJson, Seed = seed }, []);

        var allEntities = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.DeletedAtUtc == null)
            .OrderBy(value => value.Id).ToArrayAsync(cancellationToken);
        var entities = allEntities.ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (var (role, entityId) in needed)
            if (!entities.ContainsKey(entityId)) problems.Add($"UNKNOWN_ENTITY: Role '{role}' names unavailable entity '{entityId}'.");
        if (problems.Count > 0) return new(null, problems);

        var componentRows = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId).ToArrayAsync(cancellationToken);
        var reverseComponents = mapping.Components.ToDictionary(value =>
            (value.Value.QualifiedTypeId, value.Value.TypeVersion, value.Value.SchemaHash), value => value.Key);
        var components = componentRows
            .Where(value => reverseComponents.ContainsKey((value.QualifiedTypeId, value.TypeVersion, value.SchemaHash)))
            .GroupBy(value => value.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToDictionary(
                value => reverseComponents[(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash)],
                value => value.Data, StringComparer.Ordinal), StringComparer.Ordinal);
        var containments = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId).ToArrayAsync(cancellationToken);
        var containerOf = containments.ToDictionary(value => value.ContainedEntityId, StringComparer.Ordinal);
        var contents = containments.GroupBy(value => value.ContainerEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => new Node(
                value.ContainerEntityId, value.ContainedEntityId,
                entities.TryGetValue(value.ContainedEntityId, out var entity) ? entity.Name : "",
                value.Slot)).Where(value => entities.ContainsKey(value.Id)).ToList(), StringComparer.Ordinal);
        var relationships = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId).ToArrayAsync(cancellationToken);
        var reverseRelationships = mapping.Relationships.ToDictionary(value => value.Value, value => value.Key, StringComparer.Ordinal);

        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (role, entityId) in needed)
        {
            var requirement = requirements.Roles[role];
            foreach (var reference in requirement.ComponentReferences ?? [])
            {
                CollectReference(entityId, role, reference);
                foreach (var child in Descendants(entityId, requirement.ContentsDepth ?? 1, contents))
                    CollectReference(child.Id, role, reference);
            }
        }
        var referenceProjection = new Dictionary<string, ReferencedEntityProjection>(StringComparer.Ordinal);
        foreach (var (entityId, localIds) in references)
        {
            if (!entities.ContainsKey(entityId) || !components.TryGetValue(entityId, out var values)
                || localIds.Any(value => !values.ContainsKey(value)))
            {
                problems.Add($"COMPONENT_REFERENCE_TARGET_MISSING: Target '{entityId}' is unavailable or incomplete.");
                continue;
            }
            referenceProjection[entityId] = new(entityId, values
                .Where(value => localIds.Contains(value.Key)).ToDictionary(StringComparer.Ordinal));
        }

        var projection = new MechanicProjection
        {
            Input = inputJson,
            Seed = seed,
            References = referenceProjection
        };
        foreach (var (role, entityId) in needed)
        {
            var requirement = requirements.Roles[role];
            components.TryGetValue(entityId, out var values);
            values ??= new Dictionary<string, string>(StringComparer.Ordinal);
            containerOf.TryGetValue(entityId, out var containment);
            IReadOnlyList<RelationshipProjection>? projectedRelationships = null;
            if (requirement.IncludeRelationships)
            {
                var touching = relationships.Where(value => value.FromEntityId == entityId || value.ToEntityId == entityId).ToArray();
                foreach (var edge in touching)
                    if (!reverseRelationships.ContainsKey(edge.QualifiedKind))
                        problems.Add($"RELATIONSHIP_MAPPING_MISSING: No local relationship kind maps '{edge.QualifiedKind}'.");
                projectedRelationships = touching.Where(value => reverseRelationships.ContainsKey(value.QualifiedKind))
                    .OrderBy(value => reverseRelationships[value.QualifiedKind], StringComparer.Ordinal)
                    .ThenBy(value => value.FromEntityId, StringComparer.Ordinal).ThenBy(value => value.ToEntityId, StringComparer.Ordinal)
                    .Select(value => new RelationshipProjection(value.FromEntityId, value.ToEntityId,
                        reverseRelationships[value.QualifiedKind], value.Data)).ToArray();
            }
            projection.Roles[role] = new EntityProjection(
                entityId,
                entities[entityId].Name,
                values.Where(value => requirement.Components.Contains(value.Key, StringComparer.Ordinal))
                    .ToDictionary(StringComparer.Ordinal),
                containment?.ContainerEntityId,
                containment?.Slot ?? "",
                requirement.IncludeContents ? BuildContents(entityId, requirement.ContentsDepth ?? 1,
                    requirement.ContentComponentIds ?? [], requirement.ContentsDepth is not null
                        || (requirement.ContentComponentIds?.Count ?? 0) > 0,
                    contents, components, role, problems) : null,
                projectedRelationships);
        }
        return problems.Count == 0 ? new(projection, []) : new(null, problems);

        void CollectReference(string entityId, string role, ComponentReferenceRequirement reference)
        {
            if (!components.TryGetValue(entityId, out var values)
                || !values.TryGetValue(reference.SourceComponentId, out var raw)) return;
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(reference.Field, out var field)
                    || field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString()))
                {
                    problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' component '{reference.SourceComponentId}' lacks string field '{reference.Field}'.");
                    return;
                }
                var target = field.GetString()!.Trim();
                if (!references.TryGetValue(target, out var ids)) references[target] = ids = new(StringComparer.Ordinal);
                ids.UnionWith(reference.TargetComponentIds);
            }
            catch (JsonException) { problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' contains invalid JSON."); }
        }
    }

    private static IEnumerable<Node> Descendants(string root, int depth, IReadOnlyDictionary<string, List<Node>> contents)
    {
        if (depth < 1 || !contents.TryGetValue(root, out var children)) yield break;
        foreach (var child in children)
        {
            yield return child;
            foreach (var nested in Descendants(child.Id, depth - 1, contents)) yield return nested;
        }
    }

    private static IReadOnlyList<ContainedProjection> BuildContents(
        string root,
        int depth,
        IReadOnlyList<string> allowed,
        bool enforceNodeLimit,
        IReadOnlyDictionary<string, List<Node>> contents,
        IReadOnlyDictionary<string, Dictionary<string, string>> components,
        string role,
        List<string> problems)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var count = 0;
        IReadOnlyList<ContainedProjection> Build(string container, int remaining)
        {
            if (!contents.TryGetValue(container, out var children)) return [];
            var result = new List<ContainedProjection>();
            foreach (var child in children.OrderBy(value => value.Name, StringComparer.Ordinal)
                         .ThenBy(value => value.Slot, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal))
            {
                if (!visited.Add(child.Id)) { problems.Add($"CONTAINMENT_PROJECTION_CYCLE: Role '{role}' reaches '{child.Id}'."); return []; }
                count++;
                if (enforceNodeLimit && count > ProjectionLimits.MaxContainedNodes) { problems.Add($"CONTAINMENT_PROJECTION_LIMIT: Role '{role}' exceeds the node limit."); return []; }
                IReadOnlyDictionary<string, string>? selected = allowed.Count == 0 ? null
                    : components.TryGetValue(child.Id, out var values)
                        ? values.Where(value => allowed.Contains(value.Key, StringComparer.Ordinal)).ToDictionary(StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                var nested = remaining > 1 ? Build(child.Id, remaining - 1) : null;
                visited.Remove(child.Id);
                result.Add(new(child.Id, child.Name, child.Slot, selected, nested));
            }
            return result;
        }
        return Build(root, depth);
    }
}
