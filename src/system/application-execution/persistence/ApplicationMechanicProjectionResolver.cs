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
            .SelectMany(value => value.Components.Concat(value.OptionalComponents ?? [])
                .Concat(value.ContentComponentIds ?? [])
                .Concat((value.ComponentReferences ?? []).SelectMany(reference =>
                    new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds)
                        .Concat(reference.OptionalTargetComponentIds ?? [])))
                .Concat((value.RelationshipComponents ?? []).SelectMany(reference =>
                    reference.TargetComponentIds)))
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
            return new(new MechanicProjection { StateSpaceId = stateSpaceId, Input = inputJson, Seed = seed }, []);

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
        var componentRevisions = componentRows
            .Where(value => reverseComponents.ContainsKey((value.QualifiedTypeId, value.TypeVersion, value.SchemaHash)))
            .GroupBy(value => value.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToDictionary(
                value => reverseComponents[(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash)],
                value => (int?)value.Revision, StringComparer.Ordinal), StringComparer.Ordinal);
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
        var requiredReferences = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
                || requiredReferences[entityId].Any(value => !values.ContainsKey(value)))
            {
                problems.Add($"COMPONENT_REFERENCE_TARGET_MISSING: Target '{entityId}' is unavailable or incomplete.");
                continue;
            }
            referenceProjection[entityId] = new(entityId, values
                .Where(value => localIds.Contains(value.Key)).ToDictionary(StringComparer.Ordinal));
        }

        var projection = new MechanicProjection
        {
            StateSpaceId = stateSpaceId,
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
            IReadOnlyList<RelatedEntityProjection>? projectedRelated = null;
            var relatedRequirements = requirement.RelationshipComponents ?? [];
            if (relatedRequirements.Count > 0)
            {
                var related = new List<RelatedEntityProjection>();
                foreach (var relatedRequirement in relatedRequirements)
                {
                    if (!mapping.Relationships.TryGetValue(relatedRequirement.Kind, out var qualifiedKind))
                    {
                        problems.Add($"RELATIONSHIP_MAPPING_MISSING: No exact relationship kind maps '{relatedRequirement.Kind}'.");
                        continue;
                    }
                    var matches = relationships.Where(value => value.QualifiedKind == qualifiedKind &&
                        (relatedRequirement.Direction == "either" &&
                            (value.FromEntityId == entityId || value.ToEntityId == entityId) ||
                         relatedRequirement.Direction == "outgoing" && value.FromEntityId == entityId ||
                         relatedRequirement.Direction == "incoming" && value.ToEntityId == entityId));
                    foreach (var edge in matches)
                    {
                        var endpointId = edge.FromEntityId == entityId ? edge.ToEntityId : edge.FromEntityId;
                        if (!entities.TryGetValue(endpointId, out var endpoint) ||
                            !components.TryGetValue(endpointId, out var endpointComponents) ||
                            relatedRequirement.TargetComponentIds.Any(value => !endpointComponents.ContainsKey(value)))
                        {
                            problems.Add($"RELATIONSHIP_COMPONENT_TARGET_MISSING: Related endpoint '{endpointId}' is unavailable or incomplete.");
                            continue;
                        }
                        related.Add(new(endpointId, endpoint.Name, edge.FromEntityId, edge.ToEntityId,
                            relatedRequirement.Kind, edge.Data, endpointComponents
                                .Where(value => relatedRequirement.TargetComponentIds.Contains(value.Key, StringComparer.Ordinal))
                                .ToDictionary(StringComparer.Ordinal)));
                        RecordComponentRevisions(endpointId, relatedRequirement.TargetComponentIds);
                    }
                }
                if (related.Count > ProjectionLimits.MaxRelatedNodes)
                    problems.Add($"RELATIONSHIP_COMPONENT_LIMIT_EXCEEDED: Role '{role}' exceeds the related-node limit.");
                else
                    projectedRelated = related.OrderBy(value => value.Kind, StringComparer.Ordinal)
                        .ThenBy(value => value.FromEntityId, StringComparer.Ordinal)
                        .ThenBy(value => value.ToEntityId, StringComparer.Ordinal)
                        .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            }
            projection.Roles[role] = new EntityProjection(
                entityId,
                entities[entityId].Name,
                values.Where(value => requirement.Components.Concat(requirement.OptionalComponents ?? [])
                        .Contains(value.Key, StringComparer.Ordinal))
                    .ToDictionary(StringComparer.Ordinal),
                containment?.ContainerEntityId,
                containment?.Slot ?? "",
                requirement.IncludeContents ? BuildContents(entityId, requirement.ContentsDepth ?? 1,
                    requirement.ContentComponentIds ?? [], requirement.ContentsDepth is not null
                        || (requirement.ContentComponentIds?.Count ?? 0) > 0,
                    RelevantContentIds(requirement, needed),
                    contents, components, role, problems) : null,
                projectedRelationships,
                projectedRelated);
            RecordComponentRevisions(entityId,
                requirement.Components.Concat(requirement.OptionalComponents ?? []));
            if (requirement.IncludeContents)
            {
                // The optimistic-concurrency snapshot must cover exactly what the mechanic was
                // shown. A filtered role never saw the pruned siblings, so it cannot depend on
                // them: guarding them would both invent conflicts and, on a large world, blow the
                // snapshot's own content limit.
                var surviving = SurvivingContentIds(entityId, requirement.ContentsDepth ?? 1,
                    RelevantContentIds(requirement, needed), contents);
                foreach (var child in Descendants(entityId, requirement.ContentsDepth ?? 1, contents))
                    if (surviving is null || surviving.Contains(child.Id))
                        RecordComponentRevisions(child.Id, requirement.ContentComponentIds ?? []);
                RecordContainmentRevisions(entityId, requirement.ContentsDepth ?? 1, surviving);
            }
        }
        foreach (var (entityId, localIds) in references)
            RecordComponentRevisions(entityId, localIds);
        return problems.Count == 0 ? new(projection, []) : new(null, problems);

        void RecordComponentRevisions(string entityId, IEnumerable<string> localIds)
        {
            if (!projection.ComponentRevisions.TryGetValue(entityId, out var revisions))
                projection.ComponentRevisions[entityId] = revisions = new(StringComparer.Ordinal);
            componentRevisions.TryGetValue(entityId, out var observed);
            foreach (var localId in localIds.Distinct(StringComparer.Ordinal))
                revisions[localId] = observed is not null && observed.TryGetValue(localId, out var revision)
                    ? revision : null;
        }

        void RecordContainmentRevisions(string rootEntityId, int depth, IReadOnlySet<string>? surviving)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Visit(rootEntityId, depth);

            void Visit(string containerEntityId, int remainingDepth)
            {
                if (remainingDepth < 1 || !visited.Add(containerEntityId)) return;
                var roster = containments
                    .Where(value => value.ContainerEntityId == containerEntityId)
                    .OrderBy(value => value.ContainedEntityId, StringComparer.Ordinal)
                    .ToArray();

                // A containment expectation is an all-or-nothing roster assertion: the applier
                // rejects the batch unless the container's children still match it exactly. Only a
                // role that was shown a container's whole roster may assert one. A filtered role
                // was shown a path, not a roster, so it records none -- asserting a partial roster
                // would fail every time, and asserting the full one would guard reads it never had.
                if (surviving is not null && roster.Any(value => !surviving.Contains(value.ContainedEntityId)))
                    return;

                projection.ContainmentRevisions[containerEntityId] = roster
                    .Select(value => new ContainmentRevision(value.ContainedEntityId, value.Slot, value.Revision))
                    .ToArray();
                if (remainingDepth == 1 || !contents.TryGetValue(containerEntityId, out var children)) return;
                foreach (var child in children.Where(value => entities.ContainsKey(value.Id)
                                 && (surviving is null || surviving.Contains(value.Id)))
                             .OrderBy(value => value.Id, StringComparer.Ordinal))
                    Visit(child.Id, remainingDepth - 1);
            }
        }

        void CollectReference(string entityId, string role, ComponentReferenceRequirement reference)
        {
            if (!components.TryGetValue(entityId, out var values)
                || !values.TryGetValue(reference.SourceComponentId, out var raw)) return;
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(reference.Field, out var field))
                {
                    problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' component '{reference.SourceComponentId}' lacks reference field '{reference.Field}'.");
                    return;
                }
                var target = field.ValueKind == JsonValueKind.String ? field.GetString() :
                    field.ValueKind == JsonValueKind.Object && field.EnumerateObject().Count() == 1 &&
                    field.TryGetProperty("entityId", out var referencedId) && referencedId.ValueKind == JsonValueKind.String
                        ? referencedId.GetString() : null;
                if (string.IsNullOrWhiteSpace(target))
                {
                    problems.Add($"COMPONENT_REFERENCE_INVALID: Role '{role}' component '{reference.SourceComponentId}' field '{reference.Field}' is not an entity reference.");
                    return;
                }
                target = target.Trim();
                if (!references.TryGetValue(target, out var ids))
                    references[target] = ids = new(StringComparer.Ordinal);
                if (!requiredReferences.TryGetValue(target, out var requiredIds))
                    requiredReferences[target] = requiredIds = new(StringComparer.Ordinal);
                requiredIds.UnionWith(reference.TargetComponentIds);
                ids.UnionWith(reference.TargetComponentIds);
                ids.UnionWith(reference.OptionalTargetComponentIds ?? []);
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

    /// <summary>
    /// The exact entity ids a role's contents projection is declared to be about. A role that names
    /// other roles in <c>contentsRelevantToRoles</c> is saying it only needs to see those entities'
    /// positions inside its own containment tree, not the whole tree. Returns null when the role
    /// declares no filter, which keeps the unfiltered projection byte-identical to before.
    /// </summary>
    private static IReadOnlySet<string>? RelevantContentIds(
        RoleRequirement requirement,
        IReadOnlyDictionary<string, string> assignments)
    {
        var roles = requirement.ContentsRelevantToRoles ?? [];
        if (roles.Count == 0) return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
            if (assignments.TryGetValue(role, out var entityId) && !string.IsNullOrWhiteSpace(entityId))
                ids.Add(entityId);
        return ids;
    }

    /// <summary>
    /// The contained ids a filtered role actually receives: every entity the filter names, plus the
    /// ancestors that still lead to one, within the declared depth. Null when the role declares no
    /// filter, meaning the whole declared subtree survives.
    /// </summary>
    private static IReadOnlySet<string>? SurvivingContentIds(
        string root,
        int depth,
        IReadOnlySet<string>? relevant,
        IReadOnlyDictionary<string, List<Node>> contents)
    {
        if (relevant is null) return null;
        var surviving = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };

        bool Walk(string container, int remaining)
        {
            if (remaining < 1 || !contents.TryGetValue(container, out var children)) return false;
            var kept = false;
            foreach (var child in children)
            {
                if (!visited.Add(child.Id)) continue;
                var deeper = Walk(child.Id, remaining - 1);
                visited.Remove(child.Id);
                if (!relevant.Contains(child.Id) && !deeper) continue;
                surviving.Add(child.Id);
                kept = true;
            }
            return kept;
        }

        Walk(root, depth);
        return surviving;
    }

    private static IReadOnlyList<ContainedProjection> BuildContents(
        string root,
        int depth,
        IReadOnlyList<string> allowed,
        bool enforceNodeLimit,
        IReadOnlySet<string>? relevant,
        IReadOnlyDictionary<string, List<Node>> contents,
        IReadOnlyDictionary<string, Dictionary<string, string>> components,
        string role,
        List<string> problems)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var count = 0;
        var aborted = false;
        IReadOnlyList<ContainedProjection> Build(string container, int remaining)
        {
            if (!contents.TryGetValue(container, out var children)) return [];
            var result = new List<ContainedProjection>();
            foreach (var child in children.OrderBy(value => value.Name, StringComparer.Ordinal)
                         .ThenBy(value => value.Slot, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal))
            {
                if (aborted) return [];
                if (!visited.Add(child.Id)) { problems.Add($"CONTAINMENT_PROJECTION_CYCLE: Role '{role}' reaches '{child.Id}'."); aborted = true; return []; }
                var nested = remaining > 1 ? Build(child.Id, remaining - 1) : null;
                visited.Remove(child.Id);
                if (aborted) return [];

                // A declared relevance filter keeps a node only when it is itself relevant or when it
                // still leads to something relevant. Dropping the rest preserves every surviving
                // node's depth and slot, so a mechanic's containment test reads exactly as before --
                // it just is not handed the parts of the world it never asked about.
                if (relevant is not null && !relevant.Contains(child.Id) && (nested is null || nested.Count == 0))
                    continue;

                count++;
                if (enforceNodeLimit && count > ProjectionLimits.MaxContainedNodes)
                {
                    problems.Add($"CONTAINMENT_PROJECTION_LIMIT: Role '{role}' projects more than " +
                        $"{ProjectionLimits.MaxContainedNodes} contained nodes. Declare " +
                        "'contentsRelevantToRoles' on this role to project only the paths it references.");
                    aborted = true;
                    return [];
                }
                IReadOnlyDictionary<string, string>? selected = allowed.Count == 0 ? null
                    : components.TryGetValue(child.Id, out var values)
                        ? values.Where(value => allowed.Contains(value.Key, StringComparer.Ordinal)).ToDictionary(StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                result.Add(new(child.Id, child.Name, child.Slot, selected, nested));
            }
            return result;
        }
        return Build(root, depth);
    }
}
