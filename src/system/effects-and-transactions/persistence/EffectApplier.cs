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
public sealed class EffectApplier(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IGuardRouter? guards = null,
    IEventLedger? events = null,
    IEventRouter? reactions = null) : IEffectApplier
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
        string causationEventId = "",
        IReadOnlyList<DeclaredEvent>? declaredEvents = null)
    {
        effects ??= [];
        declaredEvents ??= [];

        if (effects.Count == 0 && declaredEvents.Count == 0)
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
            // state as a commit. An ambient transaction belongs to the calling execution
            // boundary's validation pass, which intentionally does not mutate it; its real apply
            // below is still guarded before that boundary commits.
            if (_guards is not null && _db.Database.CurrentTransaction is null)
            {
                return await DryRunWithGuardsAsync(effects, cancellationToken);
            }

            return new EffectResult(Applied: false, Count: 0, Problems: [])
            {
                ProposedEvents = Proposals(
                    effects,
                    Unapplied(effects),
                    Operation.NewId(),
                    depth: 0,
                    causationEventId: string.Empty)
            };
        }

        return await ApplyValidatedAsync(effects, declaredEvents, rootOperationId, depth, causationEventId, cancellationToken);
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
        IReadOnlyList<DeclaredEvent> declaredEvents,
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

        // One receipt per applied effect: the id it actually touched, and the state it displaced.
        // Prior state cannot be read after the fact — it is gone — so each receipt is taken one
        // step before the store overwrites it, inside this transaction.
        var receipts = new EffectReceipt[effects.Count];

        try
        {
            for (; index < effects.Count; index++)
            {
                receipts[index] = await ApplyOneAsync(index, effects[index], cancellationToken);
            }

            var proposals = Proposals(effects, receipts, correlation, depth, causationEventId).ToList();
            if (declaredEvents.Count > 0)
            {
                var declared = await DerivedEvents.ProposeAsync(
                    _db, declaredEvents, "root action", "action:" + correlation, correlation,
                    causationEventId, depth, cancellationToken);
                if (!declared.Ok)
                {
                    if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    return new EffectResult(Applied: false, Count: 0, Problems: [])
                    {
                        Blocked = true,
                        BlockCode = declared.Code,
                        BlockReason = declared.Reason,
                        ProposedEvents = proposals
                    };
                }
                // Declared semantic events describe the already-proposed structural batch. Their
                // ordinal therefore starts after every structural proposal; sharing zero-based
                // ordinals would interleave them under the ledger's canonical ordering.
                var declaredStart = proposals.Count;
                proposals.AddRange(declared.Proposals.Select((proposal, declaredOrdinal) => proposal with
                {
                    Ordinal = declaredStart + declaredOrdinal
                }));
            }
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

        // Runs across the whole chain, not per event, so notices from one committed change read
        // back in the order the rules made them.
        var noticeOrdinal = 0;

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

                // Then whatever the rule declared happened beyond what its effects describe. This
                // runs AFTER the effects on purpose: a guard on a declared event should see the
                // world the same reaction just changed, not the world half way through its work.
                if (outcome.Events.Count > 0)
                {
                    var derived = await DerivedEvents.ProposeAsync(
                        _db,
                        outcome.Events,
                        outcome.Execution.SubscriptionId,
                        outcome.Execution.Id,
                        correlation,
                        @event.Id,
                        @event.Depth + 1,
                        cancellationToken);

                    if (!derived.Ok)
                    {
                        return EventRoutingResult.Abort(derived.Code, derived.Reason);
                    }

                    // Proposed, not accepted: proposing and guarding an event is the work the
                    // budget exists to bound, so a chain whose events are all vetoed still spends
                    // it. Counted before the guards run, because a limit enforced afterwards has
                    // already paid the cost.
                    var spent = budget.CountEvents(derived.Proposals.Count);

                    if (spent is not null)
                    {
                        return Overspent(spent, $"subscription '{outcome.Execution.SubscriptionId}'");
                    }

                    if (_guards is not null)
                    {
                        var guarded = await _guards.EvaluateAsync(derived.Proposals, cancellationToken);

                        // The same veto as any other, taking down the same root. A declared event
                        // is not a back door around the rules that govern the change it followed.
                        if (!guarded.Allowed)
                        {
                            return EventRoutingResult.Abort(guarded.Code, guarded.Reason);
                        }
                    }

                    IReadOnlyList<EventDetail> announced = _events is not null
                        ? await _events.WriteAcceptedAsync(derived.Proposals, correlation, cancellationToken)
                        : [];

                    foreach (var child in announced)
                    {
                        queue.Enqueue(child);
                    }
                }

                // And whatever it wants a person told. Last, because a notice describes work
                // that is finished: raising it before the effects and events it talks about would
                // be a statement about something that had not happened yet.
                if (outcome.Notifications.Count > 0)
                {
                    var notices = await DeclaredNotifications.BuildAsync(
                        _db,
                        outcome.Notifications,
                        outcome.Execution.SubscriptionId,
                        outcome.Execution.Id,
                        correlation,
                        @event.Id,
                        noticeOrdinal,
                        cancellationToken);

                    if (!notices.Ok)
                    {
                        return EventRoutingResult.Abort(notices.Code, notices.Reason);
                    }

                    _db.Notifications.AddRange(notices.Rows);
                    noticeOrdinal += notices.Rows.Count;
                }

                _db.EventExecutions.Add(outcome.Execution);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private static EventRoutingResult Overspent(string code, string where) =>
        EventRoutingResult.Abort(code, $"{ChainBudget.Explain(code)} Reached by {where}.");

    /// <summary>
    /// Turns receipts into proposed events.
    ///
    /// The split between the top level of a payload and its <c>before</c>/<c>after</c> is a rule,
    /// not a habit: the top level IDENTIFIES what changed and holds only filterable scalars, which
    /// is what a subscription's <c>payloadEquals</c> can match on. State lives in the two
    /// snapshots and is never filterable. Without that line, every new field would be an argument
    /// about which half it belongs in.
    /// </summary>
    private static IReadOnlyList<ProposedEvent> Proposals(
        IReadOnlyList<Effect> effects,
        IReadOnlyList<EffectReceipt> receipts,
        string correlationId,
        int depth,
        string causationEventId)
    {
        var proposals = new List<ProposedEvent>(effects.Count);

        for (var ordinal = 0; ordinal < effects.Count; ordinal++)
        {
            var effect = effects[ordinal];
            var receipt = receipts[ordinal];

            var entityId = !string.IsNullOrWhiteSpace(receipt.EntityId)
                ? receipt.EntityId
                : effect.EntityId.Trim();

            var before = Snapshot(receipt.BeforeJson);
            var after = Snapshot(receipt.AfterJson);

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
                    payload["effectIndex"] = receipt.Index;
                    payload["entityId"] = entityId;
                    payload["name"] = effect.Name.Trim();
                    payload["before"] = before;
                    payload["after"] = after;
                    break;

                case EffectType.EntityDelete:
                    type = "world.entity.deleted";
                    payload["effectIndex"] = receipt.Index;
                    payload["entityId"] = entityId;

                    // The whole entity, components and all. Deletion is the one change where the
                    // ledger is the only remaining copy of what was there.
                    payload["before"] = before;
                    payload["after"] = after;
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
                    payload["effectIndex"] = receipt.Index;
                    payload["entityId"] = entityId;
                    payload["definitionId"] = definitionId;

                    // A merge states its patch as well as its result, because the two are
                    // different facts: the patch is what the rule asked for, the after is what the
                    // world made of it, and a shallow merge is exactly where those diverge.
                    if (Type(effect) == EffectType.ComponentMerge)
                    {
                        payload["patch"] = Data(effect);
                    }

                    payload["before"] = before;
                    payload["after"] = after;
                    break;

                case EffectType.ComponentRemove:
                    type = "world.component.removed";
                    payload["effectIndex"] = receipt.Index;
                    payload["entityId"] = entityId;
                    payload["definitionId"] = definitionId;
                    payload["before"] = before;
                    payload["after"] = after;
                    break;

                case EffectType.ContainmentMove:
                    type = "world.containment.moved";
                    payload["effectIndex"] = receipt.Index;
                    payload["entityId"] = entityId;

                    // Explicitly null when the entity was moved to nowhere. The schema allows null
                    // here precisely so "taken out of its container" is expressible.
                    payload["toEntityId"] = toEntityId.Length == 0 ? null : toEntityId;
                    payload["slot"] = effect.Slot.Trim();

                    // Container and slot as they stood, and as they stand. "Moved out of the
                    // saddlebag into the pack" needs both ends; the effect only ever said one.
                    payload["before"] = before;
                    payload["after"] = after;

                    if (toEntityId.Length > 0)
                    {
                        ids.Add(toEntityId);
                    }

                    break;

                case EffectType.RelationshipCreate:
                    type = "world.relationship.created";
                    payload["effectIndex"] = receipt.Index;
                    payload["fromEntityId"] = entityId;
                    payload["toEntityId"] = toEntityId;
                    payload["kind"] = effect.Kind.Trim();
                    payload["before"] = before;
                    payload["after"] = after;
                    ids.Add(toEntityId);
                    break;

                case EffectType.RelationshipRemove:
                    type = "world.relationship.removed";
                    payload["effectIndex"] = receipt.Index;
                    payload["fromEntityId"] = entityId;
                    payload["toEntityId"] = toEntityId;
                    payload["kind"] = effect.Kind.Trim();
                    payload["before"] = before;
                    payload["after"] = after;
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

    /// <summary>A captured snapshot as JSON, or JSON null when there was nothing there.</summary>
    private static JsonNode? Snapshot(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);

    /// <summary>
    /// Canonical JSON, so two snapshots of one state are byte-identical and a reader diffing two
    /// events sees only the difference that is really there. Null stays null.
    /// </summary>
    private static string? Canonical(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)?.ToJsonString();

    /// <summary>Receipts for effects that were never applied — the dry-run path with no guards.</summary>
    private static IReadOnlyList<EffectReceipt> Unapplied(IReadOnlyList<Effect> effects)
    {
        var receipts = new EffectReceipt[effects.Count];

        for (var index = 0; index < effects.Count; index++)
        {
            receipts[index] = EffectReceipt.Unapplied(index, effects[index].EntityId.Trim());
        }

        return receipts;
    }

    /// <summary>
    /// A whole entity as a canonical snapshot, or null when it does not exist — which is also what
    /// a soft-deleted entity returns, correctly: it is gone as far as anything but the ledger is
    /// concerned.
    /// </summary>
    private async Task<string?> EntityJsonAsync(string entityId, CancellationToken cancellationToken)
    {
        var entity = await _world.GetEntityAsync(entityId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var components = new JsonObject();

        // Ordinal, so the same state snapshots to the same bytes whatever order the store returned.
        foreach (var component in entity.Components.OrderBy(c => c.DefinitionId, StringComparer.Ordinal))
        {
            components[component.DefinitionId] = Snapshot(component.Data) ?? new JsonObject();
        }

        return new JsonObject
        {
            ["id"] = entity.Id,
            ["name"] = entity.Name,
            ["containerId"] = entity.ContainerId,
            ["slot"] = entity.ContainerSlot,
            ["components"] = components
        }.ToJsonString();
    }

    /// <summary>One component's data, or null when the entity or the component is not there.</summary>
    private async Task<string?> ComponentJsonAsync(string entityId, string definitionId, CancellationToken cancellationToken)
    {
        var entity = await _world.GetEntityAsync(entityId, cancellationToken);

        var component = entity?.Components
            .FirstOrDefault(c => string.Equals(c.DefinitionId, definitionId, StringComparison.Ordinal));

        // An existing component with empty data is still an existing component, so it snapshots as
        // an empty object rather than as absent. The difference is the whole point of the field.
        return component is null ? null : Canonical(component.Data) ?? "{}";
    }

    /// <summary>Where an entity sits: its container and slot, or null when it does not exist.</summary>
    private async Task<string?> ContainmentJsonAsync(string entityId, CancellationToken cancellationToken)
    {
        var entity = await _world.GetEntityAsync(entityId, cancellationToken);

        return entity is null
            ? null
            : new JsonObject
            {
                ["containerId"] = entity.ContainerId,
                ["slot"] = entity.ContainerSlot
            }.ToJsonString();
    }

    /// <summary>One relationship's data, or null when that edge does not exist.</summary>
    private async Task<string?> RelationshipJsonAsync(
        string fromEntityId,
        string toEntityId,
        string kind,
        CancellationToken cancellationToken)
    {
        // Outgoing only: the edge being created or removed is this entity's, and including
        // incoming ones would make a symmetric kind between the same pair ambiguous.
        var edges = await _world.GetRelationshipsAsync(fromEntityId, includeIncoming: false, cancellationToken);

        var match = edges.FirstOrDefault(edge =>
            string.Equals(edge.ToEntityId, toEntityId, StringComparison.Ordinal)
            && string.Equals(edge.Kind, kind, StringComparison.Ordinal));

        return match is null ? null : Canonical(match.Data) ?? "{}";
    }

    private async Task<EffectResult> DryRunWithGuardsAsync(IReadOnlyList<Effect> effects, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var index = 0;
        try
        {
            var receipts = new EffectReceipt[effects.Count];
            for (; index < effects.Count; index++)
            {
                receipts[index] = await ApplyOneAsync(index, effects[index], cancellationToken);
            }
            var proposals = Proposals(effects, receipts, Operation.NewId(), 0, string.Empty);
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
    /// Applies one effect and returns a receipt: the id it actually touched, and the state it
    /// displaced.
    ///
    /// The id is not always the one the effect named — validation requires an explicit id on
    /// entity.create so a later effect in the same list can reference it, but the store can still
    /// mint one when called directly, and an event naming a blank entity would be a record of
    /// something happening to nothing.
    ///
    /// The before snapshot is read HERE rather than by the producer, because here is the only
    /// place it still exists. One read before the write and, where the store does not hand it back,
    /// one after. Two reads per effect is the price of an audit trail that can say what a change
    /// replaced; a ledger that only records the new value cannot answer the question anybody
    /// actually asks of it.
    /// </summary>
    private async Task<EffectReceipt> ApplyOneAsync(int index, Effect effect, CancellationToken cancellationToken)
    {
        var entityId = effect.EntityId.Trim();
        var toEntityId = effect.ToEntityId.Trim();
        var definitionId = effect.DefinitionId.Trim();
        var kind = effect.Kind.Trim();
        var data = string.IsNullOrWhiteSpace(effect.Data) ? "{}" : effect.Data;

        switch (Type(effect))
        {
            case EffectType.EntityCreate:
            {
                entityId = (await _world.CreateEntityAsync(effect.Name.Trim(), entityId, cancellationToken)).Id;

                return new EffectReceipt(index, entityId, null, await EntityJsonAsync(entityId, cancellationToken));
            }

            case EffectType.EntityDelete:
            {
                var before = await EntityJsonAsync(entityId, cancellationToken);
                await _world.DeleteEntityAsync(entityId, cancellationToken);

                return new EffectReceipt(index, entityId, before, null);
            }

            // add and set differ only in whether an existing component is an error, and validation
            // has already answered that — so by the time we get here they do the same thing.
            case EffectType.ComponentAdd:
            case EffectType.ComponentSet:
            {
                var before = await ComponentJsonAsync(entityId, definitionId, cancellationToken);
                var written = await _world.SetComponentAsync(entityId, definitionId, data, cancellationToken);

                return new EffectReceipt(index, entityId, before, Canonical(written.Data));
            }

            case EffectType.ComponentMerge:
            {
                var before = await ComponentJsonAsync(entityId, definitionId, cancellationToken);
                var written = await _world.MergeComponentAsync(entityId, definitionId, data, cancellationToken);

                return new EffectReceipt(index, entityId, before, Canonical(written.Data));
            }

            case EffectType.ComponentRemove:
            {
                var before = await ComponentJsonAsync(entityId, definitionId, cancellationToken);
                await _world.RemoveComponentAsync(entityId, definitionId, cancellationToken);

                return new EffectReceipt(index, entityId, before, null);
            }

            case EffectType.ContainmentMove:
            {
                var before = await ContainmentJsonAsync(entityId, cancellationToken);

                await _world.MoveAsync(
                    entityId,
                    toEntityId.Length == 0 ? null : toEntityId,
                    effect.Slot.Trim(),
                    cancellationToken);

                return new EffectReceipt(index, entityId, before, await ContainmentJsonAsync(entityId, cancellationToken));
            }

            case EffectType.RelationshipCreate:
            {
                var before = await RelationshipJsonAsync(entityId, toEntityId, kind, cancellationToken);
                var written = await _world.RelateAsync(entityId, toEntityId, kind, data, cancellationToken);

                return new EffectReceipt(index, entityId, before, Canonical(written.Data));
            }

            case EffectType.RelationshipRemove:
            {
                var before = await RelationshipJsonAsync(entityId, toEntityId, kind, cancellationToken);
                await _world.UnrelateAsync(entityId, toEntityId, kind, cancellationToken);

                return new EffectReceipt(index, entityId, before, null);
            }

            default:
                // Unreachable: validation rejects an unknown effect type long before this, and the
                // producer throws on one rather than inventing an event for it.
                return EffectReceipt.Unapplied(index, entityId);
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
