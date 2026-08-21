using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogWorldFeature13Tests : IDisposable
{
    private const string Dragon = "mount.feature-13.dragon", Route = "aerial-route.feature-13.gate-to-observatory", Root = "world.feature-01.fixture", Gate = "location.feature-01.gate", Observatory = "location.feature-01.observatory";
    private const string ConveyanceComponent = "game.core.world.aerial-conveyance", RouteComponent = "game.core.world.aerial-route";
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-13-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_has_generic_aerial_state_and_a_non_adjacent_gate_to_observatory_route()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy); AssertFixture(contents);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.travel"));
        var dragon = (await world.GetEntityAsync(Dragon))!; Assert.Equal(Gate, dragon.ContainerId); Assert.Equal("presence", dragon.ContainerSlot); AssertConveyance(Component(dragon, ConveyanceComponent));
        var route = (await world.GetEntityAsync(Route))!; AssertRoute(Component(route, RouteComponent)); AssertLinks((await world.GetRelationshipsAsync(Route, includeIncoming: false)).Select(ToLink));
        Assert.DoesNotContain((await world.GetRelationshipsAsync(Gate, includeIncoming: false)), edge => edge.Kind == "game.core.world.location.connected-to" && edge.ToEntityId == Observatory);
    }

    [Fact]
    public void Closed_aerial_state_and_links_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertConveyance("{}"));
        Assert.Throws<InvalidOperationException>(() => AssertConveyance("""{"status":"active","summary":" dragon","visibility":"party","travelMode":"air","speedUnitsPerMinute":30}"""));
        Assert.Throws<InvalidOperationException>(() => AssertConveyance("""{"status":"active","summary":"dragon","visibility":"party","travelMode":"ground","speedUnitsPerMinute":30}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRoute("""{"status":"active","summary":"route","visibility":"party","travelMode":"air","distanceUnits":0}"""));
        Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Route, Root, "game.core.world.aerial-route.in-world", "{}"), new(Route, Gate, "game.core.world.aerial-route.from", "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Route, Root, "game.core.world.aerial-route.in-world", "{}"), new(Route, Gate, "game.core.world.aerial-route.from", "{}"), new(Route, Gate, "game.core.world.aerial-route.to", "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Route, Root, "game.core.world.aerial-route.in-world", "{}"), new(Route, Gate, "game.core.world.aerial-route.from", "{}"), new(Route, Observatory, "game.core.world.aerial-route.to", "{\"altitude\":1}") ]));
    }

    [Fact]
    public async Task Replacing_dragon_speed_does_not_change_ground_routes_clock_or_topology()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var cart = (await world.GetEntityAsync("conveyance.feature-12.horse-cart"))!; var root = (await world.GetEntityAsync(Root))!; var groundRoute = (await world.GetEntityAsync("conveyance-route.feature-12.gate-to-market-ground"))!; var gateLinks = await world.GetRelationshipsAsync(Gate);
        await world.SetComponentAsync(Dragon, ConveyanceComponent, """{"status":"active","summary":"A calm fixture dragon prepared for the reviewed aerial route.","visibility":"party","travelMode":"air","speedUnitsPerMinute":40}""");
        Assert.Equal(Gate, cart.ContainerId); Assert.Equal("{\"calendarId\":\"lantern-compact-epoch\",\"currentMinute\":0,\"revision\":0}", Component(root, "game.core.world.clock")); Assert.Equal("ground", JsonDocument.Parse(Component(groundRoute, "game.core.world.conveyance-route")).RootElement.GetProperty("travelMode").GetString()); Assert.Equal(gateLinks.Select(Key), (await world.GetRelationshipsAsync(Gate)).Select(Key));
    }

    [Fact]
    public async Task Aerial_conveyance_journey_moves_dragon_and_rider_over_non_adjacent_endpoints()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        const string travel = "mechanic.game.core.world.aerial-conveyance.travel"; Assert.NotNull(await mechanics.GetAsync(travel));

        var result = await JourneyAsync(Runner(db, world, mechanics));

        Assert.True(result.Ok, result.Error?.Why); Assert.Equal(travel, result.Mechanic!.Id); Assert.Equal(3, result.AppliedCount); Assert.Equal(3, result.Output.Effects.Count);
        Assert.Equal(Observatory, (await world.GetEntityAsync(Dragon))!.ContainerId); Assert.Equal("presence", (await world.GetEntityAsync(Dragon))!.ContainerSlot);
        Assert.Equal(Observatory, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); AssertClock((await world.GetEntityAsync(Root))!, 20, 1);
        Assert.Equal(new[] { "world.containment.moved", "world.containment.moved", "world.component.replaced" }, (await new EventLedger(db).FindAsync(rootOperationId: result.OperationId)).Select(e => e.TypeId));
    }

    [Fact]
    public async Task Ceiling_division_and_invalid_or_replayed_aerial_journeys_leave_no_partial_state()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = Runner(db, world, mechanics); var baseline = await JourneyStateAsync(world);

        await world.MoveAsync(Dragon, Observatory, "presence"); var splitBefore = await JourneyStateAsync(world);
        var split = await JourneyAsync(runner); Assert.False(split.Ok); Assert.Equal(splitBefore, await JourneyStateAsync(world));
        await world.MoveAsync(Dragon, Gate, "presence");

        await world.SetComponentAsync(Dragon, ConveyanceComponent, """{"status":"archived","summary":"A calm fixture dragon prepared for the reviewed aerial route.","visibility":"party","travelMode":"air","speedUnitsPerMinute":30}""");
        var inactive = await JourneyAsync(runner); Assert.False(inactive.Ok); Assert.Equal(baseline with { ConveyanceData = Component((await world.GetEntityAsync(Dragon))!, ConveyanceComponent) }, await JourneyStateAsync(world));

        await world.SetComponentAsync(Dragon, ConveyanceComponent, baseline.ConveyanceData);
        await world.SetComponentAsync(Root, "game.core.world.clock", """{"calendarId":"lantern-compact-epoch","currentMinute":999999981,"revision":0}""");
        var overflow = await JourneyAsync(runner); Assert.False(overflow.Ok); Assert.Equal(Gate, (await world.GetEntityAsync(Dragon))!.ContainerId); Assert.Equal(Gate, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); AssertClock((await world.GetEntityAsync(Root))!, 999999981, 0);

        await world.SetComponentAsync(Root, "game.core.world.clock", baseline.ClockData);
        await world.SetComponentAsync(Dragon, ConveyanceComponent, """{"status":"active","summary":"A calm fixture dragon prepared for the reviewed aerial route.","visibility":"party","travelMode":"air","speedUnitsPerMinute":32}""");
        var nonDivisible = await JourneyAsync(runner); Assert.True(nonDivisible.Ok, nonDivisible.Error?.Why); AssertClock((await world.GetEntityAsync(Root))!, 19, 1);
        var stale = await JourneyAsync(runner); Assert.False(stale.Ok); Assert.Equal(Observatory, (await world.GetEntityAsync(Dragon))!.ContainerId); Assert.Equal(Observatory, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); AssertClock((await world.GetEntityAsync(Root))!, 19, 1);
    }

    private static void AssertFixture(CatalogContents contents) { Assert.Contains(contents.Components, component => component.Id == ConveyanceComponent && !string.IsNullOrWhiteSpace(component.Schema)); Assert.Contains(contents.Components, component => component.Id == RouteComponent && !string.IsNullOrWhiteSpace(component.Schema)); var dragon = contents.Entities.Single(entity => entity.Id == Dragon); Assert.Equal(Gate, dragon.ContainerId); Assert.Equal("presence", dragon.ContainerSlot); AssertConveyance(dragon.Components.Single(component => component.DefinitionId == ConveyanceComponent).Data); AssertRoute(contents.Entities.Single(entity => entity.Id == Route).Components.Single(component => component.DefinitionId == RouteComponent).Data); AssertLinks(contents.Relationships!.Relationships.Where(link => link.From == Route).Select(ToLink)); }
    private static void AssertConveyance(string json) { using var d = JsonDocument.Parse(json); var x = d.RootElement; var status = Text(x, "status", 10); if (x.ValueKind != JsonValueKind.Object || x.EnumerateObject().Count() != 5 || (status != "active" && status != "archived") || Text(x, "visibility", 10) is not ("public" or "party" or "gm") || Text(x, "travelMode", 10) != "air" || !x.TryGetProperty("speedUnitsPerMinute", out var speed) || !speed.TryGetInt32(out var n) || n is < 1 or > 10000) throw new InvalidOperationException("Aerial conveyance state is invalid."); Text(x, "summary", 1000); }
    private static void AssertRoute(string json) { using var d = JsonDocument.Parse(json); var x = d.RootElement; var status = Text(x, "status", 10); if (x.ValueKind != JsonValueKind.Object || x.EnumerateObject().Count() != 5 || (status != "active" && status != "archived") || Text(x, "visibility", 10) is not ("public" or "party" or "gm") || Text(x, "travelMode", 10) != "air" || !x.TryGetProperty("distanceUnits", out var distance) || !distance.TryGetInt32(out var n) || n is < 1 or > 1000000) throw new InvalidOperationException("Aerial route state is invalid."); Text(x, "summary", 1000); }
    private static void AssertLinks(IEnumerable<Link> links) { var list = links.ToArray(); if (list.Length != 3 || list.Any(link => link.From != Route || link.Data != "{}") || list.Count(link => link.Kind == "game.core.world.aerial-route.in-world" && link.To == Root) != 1 || list.Count(link => link.Kind == "game.core.world.aerial-route.from" && link.To == Gate) != 1 || list.Count(link => link.Kind == "game.core.world.aerial-route.to" && link.To == Observatory) != 1) throw new InvalidOperationException("Aerial route links are invalid."); }
    private static string Text(JsonElement root, string name, int maximum) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString() == value.GetString()!.Trim() && value.GetString()!.Length <= maximum ? value.GetString()! : throw new InvalidOperationException($"{name} is invalid.");
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static Task<ActionRunResult> JourneyAsync(ActionRunner runner) => runner.RunAsync(new ActionRequest { Intent = "fly the dragon to the observatory", RoleEntityIds = new Dictionary<string, string> { ["rider"] = "traveller.feature-02.fixture", ["conveyance"] = Dragon, ["origin"] = Gate, ["destination"] = Observatory, ["aerialRoute"] = Route, ["world"] = Root }, Input = "{}", Seed = 1313 });
    private static async Task<JourneyState> JourneyStateAsync(WorldStore world) { var dragon = (await world.GetEntityAsync(Dragon))!; var rider = (await world.GetEntityAsync("traveller.feature-02.fixture"))!; var root = (await world.GetEntityAsync(Root))!; return new(dragon.ContainerId, dragon.ContainerSlot, rider.ContainerId, rider.ContainerSlot, Component(dragon, ConveyanceComponent), Component(root, "game.core.world.clock")); }
    private static void AssertClock(EntitySnapshot root, long minute, long revision) { using var document = JsonDocument.Parse(Component(root, "game.core.world.clock")); Assert.Equal(minute, document.RootElement.GetProperty("currentMinute").GetInt64()); Assert.Equal(revision, document.RootElement.GetProperty("revision").GetInt64()); }
    private static string Component(EntitySnapshot entity, string id) => entity.Components.Single(component => component.DefinitionId == id).Data; private static string Key(RelationshipView link) => $"{link.FromEntityId}|{link.ToEntityId}|{link.Kind}|{link.Data}"; private static Link ToLink(RelationshipEntry link) => new(link.From, link.To, link.Kind, link.Data); private static Link ToLink(RelationshipView link) => new(link.FromEntityId, link.ToEntityId, link.Kind, link.Data);
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record JourneyState(string? ConveyanceContainer, string ConveyanceSlot, string? RiderContainer, string RiderSlot, string ConveyanceData, string ClockData);
}
