using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Builds the read-only virtual world used while a root assembles an atomic new-entity bundle.
/// It validates the complete accumulated effect sequence on every append but never applies it.
/// </summary>
public sealed class StagedWorldComposer(IEffectApplier effects, IWorldStore world) : IStagedWorldComposer
{
    private readonly IEffectApplier _effects = effects;
    private readonly IWorldStore _world = world;

    public Task<StagedWorldPlan> StartAsync(
        StagedWorldBoundary boundary,
        CancellationToken cancellationToken = default)
    {
        if (boundary is null || string.IsNullOrWhiteSpace(boundary.Target?.EntityId) ||
            string.IsNullOrWhiteSpace(boundary.Target?.Name))
            return Task.FromResult(Invalid(boundary, "INVALID_STAGED_TARGET", "target", "A staged target needs a non-empty entity id and name."));

        if (boundary.AllowedEntityIds is null || !boundary.AllowedEntityIds.Contains(boundary.Target.EntityId))
            return Task.FromResult(Invalid(boundary, "STAGED_TARGET_NOT_ALLOWED", "allowedEntityIds", "The staged target must be included in the root's allowed entity ids."));

        var initial = new Effect
        {
            Type = EffectType.EntityCreate,
            EntityId = boundary.Target.EntityId,
            Name = boundary.Target.Name
        };
        return BuildAsync(boundary, [initial], cancellationToken);
    }

    public Task<StagedWorldPlan> AppendAsync(
        StagedWorldPlan prior,
        IReadOnlyList<Effect> fragment,
        CancellationToken cancellationToken = default)
    {
        if (prior is null || !prior.Valid)
            return Task.FromResult(Invalid(prior?.Boundary, "STAGED_PLAN_REQUIRED", "prior", "Append requires a valid staged plan."));

        fragment ??= [];
        var combined = prior.Effects.Concat(fragment).ToArray();
        return BuildAsync(prior.Boundary, combined, cancellationToken);
    }

    private async Task<StagedWorldPlan> BuildAsync(
        StagedWorldBoundary boundary,
        IReadOnlyList<Effect> effects,
        CancellationToken cancellationToken)
    {
        var boundaryProblem = BoundaryProblem(boundary, effects);
        if (boundaryProblem is not null) return Invalid(boundary, boundaryProblem.Code, boundaryProblem.Path, boundaryProblem.Reason);

        var result = await _effects.ApplyAsync(effects, dryRun: true, cancellationToken: cancellationToken);
        if (!result.Valid || result.Blocked)
        {
            var reason = result.Blocked
                ? $"Dry-run guard rejected the staged bundle: {result.BlockCode}: {result.BlockReason}"
                : string.Join(" ", result.Problems.Select(problem => $"[{problem.Index}] {problem.Problem}"));
            return Invalid(boundary, "STAGED_EFFECTS_INVALID", "effects", reason);
        }

        return new("valid", boundary, effects.ToArray(), new StagedWorldStore(_world, effects), []);
    }

    private static StagedWorldProblem? BoundaryProblem(StagedWorldBoundary boundary, IReadOnlyList<Effect> effects)
    {
        foreach (var (effect, index) in effects.Select((effect, index) => (effect, index)))
        {
            if (effect is null) continue;
            if (!Allowed(effect.EntityId, boundary.AllowedEntityIds))
                return new("STAGED_ENTITY_NOT_ALLOWED", $"effects[{index}].entityId", "A staged child may name only an entity id declared by the root boundary.");
            if (!string.IsNullOrWhiteSpace(effect.ToEntityId) && !Allowed(effect.ToEntityId, boundary.AllowedEntityIds))
                return new("STAGED_ENTITY_NOT_ALLOWED", $"effects[{index}].toEntityId", "A staged child may name only an entity id declared by the root boundary.");
        }

        var first = effects.FirstOrDefault();
        if (first is null || first.Type != EffectType.EntityCreate || first.EntityId != boundary.Target.EntityId || first.Name != boundary.Target.Name)
            return new("STAGED_TARGET_ORDER_INVALID", "effects[0]", "The root target creation must remain the first unchanged staged effect.");
        return null;
    }

    private static bool Allowed(string? id, IReadOnlySet<string> allowed) =>
        !string.IsNullOrWhiteSpace(id) && allowed.Contains(id.Trim());

    private static StagedWorldPlan Invalid(StagedWorldBoundary? boundary, string code, string path, string reason) =>
        new("invalid", boundary ?? new(new("", ""), new HashSet<string>(StringComparer.Ordinal)), [], null, [new(code, path, reason)]);
}
