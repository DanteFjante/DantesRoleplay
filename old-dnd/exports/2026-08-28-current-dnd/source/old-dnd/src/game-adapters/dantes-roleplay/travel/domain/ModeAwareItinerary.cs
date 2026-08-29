using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.World;

/// <summary>Closed read request for Feature 16's mode-aware itinerary.</summary>
public sealed record ModeAwareItineraryQuery(string WorldId, string TravellerId, string DestinationLocationId, string? GroundConveyanceId = null, string? AerialConveyanceId = null);
public sealed record ModeAwareItineraryLeg(int Index, string Mode, string FromLocationId, string ToLocationId, string RouteOrPortalId, string? ConveyanceId, int EstimatedMinutes);
public sealed record ModeAwareItineraryProjection(string Status, string WorldId, string TravellerId, string OriginLocationId, string DestinationLocationId, string? ItineraryFingerprint, int? EstimatedTotalMinutes, IReadOnlyList<ModeAwareItineraryLeg> Legs);
public sealed record ModeAwareItineraryResult(ModeAwareItineraryProjection? Projection, string ErrorCode = "", string ErrorMessage = "")
{
    public bool Ok => Projection is not null && ErrorCode.Length == 0;
    public static ModeAwareItineraryResult Fail(string code, string message) => new(null, code, message);
    public static ModeAwareItineraryResult Success(ModeAwareItineraryProjection projection) => new(projection);
}
public interface IModeAwareItineraryReader { Task<ModeAwareItineraryResult> ReadAsync(ModeAwareItineraryQuery query, CancellationToken cancellationToken = default); }

/// <summary>
/// Read-only state-space planner.  A state includes a selected conveyance's location, so a plan
/// cannot use a cart or dragon after the traveller has walked or teleported away from it.
/// </summary>
public sealed class ModeAwareItineraryReader(IWorldStore world) : IModeAwareItineraryReader
{
    private const string Traveller = "game.core.world.traveller", Location = "game.core.world.location", Root = "game.core.world.root", Clock = "game.core.world.clock";
    private const string FootRoute = "game.core.world.route", Availability = "game.core.world.route.availability", Ground = "game.core.world.conveyance", GroundRoute = "game.core.world.conveyance-route", Air = "game.core.world.aerial-conveyance", AirRoute = "game.core.world.aerial-route", Portal = "game.core.world.teleport-gate";
    private const string Connected = "game.core.world.location.connected-to";
    private readonly IWorldStore _world = world;

    public async Task<ModeAwareItineraryResult> ReadAsync(ModeAwareItineraryQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || !Id(query.WorldId) || !Id(query.TravellerId) || !Id(query.DestinationLocationId) || !OptionalId(query.GroundConveyanceId) || !OptionalId(query.AerialConveyanceId)) return Fail("INVALID_ITINERARY", "worldId, travellerId, destinationLocationId, and optional conveyance ids must be trimmed nonblank ids.");
        var root = await _world.GetEntityAsync(query.WorldId, cancellationToken); var traveller = await _world.GetEntityAsync(query.TravellerId, cancellationToken); var destination = await _world.GetEntityAsync(query.DestinationLocationId, cancellationToken);
        if (root is null || traveller is null || destination is null) return Fail("UNKNOWN_ITINERARY_ENTITY", "worldId, travellerId, and destinationLocationId must name existing entities.");
        if (!ActiveRoot(Component(root, Root)) || !ClockData(Component(root, Clock), out var revision) || !ActiveTraveller(Component(traveller, Traveller)) || traveller.ContainerId is null || traveller.ContainerSlot != "presence") return Fail("INVALID_ITINERARY_STATE", "The supplied world must have a valid clock and travellerId must be an active present traveller.");
        var origin = await _world.GetEntityAsync(traveller.ContainerId, cancellationToken);
        if (origin is null || !ActiveLocation(Component(origin, Location)) || !ActiveLocation(Component(destination, Location)) || !SiblingLocations(origin, destination)) return Fail("INVALID_ITINERARY_LOCATION", "The derived origin and destinationLocationId must be active sibling locations.");
        if (origin.Id == destination.Id) return Success(Empty("already-there", query, origin.Id));

        var ground = await SelectedAsync(query.GroundConveyanceId, Ground, "ground", origin.Id, cancellationToken);
        if (!ground.Ok) return Success(Empty("unavailable-resource", query, origin.Id));
        var air = await SelectedAsync(query.AerialConveyanceId, Air, "air", origin.Id, cancellationToken);
        if (!air.Ok) return Success(Empty("unavailable-resource", query, origin.Id));

        var edges = new List<Edge>();
        await AddFootAsync(edges, root.Id, cancellationToken);
        if (ground.Entity is not null) await AddConveyanceAsync(edges, root.Id, ground.Entity, "ground", GroundRoute, cancellationToken);
        if (air.Entity is not null) await AddConveyanceAsync(edges, root.Id, air.Entity, "air", AirRoute, cancellationToken);
        await AddPortalsAsync(edges, root.Id, cancellationToken);

        var structural = Shortest(edges, origin.Id, destination.Id, ground.Entity?.ContainerId, air.Entity?.ContainerId, includeClosedFoot: true);
        if (structural.Status == SearchStatus.TooLong) return Success(Empty("too-long", query, origin.Id));
        if (structural.Legs is null) return Success(Empty("unreachable", query, origin.Id));
        var eligible = Shortest(edges, origin.Id, destination.Id, ground.Entity?.ContainerId, air.Entity?.ContainerId, includeClosedFoot: false);
        if (eligible.Status == SearchStatus.TooLong) return Success(Empty("too-long", query, origin.Id));
        if (eligible.Legs is null) return Success(Empty("blocked", query, origin.Id));
        if (eligible.Legs.Count is < 1 or > 64 || eligible.Minutes > 1_000_000_000) return Success(Empty("too-long", query, origin.Id));

        var legs = eligible.Legs.Select((edge, index) => new ModeAwareItineraryLeg(index, edge.Mode, edge.From, edge.To, edge.Id, edge.ConveyanceId, edge.Minutes)).ToArray();
        var fingerprint = Fingerprint(query, origin.Id, revision, legs);
        return Success(new("ready", root.Id, traveller.Id, origin.Id, destination.Id, fingerprint, eligible.Minutes, legs));
    }

    private async Task<(bool Ok, EntitySnapshot? Entity)> SelectedAsync(string? id, string component, string mode, string originId, CancellationToken ct)
    {
        if (id is null) return (true, null);
        var entity = await _world.GetEntityAsync(id, ct);
        if (entity is null || entity.ContainerId != originId || entity.ContainerSlot != "presence" || !Conveyance(Component(entity, component), mode)) return (false, null);
        return (true, entity);
    }

    private async Task AddFootAsync(List<Edge> edges, string worldId, CancellationToken ct)
    {
        foreach (var route in await EntitiesAsync(FootRoute, ct))
        {
            if (!Foot(Component(route, FootRoute), out var minutes) || !Links(await _world.GetRelationshipsAsync(route.Id, false, ct), route.Id, "game.core.world.route", worldId, out var from, out var to)) continue;
            if (!await EndpointsAsync(from, to, true, ct)) continue;
            if (!AvailabilityData(Component(route, Availability), out var open)) continue;
            edges.Add(new(route.Id, "on-foot", from, to, minutes, null, open));
        }
    }

    private async Task AddConveyanceAsync(List<Edge> edges, string worldId, EntitySnapshot conveyance, string mode, string routeComponent, CancellationToken ct)
    {
        foreach (var route in await EntitiesAsync(routeComponent, ct))
        {
            if (!Distance(Component(route, routeComponent), routeComponent, mode, out var distance) || !Links(await _world.GetRelationshipsAsync(route.Id, false, ct), route.Id, routeComponent, worldId, out var from, out var to)) continue;
            if (!await EndpointsAsync(from, to, mode == "ground", ct)) continue;
            var speed = Speed(Component(conveyance, mode == "ground" ? Ground : Air), mode);
            if (speed is null) continue;
            edges.Add(new(route.Id, mode, from, to, Ceiling(distance, speed.Value), conveyance.Id, true));
        }
    }

    private async Task AddPortalsAsync(List<Edge> edges, string worldId, CancellationToken ct)
    {
        foreach (var portal in await EntitiesAsync(Portal, ct))
        {
            if (!FixedPortal(Component(portal, Portal)) || portal.ContainerId is null || portal.ContainerSlot != "presence") continue;
            var links = await _world.GetRelationshipsAsync(portal.Id, false, ct);
            if (links.Count != 2 || links.Any(x => x.FromEntityId != portal.Id || !Empty(x.Data))) continue;
            var scope = links.Where(x => x.Kind == "game.core.world.teleport-gate.in-world" && x.ToEntityId == worldId).ToArray(); var to = links.Where(x => x.Kind == "game.core.world.teleport-gate.to").ToArray();
            if (scope.Length != 1 || to.Length != 1 || to[0].ToEntityId == portal.ContainerId || !await EndpointsAsync(portal.ContainerId, to[0].ToEntityId, false, ct)) continue;
            edges.Add(new(portal.Id, "portal", portal.ContainerId, to[0].ToEntityId, 0, null, true));
        }
    }

    private async Task<IReadOnlyList<EntitySnapshot>> EntitiesAsync(string component, CancellationToken ct) => await _world.GetEntitiesAsync((await _world.FindEntitiesAsync(withDefinitionId: component, limit: 10_000, cancellationToken: ct)).Select(x => x.Id), ct);
    private async Task<bool> EndpointsAsync(string from, string to, bool adjacency, CancellationToken ct)
    {
        var pair = await _world.GetEntitiesAsync([from, to], ct); if (pair.Count != 2) return false;
        var a = pair.Single(x => x.Id == from); var b = pair.Single(x => x.Id == to);
        if (!ActiveLocation(Component(a, Location)) || !ActiveLocation(Component(b, Location)) || !SiblingLocations(a, b)) return false;
        if (!adjacency) return true;
        return (await _world.GetRelationshipsAsync(a.Id, true, ct)).Count(x => x.Kind == Connected && Empty(x.Data) && string.CompareOrdinal(x.FromEntityId, x.ToEntityId) < 0 && ((x.FromEntityId == a.Id && x.ToEntityId == b.Id) || (x.FromEntityId == b.Id && x.ToEntityId == a.Id))) == 1;
    }

    private static bool Links(IReadOnlyList<RelationshipView> links, string owner, string prefix, string world, out string from, out string to)
    {
        from = to = string.Empty; if (links.Count != 3 || links.Any(x => x.FromEntityId != owner || !Empty(x.Data))) return false;
        var scope = links.Where(x => x.Kind == prefix + ".in-world" && x.ToEntityId == world).ToArray(); var starts = links.Where(x => x.Kind == prefix + ".from").ToArray(); var ends = links.Where(x => x.Kind == prefix + ".to").ToArray();
        if (scope.Length != 1 || starts.Length != 1 || ends.Length != 1 || starts[0].ToEntityId == ends[0].ToEntityId) return false; from = starts[0].ToEntityId; to = ends[0].ToEntityId; return true;
    }

    private static Search Shortest(IReadOnlyList<Edge> edges, string start, string goal, string? ground, string? air, bool includeClosedFoot)
    {
        var initial = new State(start, ground, air); var queue = new List<Path> { new(initial, 0, []) }; var best = new Dictionary<State, Path> { [initial] = queue[0] }; var visits = 0;
        while (queue.Count > 0)
        {
            if (++visits > 50_000) return new(SearchStatus.TooLong, null, 0);
            queue.Sort(Path.Compare); var current = queue[0]; queue.RemoveAt(0); if (!ReferenceEquals(best[current.State], current)) continue;
            if (current.State.Traveller == goal) return new(SearchStatus.Ready, current.Legs, current.Minutes);
            foreach (var edge in edges.Where(x => x.From == current.State.Traveller && (includeClosedFoot || x.Mode != "on-foot" || x.Open)).OrderBy(x => x, Comparer<Edge>.Create(Edge.Compare)))
            {
                if (edge.Mode == "ground" && current.State.Ground != edge.From) continue; if (edge.Mode == "air" && current.State.Air != edge.From) continue;
                var nextState = new State(edge.To, edge.Mode == "ground" ? edge.To : current.State.Ground, edge.Mode == "air" ? edge.To : current.State.Air);
                var next = new Path(nextState, current.Minutes + edge.Minutes, [.. current.Legs, edge]);
                if (!best.TryGetValue(nextState, out var prior) || Path.Compare(next, prior) < 0) { best[nextState] = next; queue.Add(next); }
            }
        }
        return new(SearchStatus.None, null, 0);
    }

    private sealed record Edge(string Id, string Mode, string From, string To, int Minutes, string? ConveyanceId, bool Open)
    {
        public static int Compare(Edge x, Edge y) { var mode = Rank(x.Mode).CompareTo(Rank(y.Mode)); if (mode != 0) return mode; var id = string.CompareOrdinal(x.Id, y.Id); return id != 0 ? id : string.CompareOrdinal(x.To, y.To); }
        private static int Rank(string mode) => mode switch { "portal" => 0, "on-foot" => 1, "ground" => 2, "air" => 3, _ => 4 };
    }
    private sealed record State(string Traveller, string? Ground, string? Air);
    private sealed record Path(State State, int Minutes, IReadOnlyList<Edge> Legs)
    {
        public static int Compare(Path x, Path y) { var byTime = x.Minutes.CompareTo(y.Minutes); if (byTime != 0) return byTime; var byLegs = x.Legs.Count.CompareTo(y.Legs.Count); if (byLegs != 0) return byLegs; for (var i = 0; i < x.Legs.Count; i++) { var edge = Edge.Compare(x.Legs[i], y.Legs[i]); if (edge != 0) return edge; } return string.CompareOrdinal(x.State.Traveller, y.State.Traveller); }
    }
    private enum SearchStatus { None, Ready, TooLong }
    private sealed record Search(SearchStatus Status, IReadOnlyList<Edge>? Legs, int Minutes);

    private static ModeAwareItineraryResult Fail(string code, string message) => ModeAwareItineraryResult.Fail(code, message);
    private static ModeAwareItineraryResult Success(ModeAwareItineraryProjection result) => ModeAwareItineraryResult.Success(result);
    private static ModeAwareItineraryProjection Empty(string status, ModeAwareItineraryQuery q, string origin) => new(status, q.WorldId, q.TravellerId, origin, q.DestinationLocationId, null, null, []);
    private static string Fingerprint(ModeAwareItineraryQuery q, string origin, long revision, IEnumerable<ModeAwareItineraryLeg> legs) { var value = string.Join("|", new[] { q.WorldId, q.TravellerId, q.DestinationLocationId, q.GroundConveyanceId ?? "", q.AerialConveyanceId ?? "", origin, revision.ToString() }.Concat(legs.Select(x => $"{x.Index}:{x.Mode}:{x.FromLocationId}:{x.ToLocationId}:{x.RouteOrPortalId}:{x.ConveyanceId}:{x.EstimatedMinutes}"))); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(); }
    private static string? Component(EntitySnapshot entity, string id) => entity.Components.SingleOrDefault(x => x.DefinitionId == id)?.Data;
    private static bool Id(string? x) => !string.IsNullOrWhiteSpace(x) && x == x.Trim(); private static bool OptionalId(string? x) => x is null || Id(x); private static bool SiblingLocations(EntitySnapshot x, EntitySnapshot y) => x.ContainerId is not null && x.ContainerId == y.ContainerId && x.ContainerSlot == "location" && y.ContainerSlot == "location";
    private static bool Empty(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool ActiveTraveller(string? json) => Closed(json, ["status"], out var x) && x.GetProperty("status").GetString() == "active";
    private static bool ActiveRoot(string? json) => Closed(json, ["status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && Text(x, "summary", 1000) && Visibility(x);
    private static bool ActiveLocation(string? json) => Closed(json, ["kind", "status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && x.GetProperty("kind").GetString() is "region" or "settlement" or "site" or "interior" && Text(x, "summary", 1000) && Visibility(x);
    private static bool ClockData(string? json, out long revision) { revision = 0; return Closed(json, ["calendarId", "currentMinute", "revision"], out var x) && Text(x, "calendarId", 100) && x.GetProperty("currentMinute").TryGetInt64(out var current) && current is >= 0 and <= 1_000_000_000 && x.GetProperty("revision").TryGetInt64(out revision) && revision is >= 0 and <= int.MaxValue; }
    private static bool Foot(string? json, out int minutes) { minutes = 0; return Closed(json, ["durationMinutes", "mode", "status", "summary", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && x.GetProperty("mode").GetString() == "on-foot" && Text(x, "summary", 1000) && Visibility(x) && x.GetProperty("durationMinutes").TryGetInt32(out minutes) && minutes is >= 1 and <= 1440; }
    private static bool AvailabilityData(string? json, out bool open) { open = false; if (!Closed(json, ["status"], out var x)) return false; var state = x.GetProperty("status").GetString(); open = state == "open"; return open || state == "closed"; }
    private static bool Conveyance(string? json, string mode) { if (!Closed(json, ["speedUnitsPerMinute", "status", "summary", "travelMode", "visibility"], out var x)) return false; return x.GetProperty("status").GetString() == "active" && x.GetProperty("travelMode").GetString() == mode && Text(x, "summary", 1000) && Visibility(x) && x.GetProperty("speedUnitsPerMinute").TryGetInt32(out var speed) && speed is >= 1 and <= 10000; }
    private static int? Speed(string? json, string mode) => Conveyance(json, mode) && JsonDocument.Parse(json!).RootElement.GetProperty("speedUnitsPerMinute").TryGetInt32(out var speed) ? speed : null;
    private static bool Distance(string? json, string component, string mode, out int distance) { distance = 0; return Closed(json, ["distanceUnits", "status", "summary", "travelMode", "visibility"], out var x) && x.GetProperty("status").GetString() == "active" && x.GetProperty("travelMode").GetString() == mode && Text(x, "summary", 1000) && Visibility(x) && x.GetProperty("distanceUnits").TryGetInt32(out distance) && distance is >= 1 and <= 1_000_000; }
    private static bool FixedPortal(string? json) => Closed(json, ["kind", "status", "summary", "visibility"], out var x) && x.GetProperty("kind").GetString() == "fixed-portal" && x.GetProperty("status").GetString() == "active" && Text(x, "summary", 1000) && Visibility(x);
    private static int Ceiling(int a, int b) => (a + b - 1) / b;
    private static bool Closed(string? json, string[] keys, out JsonElement root) { root = default; try { using var d = JsonDocument.Parse(json ?? ""); root = d.RootElement.Clone(); return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(keys.Order(StringComparer.Ordinal)); } catch { return false; } }
    private static bool Text(JsonElement x, string key, int max) => x.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) && p.GetString() == p.GetString()!.Trim() && p.GetString()!.Length <= max;
    private static bool Visibility(JsonElement x) => x.GetProperty("visibility").GetString() is "public" or "party" or "gm";
}
