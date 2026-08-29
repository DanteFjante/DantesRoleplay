using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

/// <summary>Reads only immutable projection declarations; it does not materialize values or infer code dependencies.</summary>
public sealed class SqliteProjectionImpactSnapshotReader(DantesRoleplayDbContext db) : IProjectionImpactSnapshotReader
{
    public ProjectionImpactSnapshot Read(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var definitionIds = db.Set<ProjectionDefinitionRecord>().AsNoTracking()
            .Where(row => row.ApplicationId == applicationId.Value)
            .Select(row => row.QualifiedId)
            .ToArray();
        var versions = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking()
            .Where(row => definitionIds.Contains(row.QualifiedId)).ToArray();
        var components = db.Set<ProjectionComponentInputRecord>().AsNoTracking()
            .Where(row => definitionIds.Contains(row.QualifiedId)).ToArray();
        var dependencies = db.Set<ProjectionDependencyInputRecord>().AsNoTracking()
            .Where(row => definitionIds.Contains(row.QualifiedId)).ToArray();
        var mappings = db.Set<ProjectionMappingRecord>().AsNoTracking()
            .Where(row => definitionIds.Contains(row.QualifiedId)).ToArray();

        var nodes = new Dictionary<string, ProjectionImpactNode>(StringComparer.Ordinal);
        var edges = new HashSet<ProjectionImpactEdge>();
        foreach (var version in versions)
        {
            var id = ProjectionId(version.QualifiedId, version.Version);
            Add(nodes, new(id, "projection", version.QualifiedId, version.Version, version.ContentHash, null));
        }

        foreach (var component in components)
        {
            var consumerId = ProjectionId(component.QualifiedId, component.Version);
            foreach (var mapping in mappings.Where(mapping => mapping.QualifiedId == component.QualifiedId
                         && mapping.Version == component.Version && mapping.InputId == component.InputId))
            {
                var dependencyId = ComponentFieldId(
                    component.QualifiedTypeId, component.TypeVersion, mapping.SourcePointer);
                Add(nodes, new(dependencyId, "component-field", component.QualifiedTypeId,
                    component.TypeVersion, component.SchemaHash, mapping.SourcePointer));
                edges.Add(new(dependencyId, consumerId, "reads-component-field"));
            }
        }

        foreach (var dependency in dependencies)
        {
            var dependencyId = ProjectionId(dependency.DependencyQualifiedId, dependency.DependencyVersion);
            var consumerId = ProjectionId(dependency.QualifiedId, dependency.Version);
            edges.Add(new(dependencyId, consumerId, "depends-on-projection"));
        }

        return new(applicationId,
            Array.AsReadOnly(nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(edges.OrderBy(edge => edge.DependencyId, StringComparer.Ordinal)
                .ThenBy(edge => edge.ConsumerId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Reason, StringComparer.Ordinal).ToArray()));
    }

    internal static string ProjectionId(string qualifiedId, int version) =>
        $"projection:{qualifiedId}@{version}";

    internal static string ComponentFieldId(string qualifiedId, int version, string pointer) =>
        $"component:{qualifiedId}@{version}#{pointer}";

    private static void Add(IDictionary<string, ProjectionImpactNode> nodes, ProjectionImpactNode node)
    {
        if (nodes.TryGetValue(node.Id, out var existing) && existing != node)
            throw new InvalidOperationException("The persisted dependency graph contains inconsistent exact contracts.");
        nodes[node.Id] = node;
    }
}
