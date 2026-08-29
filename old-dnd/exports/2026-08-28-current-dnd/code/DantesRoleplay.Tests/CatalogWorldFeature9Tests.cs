using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Feature 9 Slice 1 fixes display anchors without changing world topology or travel.</summary>
public sealed class CatalogWorldFeature9Tests : IDisposable
{
    private const string Region = "region.feature-01.fixture";
    private const string Gate = "location.feature-01.gate";
    private const string Market = "location.feature-01.market";
    private const string Observatory = "location.feature-01.observatory";
    private const string Anchor = "game.core.world.map.anchor";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-09-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_has_exactly_one_unique_anchor_on_each_direct_fixture_location()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy); AssertFixture(contents);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.spatial"));

        var placements = new[] { (Gate, 150, 650), (Market, 500, 500), (Observatory, 850, 250) };
        foreach (var (id, x, y) in placements)
        {
            var location = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(id));
            Assert.Equal(Region, location.ContainerId); Assert.Equal("location", location.ContainerSlot);
            AssertAnchor(Assert.Single(location.Components, component => component.DefinitionId == Anchor).Data, x, y);
        }
        Assert.Equal(3, placements.Select(p => (p.Item2, p.Item3)).Distinct().Count());
    }

    [Fact]
    public void Closed_anchor_data_and_direct_region_scope_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertAnchor("{}", 0, 0));
        Assert.Throws<InvalidOperationException>(() => AssertAnchor("""{"x":1.5,"y":2}""", 0, 0));
        Assert.Throws<InvalidOperationException>(() => AssertAnchor("""{"x":-1,"y":2}""", 0, 0));
        Assert.Throws<InvalidOperationException>(() => AssertAnchor("""{"x":1,"y":2,"z":3}""", 0, 0));
        Assert.Throws<InvalidOperationException>(() => AssertPlacement([new(Gate, Region, "location", "site", "active", 1, 1), new(Market, Region, "location", "site", "active", 1, 1), new(Observatory, Region, "location", "interior", "active", 3, 3)]));
        Assert.Throws<InvalidOperationException>(() => AssertPlacement([new(Gate, Region, "location", "site", "active", 1, 1), new(Market, Region, "location", "site", "archived", 2, 2), new(Observatory, Gate, "location", "interior", "active", 3, 3)]));
    }

    [Fact]
    public async Task Replacing_an_anchor_changes_no_containment_adjacency_route_or_clock_state()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var before = await WorldStateAsync(world);
        await world.SetComponentAsync(Gate, Anchor, """{"x":200,"y":600}""");
        var after = await WorldStateAsync(world);
        Assert.Equal(before.GateContainer, after.GateContainer); Assert.Equal(before.GateSlot, after.GateSlot);
        Assert.Equal("{\"x\":200,\"y\":600}", after.GateAnchor); Assert.Equal(before.Clock, after.Clock);
        Assert.Equal(before.Route, after.Route); Assert.Equal(before.Edges, after.Edges);
    }

    [Fact]
    public async Task Two_public_graph_reads_build_the_stable_trusted_gm_layout_without_a_world_write()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var procedures = new ProcedureStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var before = await WorldStateAsync(world); var operationsBefore = await db.Operations.CountAsync();

        var topology = Projection(await GraphAsync(db, procedures, world, mechanics, Region,
            ["game.core.world.location", Anchor], 1, ["game.core.world.location.connected-to"], 1, 50, 100));
        var routes = Projection(await GraphAsync(db, procedures, world, mechanics, "world.feature-01.fixture",
            ["game.core.world.route"], 0, ["game.core.world.route.in-world", "game.core.world.route.from", "game.core.world.route.to"], 2, 100, 150));
        var layout = BuildLayout(topology, routes);

        Assert.Equal(Region, layout.Region.Id); Assert.Equal(new[] { Gate, Market, Observatory }, layout.Locations.Select(location => location.Id));
        Assert.Equal(new[] { (Gate, Market), (Market, Observatory) }, layout.Adjacency.Select(edge => (edge.FromLocationId, edge.ToLocationId)));
        var route = Assert.Single(layout.Routes); Assert.Equal("route.feature-08.gate-to-market-on-foot", route.Id); Assert.Equal(Gate, route.FromLocationId); Assert.Equal(Market, route.ToLocationId); Assert.Equal(30, route.DurationMinutes);
        var after = await WorldStateAsync(world); Assert.Equal(before.GateContainer, after.GateContainer); Assert.Equal(before.GateSlot, after.GateSlot); Assert.Equal(before.GateAnchor, after.GateAnchor); Assert.Equal(before.Clock, after.Clock); Assert.Equal(before.Route, after.Route); Assert.Equal(before.Edges, after.Edges); Assert.Equal(operationsBefore + 2, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Layout_recipe_rejects_a_malformed_anchor_instead_of_returning_partial_display_data()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var procedures = new ProcedureStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await world.SetComponentAsync(Gate, Anchor, """{"x":150,"y":650,"z":0}""");
        var topology = Projection(await GraphAsync(db, procedures, world, mechanics, Region, ["game.core.world.location", Anchor], 1, ["game.core.world.location.connected-to"], 1, 50, 100));
        var routes = Projection(await GraphAsync(db, procedures, world, mechanics, "world.feature-01.fixture", ["game.core.world.route"], 0, ["game.core.world.route.in-world", "game.core.world.route.from", "game.core.world.route.to"], 2, 100, 150));

        Assert.Throws<InvalidOperationException>(() => BuildLayout(topology, routes));
    }

    private static void AssertFixture(CatalogContents contents)
    {
        Assert.Contains(contents.Components, component => component.Id == Anchor && !string.IsNullOrWhiteSpace(component.Schema));
        var anchors = new List<Placement>();
        foreach (var (id, x, y) in new[] { (Gate, 150, 650), (Market, 500, 500), (Observatory, 850, 250) })
        {
            var entity = contents.Entities.Single(candidate => candidate.Id == id);
            Assert.Equal(Region, entity.ContainerId); Assert.Equal("location", entity.ContainerSlot);
            AssertAnchor(entity.Components.Single(component => component.DefinitionId == Anchor).Data, x, y);
            anchors.Add(new(id, Region, "location", "site", "active", x, y));
        }
        AssertPlacement(anchors);
    }

    private static async Task<ToolEnvelope> GraphAsync(DantesRoleplayDbContext db, IProcedureStore procedures, IWorldStore world, IMechanicStore mechanics, string id, string[] components, int containmentDepth, string[] relationships, int relationshipDepth, int maxNodes, int maxEdges) =>
        await new QueryTool().QueryAsync(procedures, world, new GraphProjectionReader(world), new JourneyPlanReader(world), new ModeAwareItineraryReader(world), null!, null!, mechanics, new EventTypeStore(db), new SubscriptionStore(db), new EventLedger(db), new OperationLog(db), new NotificationStore(db), "graph", id: id, componentIds: components, containmentDepth: containmentDepth, relationshipKinds: relationships, relationshipDepth: relationshipDepth, maxNodes: maxNodes, maxEdges: maxEdges);
    private static GraphProjection Projection(ToolEnvelope envelope) => Assert.IsType<GraphProjection>(envelope.Data);

    private static Layout BuildLayout(GraphProjection topology, GraphProjection routes)
    {
        if (topology.Truncated is not null || routes.Truncated is not null) throw new InvalidOperationException("Map layout input is truncated.");
        var region = topology.Nodes.SingleOrDefault(node => node.Id == topology.RootId) ?? throw new InvalidOperationException("Selected region is missing.");
        var regionState = Location(region); if (regionState.Kind != "region" || regionState.Status != "active") throw new InvalidOperationException("Selected region is invalid.");
        var locations = topology.Nodes.Where(node => node.ContainerId == region.Id && node.ContainerSlot == "location").Select(node =>
        {
            var state = Location(node); if (state.Status != "active") throw new InvalidOperationException("Inactive direct location cannot be displayed.");
            var anchor = AnchorData(node); return new LayoutLocation(node.Id, node.Name, state.Kind, state.Summary, anchor.X, anchor.Y);
        }).OrderBy(location => location.Id, StringComparer.Ordinal).ToList();
        if (locations.Count == 0 || locations.Select(location => (location.X, location.Y)).Distinct().Count() != locations.Count) throw new InvalidOperationException("Displayed anchors must be present and unique.");
        var ids = locations.Select(location => location.Id).ToHashSet(StringComparer.Ordinal);
        var adjacency = topology.Edges.Select(edge =>
        {
            if (edge.Kind != "game.core.world.location.connected-to" || edge.Data != "{}" || !ids.Contains(edge.FromEntityId) || !ids.Contains(edge.ToEntityId)) throw new InvalidOperationException("Map adjacency is invalid or outside the region.");
            return new LayoutAdjacency(edge.FromEntityId, edge.ToEntityId);
        }).OrderBy(edge => edge.FromLocationId, StringComparer.Ordinal).ThenBy(edge => edge.ToLocationId, StringComparer.Ordinal).ToList();
        var routeNodes = routes.Nodes.Where(node => node.Components.Any(component => component.DefinitionId == "game.core.world.route"));
        var layoutRoutes = routeNodes.Select(node => RouteData(node, routes.Edges, ids)).OrderBy(route => route.Id, StringComparer.Ordinal).ToList();
        return new Layout(new LayoutRegion(region.Id, region.Name), locations, adjacency, layoutRoutes);
    }

    private static (string Kind, string Status, string Summary) Location(GraphNode node)
    {
        using var document = JsonDocument.Parse(node.Components.Single(component => component.DefinitionId == "game.core.world.location").Data); var state = document.RootElement;
        if (state.ValueKind != JsonValueKind.Object || state.EnumerateObject().Count() != 4 || !state.TryGetProperty("kind", out var kind) || !state.TryGetProperty("status", out var status) || !state.TryGetProperty("summary", out var summary) || kind.ValueKind != JsonValueKind.String || status.ValueKind != JsonValueKind.String || summary.ValueKind != JsonValueKind.String) throw new InvalidOperationException("Location data is invalid.");
        return (kind.GetString()!, status.GetString()!, summary.GetString()!);
    }
    private static (int X, int Y) AnchorData(GraphNode node)
    {
        using var document = JsonDocument.Parse(node.Components.Single(component => component.DefinitionId == Anchor).Data); var anchor = document.RootElement;
        if (anchor.ValueKind != JsonValueKind.Object || anchor.EnumerateObject().Count() != 2 || !anchor.TryGetProperty("x", out var x) || !anchor.TryGetProperty("y", out var y) || !x.TryGetInt32(out var px) || !y.TryGetInt32(out var py) || px is < 0 or > 1000 || py is < 0 or > 1000) throw new InvalidOperationException("Map anchor is invalid.");
        return (px, py);
    }
    private static LayoutRoute RouteData(GraphNode route, IReadOnlyList<GraphEdge> edges, IReadOnlySet<string> locationIds)
    {
        using var document = JsonDocument.Parse(route.Components.Single(component => component.DefinitionId == "game.core.world.route").Data); var state = document.RootElement;
        if (state.ValueKind != JsonValueKind.Object || state.EnumerateObject().Count() != 5 || state.GetProperty("status").GetString() != "active" || state.GetProperty("mode").GetString() != "on-foot" || !state.GetProperty("durationMinutes").TryGetInt32(out var minutes) || minutes is < 1 or > 1440 || state.GetProperty("visibility").ValueKind != JsonValueKind.String) throw new InvalidOperationException("Route data is invalid.");
        var links = edges.Where(edge => edge.FromEntityId == route.Id).ToArray();
        var scope = links.SingleOrDefault(edge => edge.Kind == "game.core.world.route.in-world" && edge.Data == "{}");
        var from = links.SingleOrDefault(edge => edge.Kind == "game.core.world.route.from" && edge.Data == "{}");
        var to = links.SingleOrDefault(edge => edge.Kind == "game.core.world.route.to" && edge.Data == "{}");
        if (links.Length != 3 || scope is null || from is null || to is null || !locationIds.Contains(from.ToEntityId) || !locationIds.Contains(to.ToEntityId) || from.ToEntityId == to.ToEntityId) throw new InvalidOperationException("Route links are invalid or outside the region.");
        return new LayoutRoute(route.Id, route.Name, from.ToEntityId, to.ToEntityId, "on-foot", minutes, state.GetProperty("visibility").GetString()!);
    }

    private static void AssertAnchor(string json, int x, int y)
    {
        using var document = JsonDocument.Parse(json); var anchor = document.RootElement;
        if (anchor.ValueKind != JsonValueKind.Object || anchor.EnumerateObject().Count() != 2 || !anchor.TryGetProperty("x", out var px) || !anchor.TryGetProperty("y", out var py) || !px.TryGetInt32(out var actualX) || !py.TryGetInt32(out var actualY) || actualX is < 0 or > 1000 || actualY is < 0 or > 1000) throw new InvalidOperationException("Anchor data is invalid.");
        if (actualX != x || actualY != y) throw new InvalidOperationException("Anchor does not match the reviewed fixture position.");
    }

    private static void AssertPlacement(IEnumerable<Placement> candidates)
    {
        var placements = candidates.ToArray();
        if (placements.Length != 3 || placements.Any(p => p.RegionId != Region || p.Slot != "location" || p.Kind == "region" || p.Status != "active") || placements.Select(p => (p.X, p.Y)).Distinct().Count() != placements.Length) throw new InvalidOperationException("Anchors must be unique active direct locations in the selected region.");
    }

    private static async Task<WorldState> WorldStateAsync(WorldStore world)
    {
        var gate = (await world.GetEntityAsync(Gate))!; var root = (await world.GetEntityAsync("world.feature-01.fixture"))!;
        var edges = await world.GetRelationshipsAsync(Gate);
        var route = (await world.GetEntityAsync("route.feature-08.gate-to-market-on-foot"))!;
        return new(gate.ContainerId, gate.ContainerSlot, gate.Components.Single(c => c.DefinitionId == Anchor).Data, root.Components.Single(c => c.DefinitionId == "game.core.world.clock").Data, route.Components.Single(c => c.DefinitionId == "game.core.world.route").Data, edges.Select(e => $"{e.FromEntityId}|{e.ToEntityId}|{e.Kind}|{e.Data}").OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Placement(string Id, string RegionId, string Slot, string Kind, string Status, int X, int Y);
    private sealed record WorldState(string? GateContainer, string GateSlot, string GateAnchor, string Clock, string Route, string[] Edges);
    private sealed record Layout(LayoutRegion Region, IReadOnlyList<LayoutLocation> Locations, IReadOnlyList<LayoutAdjacency> Adjacency, IReadOnlyList<LayoutRoute> Routes);
    private sealed record LayoutRegion(string Id, string Name);
    private sealed record LayoutLocation(string Id, string Name, string Kind, string Summary, int X, int Y);
    private sealed record LayoutAdjacency(string FromLocationId, string ToLocationId);
    private sealed record LayoutRoute(string Id, string Name, string FromLocationId, string ToLocationId, string Mode, int DurationMinutes, string Visibility);
}
