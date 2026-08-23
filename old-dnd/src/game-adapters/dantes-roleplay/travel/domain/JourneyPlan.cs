using System.Text.Json;

namespace DantesRoleplay.World;

/// <summary>Closed request for one read-only, on-foot itinerary.</summary>
public sealed record JourneyPlanQuery(string WorldId, string TravellerId, string DestinationId);
public sealed record JourneyPlanLeg(string RouteId, string FromId, string ToId, int DurationMinutes);
public sealed record JourneyPlanProjection(string Status, string WorldId, string TravellerId, string OriginId, string DestinationId, long ClockRevision, IReadOnlyList<JourneyPlanLeg> Legs, int TotalDurationMinutes);
public sealed record JourneyPlanResult(JourneyPlanProjection? Projection, string ErrorCode = "", string ErrorMessage = "")
{
    public bool Ok => Projection is not null && ErrorCode.Length == 0;
    public static JourneyPlanResult Fail(string code, string message) => new(null, code, message);
    public static JourneyPlanResult Success(JourneyPlanProjection projection) => new(projection);
}
public interface IJourneyPlanReader { Task<JourneyPlanResult> ReadAsync(JourneyPlanQuery query, CancellationToken cancellationToken = default); }

/// <summary>World-owned eligibility and deterministic shortest-path read. It writes nothing.</summary>
public sealed class JourneyPlanReader(IWorldStore world) : IJourneyPlanReader
{
    private const string Traveller = "game.core.world.traveller", Location = "game.core.world.location", Root = "game.core.world.root", Clock = "game.core.world.clock", Route = "game.core.world.route", Availability = "game.core.world.route.availability";
    private const string Scope = "game.core.world.route.in-world", From = "game.core.world.route.from", To = "game.core.world.route.to", Connected = "game.core.world.location.connected-to";
    private readonly IWorldStore _world = world;

    public async Task<JourneyPlanResult> ReadAsync(JourneyPlanQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || !Id(query.WorldId) || !Id(query.TravellerId) || !Id(query.DestinationId)) return JourneyPlanResult.Fail("INVALID_JOURNEY_PLAN", "worldId, travellerId, and destinationId must be trimmed nonblank ids.");
        var root = await _world.GetEntityAsync(query.WorldId, cancellationToken); var traveller = await _world.GetEntityAsync(query.TravellerId, cancellationToken); var destination = await _world.GetEntityAsync(query.DestinationId, cancellationToken);
        if (root is null || traveller is null || destination is null) return JourneyPlanResult.Fail("UNKNOWN_JOURNEY_PLAN_ENTITY", "worldId, travellerId, and destinationId must name existing entities.");
        if (!ActiveTraveller(Component(traveller, Traveller)) || traveller.ContainerId is null || traveller.ContainerSlot != "presence") return JourneyPlanResult.Fail("INVALID_JOURNEY_PLAN_TRAVELLER", "travellerId must name an active traveller directly present at one location.");
        var origin = await _world.GetEntityAsync(traveller.ContainerId, cancellationToken);
        if (origin is null || !ActiveLocation(Component(origin, Location)) || !ActiveLocation(Component(destination, Location))) return JourneyPlanResult.Fail("INVALID_JOURNEY_PLAN_LOCATION", "Traveller origin and destination must be active locations.");
        if (!ActiveRoot(Component(root, Root)) || !ClockRevision(Component(root, Clock), out var revision)) return JourneyPlanResult.Fail("INVALID_JOURNEY_PLAN_WORLD", "worldId must name an active root with a valid clock.");
        if (origin.Id == destination.Id) return JourneyPlanResult.Success(Empty("already-there", query, origin.Id, revision));
        if (origin.ContainerId is null || origin.ContainerId != destination.ContainerId || origin.ContainerSlot != "location" || destination.ContainerSlot != "location") return JourneyPlanResult.Fail("INVALID_JOURNEY_PLAN_LOCATION", "Origin and destination must be active sibling locations.");

        var summaries = await _world.FindEntitiesAsync(withDefinitionId: Route, limit: 10000, cancellationToken: cancellationToken);
        var candidates = await _world.GetEntitiesAsync(summaries.Select(x => x.Id), cancellationToken);
        var all = new List<JourneyPlanLeg>(); var open = new List<JourneyPlanLeg>();
        foreach (var route in candidates.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var edge = await EligibleAsync(route, root.Id, cancellationToken);
            if (edge is null) continue;
            all.Add(edge.Value.Leg); if (edge.Value.Open) open.Add(edge.Value.Leg);
        }
        var structural = Shortest(all, origin.Id, destination.Id);
        if (structural is null) return JourneyPlanResult.Success(Empty("unreachable", query, origin.Id, revision));
        var selected = Shortest(open, origin.Id, destination.Id);
        if (selected is null) return JourneyPlanResult.Success(Empty("blocked", query, origin.Id, revision));
        var minutes = selected.Sum(x => x.DurationMinutes);
        if (selected.Count > 20 || minutes > 14400) return JourneyPlanResult.Success(Empty("too-long", query, origin.Id, revision));
        return JourneyPlanResult.Success(new JourneyPlanProjection("ready", root.Id, traveller.Id, origin.Id, destination.Id, revision, selected, minutes));
    }

    private async Task<(JourneyPlanLeg Leg, bool Open)?> EligibleAsync(EntitySnapshot route, string worldId, CancellationToken ct)
    {
        if (!RouteData(Component(route, Route), out var duration)) return null;
        var links = await _world.GetRelationshipsAsync(route.Id, includeIncoming: false, ct);
        if (links.Count != 3 || links.Any(x => x.FromEntityId != route.Id || !EmptyObject(x.Data))) return null;
        var scopes = links.Where(x => x.Kind == Scope).ToArray(); var froms = links.Where(x => x.Kind == From).ToArray(); var tos = links.Where(x => x.Kind == To).ToArray();
        if (scopes.Length != 1 || froms.Length != 1 || tos.Length != 1) return null;
        var scope = scopes[0]; var from = froms[0]; var to = tos[0];
        if (scope.ToEntityId != worldId || from.ToEntityId == to.ToEntityId) return null;
        var a = await _world.GetEntityAsync(from.ToEntityId, ct); var b = await _world.GetEntityAsync(to.ToEntityId, ct);
        if (a is null || b is null || !ActiveLocation(Component(a, Location)) || !ActiveLocation(Component(b, Location)) || a.ContainerId is null || a.ContainerId != b.ContainerId || a.ContainerSlot != "location" || b.ContainerSlot != "location") return null;
        var adjacency = await _world.GetRelationshipsAsync(a.Id, includeIncoming: true, ct);
        var matching = adjacency.Count(x => x.Kind == Connected && EmptyObject(x.Data) && string.CompareOrdinal(x.FromEntityId, x.ToEntityId) < 0 && ((x.FromEntityId == a.Id && x.ToEntityId == b.Id) || (x.FromEntityId == b.Id && x.ToEntityId == a.Id)));
        if (matching != 1) return null;
        var availability = Component(route, Availability); if (!AvailabilityData(availability, out var isOpen)) return null;
        return (new JourneyPlanLeg(route.Id, a.Id, b.Id, duration), isOpen);
    }

    private static IReadOnlyList<JourneyPlanLeg>? Shortest(IEnumerable<JourneyPlanLeg> edges, string start, string goal)
    {
        var queue = new List<Path> { new(start, 0, []) }; var best = new Dictionary<string, Path>(StringComparer.Ordinal) { [start] = queue[0] };
        while (queue.Count > 0)
        {
            queue.Sort(Path.Compare); var current = queue[0]; queue.RemoveAt(0); if (!ReferenceEquals(best[current.Node], current)) continue;
            if (current.Node == goal) return current.Legs;
            foreach (var edge in edges.Where(e => e.FromId == current.Node).OrderBy(e => e.RouteId, StringComparer.Ordinal).ThenBy(e => e.ToId, StringComparer.Ordinal))
            {
                var next = new Path(edge.ToId, current.Minutes + edge.DurationMinutes, [.. current.Legs, edge]);
                if (!best.TryGetValue(next.Node, out var previous) || Path.Compare(next, previous) < 0) { best[next.Node] = next; queue.Add(next); }
            }
        }
        return null;
    }
    private sealed record Path(string Node, int Minutes, IReadOnlyList<JourneyPlanLeg> Legs)
    {
        public static int Compare(Path x, Path y) { var byMinutes = x.Minutes.CompareTo(y.Minutes); if (byMinutes != 0) return byMinutes; var xKey = string.Join("\u001f", x.Legs.Select(l => l.RouteId)); var yKey = string.Join("\u001f", y.Legs.Select(l => l.RouteId)); var byRoutes = string.CompareOrdinal(xKey, yKey); if (byRoutes != 0) return byRoutes; var byDestinations = string.CompareOrdinal(string.Join("\u001f", x.Legs.Select(l => l.ToId)), string.Join("\u001f", y.Legs.Select(l => l.ToId))); return byDestinations != 0 ? byDestinations : string.CompareOrdinal(x.Node, y.Node); }
    }
    private static string? Component(EntitySnapshot e, string id) => e.Components.SingleOrDefault(c => c.DefinitionId == id)?.Data;
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    private static bool EmptyObject(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool ActiveTraveller(string? json) => Closed(json, ["status"], out var x) && x.GetProperty("status").GetString() == "active";
    private static bool ActiveRoot(string? json) => Closed(json, ["status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && Text(x, "summary", 1000) && Visibility(x);
    private static bool ActiveLocation(string? json) => Closed(json, ["kind", "status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && x.GetProperty("kind").GetString() is "region" or "settlement" or "site" or "interior" && Text(x, "summary", 1000) && Visibility(x);
    private static bool RouteData(string? json, out int duration) { duration = 0; return Closed(json, ["durationMinutes", "mode", "status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && x.GetProperty("mode").GetString() == "on-foot" && Text(x, "summary", 1000) && Visibility(x) && x.GetProperty("durationMinutes").TryGetInt32(out duration) && duration is >= 1 and <= 1440; }
    private static bool AvailabilityData(string? json, out bool open) { open = false; if (!Closed(json, ["status"], out var x)) return false; var value = x.GetProperty("status").GetString(); open = value == "open"; return open || value == "closed"; }
    private static bool ClockRevision(string? json, out long revision) { revision = 0; return Closed(json, ["calendarId", "currentMinute", "revision"], out var x) && Text(x, "calendarId", 100) && x.GetProperty("currentMinute").TryGetInt64(out var minute) && minute is >= 0 and <= 1000000000 && x.GetProperty("revision").TryGetInt64(out revision) && revision is >= 0 and <= 2147483647; }
    private static bool Closed(string? json, string[] keys, out JsonElement root) { root = default; try { using var d = JsonDocument.Parse(json ?? ""); root = d.RootElement.Clone(); return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(keys.Order(StringComparer.Ordinal)); } catch { return false; } }
    private static bool Text(JsonElement x, string key, int maximum) => x.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString() == value.GetString()!.Trim() && value.GetString()!.Length <= maximum;
    private static bool Visibility(JsonElement x) => x.GetProperty("visibility").GetString() is "public" or "party" or "gm";
    private static JourneyPlanProjection Empty(string status, JourneyPlanQuery query, string origin, long revision) => new(status, query.WorldId, query.TravellerId, origin, query.DestinationId, revision, [], 0);
}
