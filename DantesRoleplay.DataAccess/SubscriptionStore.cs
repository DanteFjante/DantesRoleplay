using System.Text.Json;
using DantesRoleplay.Categories;
using DantesRoleplay.Content;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Append-only registration storage. It deliberately does not route or execute subscriptions.</summary>
public sealed class SubscriptionStore(DantesRoleplayDbContext db) : ISubscriptionStore
{
    private readonly DantesRoleplayDbContext _db = db;
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public async Task<IReadOnlyList<SubscriptionSummary>> FindAsync(string? query = null, string? category = null, string? scope = null, bool includeInactive = false, int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = _db.Subscriptions.Join(_db.SubscriptionVersions, s => new { SubscriptionId = s.Id, Version = s.CurrentVersion }, v => new { v.SubscriptionId, v.Version }, (s, v) => new { s, v });
        if (!includeInactive) rows = rows.Where(x => x.s.Status != SubscriptionStatus.Archived);
        if (!string.IsNullOrWhiteSpace(category)) rows = rows.Where(x => x.s.Category == category || x.s.Category.StartsWith(category + "."));
        if (!string.IsNullOrWhiteSpace(scope)) rows = rows.Where(x => x.s.Scope == scope || x.s.Scope == "");
        var list = await rows.OrderBy(x => x.s.Category).ThenBy(x => x.s.Id).Select(x => new { x.s, x.v }).ToListAsync(cancellationToken);
        var results = new List<SubscriptionSummary>();
        foreach (var row in list)
        {
            if (!string.IsNullOrWhiteSpace(query) && !($"{row.s.Id} {row.s.Category} {row.v.EventTypeId} {row.v.EventMechanicId}").Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            results.Add(new(row.s.Id, row.s.Category, row.v.EventTypeId, row.v.EventMechanicId, row.v.Mode, row.v.Order, row.s.Scope, row.s.Status, row.s.CurrentVersion, await HealthyAsync(row.v, cancellationToken)));
            if (results.Count >= limit) break;
        }
        return results;
    }

    public async Task<SubscriptionDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (subscription is null) return null;
        var wanted = version ?? subscription.CurrentVersion;
        var row = await _db.SubscriptionVersions.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionId == id && x.Version == wanted, cancellationToken); if (row is null) return null;
        var latest = await _db.SubscriptionVersions.Where(x => x.SubscriptionId == id).MaxAsync(x => (int?)x.Version, cancellationToken) ?? subscription.CurrentVersion;
        return ToDetail(subscription, row, latest, await HealthyAsync(row, cancellationToken));
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) => _db.Subscriptions.AnyAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionCheck>> CheckAsync(WriteSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var checks = new List<SubscriptionCheck>();
        var idOk = !string.IsNullOrWhiteSpace(request.Id) && request.Id.Length is >= 3 and <= 200 && System.Text.RegularExpressions.Regex.IsMatch(request.Id, "^subscription(\\.[a-z][a-z0-9-]*)+$");
        checks.Add(new("id-format", idOk, idOk ? "Permanent subscription.* id." : "Id must be a permanent lowercase dotted id beginning subscription."));
        var categoryOk = CategoryPath.TryValidate(request.Category, out var categoryProblem);
        checks.Add(new("category-path", categoryOk, categoryOk ? "Category path is valid." : categoryProblem));
        var existing = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        checks.Add(new("create-or-revise", true, existing is null ? "Creates version 1." : $"Appends version {existing.CurrentVersion + 1}."));
        var currentVersion = existing is null
            ? null
            : await _db.SubscriptionVersions.AsNoTracking().FirstAsync(
                x => x.SubscriptionId == request.Id && x.Version == existing.CurrentVersion,
                cancellationToken);
        var modeStable = currentVersion is null || currentVersion.Mode == request.Mode;
        checks.Add(new("mode-immutable", modeStable, modeStable ? "Mode is stable." : "Mode cannot change. Create a new subscription id for a guard or reaction."));
        checks.Add(new("change-note", existing is null || !string.IsNullOrWhiteSpace(request.ChangeNote), existing is null || !string.IsNullOrWhiteSpace(request.ChangeNote) ? "Change note present when revising." : "A nonempty changeNote is required when revising."));
        checks.Add(new("order", request.Order is >= -1000 and <= 1000, "Order must be between -1000 and 1000."));
        checks.Add(new("per-chain-limit", request.MaxExecutionsPerChain is >= 1 and <= 8, "maxExecutionsPerChain must be 1–8."));

        var eventType = await _db.EventTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.EventTypeId, cancellationToken);
        checks.Add(new("event-type-active", eventType?.Status == EventTypeStatus.Active, eventType is null ? $"Event type '{request.EventTypeId}' does not exist." : eventType.Status == EventTypeStatus.Active ? "Event type is active." : $"Event type '{request.EventTypeId}' is {eventType.Status}.", true));
        var mechanic = await _db.Mechanics.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.EventMechanicId, cancellationToken);
        var mechanicVersion = mechanic is null ? null : await _db.MechanicVersions.AsNoTracking().FirstOrDefaultAsync(x => x.MechanicId == mechanic.Id && x.Version == mechanic.CurrentVersion, cancellationToken);
        var requirement = TryEventRequirement(mechanicVersion?.Requirements, out var problem);
        var requirementOk = mechanic?.Status == MechanicStatus.Active
            && requirement is not null
            && requirement.Mode.ToString().Equals(request.Mode.ToString(), StringComparison.OrdinalIgnoreCase)
            && requirement.Types.Contains(request.EventTypeId, StringComparer.Ordinal)
            && !MechanicRequirements.Parse(mechanicVersion!.Requirements).Children.Any();
        var mechanicDetail = mechanic is null
            ? $"Mechanic '{request.EventMechanicId}' does not exist."
            : mechanic.Status != MechanicStatus.Active
                ? $"Mechanic '{request.EventMechanicId}' is {mechanic.Status}."
                : requirement is null
                    ? problem
                    : !requirement.Mode.ToString().Equals(request.Mode.ToString(), StringComparison.OrdinalIgnoreCase)
                        ? $"Mechanic declares {requirement.Mode}, not the requested {request.Mode} mode."
                        : !requirement.Types.Contains(request.EventTypeId, StringComparer.Ordinal)
                            ? $"Mechanic does not declare event type '{request.EventTypeId}'."
                            : MechanicRequirements.Parse(mechanicVersion!.Requirements).Children.Any()
                                ? "An event mechanic cannot declare child mechanics."
                                : "Mechanic declares the exact event type and mode.";
        checks.Add(new("event-mechanic", requirementOk, mechanicDetail, true));

        var fixedRoles = ParseFixedRoleEntityIds(request.FixedRoleEntityIdsJson, checks);
        var tracked = ParseIds(request.TrackedEntityIdsJson, "trackedEntityIds", checks);
        _ = ParseObject(request.PayloadEqualsJson, "payloadEquals", checks, scalarOnly: true, maxProperties: 32);
        if (requirement is not null && mechanicVersion is not null && fixedRoles is not null)
        {
            var all = MechanicRequirements.Parse(mechanicVersion.Requirements).Roles;
            var required = all.Where(x => !x.Value.Optional).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            var supplied = fixedRoles.Keys.ToHashSet(StringComparer.Ordinal);
            checks.Add(new("fixed-roles", required.SetEquals(supplied) || (required.IsSubsetOf(supplied) && supplied.All(all.ContainsKey)), required.SetEquals(supplied) || (required.IsSubsetOf(supplied) && supplied.All(all.ContainsKey)) ? "Fixed role bindings satisfy ordinary mechanic roles." : "Fixed roles must include every required ordinary role and no unknown role."));
        }
        var roleEntityIds = fixedRoles is null
            ? Enumerable.Empty<string>()
            : fixedRoles.Values;
        var ids = roleEntityIds.Concat(tracked ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count > 0) { var known = await _db.Entities.Where(x => x.DeletedAt == null && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken); checks.Add(new("entities-exist", known.Count == ids.Count, known.Count == ids.Count ? "Referenced entities exist." : $"Missing entities: {string.Join(", ", ids.Except(known, StringComparer.Ordinal))}.")); }
        if (requirement is not null) { var wanted = requirement.Components.Distinct(StringComparer.Ordinal).ToList(); var known = await _db.ComponentDefinitions.Where(x => wanted.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken); checks.Add(new("event-components-exist", known.Count == wanted.Count, known.Count == wanted.Count ? "Declared event components exist." : $"Missing event components: {string.Join(", ", wanted.Except(known, StringComparer.Ordinal))}.")); }
        return checks;
    }

    public async Task<WriteSubscriptionResult> WriteAsync(WriteSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var checks = await CheckAsync(request, cancellationToken); var failed = checks.FirstOrDefault(x => x.Blocking && !x.Passed); if (failed is not null) throw new ArgumentException(failed.Detail);
        var fixedRoles = CanonicalObject(request.FixedRoleEntityIdsJson); var tracked = CanonicalIds(request.TrackedEntityIdsJson); var payload = CanonicalObject(request.PayloadEqualsJson); var now = DateTime.UtcNow;
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken); var created = subscription is null;
        if (subscription is null) { subscription = new Subscription { Id = request.Id, Category = request.Category, Scope = request.Scope, Status = request.Status ?? SubscriptionStatus.Draft, CreatedAt = now, UpdatedAt = now }; _db.Subscriptions.Add(subscription); }
        else { subscription.Category = request.Category; subscription.Scope = request.Scope; subscription.UpdatedAt = now; if (request.Status is { } status) subscription.Status = status; }
        var version = (await _db.SubscriptionVersions.Where(x => x.SubscriptionId == request.Id).MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var row = new SubscriptionVersion { SubscriptionId = subscription.Id, Version = version, EventTypeId = request.EventTypeId, EventMechanicId = request.EventMechanicId, Mode = request.Mode, Order = request.Order, FixedRoleEntityIdsJson = fixedRoles, TrackedEntityIdsJson = tracked, PayloadEqualsJson = payload, MaxExecutionsPerChain = request.MaxExecutionsPerChain, ChangeNote = request.ChangeNote, CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy, SourceHash = ContentHash.Of(subscription.Category, request.EventTypeId, request.EventMechanicId, request.Mode.ToString(), request.Order.ToString(), fixedRoles, tracked, payload, request.MaxExecutionsPerChain.ToString(), subscription.Scope, (request.Status ?? subscription.Status).ToString()), CreatedAt = now };
        _db.SubscriptionVersions.Add(row); subscription.CurrentVersion = version; await _db.SaveChangesAsync(cancellationToken); return new(ToDetail(subscription, row, version, await HealthyAsync(row, cancellationToken)), created);
    }

    private async Task<bool> HealthyAsync(SubscriptionVersion row, CancellationToken cancellationToken) =>
        await _db.EventTypes.AnyAsync(x => x.Id == row.EventTypeId && x.Status == EventTypeStatus.Active, cancellationToken) &&
        await _db.Mechanics.AnyAsync(x => x.Id == row.EventMechanicId && x.Status == MechanicStatus.Active, cancellationToken);
    private static EventMechanicRequirement? TryEventRequirement(string? json, out string problem) { problem = "Mechanic does not declare an event requirement."; try { var r = MechanicRequirements.Parse(json ?? "{}"); if (r.Event is null) return null; if (r.Event.Types.Count == 0) { problem = "Event requirement needs at least one type."; return null; } return r.Event; } catch (JsonException ex) { problem = $"Mechanic requirements are invalid: {ex.Message}"; return null; } }
    private static Dictionary<string, string>? ParseFixedRoleEntityIds(string json, List<SubscriptionCheck> checks)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var entityId = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(property.Name)
                    || string.IsNullOrWhiteSpace(entityId)
                    || entityId != entityId.Trim())
                {
                    throw new JsonException();
                }

                result[property.Name] = entityId;
            }

            checks.Add(new("fixedRoleEntityIds", true, "fixedRoleEntityIds is a closed object of entity ids."));
            return result;
        }
        catch (JsonException)
        {
            checks.Add(new("fixedRoleEntityIds", false, "fixedRoleEntityIds must be a JSON object with nonempty string entity ids."));
            return null;
        }
    }

    private static Dictionary<string, string>? ParseObject(string json, string name, List<SubscriptionCheck> checks, bool scalarOnly, int maxProperties = int.MaxValue) { try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (d.RootElement.ValueKind != JsonValueKind.Object || d.RootElement.EnumerateObject().Count() > maxProperties) throw new JsonException(); var result = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var p in d.RootElement.EnumerateObject()) { if (string.IsNullOrWhiteSpace(p.Name) || (scalarOnly && p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)) throw new JsonException(); result[p.Name] = p.Value.GetRawText(); } checks.Add(new(name, true, $"{name} is a closed object.")); return result; } catch (JsonException) { checks.Add(new(name, false, $"{name} must be a JSON object with at most {maxProperties} scalar values.")); return null; } }
    private static IReadOnlyList<string>? ParseIds(string json, string name, List<SubscriptionCheck> checks) { try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); if (d.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException(); var ids = d.RootElement.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()?.Trim() ?? "" : "").ToList(); if (ids.Count > 100 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw new JsonException(); checks.Add(new(name, true, $"{name} contains {ids.Count} id(s).")); return ids; } catch (JsonException) { checks.Add(new(name, false, $"{name} must be a distinct string array of at most 100 ids.")); return null; } }
    private static string CanonicalObject(string json) { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return "{" + string.Join(",", d.RootElement.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name, Compact) + ":" + x.Value.GetRawText())) + "}"; }
    private static string CanonicalIds(string json) { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); return JsonSerializer.Serialize(d.RootElement.EnumerateArray().Select(x => x.GetString()!.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal), Compact); }
    private static SubscriptionDetail ToDetail(Subscription s, SubscriptionVersion v, int latest, bool healthy) => new(s.Id, s.Category, v.EventTypeId, v.EventMechanicId, v.Mode, v.Order, v.FixedRoleEntityIdsJson, v.TrackedEntityIdsJson, v.PayloadEqualsJson, v.MaxExecutionsPerChain, s.Scope, s.Status, v.Version, latest, v.CreatedBy, v.ChangeNote, v.CreatedAt, healthy) { SourceHash = v.SourceHash };
}
