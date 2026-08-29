using System.Text.Json;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Closed, fail-closed guard dispatcher. It stores no event or execution rows.</summary>
public sealed class GuardRouter(
    DantesRoleplayDbContext db,
    IMechanicStore mechanics,
    IProjectionResolver projections,
    IMechanicEngine engine,
    IWorldStore world) : IGuardRouter
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IMechanicStore _mechanics = mechanics;
    private readonly IProjectionResolver _projections = projections;
    private readonly IMechanicEngine _engine = engine;
    private readonly IWorldStore _world = world;

    /// <summary>
    /// Denial codes are a vocabulary, not free text: a session that sees one has to be able to
    /// match on it. Same shape the plan states for a guard's own returned code.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CodeShape =
        new("^[A-Z][A-Z0-9_]{2,63}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<GuardResult> EvaluateAsync(IReadOnlyList<ProposedEvent> proposals, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<GuardEvaluation>();

        // A proposed event has not reached the ledger yet, but its sequence is already
        // determined: the next sequence in its correlation, in proposal order. Predicting that
        // same value lets a guard use the chain seed derivation reactions use after acceptance.
        var correlations = proposals
            .Select(proposal => proposal.CorrelationId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var previousSequences = await _db.Events.AsNoTracking()
            .Where(@event => correlations.Contains(@event.CorrelationId))
            .GroupBy(@event => @event.CorrelationId)
            .Select(group => new { CorrelationId = group.Key, Sequence = group.Max(@event => @event.Sequence) })
            .ToDictionaryAsync(row => row.CorrelationId, row => row.Sequence, StringComparer.Ordinal, cancellationToken);
        var nextSequences = correlations.ToDictionary(
            correlation => correlation,
            correlation => previousSequences.GetValueOrDefault(correlation, -1) + 1,
            StringComparer.Ordinal);
        var guardOrdinal = 0;

        // The version in force for each type being proposed, read once. It goes into the envelope
        // a guard sees, so a guard can branch on the schema version it was written against instead
        // of assuming the current one.
        var wantedTypes = proposals.Select(x => x.Type).Distinct(StringComparer.Ordinal).ToList();
        var typeVersions = await _db.EventTypes.AsNoTracking()
            .Where(t => wantedTypes.Contains(t.Id))
            .Select(t => new { t.Id, t.CurrentVersion })
            .ToDictionaryAsync(t => t.Id, t => t.CurrentVersion, StringComparer.Ordinal, cancellationToken);
        foreach (var proposal in proposals.OrderBy(x => x.Ordinal))
        {
            var sequence = nextSequences[proposal.CorrelationId]++;
            var rows = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == SubscriptionStatus.Active && (s.Scope == proposal.Scope || s.Scope == ""))
                .Join(_db.SubscriptionVersions.AsNoTracking(), s => new { SubscriptionId = s.Id, Version = s.CurrentVersion }, v => new { v.SubscriptionId, v.Version }, (s, v) => new { s, v })
                .Where(x => x.v.Mode == SubscriptionMode.Guard && x.v.EventTypeId == proposal.Type)
                .OrderBy(x => x.v.Order).ThenBy(x => x.s.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!Matches(row.v, proposal))
                {
                    continue;
                }

                var detail = await _mechanics.GetAsync(row.v.EventMechanicId, cancellationToken: cancellationToken);
                if (detail is null || detail.Status != MechanicStatus.Active)
                {
                    return GuardResult.Deny(evaluations, "GUARD_UNAVAILABLE", $"Guard '{row.s.Id}' targets an unavailable mechanic.");
                }

                var requirements = MechanicRequirements.Parse(detail.Requirements);
                if (requirements.Event is null
                    || requirements.Event.Mode != EventMechanicMode.Guard
                    || !requirements.Event.Types.Contains(proposal.Type, StringComparer.Ordinal))
                {
                    return GuardResult.Deny(evaluations, "GUARD_UNAVAILABLE", $"Guard '{row.s.Id}' no longer declares '{proposal.Type}'.");
                }

                var bindings = ParseBindings(row.v.FixedRoleEntityIdsJson);
                if (bindings is null)
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_BINDINGS", $"Guard '{row.s.Id}' has corrupt fixed role bindings.");
                }

                var seed = EventRouter.DeriveSeed(
                    EventRouter.RootSeedFrom(proposal.CorrelationId),
                    sequence,
                    row.s.Id,
                    "guard",
                    guardOrdinal++);
                var resolved = await _projections.ResolveAsync(requirements, bindings, "{}", seed, cancellationToken);
                if (!resolved.Ok)
                {
                    return GuardResult.Deny(evaluations, "GUARD_PROJECTION_FAILED", string.Join(" ", resolved.Problems));
                }
                // The full envelope, not the bare payload. A guard needs to know which chain it is
                // being asked about and how deep it is, and the contract requires it to branch on
                // ctx.event.mode rather than guess from which fields happen to be present.
                var projection = resolved.Projection! with
                {
                    Event = EventEnvelope.ForGuard(proposal, typeVersions.GetValueOrDefault(proposal.Type)),
                    EventEntities = await AffectedEntities.ProjectAsync(
                        _world, proposal.EntityIds, requirements.Event.Components, cancellationToken)
                };
                var run = await _engine.RunAsync(detail.Source, projection, ExecutionLimits.Default, cancellationToken);
                if (!run.Ok)
                {
                    return GuardResult.Deny(evaluations, run.LimitHit.Length > 0 ? "GUARD_LIMIT" : "GUARD_FAILED", run.Error);
                }

                var output = run.Output;
                // Effects are forbidden outright — a guard that could change the world would be a
                // rule wearing a veto's clothes. Narration and data are ALLOWED, which the earlier
                // check got backwards: "allowed because the ward was already spent" is exactly the
                // kind of thing an audit wants, and refusing it made an explaining guard a failing
                // guard.
                if (output.Effects.Count != 0)
                {
                    return GuardResult.Deny(evaluations, "GUARD_FORBIDDEN_OUTPUT", $"Guard '{row.s.Id}' returned effects. A guard decides; it does not change the world.");
                }

                if (output.Decision is not ("allow" or "deny"))
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' must return decision allow or deny.");
                }

                if (output.Decision == "deny" && (string.IsNullOrWhiteSpace(output.Code) || string.IsNullOrWhiteSpace(output.Reason)))
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' must supply code and reason when denying.");
                }

                if (output.Decision == "deny" && !CodeShape.IsMatch(output.Code.Trim()))
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' returned code '{output.Code}'. A denial code is 3-64 characters of A-Z, 0-9 and underscore, starting with a letter.");
                }

                if (output.Decision == "deny" && output.Reason.Trim().Length > 500)
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' returned a reason of {output.Reason.Trim().Length} characters; the limit is 500.");
                }

                if (output.Decision == "allow" && !string.IsNullOrWhiteSpace(output.Code))
                {
                    return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' supplied a denial code while allowing.");
                }

                evaluations.Add(new GuardEvaluation(row.s.Id, row.v.Version, detail.Id, detail.Version, row.v.Order, seed, output.Decision, output.Code, output.Reason));
                if (output.Decision == "deny")
                {
                    return GuardResult.Deny(evaluations, output.Code, output.Reason);
                }
            }
        }
        return GuardResult.Allow(evaluations);
    }

    private static bool Matches(SubscriptionVersion subscription, ProposedEvent proposal)
    {
        try
        {
            using var payload = JsonDocument.Parse(proposal.PayloadJson);
            using var filter = JsonDocument.Parse(subscription.PayloadEqualsJson);
            if (filter.RootElement.EnumerateObject().Any(property => !payload.RootElement.TryGetProperty(property.Name, out var value) || value.GetRawText() != property.Value.GetRawText())) return false;
            using var tracked = JsonDocument.Parse(subscription.TrackedEntityIdsJson);
            var ids = tracked.RootElement.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>();
            return !ids.Any() || ids.Intersect(proposal.EntityIds, StringComparer.Ordinal).Any();
        }
        catch (JsonException) { return false; }
    }

    private static Dictionary<string, string>? ParseBindings(string json)
    {
        try { using var document = JsonDocument.Parse(json); return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString() ?? string.Empty, StringComparer.Ordinal); }
        catch (JsonException) { return null; }
    }

}
