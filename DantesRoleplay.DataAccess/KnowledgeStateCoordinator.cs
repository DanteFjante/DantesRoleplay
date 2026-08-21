using System.Text.Json;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Slice 1's only trusted-host owner for effective knowledge state.</summary>
public sealed class KnowledgeStateCoordinator(IWorldStore world) : IKnowledgeStateCoordinator
{
    private const string Fact = "game.core.world.fact";
    private const string Rumour = "game.core.world.rumour";
    private const string Secret = "game.core.world.secret";
    private const string Clue = "game.core.world.clue";
    private const string Classification = "game.core.world.knowledge.classification";
    private const string KnowledgeWorld = "game.core.world.knowledge.in-world";
    private const string Baseline = "game.core.world.knowledge.baseline";
    private const string State = "game.core.world.knowledge.state";
    private const string WorldRoot = "game.core.world.root";
    private const string Location = "game.core.world.location";
    private const string Faction = "game.core.world.faction";
    private const string FactionWorld = "game.core.world.faction.in-world";
    private const string FactionMember = "game.core.world.faction.member";

    private readonly IWorldStore _world = world;

    public async Task<KnowledgeStateWriteResult> RecordStateAsync(
        RecordKnowledgeStateRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        var knowledgeId = request?.KnowledgeId ?? string.Empty;
        if (request is null || !ActorId(actorId) || !KnowledgeEpistemicStates.All.Contains(request.State))
            return Reject(actorId, knowledgeId, request?.State, "INVALID_KNOWLEDGE_STATE_REQUEST", "payload", "A state write requires canonical actor and knowledge ids and one closed epistemic state.");

        if (await _world.GetEntityAsync(actorId, cancellationToken) is null)
            return Reject(actorId, knowledgeId, request.State, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing actor entity.");
        var knowledge = await ReadKnowledgeAsync(knowledgeId, cancellationToken);
        if (knowledge.Problem is not null)
            return Reject(actorId, knowledgeId, request.State, knowledge.Problem.Code, knowledge.Problem.Path, knowledge.Problem.Reason);

        await _world.RelateAsync(actorId, knowledgeId, State, $"{{\"state\":\"{request.State}\"}}", cancellationToken);
        return new("recorded", actorId, knowledgeId, request.State, []);
    }

    public async Task<KnowledgeStateWriteResult> RecordBaselineAsync(
        RecordKnowledgeBaselineRequest request,
        CancellationToken cancellationToken = default)
    {
        var scopeId = request?.ScopeId ?? string.Empty;
        var knowledgeId = request?.KnowledgeId ?? string.Empty;
        if (request is null || !Id(scopeId) || !Id(knowledgeId))
            return Reject(scopeId, knowledgeId, null, "INVALID_KNOWLEDGE_BASELINE_REQUEST", "payload", "A baseline write requires two canonical entity ids.");

        var knowledge = await ReadKnowledgeAsync(knowledgeId, cancellationToken);
        if (knowledge.Problem is not null)
            return Reject(scopeId, knowledgeId, null, knowledge.Problem.Code, knowledge.Problem.Path, knowledge.Problem.Reason);
        var scope = await ScopeAsync(scopeId, knowledge.WorldId!, cancellationToken);
        if (scope.Problem is not null)
            return Reject(scopeId, knowledgeId, null, scope.Problem.Code, scope.Problem.Path, scope.Problem.Reason);

        await _world.RelateAsync(scopeId, knowledgeId, Baseline, "{\"inheritance\":\"current-scope\"}", cancellationToken);
        return new("recorded", scopeId, knowledgeId, "known", []);
    }

    public async Task<EffectiveKnowledgeStateResult> ResolveAsync(
        string actorId,
        string knowledgeId,
        CancellationToken cancellationToken = default)
    {
        if (!ActorId(actorId) || !Id(knowledgeId))
            return Fail("INVALID_KNOWLEDGE_QUERY", "payload", "Resolve requires canonical actor and knowledge ids.");
        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null) return Fail("ACTOR_NOT_FOUND", "actorId", "actorId must name an existing actor entity.");
        var knowledge = await ReadKnowledgeAsync(knowledgeId, cancellationToken);
        if (knowledge.Problem is not null) return Fail(knowledge.Problem.Code, knowledge.Problem.Path, knowledge.Problem.Reason);

        var actorLinks = await _world.GetRelationshipsAsync(actorId, includeIncoming: false, cancellationToken);
        var explicitStates = actorLinks.Where(link => link.Kind == State && link.ToEntityId == knowledgeId).ToArray();
        if (explicitStates.Length > 1) return Fail("KNOWLEDGE_STATE_DUPLICATE", "actorId", "An actor may have only one current explicit state for a knowledge record.");
        if (explicitStates.Length == 1)
        {
            var state = ParseState(explicitStates[0].Data);
            if (state is null) return Fail("KNOWLEDGE_STATE_INVALID", "state", "The explicit state relationship must contain exactly one valid state.");
            return Success(actorId, knowledgeId, knowledge.WorldId!, state, "explicit-state", actorId);
        }

        var incoming = await _world.GetRelationshipsAsync(knowledgeId, includeIncoming: true, cancellationToken);
        var baselines = incoming.Where(link => link.Kind == Baseline && link.ToEntityId == knowledgeId).ToArray();
        foreach (var baseline in baselines.OrderBy(link => link.FromEntityId, StringComparer.Ordinal))
        {
            if (!CurrentScope(baseline.Data)) return Fail("KNOWLEDGE_BASELINE_INVALID", "baseline", "A baseline relationship must contain exactly current-scope inheritance.");
            var scope = await ScopeAsync(baseline.FromEntityId, knowledge.WorldId!, cancellationToken);
            if (scope.Problem is not null) return Fail(scope.Problem.Code, scope.Problem.Path, scope.Problem.Reason);
            if (scope.Kind == "faction" && await IsFactionMemberAsync(scope.Id!, actorId, cancellationToken))
                return Success(actorId, knowledgeId, knowledge.WorldId!, "known", "faction-baseline", scope.Id);
            if (scope.Kind == "region" && await IsInRegionAsync(actor!, scope.Id!, cancellationToken))
                return Success(actorId, knowledgeId, knowledge.WorldId!, "known", "region-baseline", scope.Id);
        }

        var worldBaseline = baselines.SingleOrDefault(link => link.FromEntityId == knowledge.WorldId);
        if (worldBaseline is not null)
        {
            if (!CurrentScope(worldBaseline.Data)) return Fail("KNOWLEDGE_BASELINE_INVALID", "baseline", "A baseline relationship must contain exactly current-scope inheritance.");
            return Success(actorId, knowledgeId, knowledge.WorldId!, "known", "world-baseline", knowledge.WorldId);
        }

        return Success(actorId, knowledgeId, knowledge.WorldId!, "unknown", "derived-unknown", null);
    }

    private async Task<(string? WorldId, KnowledgeStateProblem? Problem)> ReadKnowledgeAsync(string knowledgeId, CancellationToken cancellationToken)
    {
        if (!Id(knowledgeId)) return (null, Problem("INVALID_KNOWLEDGE_ID", "knowledgeId", "knowledgeId must be a canonical entity id."));
        var entity = await _world.GetEntityAsync(knowledgeId, cancellationToken);
        if (entity is null) return (null, Problem("KNOWLEDGE_NOT_FOUND", "knowledgeId", "knowledgeId must name an existing knowledge entity."));
        if (entity.Components.Count(component => component.DefinitionId is Fact or Rumour or Secret or Clue) != 1)
            return (null, Problem("KNOWLEDGE_KIND_INVALID", "knowledgeId", "Knowledge must have exactly one fact, rumour, secret, or clue component."));
        var classifications = entity.Components.Where(component => component.DefinitionId == Classification).ToArray();
        if (classifications.Length != 1 || !ClassificationValid(classifications[0].Data))
            return (null, Problem("KNOWLEDGE_CLASSIFICATION_INVALID", "knowledgeId", "Knowledge must have one closed valid classification component."));
        var links = await _world.GetRelationshipsAsync(knowledgeId, includeIncoming: false, cancellationToken);
        var worlds = links.Where(link => link.Kind == KnowledgeWorld && link.FromEntityId == knowledgeId && Empty(link.Data)).ToArray();
        if (worlds.Length != 1) return (null, Problem("KNOWLEDGE_WORLD_INVALID", "knowledgeId", "Knowledge must have exactly one empty-data world scope link."));
        var root = await _world.GetEntityAsync(worlds[0].ToEntityId, cancellationToken);
        if (root is null || !Active(Component(root, WorldRoot)))
            return (null, Problem("KNOWLEDGE_WORLD_INVALID", "knowledgeId", "The knowledge world scope must name an active world root."));
        return (root.Id, null);
    }

    private async Task<(string? Id, string? Kind, KnowledgeStateProblem? Problem)> ScopeAsync(string scopeId, string worldId, CancellationToken cancellationToken)
    {
        var entity = await _world.GetEntityAsync(scopeId, cancellationToken);
        if (entity is null) return (null, null, Problem("KNOWLEDGE_SCOPE_NOT_FOUND", "scopeId", "A baseline scope must name an existing entity."));
        if (scopeId == worldId && Active(Component(entity, WorldRoot))) return (scopeId, "world", null);
        if (Region(Component(entity, Location)) && await ContainedByAsync(scopeId, worldId, cancellationToken)) return (scopeId, "region", null);
        if (Active(Component(entity, Faction)) && await HasLinkAsync(scopeId, worldId, FactionWorld, cancellationToken)) return (scopeId, "faction", null);
        return (null, null, Problem("KNOWLEDGE_SCOPE_INVALID", "scopeId", "Baseline scopes must be the scoped world, a region in that world, or a faction in that world."));
    }

    private async Task<bool> IsFactionMemberAsync(string factionId, string actorId, CancellationToken cancellationToken) =>
        await HasLinkAsync(factionId, actorId, FactionMember, cancellationToken);

    private async Task<bool> IsInRegionAsync(EntitySnapshot actor, string regionId, CancellationToken cancellationToken)
    {
        var current = actor.ContainerId;
        for (var depth = 0; depth < 20 && current is not null; depth++)
        {
            if (current == regionId) return true;
            current = (await _world.GetEntityAsync(current, cancellationToken))?.ContainerId;
        }
        return false;
    }

    private async Task<bool> ContainedByAsync(string entityId, string ancestorId, CancellationToken cancellationToken)
    {
        var current = (await _world.GetEntityAsync(entityId, cancellationToken))?.ContainerId;
        for (var depth = 0; depth < 20 && current is not null; depth++)
        {
            if (current == ancestorId) return true;
            current = (await _world.GetEntityAsync(current, cancellationToken))?.ContainerId;
        }
        return false;
    }

    private async Task<bool> HasLinkAsync(string fromId, string toId, string kind, CancellationToken cancellationToken) =>
        (await _world.GetRelationshipsAsync(fromId, includeIncoming: false, cancellationToken))
        .Any(link => link.ToEntityId == toId && link.Kind == kind && Empty(link.Data));

    private static EffectiveKnowledgeStateResult Success(string actor, string knowledge, string world, string state, string sourceKind, string? sourceId) =>
        new(new(actor, knowledge, world, state, sourceKind, sourceId), []);
    private static EffectiveKnowledgeStateResult Fail(string code, string path, string reason) => new(null, [Problem(code, path, reason)]);
    private static KnowledgeStateWriteResult Reject(string actorOrScope, string knowledge, string? state, string code, string path, string reason) =>
        new("rejected", actorOrScope, knowledge, state, [Problem(code, path, reason)]);
    private static KnowledgeStateProblem Problem(string code, string path, string reason) => new(code, path, reason);

    private static string? Component(EntitySnapshot entity, string definition) => entity.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data;
    private static bool ActorId(string? id) => Id(id) && id!.StartsWith("actor.", StringComparison.Ordinal);
    private static bool Id(string? id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 200 && id.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Empty(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool CurrentScope(string json) { try { using var d = JsonDocument.Parse(json); var x = d.RootElement; return x.ValueKind == JsonValueKind.Object && x.EnumerateObject().Count() == 1 && x.TryGetProperty("inheritance", out var value) && value.GetString() == "current-scope"; } catch { return false; } }
    private static string? ParseState(string json) { try { using var d = JsonDocument.Parse(json); var x = d.RootElement; return x.ValueKind == JsonValueKind.Object && x.EnumerateObject().Count() == 1 && x.TryGetProperty("state", out var value) && value.ValueKind == JsonValueKind.String && KnowledgeEpistemicStates.All.Contains(value.GetString()!) ? value.GetString() : null; } catch { return null; } }
    private static bool ClassificationValid(string json) { try { using var d = JsonDocument.Parse(json); var x = d.RootElement; return x.ValueKind == JsonValueKind.Object && x.EnumerateObject().Count() == 2 && x.TryGetProperty("subjectKind", out var subject) && subject.ValueKind == JsonValueKind.String && subject.GetString() is "state" or "event" or "identity" or "relationship" or "location" or "capability" or "rule" or "quantity" or "intention" or "negative" && x.TryGetProperty("sensitivity", out var sensitivity) && sensitivity.ValueKind == JsonValueKind.String && sensitivity.GetString() is "open" or "discreet" or "confidential" or "secret"; } catch { return false; } }
    private static bool Active(string? json) { try { using var d = JsonDocument.Parse(json ?? string.Empty); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("status", out var status) && status.GetString() == "active"; } catch { return false; } }
    private static bool Region(string? json) { try { using var d = JsonDocument.Parse(json ?? string.Empty); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "region" && d.RootElement.TryGetProperty("status", out var status) && status.GetString() == "active"; } catch { return false; } }
}
