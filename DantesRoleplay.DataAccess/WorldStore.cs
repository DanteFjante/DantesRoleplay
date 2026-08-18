using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// The entity-component store. This is the whole of P5/P6.
///
/// Note what is absent: there is no method here that mentions a stat, a location, an item or a
/// character. Adding any of those to the world is <see cref="DefineComponentAsync"/> followed by
/// <see cref="SetComponentAsync"/> — data, not schema.
/// </summary>
public sealed class WorldStore(DantesRoleplayDbContext db) : IWorldStore
{
    private readonly DantesRoleplayDbContext _db = db;

    // ---- entities -------------------------------------------------------------------

    public async Task<EntitySnapshot> CreateEntityAsync(
        string name,
        string? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entity = new Entity
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id.Trim(),
            Name = name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.Entities.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new EntitySnapshot(entity.Id, entity.Name, [], null, string.Empty);
    }

    public async Task<EntitySnapshot?> GetEntityAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var results = await GetEntitiesAsync([id], cancellationToken);
        return results.Count == 0 ? null : results[0];
    }

    public async Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
        => await GetEntitiesCoreAsync(ids, componentDefinitionIds: null, cancellationToken);

    public async Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(
        IEnumerable<string> ids,
        IReadOnlyCollection<string> componentDefinitionIds,
        CancellationToken cancellationToken = default)
        => await GetEntitiesCoreAsync(ids, componentDefinitionIds, cancellationToken);

    private async Task<IReadOnlyList<EntitySnapshot>> GetEntitiesCoreAsync(
        IEnumerable<string> ids,
        IReadOnlyCollection<string>? componentDefinitionIds,
        CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var entitiesQuery = _db.Entities
            .AsNoTracking()
            .Where(e => wanted.Contains(e.Id) && e.DeletedAt == null);

        entitiesQuery = componentDefinitionIds is null
            ? entitiesQuery.Include(e => e.Components)
            : entitiesQuery.Include(e => e.Components.Where(
                component => componentDefinitionIds.Contains(component.DefinitionId)));

        var entities = await entitiesQuery.ToListAsync(cancellationToken);

        var containers = await _db.Containments
            .AsNoTracking()
            .Where(c => wanted.Contains(c.ContainedId))
            .ToDictionaryAsync(c => c.ContainedId, cancellationToken);

        var contents = await _db.Containments
            .AsNoTracking()
            .Where(c => wanted.Contains(c.ContainerId))
            .Join(
                _db.Entities.Where(e => e.DeletedAt == null),
                c => c.ContainedId,
                e => e.Id,
                (c, e) => new { c.ContainerId, e.Id, e.Name, c.Slot })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var contentsByContainer = contents
            .GroupBy(x => x.ContainerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ContainmentView>)group
                    .Select(x => new ContainmentView(x.Id, x.Name, x.Slot))
                    .ToList(),
                StringComparer.Ordinal);

        return entities.Select(e =>
        {
            containers.TryGetValue(e.Id, out var containment);
            contentsByContainer.TryGetValue(e.Id, out var contained);

            return new EntitySnapshot(
                e.Id,
                e.Name,
                e.Components
                    .OrderBy(c => c.DefinitionId, StringComparer.Ordinal)
                    .Select(c => new ComponentView(c.DefinitionId, c.Data, c.Revision))
                    .ToList(),
                containment?.ContainerId,
                containment?.Slot ?? string.Empty,
                contained ?? []);
        }).ToList();
    }

    public async Task<IReadOnlyList<EntitySummary>> FindEntitiesAsync(
        string? nameQuery = null,
        string? withDefinitionId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Entities.AsNoTracking().Where(e => e.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            var pattern = $"%{Escape(nameQuery.Trim())}%";
            query = query.Where(e => EF.Functions.Like(e.Name, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(withDefinitionId))
        {
            query = query.Where(e => e.Components.Any(c => c.DefinitionId == withDefinitionId));
        }

        return await query
            .OrderBy(e => e.Name)
            .Take(limit)
            .Select(e => new EntitySummary(
                e.Id,
                e.Name,
                e.Components.Select(c => c.DefinitionId).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Entities.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entity is null || entity.DeletedAt is not null)
        {
            return false;
        }

        entity.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- component definitions ------------------------------------------------------

    public async Task<ComponentDefinitionView> DefineComponentAsync(
        string id,
        string name,
        string description,
        string schema = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = DateTime.UtcNow;

        var definition = await _db.ComponentDefinitions
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (definition is null)
        {
            definition = new ComponentDefinition
            {
                Id = id.Trim(),
                Name = name.Trim(),
                Description = description,
                Schema = schema,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.ComponentDefinitions.Add(definition);
        }
        else
        {
            definition.Name = name.Trim();
            definition.Description = description;
            definition.Schema = schema;
            definition.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var usage = await _db.Components.CountAsync(c => c.DefinitionId == definition.Id, cancellationToken);

        return new ComponentDefinitionView(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.Schema,
            usage);
    }

    public async Task<IReadOnlyList<ComponentDefinitionView>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ComponentDefinitions
            .AsNoTracking()
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                d.Schema,
                UsageCount = _db.Components.Count(c => c.DefinitionId == d.Id)
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => new ComponentDefinitionView(d.Id, d.Name, d.Description, d.Schema, d.UsageCount))
            .ToList();
    }

    // ---- components -----------------------------------------------------------------

    public Task<ComponentView> SetComponentAsync(
        string entityId,
        string definitionId,
        string json,
        CancellationToken cancellationToken = default) =>
        UpsertComponentAsync(entityId, definitionId, json, merge: false, cancellationToken);

    public Task<ComponentView> MergeComponentAsync(
        string entityId,
        string definitionId,
        string json,
        CancellationToken cancellationToken = default) =>
        UpsertComponentAsync(entityId, definitionId, json, merge: true, cancellationToken);

    private async Task<ComponentView> UpsertComponentAsync(
        string entityId,
        string definitionId,
        string json,
        bool merge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        var incoming = ParseObject(json);

        var entityExists = await _db.Entities
            .AnyAsync(e => e.Id == entityId && e.DeletedAt == null, cancellationToken);

        if (!entityExists)
        {
            throw new InvalidOperationException($"Unknown entity '{entityId}'.");
        }

        var definitionExists = await _db.ComponentDefinitions
            .AnyAsync(d => d.Id == definitionId, cancellationToken);

        if (!definitionExists)
        {
            // Deliberately a hard failure rather than an implicit create: an undeclared component
            // type is almost always a typo, and a silently-created one is invisible forever after.
            throw new InvalidOperationException(
                $"Unknown component definition '{definitionId}'. Define it first.");
        }

        var now = DateTime.UtcNow;

        var component = await _db.Components
            .FirstOrDefaultAsync(c => c.EntityId == entityId && c.DefinitionId == definitionId, cancellationToken);

        if (component is null)
        {
            component = new Component
            {
                EntityId = entityId,
                DefinitionId = definitionId,
                Data = incoming.ToJsonString(),
                Revision = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Components.Add(component);
        }
        else
        {
            if (merge)
            {
                var existing = ParseObject(component.Data);

                foreach (var property in incoming)
                {
                    // Shallow by design: a deep merge cannot express "remove this nested key",
                    // and a caller who needs one can read, modify and Set.
                    existing[property.Key] = property.Value?.DeepClone();
                }

                component.Data = existing.ToJsonString();
            }
            else
            {
                component.Data = incoming.ToJsonString();
            }

            component.Revision++;
            component.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ComponentView(component.DefinitionId, component.Data, component.Revision);
    }

    public async Task<bool> RemoveComponentAsync(
        string entityId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        var component = await _db.Components
            .FirstOrDefaultAsync(c => c.EntityId == entityId && c.DefinitionId == definitionId, cancellationToken);

        if (component is null)
        {
            return false;
        }

        _db.Components.Remove(component);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- containment ----------------------------------------------------------------

    public async Task MoveAsync(
        string containedId,
        string? containerId,
        string slot = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containedId);

        if (containedId == containerId)
        {
            throw new InvalidOperationException("An entity cannot contain itself.");
        }

        var existing = await _db.Containments
            .FirstOrDefaultAsync(c => c.ContainedId == containedId, cancellationToken);

        if (containerId is null)
        {
            if (existing is not null)
            {
                _db.Containments.Remove(existing);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (await WouldCycleAsync(containedId, containerId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Moving '{containedId}' into '{containerId}' would create a containment cycle.");
        }

        if (existing is null)
        {
            _db.Containments.Add(new Containment
            {
                ContainerId = containerId,
                ContainedId = containedId,
                Slot = slot,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ContainerId = containerId;
            existing.Slot = slot;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Walks up from the prospective container. Without this a bag can be put inside itself via
    /// a chain, and every later traversal loops forever.
    /// </summary>
    private async Task<bool> WouldCycleAsync(
        string containedId,
        string containerId,
        CancellationToken cancellationToken)
    {
        var current = containerId;
        var guard = 0;

        while (current is not null && guard++ < 100)
        {
            if (current == containedId)
            {
                return true;
            }

            current = await _db.Containments
                .Where(c => c.ContainedId == current)
                .Select(c => c.ContainerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    public async Task<IReadOnlyList<ContainmentView>> GetContentsAsync(
        string containerId,
        CancellationToken cancellationToken = default)
    {
        // Project the join into an ANONYMOUS type and order on that. Ordering on a property of a
        // constructed record (OrderBy(v => v.Name) where v is a ContainmentView) is not
        // translatable — EF cannot see through the constructor to a column.
        var rows = await _db.Containments
            .AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .Join(
                _db.Entities.Where(e => e.DeletedAt == null),
                c => c.ContainedId,
                e => e.Id,
                (c, e) => new { e.Id, e.Name, c.Slot })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ContainmentView(x.Id, x.Name, x.Slot)).ToList();
    }

    // ---- relationships --------------------------------------------------------------

    public async Task<RelationshipView> RelateAsync(
        string fromEntityId,
        string toEntityId,
        string kind,
        string json = "{}",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var data = ParseObject(json).ToJsonString();

        var existing = await _db.Relationships.FirstOrDefaultAsync(
            r => r.FromEntityId == fromEntityId && r.ToEntityId == toEntityId && r.Kind == kind,
            cancellationToken);

        if (existing is null)
        {
            existing = new Relationship
            {
                FromEntityId = fromEntityId,
                ToEntityId = toEntityId,
                Kind = kind,
                Data = data,
                CreatedAt = DateTime.UtcNow
            };
            _db.Relationships.Add(existing);
        }
        else
        {
            existing.Data = data;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new RelationshipView(existing.FromEntityId, existing.ToEntityId, existing.Kind, existing.Data);
    }

    public async Task<bool> UnrelateAsync(
        string fromEntityId,
        string toEntityId,
        string kind,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.Relationships.FirstOrDefaultAsync(
            r => r.FromEntityId == fromEntityId && r.ToEntityId == toEntityId && r.Kind == kind,
            cancellationToken);

        if (existing is null)
        {
            return false;
        }

        _db.Relationships.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RelationshipView>> GetRelationshipsAsync(
        string entityId,
        bool includeIncoming = true,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Relationships.AsNoTracking();

        query = includeIncoming
            ? query.Where(r => r.FromEntityId == entityId || r.ToEntityId == entityId)
            : query.Where(r => r.FromEntityId == entityId);

        return await query
            .OrderBy(r => r.Kind)
            .Select(r => new RelationshipView(r.FromEntityId, r.ToEntityId, r.Kind, r.Data))
            .ToListAsync(cancellationToken);
    }

    // ---- helpers --------------------------------------------------------------------

    /// <summary>
    /// Component payloads must be JSON objects, not arrays or scalars. Rejecting anything else
    /// here keeps merge well-defined and stops a malformed write from being discovered days later.
    /// </summary>
    private static JsonObject ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Component data is not valid JSON: {ex.Message}", ex);
        }

        return node as JsonObject
            ?? throw new InvalidOperationException("Component data must be a JSON object.");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
