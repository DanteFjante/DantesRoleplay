using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>Feature 8 Slice 1 fixes one directed route without adding the journey action yet.</summary>
public sealed class CatalogWorldFeature8Tests : IDisposable
{
    private const string Route = "route.feature-08.gate-to-market-on-foot";
    private const string Root = "world.feature-01.fixture";
    private const string Gate = "location.feature-01.gate";
    private const string Market = "location.feature-01.market";
    private const string RouteComponent = "game.core.world.route";
    private const string TravelMechanic = "mechanic.game.core.world.route.travel-on-foot";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-08-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_contains_the_confirmed_directed_gate_to_market_route_without_changing_existing_topology()
    {
        Copy(Catalog(), _copy);
        var contents = await CatalogReader.ReadAsync(_copy);
        AssertRouteFixture(contents);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_copy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.travel"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.time"));

        var route = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(Route));
        AssertRouteData(Assert.Single(route.Components, c => c.DefinitionId == RouteComponent).Data);
        var links = await world.GetRelationshipsAsync(Route, includeIncoming: false);
        AssertRouteLinks(links.Select(link => new Link(link.FromEntityId, link.ToEntityId, link.Kind, link.Data)));

        var traveller = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync("traveller.feature-02.fixture"));
        Assert.Equal(Gate, traveller.ContainerId);
        Assert.Equal("presence", traveller.ContainerSlot);
        Assert.Equal("{}", (await world.GetRelationshipsAsync(Gate, includeIncoming: false)).Single(link => link.Kind == "game.core.world.location.connected-to").Data);
    }

    [Fact]
    public void Closed_route_data_and_link_conventions_reject_invalid_fixture_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertRouteData("{}"));
        Assert.Throws<InvalidOperationException>(() => AssertRouteData("""{"status":"active","summary":"road","visibility":"party","mode":"horse","durationMinutes":30}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRouteData("""{"status":"active","summary":" road","visibility":"party","mode":"on-foot","durationMinutes":30}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRouteData("""{"status":"active","summary":"road","visibility":"party","mode":"on-foot","durationMinutes":0}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRouteData("""{"status":"active","summary":"road","visibility":"party","mode":"on-foot","durationMinutes":30,"distance":2}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRouteLinks([new(Route, Root, "game.core.world.route.in-world", "{}"), new(Route, Gate, "game.core.world.route.from", "{}"), new(Route, Gate, "game.core.world.route.to", "{}")]));
        Assert.Throws<InvalidOperationException>(() => AssertRouteLinks([new(Route, Root, "game.core.world.route.in-world", "{}"), new(Route, Gate, "game.core.world.route.from", "{}"), new(Route, Market, "game.core.world.route.to", "{\"blocked\":true}")]));
        Assert.Throws<InvalidOperationException>(() => AssertRouteLinks([new(Route, Root, "game.core.world.route.in-world", "{}"), new(Route, Gate, "game.core.world.route.from", "{}"), new(Route, Market, "game.core.world.route.to", "{}"), new(Route, Market, "game.core.world.route.to", "{}")]));
    }

    [Fact]
    public async Task One_active_route_moves_the_traveller_and_advances_its_root_clock_in_one_action()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync(TravelMechanic));
        var runner = Runner(db, world, mechanics);

        var result = await JourneyAsync(runner, Gate, Market);

        Assert.True(result.Ok, result.Error?.Why); Assert.Equal(TravelMechanic, result.Mechanic!.Id); Assert.Equal(2, result.AppliedCount);
        Assert.Equal(Market, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId);
        AssertClock((await world.GetEntityAsync(Root))!, 30, 1);
        var events = await new EventLedger(db).FindAsync(rootOperationId: result.OperationId);
        Assert.Equal(new[] { "world.containment.moved", "world.component.replaced", "game.core.world.clock.advanced" }, events.Select(e => e.TypeId));
        Assert.Equal(2, result.Output.Effects.Count);
    }

    [Fact]
    public async Task Reversed_stale_invalid_route_and_overflow_calls_leave_location_and_clock_unchanged()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = Runner(db, world, mechanics); var baseline = await JourneyStateAsync(world);

        var reversed = await JourneyAsync(runner, Market, Gate);
        Assert.False(reversed.Ok); Assert.Equal(baseline, await JourneyStateAsync(world));

        await world.SetComponentAsync(Route, RouteComponent, """{"status":"archived","summary":"A maintained one-way on-foot route from the fixture gate to the fixture market.","visibility":"party","mode":"on-foot","durationMinutes":30}""");
        var inactive = await JourneyAsync(runner, Gate, Market);
        Assert.False(inactive.Ok); Assert.Equal(baseline with { RouteData = (await world.GetEntityAsync(Route))!.Components.Single(c => c.DefinitionId == RouteComponent).Data }, await JourneyStateAsync(world));

        await world.SetComponentAsync(Route, RouteComponent, baseline.RouteData);
        await world.SetComponentAsync(Root, "game.core.world.clock", """{"calendarId":"lantern-compact-epoch","currentMinute":999999980,"revision":0}""");
        var overflow = await JourneyAsync(runner, Gate, Market);
        Assert.False(overflow.Ok); Assert.Equal(Gate, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); AssertClock((await world.GetEntityAsync(Root))!, 999999980, 0);

        await world.SetComponentAsync(Root, "game.core.world.clock", baseline.ClockData);
        var accepted = await JourneyAsync(runner, Gate, Market);
        Assert.True(accepted.Ok, accepted.Error?.Why);
        var stale = await JourneyAsync(runner, Gate, Market);
        Assert.False(stale.Ok); Assert.Equal(Market, (await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); AssertClock((await world.GetEntityAsync(Root))!, 30, 1);
    }

    private static void AssertRouteFixture(CatalogContents contents)
    {
        Assert.Contains(contents.Components, component => component.Id == RouteComponent && !string.IsNullOrWhiteSpace(component.Schema));
        var route = contents.Entities.Single(entity => entity.Id == Route);
        AssertRouteData(route.Components.Single(component => component.DefinitionId == RouteComponent).Data);
        AssertRouteLinks(contents.Relationships!.Relationships.Where(link => link.From == Route).Select(link => new Link(link.From, link.To, link.Kind, link.Data)));
        Assert.Contains(contents.Relationships.Relationships, link => link.From == Gate && link.To == Market && link.Kind == "game.core.world.location.connected-to" && link.Data == "{}");
    }

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static Task<ActionRunResult> JourneyAsync(ActionRunner runner, string origin, string destination) => runner.RunAsync(new ActionRequest { Intent = "take the named gate-to-market route", RoleEntityIds = new Dictionary<string, string> { ["traveller"] = "traveller.feature-02.fixture", ["origin"] = origin, ["destination"] = destination, ["route"] = Route, ["world"] = Root }, Input = "{}", Seed = 808 });
    private static async Task<JourneyState> JourneyStateAsync(WorldStore world) { var traveller = (await world.GetEntityAsync("traveller.feature-02.fixture"))!; var root = (await world.GetEntityAsync(Root))!; var route = (await world.GetEntityAsync(Route))!; return new(traveller.ContainerId, traveller.ContainerSlot, root.Components.Single(c => c.DefinitionId == "game.core.world.clock").Data, route.Components.Single(c => c.DefinitionId == RouteComponent).Data); }
    private static void AssertClock(EntitySnapshot root, long minute, long revision) { using var document = JsonDocument.Parse(root.Components.Single(c => c.DefinitionId == "game.core.world.clock").Data); Assert.Equal(minute, document.RootElement.GetProperty("currentMinute").GetInt64()); Assert.Equal(revision, document.RootElement.GetProperty("revision").GetInt64()); }

    private static void AssertRouteData(string json)
    {
        using var document = JsonDocument.Parse(json); var route = document.RootElement;
        if (route.ValueKind != JsonValueKind.Object || route.EnumerateObject().Count() != 5) throw new InvalidOperationException("Route data must be closed.");
        var status = Text(route, "status", 10); var summary = Text(route, "summary", 1000); var visibility = Text(route, "visibility", 10); var mode = Text(route, "mode", 10);
        if (status is not ("active" or "archived") || visibility is not ("public" or "party" or "gm") || mode != "on-foot" || summary != summary.Trim() || !route.TryGetProperty("durationMinutes", out var duration) || !duration.TryGetInt32(out var minutes) || minutes is < 1 or > 1440) throw new InvalidOperationException("Route data is invalid.");
    }

    private static void AssertRouteLinks(IEnumerable<Link> candidates)
    {
        var links = candidates.ToArray();
        if (links.Length != 3 || links.Any(link => link.From != Route || link.Data != "{}")) throw new InvalidOperationException("Route links must be three empty-data links from the route.");
        var world = links.SingleOrDefault(link => link.Kind == "game.core.world.route.in-world");
        var from = links.SingleOrDefault(link => link.Kind == "game.core.world.route.from");
        var to = links.SingleOrDefault(link => link.Kind == "game.core.world.route.to");
        if (world is null || from is null || to is null || world.To != Root || from.To != Gate || to.To != Market || from.To == to.To) throw new InvalidOperationException("Route scope or direction is invalid.");
    }

    private static string Text(JsonElement root, string name, int maximum) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString() == value.GetString()!.Trim() && value.GetString()!.Length <= maximum ? value.GetString()! : throw new InvalidOperationException($"{name} is invalid.");
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record JourneyState(string? TravellerContainer, string TravellerSlot, string ClockData, string RouteData);
}
