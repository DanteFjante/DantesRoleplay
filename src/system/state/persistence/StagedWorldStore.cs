using System.Text.Json.Nodes;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Read-only overlay of an ordered effect bundle over persistent world state. It deliberately
/// implements the regular store interface so existing validators can consume virtual state, but
/// every mutation method fails before it can touch the underlying store.
/// </summary>
internal sealed class StagedWorldStore(IWorldStore source, IReadOnlyList<Effect> effects) : IWorldStore
{
    private readonly IWorldStore _source = source;
    private readonly IReadOnlyList<Effect> _effects = effects.ToArray();

    public async Task<EntitySnapshot?> GetEntityAsync(string id, CancellationToken cancellationToken = default)
    {
        var state = await StateAsync(id, cancellationToken);
        if (state is null) return null;
        return new EntitySnapshot(state.Id, state.Name, state.Components.Values.OrderBy(component => component.DefinitionId, StringComparer.Ordinal).ToArray(),
            state.ContainerId, state.ContainerSlot, await GetContentsAsync(state.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var results = new List<EntitySnapshot>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var entity = await GetEntityAsync(id, cancellationToken);
            if (entity is not null) results.Add(entity);
        }
        return results;
    }

    public async Task<IReadOnlyList<EntitySnapshot>> GetEntitiesAsync(IEnumerable<string> ids, IReadOnlyCollection<string> componentDefinitionIds, CancellationToken cancellationToken = default)
    {
        var results = new List<EntitySnapshot>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var entity = await GetEntityAsync(id, cancellationToken);
            if (entity is null) continue;
            results.Add(entity with { Components = entity.Components.Where(component => componentDefinitionIds.Contains(component.DefinitionId)).ToArray() });
        }
        return results;
    }

    public async Task<IReadOnlyList<EntitySummary>> FindEntitiesAsync(string? nameQuery = null, string? withDefinitionId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>((await _source.FindEntitiesAsync(nameQuery, null, limit, cancellationToken)).Select(entity => entity.Id), StringComparer.Ordinal);
        foreach (var effect in _effects.Where(effect => effect.Type == EffectType.EntityCreate)) ids.Add(effect.EntityId);

        var results = new List<EntitySummary>();
        foreach (var id in ids)
        {
            var entity = await GetEntityAsync(id, cancellationToken);
            if (entity is null || (!string.IsNullOrWhiteSpace(nameQuery) && !entity.Name.Contains(nameQuery.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(withDefinitionId) && !entity.Components.Any(component => component.DefinitionId == withDefinitionId))) continue;
            results.Add(new EntitySummary(entity.Id, entity.Name, entity.Components.Select(component => component.DefinitionId).ToArray()));
        }
        return results.OrderBy(entity => entity.Name, StringComparer.Ordinal).ThenBy(entity => entity.Id, StringComparer.Ordinal).Take(limit).ToArray();
    }

    public Task<IReadOnlyList<ComponentDefinitionView>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
        _source.GetDefinitionsAsync(cancellationToken);

    public async Task<IReadOnlyList<ContainmentView>> GetContentsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>((await _source.GetContentsAsync(containerId, cancellationToken)).Select(content => content.ContainedId), StringComparer.Ordinal);
        foreach (var effect in _effects)
        {
            if (!string.IsNullOrWhiteSpace(effect.EntityId)) candidates.Add(effect.EntityId);
            if (!string.IsNullOrWhiteSpace(effect.ToEntityId)) candidates.Add(effect.ToEntityId);
        }

        var contents = new List<ContainmentView>();
        foreach (var id in candidates)
        {
            var state = await StateAsync(id, cancellationToken);
            if (state is not null && state.ContainerId == containerId)
                contents.Add(new ContainmentView(state.Id, state.Name, state.ContainerSlot));
        }
        return contents.OrderBy(content => content.Name, StringComparer.Ordinal).ThenBy(content => content.Slot, StringComparer.Ordinal).ThenBy(content => content.ContainedId, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<RelationshipView>> GetRelationshipsAsync(string entityId, bool includeIncoming = true, CancellationToken cancellationToken = default)
    {
        var links = (await _source.GetRelationshipsAsync(entityId, includeIncoming, cancellationToken))
            .ToDictionary(link => Key(link.FromEntityId, link.ToEntityId, link.Kind), StringComparer.Ordinal);
        foreach (var effect in _effects)
        {
            if (effect.Type is not (EffectType.RelationshipCreate or EffectType.RelationshipRemove) ||
                (effect.EntityId != entityId && (!includeIncoming || effect.ToEntityId != entityId))) continue;
            var key = Key(effect.EntityId, effect.ToEntityId, effect.Kind);
            if (effect.Type == EffectType.RelationshipCreate)
                links[key] = new RelationshipView(effect.EntityId, effect.ToEntityId, effect.Kind, effect.Data);
            else
                links.Remove(key);
        }
        return links.Values.OrderBy(link => link.Kind, StringComparer.Ordinal).ThenBy(link => link.FromEntityId, StringComparer.Ordinal).ThenBy(link => link.ToEntityId, StringComparer.Ordinal).ToArray();
    }

    public Task<EntitySnapshot> CreateEntityAsync(string name, string? id = null, CancellationToken cancellationToken = default) => ReadOnly<EntitySnapshot>();
    public Task<bool> DeleteEntityAsync(string id, CancellationToken cancellationToken = default) => ReadOnly<bool>();
    public Task<ComponentDefinitionView> DefineComponentAsync(string id, string name, string description, string schema = "", CancellationToken cancellationToken = default) => ReadOnly<ComponentDefinitionView>();
    public Task<ComponentView> SetComponentAsync(string entityId, string definitionId, string json, CancellationToken cancellationToken = default) => ReadOnly<ComponentView>();
    public Task<ComponentView> MergeComponentAsync(string entityId, string definitionId, string json, CancellationToken cancellationToken = default) => ReadOnly<ComponentView>();
    public Task<bool> RemoveComponentAsync(string entityId, string definitionId, CancellationToken cancellationToken = default) => ReadOnly<bool>();
    public Task MoveAsync(string containedId, string? containerId, string slot = "", CancellationToken cancellationToken = default) => ReadOnly();
    public Task<RelationshipView> RelateAsync(string fromEntityId, string toEntityId, string kind, string json = "{}", CancellationToken cancellationToken = default) => ReadOnly<RelationshipView>();
    public Task<bool> UnrelateAsync(string fromEntityId, string toEntityId, string kind, CancellationToken cancellationToken = default) => ReadOnly<bool>();

    private async Task<State?> StateAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _source.GetEntityAsync(id, cancellationToken);
        State? state = entity is null ? null : State.From(entity);
        foreach (var effect in _effects)
        {
            if (effect.Type == EffectType.EntityCreate && effect.EntityId == id) state = new(id, effect.Name, [], null, string.Empty);
            if (state is null || effect.EntityId != id) continue;
            switch (effect.Type)
            {
                case EffectType.EntityDelete:
                    state = null;
                    break;
                case EffectType.ComponentAdd:
                case EffectType.ComponentSet:
                    state.Components[effect.DefinitionId] = new ComponentView(effect.DefinitionId, effect.Data, state.Components.TryGetValue(effect.DefinitionId, out var prior) ? prior.Revision + 1 : 1);
                    break;
                case EffectType.ComponentMerge:
                    state.Components[effect.DefinitionId] = new ComponentView(effect.DefinitionId, Merge(state.Components.TryGetValue(effect.DefinitionId, out var current) ? current.Data : "{}", effect.Data), state.Components.TryGetValue(effect.DefinitionId, out var merged) ? merged.Revision + 1 : 1);
                    break;
                case EffectType.ComponentRemove:
                    state.Components.Remove(effect.DefinitionId);
                    break;
                case EffectType.ContainmentMove:
                    state = state with { ContainerId = string.IsNullOrWhiteSpace(effect.ToEntityId) ? null : effect.ToEntityId, ContainerSlot = effect.Slot };
                    break;
            }
        }
        return state;
    }

    private static string Merge(string existing, string incoming)
    {
        var target = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        var source = JsonNode.Parse(incoming) as JsonObject ?? new JsonObject();
        foreach (var property in source) target[property.Key] = property.Value?.DeepClone();
        return target.ToJsonString();
    }

    private static string Key(string from, string to, string kind) => $"{from}\u001f{to}\u001f{kind}";
    private static Task ReadOnly() => Task.FromException(new InvalidOperationException("A staged world is read-only; return an effect fragment to the root instead."));
    private static Task<T> ReadOnly<T>() => Task.FromException<T>(new InvalidOperationException("A staged world is read-only; return an effect fragment to the root instead."));

    private sealed record State(string Id, string Name, Dictionary<string, ComponentView> Components, string? ContainerId, string ContainerSlot)
    {
        public static State From(EntitySnapshot entity) => new(entity.Id, entity.Name, entity.Components.ToDictionary(component => component.DefinitionId, StringComparer.Ordinal), entity.ContainerId, entity.ContainerSlot);
    }
}
