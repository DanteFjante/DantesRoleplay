using System.Text.Json;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Knowledge;

/// <summary>Resolves current actor epistemic state from exact application graph vocabulary.</summary>
public sealed class ApplicationKnowledgeEffectiveStateResolver(
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges) : IKnowledgeEffectiveStateResolver
{
    public async Task<IReadOnlyDictionary<string, EffectiveKnowledgeState>> ResolveAllAsync(
        KnowledgeApplicationBinding binding,
        string actorId,
        string worldId,
        IReadOnlyList<string> knowledgeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(knowledgeIds);
        binding.Validate();
        if (!Bounded(actorId) || !Bounded(worldId) || knowledgeIds.Count > 10_000 ||
            knowledgeIds.Any(value => !Bounded(value)) ||
            knowledgeIds.Distinct(StringComparer.Ordinal).Count() != knowledgeIds.Count ||
            await entities.GetEntityAsync(binding.StateSpaceId, actorId, cancellationToken) is null)
            return new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);

        var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
        var containments = await edges.ListContainmentsAsync(binding.StateSpaceId, cancellationToken);
        var containers = containments.ToDictionary(
            value => value.ContainedEntityId, value => value, StringComparer.Ordinal);
        var graphRevision = ApplicationKnowledgeCanonicalSource.Hash(new
        {
            relationships = relationships.Select(value => new
            {
                value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.DataJson, value.Revision
            }),
            containments = containments.Select(value => new
            {
                value.ContainedEntityId, value.ContainerEntityId, value.Slot, value.Revision
            })
        });
        var componentCache = new Dictionary<(string EntityId, string TypeId), EcsComponentView?>();
        var result = new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);

        foreach (var knowledgeId in knowledgeIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            var explicitStates = relationships.Where(value =>
                value.FromEntityId == actorId && value.ToEntityId == knowledgeId &&
                value.QualifiedKind == binding.ExplicitStateRelationshipKind).ToArray();
            if (explicitStates.Length > 1) continue;
            if (explicitStates.Length == 1)
            {
                var state = ParseState(explicitStates[0].DataJson, binding);
                if (state is null) continue;
                result[knowledgeId] = State(knowledgeId, worldId, state, "explicit", actorId,
                    graphRevision, explicitStates[0].Revision);
                continue;
            }

            var baselines = relationships.Where(value =>
                value.ToEntityId == knowledgeId && value.QualifiedKind == binding.BaselineRelationshipKind)
                .OrderBy(value => value.FromEntityId, StringComparer.Ordinal).ToArray();
            if (baselines.Any(value => !Baseline(value.DataJson, binding))) continue;

            EcsRelationshipView? applicable = null;
            var invalidScope = false;
            foreach (var baseline in baselines.Where(value => value.FromEntityId != worldId))
            {
                var scope = await ResolveScopeAsync(binding, baseline.FromEntityId, actorId, worldId,
                    relationships, containers, componentCache, cancellationToken);
                if (!scope.Valid)
                {
                    invalidScope = true;
                    break;
                }
                if (scope.Applies)
                {
                    applicable = baseline;
                    break;
                }
            }
            if (invalidScope) continue;
            applicable ??= baselines.SingleOrDefault(value => value.FromEntityId == worldId);
            if (applicable is not null)
            {
                var sourceKind = applicable.FromEntityId == worldId ? "world-baseline" : "scope-baseline";
                result[knowledgeId] = State(knowledgeId, worldId, binding.BaselineState,
                    sourceKind, applicable.FromEntityId, graphRevision, applicable.Revision);
            }
            else
            {
                result[knowledgeId] = State(knowledgeId, worldId, binding.UnknownState,
                    "derived-unknown", null, graphRevision, 0);
            }
        }
        return result;
    }

    private async Task<ScopeResolution> ResolveScopeAsync(
        KnowledgeApplicationBinding binding,
        string scopeId,
        string actorId,
        string worldId,
        IReadOnlyList<EcsRelationshipView> relationships,
        IReadOnlyDictionary<string, EcsContainmentView> containers,
        Dictionary<(string EntityId, string TypeId), EcsComponentView?> cache,
        CancellationToken cancellationToken)
    {
        if (await entities.GetEntityAsync(binding.StateSpaceId, scopeId, cancellationToken) is null)
            return new(false, false);
        var faction = await ComponentAsync(binding.StateSpaceId, scopeId,
            binding.FactionComponentTypeId, cache, cancellationToken);
        if (faction is not null)
        {
            if (!ApplicationKnowledgeCanonicalSource.Text(faction.ValueJson,
                    binding.FactionStatusProperty, out var status) || status != binding.ActiveFactionStatus)
                return new(false, false);
            var inWorld = relationships.Any(value => value.FromEntityId == scopeId &&
                value.ToEntityId == worldId && value.QualifiedKind == binding.FactionWorldRelationshipKind &&
                ApplicationKnowledgeCanonicalSource.Empty(value.DataJson));
            if (!inWorld) return new(false, false);
            var member = relationships.Any(value => value.FromEntityId == scopeId &&
                value.ToEntityId == actorId && value.QualifiedKind == binding.FactionMemberRelationshipKind &&
                ApplicationKnowledgeCanonicalSource.Empty(value.DataJson));
            return new(true, member);
        }

        var location = await ComponentAsync(binding.StateSpaceId, scopeId,
            binding.LocationComponentTypeId, cache, cancellationToken);
        if (location is null || !ApplicationKnowledgeCanonicalSource.Text(
                location.ValueJson, binding.LocationStatusProperty, out var locationStatus) ||
            locationStatus != binding.ActiveLocationStatus || !ApplicationKnowledgeCanonicalSource.Text(
                location.ValueJson, binding.LocationKindProperty, out var kind) ||
            kind != binding.RegionLocationKind || !ContainedBy(scopeId, worldId, containers))
            return new(false, false);
        return new(true, ContainedBy(actorId, scopeId, containers));
    }

    private async Task<EcsComponentView?> ComponentAsync(
        string stateSpaceId,
        string entityId,
        string typeId,
        Dictionary<(string EntityId, string TypeId), EcsComponentView?> cache,
        CancellationToken cancellationToken)
    {
        var key = (entityId, typeId);
        if (!cache.TryGetValue(key, out var value))
        {
            value = await entities.GetComponentAsync(stateSpaceId, entityId, typeId, cancellationToken);
            cache.Add(key, value);
        }
        return value;
    }

    private static bool ContainedBy(
        string entityId,
        string ancestorId,
        IReadOnlyDictionary<string, EcsContainmentView> containers)
    {
        var current = entityId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; depth < 20 && containers.TryGetValue(current, out var containment); depth++)
        {
            if (!seen.Add(current)) return false;
            if (containment.ContainerEntityId == ancestorId) return true;
            current = containment.ContainerEntityId;
        }
        return false;
    }

    private static EffectiveKnowledgeState State(
        string knowledgeId,
        string worldId,
        string state,
        string sourceKind,
        string? sourceId,
        string graphRevision,
        int edgeRevision) => new(
            knowledgeId, worldId, state, sourceKind, sourceId,
            ApplicationKnowledgeCanonicalSource.Hash(new
            {
                knowledgeId, worldId, state, sourceKind, sourceId, graphRevision, edgeRevision
            }));

    private static string? ParseState(string json, KnowledgeApplicationBinding binding)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty(binding.StateProperty, out var value) ||
                value.ValueKind != JsonValueKind.String) return null;
            var state = value.GetString();
            return state is not null && (binding.ContentStates.Contains(state, StringComparer.Ordinal) ||
                state == binding.FamiliarState || state == binding.UnknownState) ? state : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool Baseline(string json, KnowledgeApplicationBinding binding)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1 &&
                   root.TryGetProperty(binding.BaselineInheritanceProperty, out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   value.GetString() == binding.BaselineInheritanceValue;
        }
        catch (JsonException) { return false; }
    }

    private static bool Bounded(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;

    private readonly record struct ScopeResolution(bool Valid, bool Applies);
}
