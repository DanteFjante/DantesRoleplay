using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Closed, fail-closed guard dispatcher. It stores no event or execution rows.</summary>
public sealed class GuardRouter(
    DantesRoleplayDbContext db,
    IMechanicStore mechanics,
    IProjectionResolver projections,
    IMechanicEngine engine) : IGuardRouter
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IMechanicStore _mechanics = mechanics;
    private readonly IProjectionResolver _projections = projections;
    private readonly IMechanicEngine _engine = engine;

    public async Task<GuardResult> EvaluateAsync(IReadOnlyList<ProposedEvent> proposals, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<GuardEvaluation>();
        foreach (var proposal in proposals.OrderBy(x => x.Ordinal))
        {
            var rows = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == SubscriptionStatus.Active && (s.Scope == proposal.Scope || s.Scope == ""))
                .Join(_db.SubscriptionVersions.AsNoTracking(), s => new { SubscriptionId = s.Id, Version = s.CurrentVersion }, v => new { v.SubscriptionId, v.Version }, (s, v) => new { s, v })
                .Where(x => x.v.Mode == SubscriptionMode.Guard && x.v.EventTypeId == proposal.Type)
                .OrderBy(x => x.v.Order).ThenBy(x => x.s.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!Matches(row.v, proposal)) continue;
                var detail = await _mechanics.GetAsync(row.v.EventMechanicId, cancellationToken: cancellationToken);
                if (detail is null || detail.Status != MechanicStatus.Active) return GuardResult.Deny(evaluations, "GUARD_UNAVAILABLE", $"Guard '{row.s.Id}' targets an unavailable mechanic.");
                var requirements = MechanicRequirements.Parse(detail.Requirements);
                if (requirements.Event is null || requirements.Event.Mode != EventMechanicMode.Guard || !requirements.Event.Types.Contains(proposal.Type, StringComparer.Ordinal)) return GuardResult.Deny(evaluations, "GUARD_UNAVAILABLE", $"Guard '{row.s.Id}' no longer declares '{proposal.Type}'.");
                var bindings = ParseBindings(row.v.FixedRoleEntityIdsJson);
                if (bindings is null) return GuardResult.Deny(evaluations, "GUARD_INVALID_BINDINGS", $"Guard '{row.s.Id}' has corrupt fixed role bindings.");
                var seed = Seed(row.s.Id, row.v.Version, proposal);
                var resolved = await _projections.ResolveAsync(requirements, bindings, "{}", seed, cancellationToken);
                if (!resolved.Ok) return GuardResult.Deny(evaluations, "GUARD_PROJECTION_FAILED", string.Join(" ", resolved.Problems));
                var projection = resolved.Projection! with { Event = proposal.PayloadJson, EventEntities = proposal.EntityIds };
                var run = await _engine.RunAsync(detail.Source, projection, ExecutionLimits.Default, cancellationToken);
                if (!run.Ok) return GuardResult.Deny(evaluations, run.LimitHit.Length > 0 ? "GUARD_LIMIT" : "GUARD_FAILED", run.Error);
                var output = run.Output;
                if (output.Effects.Count != 0 || !string.IsNullOrWhiteSpace(output.Narration) || output.Data != "{}") return GuardResult.Deny(evaluations, "GUARD_FORBIDDEN_OUTPUT", $"Guard '{row.s.Id}' may only return a decision.");
                if (output.Decision is not ("allow" or "deny")) return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' must return decision allow or deny.");
                if (output.Decision == "deny" && (string.IsNullOrWhiteSpace(output.Code) || string.IsNullOrWhiteSpace(output.Reason))) return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' must supply code and reason when denying.");
                if (output.Decision == "allow" && (!string.IsNullOrWhiteSpace(output.Code) || !string.IsNullOrWhiteSpace(output.Reason))) return GuardResult.Deny(evaluations, "GUARD_INVALID_DECISION", $"Guard '{row.s.Id}' cannot supply code or reason when allowing.");
                evaluations.Add(new GuardEvaluation(row.s.Id, row.v.Version, detail.Id, detail.Version, row.v.Order, seed, output.Decision, output.Code, output.Reason));
                if (output.Decision == "deny") return GuardResult.Deny(evaluations, output.Code, output.Reason);
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

    private static long Seed(string subscriptionId, int version, ProposedEvent proposal)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{subscriptionId}|{version}|{proposal.Type}|{proposal.Ordinal}|{proposal.PayloadJson}"));
        return BitConverter.ToInt64(bytes, 0) & long.MaxValue;
    }
}
