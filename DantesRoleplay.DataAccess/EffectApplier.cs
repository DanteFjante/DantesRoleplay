using DantesRoleplay.Effects;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Validate the whole list, then apply the whole list, or apply none of it.
///
/// The validation pass simulates the batch as it walks it, so an effect may legitimately depend on
/// an earlier one — create an entity, then put a component on it — while a reference to something
/// that exists nowhere is still caught before a single row is written.
/// </summary>
public sealed class EffectApplier(DantesRoleplayDbContext db, IWorldStore world) : IEffectApplier
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;

    public async Task<EffectResult> ApplyAsync(
        IReadOnlyList<Effect> effects,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        effects ??= [];

        if (effects.Count == 0)
        {
            return new EffectResult(Applied: !dryRun, Count: 0, Problems: []);
        }

        var problems = new List<EffectProblem>();

        // Pass one: shape. Nothing here touches the database.
        var wellFormed = new bool[effects.Count];

        for (var i = 0; i < effects.Count; i++)
        {
            var problem = EffectValidation.Check(effects[i]);

            if (problem is null)
            {
                wellFormed[i] = true;
            }
            else
            {
                problems.Add(new EffectProblem(i, Describe(effects[i]), problem));
            }
        }

        // Pass two: does what it names actually exist, given everything before it in this list?
        await CheckReferencesAsync(effects, wellFormed, problems, cancellationToken);

        if (problems.Count > 0)
        {
            return new EffectResult(Applied: false, Count: 0, Problems: OrderedByIndex(problems));
        }

        if (dryRun)
        {
            return new EffectResult(Applied: false, Count: 0, Problems: []);
        }

        return await ApplyValidatedAsync(effects, cancellationToken);
    }

    // ---- validation -------------------------------------------------------------------

    private async Task CheckReferencesAsync(
        IReadOnlyList<Effect> effects,
        bool[] wellFormed,
        List<EffectProblem> problems,
        CancellationToken cancellationToken)
    {
        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        var definitionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var effect in effects)
        {
            Collect(entityIds, effect.EntityId);
            Collect(entityIds, effect.ToEntityId);
            Collect(definitionIds, effect.DefinitionId);
        }

        // Soft-deleted rows are returned too. An id that belonged to a deleted entity is still
        // taken — ids are permanent (§3.9), and silently reusing one would make history lie.
        var known = await _db.Entities
            .AsNoTracking()
            .Where(e => entityIds.Contains(e.Id))
            .Select(e => new { e.Id, Deleted = e.DeletedAt != null })
            .ToListAsync(cancellationToken);

        var takenIds = known.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var alive = known.Where(e => !e.Deleted).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        var definitions = (await _db.ComponentDefinitions
                .AsNoTracking()
                .Where(d => definitionIds.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var components = (await _db.Components
                .AsNoTracking()
                .Where(c => entityIds.Contains(c.EntityId))
                .Select(c => new { c.EntityId, c.DefinitionId })
                .ToListAsync(cancellationToken))
            .Select(c => Pair(c.EntityId, c.DefinitionId))
            .ToHashSet(StringComparer.Ordinal);

        var relationships = (await _db.Relationships
                .AsNoTracking()
                .Where(r => entityIds.Contains(r.FromEntityId))
                .Select(r => new { r.FromEntityId, r.ToEntityId, r.Kind })
                .ToListAsync(cancellationToken))
            .Select(r => Triple(r.FromEntityId, r.ToEntityId, r.Kind))
            .ToHashSet(StringComparer.Ordinal);

        // Seeded from every create that named an id, including ones that failed pass one for some
        // other reason. Without this, a single malformed create makes every later effect that
        // references it report a second, misleading "unknown entity".
        foreach (var effect in effects)
        {
            if (Type(effect) == EffectType.EntityCreate && !string.IsNullOrWhiteSpace(effect.EntityId))
            {
                alive.Add(effect.EntityId.Trim());
            }
        }

        for (var i = 0; i < effects.Count; i++)
        {
            if (!wellFormed[i])
            {
                continue;
            }

            var effect = effects[i];
            var type = Type(effect);
            var entityId = effect.EntityId.Trim();
            var toEntityId = effect.ToEntityId.Trim();
            var definitionId = effect.DefinitionId.Trim();

            void Fault(string problem) => problems.Add(new EffectProblem(i, Describe(effect), problem));

            switch (type)
            {
                case EffectType.EntityCreate:
                    if (takenIds.Contains(entityId))
                    {
                        Fault($"Entity id '{entityId}' is already taken. Ids are permanent — pick another.");
                    }

                    takenIds.Add(entityId);
                    alive.Add(entityId);
                    break;

                case EffectType.EntityDelete:
                    if (!RequireAlive(entityId, alive, Fault))
                    {
                        break;
                    }

                    alive.Remove(entityId);
                    components.RemoveWhere(p => p.StartsWith($"{entityId}\u001f", StringComparison.Ordinal));
                    break;

                case EffectType.ComponentAdd:
                case EffectType.ComponentSet:
                case EffectType.ComponentMerge:
                    if (!RequireAlive(entityId, alive, Fault) || !RequireDefinition(definitionId, definitions, Fault))
                    {
                        break;
                    }

                    if (type == EffectType.ComponentAdd && components.Contains(Pair(entityId, definitionId)))
                    {
                        Fault(
                            $"Entity '{entityId}' already has component '{definitionId}'. " +
                            "Use component.set to replace it or component.merge to patch it.");
                        break;
                    }

                    components.Add(Pair(entityId, definitionId));
                    break;

                case EffectType.ComponentRemove:
                    if (!RequireAlive(entityId, alive, Fault))
                    {
                        break;
                    }

                    if (!components.Contains(Pair(entityId, definitionId)))
                    {
                        Fault($"Entity '{entityId}' has no component '{definitionId}' to remove.");
                        break;
                    }

                    components.Remove(Pair(entityId, definitionId));
                    break;

                case EffectType.ContainmentMove:
                    if (!RequireAlive(entityId, alive, Fault))
                    {
                        break;
                    }

                    if (toEntityId.Length > 0)
                    {
                        RequireAlive(toEntityId, alive, Fault);
                    }

                    break;

                case EffectType.RelationshipCreate:
                    if (!RequireAlive(entityId, alive, Fault) || !RequireAlive(toEntityId, alive, Fault))
                    {
                        break;
                    }

                    relationships.Add(Triple(entityId, toEntityId, effect.Kind.Trim()));
                    break;

                case EffectType.RelationshipRemove:
                    if (!RequireAlive(entityId, alive, Fault) || !RequireAlive(toEntityId, alive, Fault))
                    {
                        break;
                    }

                    var triple = Triple(entityId, toEntityId, effect.Kind.Trim());

                    if (!relationships.Contains(triple))
                    {
                        Fault($"No '{effect.Kind.Trim()}' relationship from '{entityId}' to '{toEntityId}' to remove.");
                        break;
                    }

                    relationships.Remove(triple);
                    break;
            }
        }
    }

    private static bool RequireAlive(string id, HashSet<string> alive, Action<string> fault)
    {
        if (alive.Contains(id))
        {
            return true;
        }

        fault($"Unknown entity '{id}'. Create it first, or check the id with get_entities.");
        return false;
    }

    private static bool RequireDefinition(string id, HashSet<string> definitions, Action<string> fault)
    {
        if (definitions.Contains(id))
        {
            return true;
        }

        fault($"Unknown component definition '{id}'. Declare it with define_component first.");
        return false;
    }

    // ---- application ------------------------------------------------------------------

    /// <summary>
    /// One transaction around the lot.
    ///
    /// <see cref="WorldStore"/> saves after every operation, which is correct when it is called
    /// directly and fatal for a batch — so the batch opens its own transaction and nothing those
    /// saves write becomes visible until the last effect has succeeded.
    /// </summary>
    private async Task<EffectResult> ApplyValidatedAsync(
        IReadOnlyList<Effect> effects,
        CancellationToken cancellationToken)
    {
        // A caller that already opened one (run_action, later) keeps ownership: it decides whether
        // the surrounding work commits, and a nested Begin would simply throw.
        var ownsTransaction = _db.Database.CurrentTransaction is null;

        var transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var index = 0;

        try
        {
            for (; index < effects.Count; index++)
            {
                await ApplyOneAsync(effects[index], cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new EffectResult(Applied: true, Count: effects.Count, Problems: []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Validation covers what can be known in advance; this covers what cannot — a
            // containment cycle formed by the batch itself, a constraint the store enforces. The
            // list is reported as rejected rather than partly applied, which is the whole point.
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            _db.ChangeTracker.Clear();

            var offending = index < effects.Count ? effects[index] : effects[^1];

            return new EffectResult(
                Applied: false,
                Count: 0,
                Problems: [new EffectProblem(Math.Min(index, effects.Count - 1), Describe(offending), ex.Message)]);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task ApplyOneAsync(Effect effect, CancellationToken cancellationToken)
    {
        var entityId = effect.EntityId.Trim();
        var toEntityId = effect.ToEntityId.Trim();
        var definitionId = effect.DefinitionId.Trim();
        var data = string.IsNullOrWhiteSpace(effect.Data) ? "{}" : effect.Data;

        switch (Type(effect))
        {
            case EffectType.EntityCreate:
                await _world.CreateEntityAsync(effect.Name.Trim(), entityId, cancellationToken);
                break;

            case EffectType.EntityDelete:
                await _world.DeleteEntityAsync(entityId, cancellationToken);
                break;

            // add and set differ only in whether an existing component is an error, and validation
            // has already answered that — so by the time we get here they do the same thing.
            case EffectType.ComponentAdd:
            case EffectType.ComponentSet:
                await _world.SetComponentAsync(entityId, definitionId, data, cancellationToken);
                break;

            case EffectType.ComponentMerge:
                await _world.MergeComponentAsync(entityId, definitionId, data, cancellationToken);
                break;

            case EffectType.ComponentRemove:
                await _world.RemoveComponentAsync(entityId, definitionId, cancellationToken);
                break;

            case EffectType.ContainmentMove:
                await _world.MoveAsync(
                    entityId,
                    toEntityId.Length == 0 ? null : toEntityId,
                    effect.Slot.Trim(),
                    cancellationToken);
                break;

            case EffectType.RelationshipCreate:
                await _world.RelateAsync(entityId, toEntityId, effect.Kind.Trim(), data, cancellationToken);
                break;

            case EffectType.RelationshipRemove:
                await _world.UnrelateAsync(entityId, toEntityId, effect.Kind.Trim(), cancellationToken);
                break;
        }
    }

    // ---- helpers ----------------------------------------------------------------------

    private static string Type(Effect effect) => effect.Type?.Trim() ?? string.Empty;

    private static string Describe(Effect? effect) => effect?.ToString() ?? "(null)";

    private static void Collect(HashSet<string> into, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            into.Add(value.Trim());
        }
    }

    // Unit separator, so a key cannot be forged by an id that happens to contain the delimiter.
    private static string Pair(string entityId, string definitionId) => $"{entityId}\u001f{definitionId}";

    private static string Triple(string from, string to, string kind) => $"{from}\u001f{to}\u001f{kind}";

    private static IReadOnlyList<EffectProblem> OrderedByIndex(List<EffectProblem> problems) =>
        problems.OrderBy(p => p.Index).ToList();
}
