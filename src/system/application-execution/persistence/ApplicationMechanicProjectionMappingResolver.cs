using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

public sealed class ApplicationMechanicProjectionMappingResolver(
    IPublicApplicationCatalogProvider catalogs,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IStateSpaceEdgeStore edges) : IApplicationMechanicProjectionMappingResolver
{
    public async Task<ApplicationMechanicProjectionMappingResult> ResolveAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        string qualifiedMechanicId,
        MechanicRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(requirements);
        var stateSpace = stateSpaces.Get(stateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != applicationId)
            return Failed("STATE_SPACE_APPLICATION_MISMATCH",
                "The state space is unavailable for this application.");
        if (!catalogs.TryGet(applicationId, out var catalog))
            return Failed("APPLICATION_CATALOG_UNAVAILABLE",
                "The current application catalog is unavailable.");

        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var localIds = new HashSet<string>(StringComparer.Ordinal);
        var localRelationshipKinds = new HashSet<string>(StringComparer.Ordinal);
        var dependencyVisits = 0;
        var dependencyProblem = await CollectComponentIdsAsync(
            requirements, qualifiedMechanicId, depth: 0, new HashSet<string>(StringComparer.Ordinal));
        if (dependencyProblem is not null)
            return Failed("CHILD_DEPENDENCY_INVALID", dependencyProblem);

        var components = new Dictionary<string, EcsComponentReference>(StringComparer.Ordinal);
        foreach (var localId in localIds)
        {
            var resolved = ResolveComponent(owners, localId);
            if (resolved is null)
                return Failed("COMPONENT_MAPPING_MISSING",
                    "A declared component has no exact current application mapping.");
            components[localId] = resolved;
        }

        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in requirements.AuthorizedContext is null
                     ? await edges.ListRelationshipsAsync(stateSpaceId, cancellationToken)
                     : Array.Empty<EcsRelationshipView>())
        {
            var owner = owners.FirstOrDefault(value =>
                relationship.QualifiedKind.StartsWith(value.Value + ".", StringComparison.Ordinal));
            if (owner is null)
                return Failed("RELATIONSHIP_OWNER_INVALID",
                    "A projected relationship belongs to an unrelated application.");
            // Local relationship names mirror local component names, which keep a base
            // application's qualified id whole: a mechanic declares `game.core.campaign.root` and
            // reads `game.core.campaign.in-world`. Stripping every owner prefix instead turned that
            // kind into `core.campaign.in-world`, so every read of a base-owned relationship
            // silently missed. Only this application's own prefix is removed.
            var local = owner == owners[0]
                ? relationship.QualifiedKind[(owner.Value.Length + 1)..]
                : relationship.QualifiedKind;
            if (!relationships.TryAdd(local, relationship.QualifiedKind)
                && relationships[local] != relationship.QualifiedKind)
                return Failed("RELATIONSHIP_MAPPING_AMBIGUOUS",
                    "An application relationship mapping is ambiguous.");
        }
        foreach (var local in localRelationshipKinds)
            relationships.TryAdd(local, applicationId.Value + "." + local);
        return new(new(components, relationships), []);

        async Task<string?> CollectComponentIdsAsync(
            MechanicRequirements declared,
            string currentMechanicId,
            int depth,
            HashSet<string> lineage)
        {
            foreach (var localId in declared.EffectComponentIds)
                localIds.Add(localId);
            if (declared.AuthorizedContext is { SourceSets: var sources })
            {
                foreach (var id in sources.ComponentIds()) localIds.Add(id);
            }
            foreach (var localId in declared.Roles.Values.SelectMany(value =>
                         value.Components.Concat(value.OptionalComponents ?? [])
                             .Concat(value.ContentComponentIds ?? [])
                             .Concat((value.ComponentReferences ?? []).SelectMany(reference =>
                                 new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds)
                                     .Concat(reference.OptionalTargetComponentIds ?? [])))
                             .Concat((value.RelationshipComponents ?? []).SelectMany(reference =>
                                 reference.TargetComponentIds.Concat(reference.OptionalTargetComponentIds ?? [])))))
                localIds.Add(localId);
            foreach (var kind in declared.Roles.Values
                         .SelectMany(value => value.RelationshipComponents ?? [])
                         .Select(value => value.Kind))
                localRelationshipKinds.Add(kind);

            if (declared.Children.Count == 0) return null;
            if (declared.Children.Count > 64)
                return "The declared child-mechanic count exceeds the supported limit.";
            if (depth >= 8)
                return "The declared child-mechanic depth exceeds the supported limit.";
            if (!lineage.Add(currentMechanicId))
                return "The declared child-mechanic graph contains a cycle.";

            foreach (var child in declared.Children.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (++dependencyVisits > 256)
                    return "The declared child-mechanic graph exceeds the supported traversal limit.";
                var childMechanicId = QualifyMechanicId(applicationId, child.Value.MechanicId);
                if (lineage.Contains(childMechanicId))
                    return "The declared child-mechanic graph contains a cycle.";
                CatalogRecordView childRecord;
                try
                {
                    childRecord = catalog.Inspect(new(applicationId, applicationId.Value, childMechanicId));
                }
                catch (Exception)
                {
                    return $"Declared child '{child.Key}' is unavailable.";
                }
                if (childRecord.Summary.Kind != "mechanic" || childRecord.Summary.Status != "active")
                    return $"Declared child '{child.Key}' is inactive.";
                if (child.Value.MechanicVersion > 0
                    && (childRecord.Summary.Version != child.Value.MechanicVersion
                        || childRecord.Summary.ContentFingerprint != child.Value.ContentFingerprint))
                    return $"Declared child '{child.Key}' no longer matches its exact version and fingerprint.";
                try
                {
                    using var document = JsonDocument.Parse(childRecord.ContentJson);
                    if (!document.RootElement.TryGetProperty("requirements", out var value)
                        || value.ValueKind != JsonValueKind.String)
                        return $"Declared child '{child.Key}' has invalid requirements.";
                    var childRequirements = MechanicRequirements.Parse(value.GetString()!);
                    if (childRequirements.ProjectionProblems().Count > 0
                        || childRequirements.CompositionProblems().Count > 0)
                        return $"Declared child '{child.Key}' has invalid requirements.";
                    var nested = await CollectComponentIdsAsync(childRequirements,
                        childRecord.Summary.QualifiedId, depth + 1,
                        new HashSet<string>(lineage, StringComparer.Ordinal));
                    if (nested is not null) return nested;
                }
                catch (JsonException)
                {
                    return $"Declared child '{child.Key}' has invalid requirements.";
                }
            }
            return null;
        }
    }

    private EcsComponentReference? ResolveComponent(
        IReadOnlyList<ApplicationIdentifier> owners,
        string localOrQualifiedId)
    {
        if (string.IsNullOrWhiteSpace(localOrQualifiedId) || owners.Count == 0) return null;
        var application = owners[0];
        if (localOrQualifiedId.StartsWith(application.Value + ".", StringComparison.Ordinal))
        {
            var installedId = localOrQualifiedId[(application.Value.Length + 1)..];
            var installedBase = owners.Skip(1).FirstOrDefault(owner =>
                installedId.StartsWith(owner.Value + ".", StringComparison.Ordinal));
            if (installedBase is not null)
            {
                var installedValue = componentTypes.GetLatest(installedId);
                return installedValue is not null && installedValue.Owner == installedBase
                    ? new(installedValue.QualifiedId, installedValue.Version, installedValue.SchemaHash)
                    : null;
            }
        }
        var explicitOwner = owners.FirstOrDefault(owner =>
            localOrQualifiedId.StartsWith(owner.Value + ".", StringComparison.Ordinal));
        if (explicitOwner is not null)
        {
            var explicitValue = componentTypes.GetLatest(localOrQualifiedId);
            return explicitValue is not null && explicitValue.Owner == explicitOwner
                ? new(explicitValue.QualifiedId, explicitValue.Version, explicitValue.SchemaHash)
                : null;
        }
        foreach (var owner in owners)
        {
            var qualified = owner.Value + "." + localOrQualifiedId;
            var value = componentTypes.GetLatest(qualified);
            if (value is not null)
                return new(value.QualifiedId, value.Version, value.SchemaHash);
        }
        return null;
    }

    private static string QualifyMechanicId(ApplicationIdentifier applicationId, string mechanicId) =>
        mechanicId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? mechanicId : applicationId.Value + "." + mechanicId;

    private static ApplicationMechanicProjectionMappingResult Failed(string code, string message) =>
        new(null, [new(code, message)]);
}
