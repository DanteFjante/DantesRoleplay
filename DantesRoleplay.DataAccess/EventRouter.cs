using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Runs reaction subscriptions against accepted events.
///
/// Deliberately shaped like <see cref="GuardRouter"/> — same selection, same filters — because a
/// session that has understood one has understood the other. Where they differ is what the
/// mechanic may do: a guard returns a decision and nothing else, a reaction returns effects and
/// changes the world.
///
/// Guards and reactions use the same chain-position seed derivation. A guard predicts the sequence
/// its proposal will receive if accepted; a reaction uses the accepted row's actual sequence. That
/// makes either ruling reproducible from the root correlation and its audit position alone.
///
/// It does not apply those effects. It returns them, and the effect applier applies them, so world
/// state still has exactly one doorway. That also avoids the two classes depending on each other,
/// which is not an arrangement that improves with familiarity.
///
/// Every failure aborts the whole root change. There is no partial reaction: if a rule fires on a
/// change and cannot complete, the change itself was not something the world permitted.
/// </summary>
public sealed class EventRouter(
    DantesRoleplayDbContext db,
    IMechanicStore mechanics,
    IProjectionResolver projections,
    IMechanicEngine engine,
    IWorldStore world) : IEventRouter
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IMechanicStore _mechanics = mechanics;
    private readonly IProjectionResolver _projections = projections;
    private readonly IMechanicEngine _engine = engine;
    private readonly IWorldStore _world = world;

    public async Task<EventRoutingResult> RouteAsync(
        IReadOnlyList<EventDetail> accepted,
        long rootSeed,
        ChainBudget budget,
        int ordinal = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(budget);

        var outcomes = new List<ReactionOutcome>();

        foreach (var @event in accepted.OrderBy(e => e.Sequence))
        {
            var registrations = await MatchingAsync(@event, cancellationToken);

            foreach (var registration in registrations)
            {
                if (!Matches(registration.Version, @event))
                {
                    // Excluded declaratively, so it costs nothing and counts for nothing. Only a
                    // subscription that actually runs spends the chain's budget.
                    continue;
                }

                var overspent = budget.CountExecution(registration.Id, registration.Version.MaxExecutionsPerChain);

                if (overspent is not null)
                {
                    return EventRoutingResult.Abort(
                        overspent,
                        $"{ChainBudget.Explain(overspent)} Reached by subscription "
                        + $"'{registration.Id}' handling event {@event.Id}.");
                }

                var outcome = await RunAsync(@event, registration, rootSeed, ordinal, cancellationToken);

                if (outcome.Failure is not null)
                {
                    return outcome.Failure;
                }

                outcomes.Add(outcome.Success!);
                ordinal++;
            }
        }

        return EventRoutingResult.Allow(outcomes);
    }

    // ---- selection ------------------------------------------------------------------------

    /// <summary>
    /// Active reaction registrations for this event's type and scope, in the order they will run.
    ///
    /// Ascending declared order, then id. The id tiebreak is not decoration: two subscriptions at
    /// the same order would otherwise run in whatever sequence the database returned them, and a
    /// chain that is not reproducible is not auditable.
    /// </summary>
    private async Task<IReadOnlyList<Registration>> MatchingAsync(
        EventDetail @event,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && (s.Scope == @event.Scope || s.Scope == ""))
            .Join(
                _db.SubscriptionVersions.AsNoTracking(),
                s => new { SubscriptionId = s.Id, Version = s.CurrentVersion },
                v => new { v.SubscriptionId, v.Version },
                (s, v) => new { s, v })
            .Where(x => x.v.Mode == SubscriptionMode.Reaction && x.v.EventTypeId == @event.TypeId)
            .OrderBy(x => x.v.Order).ThenBy(x => x.s.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new Registration(x.s.Id, x.v)).ToList();
    }

    /// <summary>
    /// The declarative filters, applied before any projection or execution.
    ///
    /// Cheap exclusions first, on purpose: a subscription that does not apply must cost nothing,
    /// or a world with many registrations becomes a world where changing anything is slow.
    /// </summary>
    private static bool Matches(SubscriptionVersion subscription, EventDetail @event)
    {
        try
        {
            using var payload = JsonDocument.Parse(@event.PayloadJson);
            using var filter = JsonDocument.Parse(subscription.PayloadEqualsJson);

            if (filter.RootElement.EnumerateObject().Any(property =>
                    !payload.RootElement.TryGetProperty(property.Name, out var value)
                    || value.GetRawText() != property.Value.GetRawText()))
            {
                return false;
            }

            using var tracked = JsonDocument.Parse(subscription.TrackedEntityIdsJson);

            var ids = tracked.RootElement.EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => x is not null)
                .Cast<string>()
                .ToList();

            // No tracked ids means "any entity". An empty intersection means this registration is
            // watching something else entirely.
            return ids.Count == 0 || ids.Intersect(@event.EntityIds, StringComparer.Ordinal).Any();
        }
        catch (JsonException)
        {
            // A corrupt filter excludes rather than matches. A registration nobody can parse should
            // not be able to fire on everything.
            return false;
        }
    }

    // ---- execution ------------------------------------------------------------------------

    private async Task<Attempt> RunAsync(
        EventDetail @event,
        Registration registration,
        long rootSeed,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var subscription = registration.Version;

        var detail = await _mechanics.GetAsync(
            subscription.EventMechanicId,
            cancellationToken: cancellationToken);

        if (detail is null || detail.Status != MechanicStatus.Active)
        {
            return Attempt.Failed("SUBSCRIBER_UNAVAILABLE",
                $"Subscription '{registration.Id}' targets mechanic "
                + $"'{subscription.EventMechanicId}', which is missing or not active.");
        }

        var requirements = MechanicRequirements.Parse(detail.Requirements);

        // The mechanic must still declare itself a reaction for this type. A subscription pointing
        // at a mechanic that has since been revised into something else is a broken registration,
        // not a licence to run whatever the mechanic became.
        if (requirements.Event is null
            || requirements.Event.Mode != EventMechanicMode.Reaction
            || !requirements.Event.Types.Contains(@event.TypeId, StringComparer.Ordinal))
        {
            return Attempt.Failed("SUBSCRIBER_UNAVAILABLE",
                $"Subscription '{registration.Id}' targets mechanic '{detail.Id}', which no longer "
                + $"declares itself a reaction to '{@event.TypeId}'.");
        }

        var bindings = ParseBindings(subscription.FixedRoleEntityIdsJson);

        if (bindings is null)
        {
            return Attempt.Failed("SUBSCRIBER_INVALID_BINDINGS",
                $"Subscription '{registration.Id}' has corrupt fixed role bindings.");
        }

        var seed = DeriveSeed(rootSeed, @event.Sequence, registration.Id, "reaction", ordinal);
        var resolved = await _projections.ResolveAsync(requirements, bindings, "{}", seed, cancellationToken);

        if (!resolved.Ok)
        {
            return Attempt.Failed("SUBSCRIBER_PROJECTION_FAILED", string.Join(" ", resolved.Problems));
        }

        var projection = resolved.Projection! with
        {
            Event = EventEnvelope.ForReaction(@event),
            EventEntities = await AffectedEntities.ProjectAsync(
                _world, @event.EntityIds, requirements.Event.Components, cancellationToken)
        };

        var run = await _engine.RunAsync(detail.Source, projection, ExecutionLimits.Default, cancellationToken);

        if (!run.Ok)
        {
            return Attempt.Failed(
                run.LimitHit.Length > 0 ? "SUBSCRIBER_LIMIT" : "SUBSCRIBER_FAILED",
                $"Subscription '{registration.Id}' running '{detail.Id}': {run.Error}");
        }

        var output = run.Output;

        // A reaction decides nothing — that is a guard's job, and a mechanic that returns a decision
        // has been registered in the wrong mode.
        if (!string.IsNullOrWhiteSpace(output.Decision))
        {
            return Attempt.Failed("SUBSCRIBER_FORBIDDEN_OUTPUT",
                $"Subscription '{registration.Id}' returned a guard decision from a reaction.");
        }

        var execution = new EventExecution
        {
            Id = Guid.NewGuid().ToString("n"),
            EventId = @event.Id,
            Ordinal = ordinal,
            SubscriptionId = registration.Id,
            SubscriptionVersion = subscription.Version,
            MechanicId = detail.Id,
            MechanicVersion = detail.Version,
            Seed = seed,
            ProjectionJson = JsonSerializer.Serialize(projection),
            OutputJson = JsonSerializer.Serialize(output),
            EffectCount = output.Effects.Count,

            // Counted separately from effects because they are limited separately, and because
            // "this rule changed nothing but announced something" is a shape worth seeing at a
            // glance in the execution log.
            EventCount = output.Events.Count,
            Narration = output.Narration,
            LogJson = JsonSerializer.Serialize(run.Log),
            ElapsedMilliseconds = run.ElapsedMilliseconds,
            LimitHit = run.LimitHit,
            CreatedAt = DateTime.UtcNow
        };

        return Attempt.Succeeded(
            new ReactionOutcome(execution, output.Effects, output.Events, output.Notifications));
    }

    // ---- determinism ----------------------------------------------------------------------

    /// <summary>
    /// Derives one reaction's seed from the chain's root seed and its exact position in it.
    ///
    /// Derived rather than drawn, so a chain replays identically from the root seed alone — which
    /// is what makes a past ruling reviewable rather than merely recorded. Every input is included
    /// with a separator so two different positions cannot encode to the same bytes: without one,
    /// subscription "a" at ordinal 12 and subscription "a1" at ordinal 2 would share a seed.
    ///
    /// Little-endian and masked to non-negative. Guards call this exact method too; one derivation
    /// keeps the separator, root-seed, and replay guarantees from drifting apart.
    /// </summary>
    public static long DeriveSeed(long rootSeed, int sequence, string subscriptionId, string mode, int ordinal)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{rootSeed}\u001f{sequence}\u001f{subscriptionId}\u001f{mode}\u001f{ordinal}"));

        return BitConverter.ToInt64(bytes, 0) & long.MaxValue;
    }

    /// <summary>
    /// A root seed nobody supplied, derived from the correlation id.
    ///
    /// Deterministic on purpose: a chain has to be replayable from what the audit row records, and
    /// the correlation id is the one value every part of the chain already carries. A random seed
    /// would make the first run unreproducible, which defeats recording it.
    /// </summary>
    public static long RootSeedFrom(string correlationId) =>
        BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(correlationId)), 0) & long.MaxValue;

    private static Dictionary<string, string>? ParseBindings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Registration(string Id, SubscriptionVersion Version);

    private sealed record Attempt(ReactionOutcome? Success, EventRoutingResult? Failure)
    {
        public static Attempt Succeeded(ReactionOutcome outcome) => new(outcome, null);

        public static Attempt Failed(string code, string reason) =>
            new(null, EventRoutingResult.Abort(code, reason));
    }
}
