using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Operations;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Validate the whole list, then apply the whole list, or apply none of it.
///
/// The validation pass simulates the batch as it walks it, so an effect may legitimately depend on
/// an earlier one — create an entity, then put a component on it — while a reference to something
/// that exists nowhere is still caught before a single row is written.
/// </summary>
public sealed class EffectApplier(DantesRoleplayDbContext db, IWorldStore world, IGuardRouter? guards = null, IEventLedger? events = null, IEventRouter? reactions = null) : IEffectApplier
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IGuardRouter? _guards = guards;
    private readonly IEventLedger? _events = events;
    private readonly IEventRouter? _reactions = reactions;

    public async Task<EffectResult> ApplyAsync(
        IReadOnlyList<Effect> effects,
        bool dryRun = false,
        CancellationToken cancellationToken = default,
        string rootOperationId = "",
        int depth = 0,
        string causationEventId = "")
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
            // Direct effect dry runs must exercise the same guards against the same final batch
            // state as a commit. An ambient transaction belongs to ActionRunner's existing
            // validation pass, which intentionally does not mutate it; its real apply below is
            // still guarded before that runner commits.
            return _guards is not null && _db.Database.CurrentTransaction is null
                ? await DryRunWithGuardsAsync(effects, cancellationToken)
                : new EffectResult(Applied: false, Count: 0, Problems: []) { ProposedEvents = Proposals(effects, new string[effects.Count], Operation.NewId(), 0, string.Empty) };
        }

        return await ApplyValidatedAsync(effects, rootOperationId, depth, causationEventId, cancellationToken);
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
        string rootOperationId,
        int depth,
        string causationEventId,
        CancellationToken cancellationToken)
    {
        // A caller that already opened one (run_action, later) keeps ownership: it decides whether
        // the surrounding work commits, and a nested Begin would simply throw.
        var ownsTransaction = _db.Database.CurrentTransaction is null;

        var transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Known before anything is proposed, so a guard sees the chain it is being asked about
        // rather than a payload with no context. Also what the accepted rows are written under.
        var correlation = string.IsNullOrWhiteSpace(rootOperationId)
            ? Operation.NewId()
            : rootOperationId.Trim();

        var index = 0;

        // The id an effect actually touched, which is not always the id it named: entity.create
        // with no id gets one from the store, and an event that reported an empty entity id would
        // be a record of something happening to nothing.
        var resolved = new string[effects.Count];

        try
        {
            for (; index < effects.Count; index++)
            {
                resolved[index] = await ApplyOneAsync(effects[index], cancellationToken);
            }

            var proposals = Proposals(effects, resolved, correlation, depth, causationEventId);
            var evaluations = Array.Empty<GuardEvaluation>() as IReadOnlyList<GuardEvaluation>;

            if (_guards is not null && proposals.Count > 0)
            {
                var guarded = await _guards.EvaluateAsync(proposals, cancellationToken);

                if (!guarded.Allowed)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }

                    _db.ChangeTracker.Clear();

                    return new EffectResult(Applied: false, Count: 0, Problems: [])
                    {
                        Blocked = true,
                        BlockCode = guarded.Code,
                        BlockReason = guarded.Reason,
                        ProposedEvents = proposals,
                        GuardEvaluations = guarded.Evaluations
                    };
                }

                evaluations = guarded.Evaluations;
            }

            // One correlation for the batch, decided above. This used to be three near-identical
            // blocks with a fresh Guid in each, and the unguarded one committed BEFORE writing its
            // events — so a failure between the two left a committed world change with no record.
            var accepted = _events is not null && proposals.Count > 0
                ? await _events.WriteAcceptedAsync(proposals, correlation, cancellationToken)
                : [];

            // The chain runs from the root change only. A child batch is applied BY the loop, so
            // letting it start a loop of its own would give every branch its own budget and defeat
            // the limits entirely.
            if (depth == 0 && _reactions is not null && accepted.Count > 0)
            {
                var routed = await RunChainAsync(accepted, correlation, cancellationToken);

                if (routed is not null)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }

                    _db.ChangeTracker.Clear();

                    return new EffectResult(Applied: false, Count: 0, Problems: [])
                    {
                        Blocked = true,
                        BlockCode = routed.Code,
                        BlockReason = routed.Reason,
                        ProposedEvents = proposals,
                        GuardEvaluations = evaluations
                    };
                }
            }

            // Committed only after the ledger write and every reaction are enrolled, so a change,
            // its events and its consequences are one atomic fact. A caller that owns the
            // transaction commits all of it when it is ready.
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new EffectResult(Applied: true, Count: effects.Count, Problems: [])
            {
                ProposedEvents = proposals,
                GuardEvaluations = evaluations,
                CorrelationId = correlation,
                AcceptedEvents = accepted
            };
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

    /// <summary>
    /// Turns applied effects into proposed events whose payloads conform to the registered schemas.
    ///
    /// The nine `world.*` event types are catalog contracts with `additionalProperties: false` and
    /// camelCase names, so the payload is built per type rather than by serialising the effect
    /// object. Serialising the effect was what shipped first, and it produced PascalCase keys plus
    /// five extra properties on every row — a payload that violated its own registered schema in
    /// three different ways, on every event ever written. Nothing validated it, so nothing said so.
    ///
    /// `data` is embedded as JSON, not as a string containing JSON. A quoted blob would technically
    /// satisfy an untyped `data` slot while being useless to every consumer.
    /// </summary>
    /// <summary>
    /// Runs the whole reactive chain: every reaction on every accepted event, and every event those
    /// reactions cause, until nothing is left or a limit stops it.
    ///
    /// Returns null on success, or the failure that fails the ENTIRE root change. There is no
    /// partial chain. A chain cut off half way would leave the world in a state no rule intended
    /// and no reader could explain — three consequences applied, the fourth not, nothing recording
    /// why — and that is strictly worse than the change not happening.
    ///
    /// A reaction's effects go back through this same applier, so they are validated and guarded
    /// exactly like any other change. A reaction is not a way around the rules that govern the
    /// change that triggered it.
    /// </summary>
    private async Task<EventRoutingResult?> RunChainAsync(
        IReadOnlyList<EventDetail> rootEvents,
        string correlation,
        CancellationToken cancellationToken)
    {
        var budget = new ChainBudget();
        var seed = EventRouter.RootSeedFrom(correlation);

        var counted = budget.CountEvents(rootEvents.Count);

        if (counted is not null)
        {
            return Overspent(counted, "the root change itself");
        }

        // FIFO, and that is enough to dequeue in sequence order: sequences only ever increase
        // within a correlation, and children are appended after the events that caused them.
        var queue = new Queue<EventDetail>(rootEvents.OrderBy(e => e.Sequence));
        var ordinal = 0;

        while (queue.Count > 0)
        {
            var @event = queue.Dequeue();

            var tooDeep = budget.CheckDepth(@event.Depth);

            if (tooDeep is not null)
            {
                return Overspent(tooDeep, $"event {@event.Id} ({@event.TypeId})");
            }

            var routed = await _reactions!.RouteAsync([@event], seed, budget, ordinal, cancellationToken);

            if (!routed.Ok)
            {
                return routed;
            }

            ordinal += routed.Outcomes.Count;

            foreach (var outcome in routed.Outcomes)
            {
                if (outcome.Effects.Count > 0)
                {
                    var applied = await ApplyAsync(
                        outcome.Effects,
                        dryRun: false,
                        cancellationToken,
                        rootOperationId: correlation,
                        depth: @event.Depth + 1,
                        causationEventId: @event.Id);

                    // A child batch that is rejected or vetoed takes the root down with it. The
                    // consequence is part of the change, not a follow-up to it.
                    if (!applied.Applied)
                    {
                        return EventRoutingResult.Abort(
                            applied.Blocked ? applied.BlockCode : "SUBSCRIBER_INVALID_EFFECTS",
                            applied.Blocked
                                ? applied.BlockReason
                                : $"Subscription '{outcome.Execution.SubscriptionId}' proposed effects that "
                                  + $"were rejected: {string.Join("; ", applied.Problems.Select(p => p.Problem))}");
                    }

                    var accepted = budget.CountEvents(applied.AcceptedEvents.Count);

                    if (accepted is not null)
                    {
                        return Overspent(accepted, $"subscription '{outcome.Execution.SubscriptionId}'");
                    }

                    foreach (var child in applied.AcceptedEvents)
                    {
                        queue.Enqueue(child);
                    }
                }

                _db.EventExecutions.Add(outcome.Execution);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private static EventRoutingResult Overspent(string code, string where) =>
        EventRoutingResult.Abort(code, $"{ChainBudget.Explain(code)} Reached by {where}.");

    private static IReadOnlyList<ProposedEvent> Proposals(
        IReadOnlyList<Effect> effects,
        IReadOnlyList<string> resolvedEntityIds,
        string correlationId,
        int depth,
        string causationEventId)
    {
        var proposals = new List<ProposedEvent>(effects.Count);

        for (var ordinal = 0; ordinal < effects.Count; ordinal++)
        {
            var effect = effects[ordinal];

            var entityId = ordinal < resolvedEntityIds.Count && !string.IsNullOrWhiteSpace(resolvedEntityIds[ordinal])
                ? resolvedEntityIds[ordinal]
                : effect.EntityId.Trim();

            var toEntityId = effect.ToEntityId.Trim();
            var definitionId = effect.DefinitionId.Trim();
            var payload = new JsonObject();

            // Declared order, not sorted. For a relationship the first id is the "from" and the
            // second the "to", and sorting them would throw away which end is which.
            var ids = new List<string> { entityId };

            string type;

            switch (Type(effect))
            {
                case EffectType.EntityCreate:
                    type = "world.entity.created";
                    payload["entityId"] = entityId;
                    payload["name"] = effect.Name.Trim();
                    break;

                case EffectType.EntityDelete:
                    type = "world.entity.deleted";
                    payload["entityId"] = entityId;
                    break;

                case EffectType.ComponentAdd:
                case EffectType.ComponentSet:
                case EffectType.ComponentMerge:
                    type = Type(effect) switch
                    {
                        EffectType.ComponentAdd => "world.component.added",
                        EffectType.ComponentSet => "world.component.replaced",
                        _ => "world.component.merged"
                    };
                    payload["entityId"] = entityId;
                    payload["definitionId"] = definitionId;
                    payload["data"] = Data(effect);
                    break;

                case EffectType.ComponentRemove:
                    type = "world.component.removed";
                    payload["entityId"] = entityId;
                    payload["definitionId"] = definitionId;
                    break;

                case EffectType.ContainmentMove:
                    type = "world.containment.moved";
                    payload["entityId"] = entityId;

                    // Explicitly null when the entity was moved to nowhere. The schema allows null
                    // here precisely so "taken out of its container" is expressible.
                    payload["toEntityId"] = toEntityId.Length == 0 ? null : toEntityId;
                    payload["slot"] = effect.Slot.Trim();

                    if (toEntityId.Length > 0)
                    {
                        ids.Add(toEntityId);
                    }

                    break;

                case EffectType.RelationshipCreate:
                    type = "world.relationship.created";
                    payload["fromEntityId"] = entityId;
                    payload["toEntityId"] = toEntityId;
                    payload["kind"] = effect.Kind.Trim();
                    payload["data"] = Data(effect);
                    ids.Add(toEntityId);
                    break;

                case EffectType.RelationshipRemove:
                    type = "world.relationship.removed";
                    payload["fromEntityId"] = entityId;
                    payload["toEntityId"] = toEntityId;
                    payload["kind"] = effect.Kind.Trim();
                    ids.Add(toEntityId);
                    break;

                default:
                    throw new InvalidOperationException($"No structural event type for '{effect.Type}'.");
            }

            proposals.Add(new ProposedEvent(
                type,
                payload.ToJsonString(),
                ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList(),
                string.Empty,
                ordinal,

                // A root world change is caused by the caller, not by another event. A reaction's
                // effects arrive here one deeper, naming the event they answer.
                Depth: depth,
                CorrelationId: correlationId,
                CausationId: causationEventId));
        }

        return proposals;
    }

    /// <summary>An effect's data as JSON, or an empty object. Never a string containing JSON.</summary>
    private static JsonNode Data(Effect effect)
    {
        if (string.IsNullOrWhiteSpace(effect.Data))
        {
            return new JsonObject();
        }

        // Validation has already rejected a malformed payload, so a failure here would mean the
        // batch was applied without being checked — worth surfacing rather than swallowing.
        return JsonNode.Parse(effect.Data) ?? new JsonObject();
    }

    private async Task<EffectResult> DryRunWithGuardsAsync(IReadOnlyList<Effect> effects, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var index = 0;
        try
        {
            var resolved = new string[effects.Count];
            for (; index < effects.Count; index++) resolved[index] = await ApplyOneAsync(effects[index], cancellationToken);
            var proposals = Proposals(effects, resolved, Operation.NewId(), 0, string.Empty);
            var guarded = await _guards!.EvaluateAsync(proposals, cancellationToken);
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return new EffectResult(Applied: false, Count: 0, Problems: [])
            {
                Blocked = !guarded.Allowed,
                BlockCode = guarded.Code,
                BlockReason = guarded.Reason,
                ProposedEvents = proposals,
                GuardEvaluations = guarded.Evaluations
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            var offending = index < effects.Count ? effects[index] : effects[^1];
            return new EffectResult(false, 0, [new EffectProblem(Math.Min(index, effects.Count - 1), Describe(offending), ex.Message)]);
        }
    }

    /// <summary>
    /// Applies one effect and returns the entity id it actually touched.
    ///
    /// Today that is always the id the effect named, because validation requires an explicit id on
    /// entity.create — deliberately, so a later effect in the same list can reference it. The store
    /// can still mint one when called directly, so reporting what it returned costs nothing and
    /// keeps an event from ever naming a blank entity. It is also the first step of the receipt
    /// pipeline the events plan describes; nothing yet relies on it doing more than echoing.
    /// </summary>
    private async Task<string> ApplyOneAsync(Effect effect, CancellationToken cancellationToken)
    {
        var entityId = effect.EntityId.Trim();
        var toEntityId = effect.ToEntityId.Trim();
        var definitionId = effect.DefinitionId.Trim();
        var data = string.IsNullOrWhiteSpace(effect.Data) ? "{}" : effect.Data;

        switch (Type(effect))
        {
            case EffectType.EntityCreate:
                entityId = (await _world.CreateEntityAsync(effect.Name.Trim(), entityId, cancellationToken)).Id;
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

        return entityId;
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
