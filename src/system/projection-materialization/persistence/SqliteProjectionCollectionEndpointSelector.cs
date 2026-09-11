using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.Projections;

/// <summary>
/// Selects lightweight collection metadata in SQLite for orderings whose ordinal semantics can be
/// reproduced exactly. Component payloads remain outside this read so the materializer can hydrate
/// only the selected page.
/// </summary>
public sealed class SqliteProjectionCollectionEndpointSelector(DantesRoleplayDbContext db) :
    IProjectionCollectionEndpointSelector
{
    private const string OrdinalCollation = "dantes_ordinal";

    public Task<ProjectionCollectionEndpointSelection?> SelectAsync(
        string stateSpaceId,
        IReadOnlyList<string> candidateEntityIds,
        IReadOnlyList<string> allEntityIds,
        IReadOnlyList<ApplicationObjectEndpointComponent> requiredItemComponents,
        IReadOnlyList<ApplicationObjectEndpointComponent> includedItemComponents,
        IReadOnlyList<ApplicationObjectOrder> order,
        CancellationToken cancellationToken = default) =>
        SelectCoreAsync(stateSpaceId, candidateEntityIds, allEntityIds, requiredItemComponents,
            includedItemComponents, order, false, cancellationToken);

    public Task<ProjectionCollectionEndpointSelection?> SelectCurrentAsync(
        string stateSpaceId,
        IReadOnlyList<string> candidateEntityIds,
        IReadOnlyList<string> allEntityIds,
        IReadOnlyList<ApplicationObjectEndpointComponent> requiredItemComponents,
        IReadOnlyList<ApplicationObjectEndpointComponent> includedItemComponents,
        IReadOnlyList<ApplicationObjectOrder> order,
        CancellationToken cancellationToken = default) =>
        SelectCoreAsync(stateSpaceId, candidateEntityIds, allEntityIds, requiredItemComponents,
            includedItemComponents, order, true, cancellationToken);

    private async Task<ProjectionCollectionEndpointSelection?> SelectCoreAsync(
        string stateSpaceId,
        IReadOnlyList<string> candidateEntityIds,
        IReadOnlyList<string> allEntityIds,
        IReadOnlyList<ApplicationObjectEndpointComponent> requiredItemComponents,
        IReadOnlyList<ApplicationObjectEndpointComponent> includedItemComponents,
        IReadOnlyList<ApplicationObjectOrder> order,
        bool currentComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        ArgumentNullException.ThrowIfNull(candidateEntityIds);
        ArgumentNullException.ThrowIfNull(allEntityIds);
        ArgumentNullException.ThrowIfNull(requiredItemComponents);
        ArgumentNullException.ThrowIfNull(includedItemComponents);
        ArgumentNullException.ThrowIfNull(order);
        if (candidateEntityIds.Count > 256 || allEntityIds.Count > 256
            || requiredItemComponents.Count > 32 || includedItemComponents.Count > 32)
            throw new ArgumentOutOfRangeException(nameof(candidateEntityIds));
        if (order.Count == 0 || order.Any(value => value.Pointer is not ("/id" or "/name")))
            return null;

        if (db.Database.GetDbConnection() is not SqliteConnection connection)
            return null;
        connection.CreateCollation(OrdinalCollation, CompareScalar);

        var candidates = candidateEntityIds.Distinct(StringComparer.Ordinal).ToArray();
        var entityIds = allEntityIds.Distinct(StringComparer.Ordinal).ToArray();
        var includedTypes = includedItemComponents.Select(value => value.Type.QualifiedTypeId)
            .Distinct(StringComparer.Ordinal).ToArray();
        var componentQuery = db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && candidates.Contains(value.EntityId)
                && includedTypes.Contains(value.QualifiedTypeId));
        var endpointQuery = db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && entityIds.Contains(value.Id)
                && value.DeletedAtUtc == null)
            .GroupJoin(componentQuery,
                entity => new { entity.StateSpaceId, EntityId = entity.Id },
                component => new { component.StateSpaceId, component.EntityId },
                (entity, matches) => new { Entity = entity, Components = matches })
            .SelectMany(value => value.Components.DefaultIfEmpty(), (value, component) => new EndpointRow
            {
                StateSpaceId = value.Entity.StateSpaceId,
                EntityId = value.Entity.Id,
                Name = value.Entity.Name,
                EntityRevision = value.Entity.Revision,
                EntityCreatedAtUtc = value.Entity.CreatedAtUtc,
                ComponentQualifiedTypeId = component == null ? null : component.QualifiedTypeId,
                ComponentTypeVersion = component == null ? 0 : component.TypeVersion,
                ComponentSchemaHash = component == null ? null : component.SchemaHash,
                ComponentRevision = component == null ? 0 : component.Revision
            });

        IOrderedQueryable<EndpointRow>? ordered = null;
        foreach (var rule in order)
            ordered = ApplyOrder(endpointQuery, ordered, rule);
        ordered = ordered!.ThenBy(value => EF.Functions.Collate(value.EntityId, OrdinalCollation));
        var rows = await ordered.ToArrayAsync(cancellationToken);
        var componentKeys = rows.Where(value => value.ComponentQualifiedTypeId is not null)
            .Select(value => (value.EntityId, value.ComponentQualifiedTypeId!, value.ComponentTypeVersion,
                value.ComponentSchemaHash!)).ToHashSet();
        var attachedKeys = rows.Where(value => value.ComponentQualifiedTypeId is not null)
            .Select(value => (value.EntityId, value.ComponentQualifiedTypeId!)).ToHashSet();
        var orderedIds = rows.Select(value => value.EntityId).Distinct(StringComparer.Ordinal)
            .Where(candidates.Contains)
            .Where(entityId => requiredItemComponents.All(required => currentComponents
                ? attachedKeys.Contains((entityId, required.Type.QualifiedTypeId))
                : componentKeys.Contains((entityId, required.Type.QualifiedTypeId,
                    required.Type.TypeVersion, required.Type.SchemaHash))))
            .ToArray();
        var entityRows = rows.DistinctBy(value => value.EntityId).Select(value => new EcsEntityView(
            value.StateSpaceId, value.EntityId, value.Name, value.EntityRevision,
            value.EntityCreatedAtUtc, null)).ToArray();
        var revisions = rows.Where(value => value.ComponentQualifiedTypeId is not null)
            .Select(value => new ProjectionSourceRevision(value.EntityId,
                new(value.ComponentQualifiedTypeId!, value.ComponentTypeVersion, value.ComponentSchemaHash!),
                value.ComponentRevision)).ToArray();
        return new(Array.AsReadOnly(orderedIds), Array.AsReadOnly(entityRows),
            Array.AsReadOnly(revisions));
    }

    private static int CompareScalar(string? left, string? right) =>
        StringComparer.Ordinal.Compare(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right));

    private static IOrderedQueryable<EndpointRow> ApplyOrder(
        IQueryable<EndpointRow> candidates,
        IOrderedQueryable<EndpointRow>? ordered,
        ApplicationObjectOrder rule)
    {
        var descending = rule.Direction == "desc";
        if (rule.Pointer == "/id")
            return ordered is null
                ? descending
                    ? candidates.OrderByDescending(value => EF.Functions.Collate(value.EntityId, OrdinalCollation))
                    : candidates.OrderBy(value => EF.Functions.Collate(value.EntityId, OrdinalCollation))
                : descending
                    ? ordered.ThenByDescending(value => EF.Functions.Collate(value.EntityId, OrdinalCollation))
                    : ordered.ThenBy(value => EF.Functions.Collate(value.EntityId, OrdinalCollation));
        return ordered is null
            ? descending
                ? candidates.OrderByDescending(value => EF.Functions.Collate(value.Name, OrdinalCollation))
                : candidates.OrderBy(value => EF.Functions.Collate(value.Name, OrdinalCollation))
            : descending
                ? ordered.ThenByDescending(value => EF.Functions.Collate(value.Name, OrdinalCollation))
                : ordered.ThenBy(value => EF.Functions.Collate(value.Name, OrdinalCollation));
    }

    private sealed class EndpointRow
    {
        public required string StateSpaceId { get; init; }
        public required string EntityId { get; init; }
        public required string Name { get; init; }
        public int EntityRevision { get; init; }
        public DateTime EntityCreatedAtUtc { get; init; }
        public string? ComponentQualifiedTypeId { get; init; }
        public int ComponentTypeVersion { get; init; }
        public string? ComponentSchemaHash { get; init; }
        public int ComponentRevision { get; init; }
    }
}
