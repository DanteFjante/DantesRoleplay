using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Actions;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class WorldJourneyPlanTests : IDisposable
{
    private const string Root = "world.feature-01.fixture", Traveller = "traveller.feature-02.fixture", Gate = "location.feature-01.gate", Market = "location.feature-01.market", Observatory = "location.feature-01.observatory", Outpost = "location.feature-14.outpost", Unreachable = "location.feature-14.unreachable";
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-14-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Read_only_planner_returns_the_stable_three_leg_on_foot_itinerary()
    {
        var fixture = await FixtureAsync(); var world = fixture.World; var planner = fixture.Planner; var before = await StateAsync(world);
        var result = await planner.ReadAsync(new(Root, Traveller, Outpost));
        Assert.True(result.Ok, result.ErrorMessage); var plan = result.Projection!;
        Assert.Equal("ready", plan.Status); Assert.Equal(0, plan.ClockRevision); Assert.Equal(90, plan.TotalDurationMinutes);
        Assert.Equal(new[] { "route.feature-08.gate-to-market-on-foot", "route.feature-14.market-to-observatory", "route.feature-14.observatory-to-outpost" }, plan.Legs.Select(x => x.RouteId));
        Assert.Equal(before, await StateAsync(world));
    }

    [Fact]
    public async Task Planner_reports_already_there_unreachable_and_blocked_without_writing()
    {
        var fixture = await FixtureAsync(); var world = fixture.World; var planner = fixture.Planner;
        var already = await planner.ReadAsync(new(Root, Traveller, Gate)); Assert.True(already.Ok); Assert.Equal("already-there", already.Projection!.Status); Assert.Empty(already.Projection.Legs);
        var unreachable = await planner.ReadAsync(new(Root, Traveller, Unreachable)); Assert.True(unreachable.Ok); Assert.Equal("unreachable", unreachable.Projection!.Status); Assert.Empty(unreachable.Projection.Legs);
        await world.SetComponentAsync("route.feature-08.gate-to-market-on-foot", "game.core.world.route.availability", """{"status":"closed"}"""); var before = await StateAsync(world);
        var blocked = await planner.ReadAsync(new(Root, Traveller, Outpost)); Assert.True(blocked.Ok); Assert.Equal("blocked", blocked.Projection!.Status); Assert.Empty(blocked.Projection.Legs); Assert.Equal(before, await StateAsync(world));
    }

    [Fact]
    public async Task Continuation_executes_only_one_leg_then_freshly_replans_and_stops_when_closed()
    {
        var fixture = await FixtureAsync(); var world = fixture.World; var planner = fixture.Planner;
        var initial = (await planner.ReadAsync(new(Root, Traveller, Outpost))).Projection!; Assert.Equal("ready", initial.Status); Assert.Equal(3, initial.Legs.Count);
        var first = initial.Legs[0]; var runner = new ActionRunner(fixture.Db, fixture.Mechanics, new ProjectionResolver(fixture.Db), new JintMechanicEngine(), new EffectApplier(fixture.Db, world, null, new EventLedger(fixture.Db)), new OperationLog(fixture.Db), new MechanicComposer(fixture.Mechanics, new ProjectionResolver(fixture.Db), new JintMechanicEngine()));

        var travelled = await runner.RunAsync(new ActionRequest { Intent = "take the named gate-to-market route", RoleEntityIds = new Dictionary<string, string> { ["traveller"] = Traveller, ["origin"] = first.FromId, ["destination"] = first.ToId, ["route"] = first.RouteId, ["world"] = Root }, Input = "{}", Seed = 1414 });
        Assert.True(travelled.Ok, travelled.Error?.Why); Assert.Equal(Market, (await world.GetEntityAsync(Traveller))!.ContainerId);

        await world.SetComponentAsync("route.feature-14.market-to-observatory", "game.core.world.route.availability", """{"status":"closed"}""");
        var replan = await planner.ReadAsync(new(Root, Traveller, Outpost)); Assert.True(replan.Ok); Assert.Equal("blocked", replan.Projection!.Status); Assert.Empty(replan.Projection.Legs);
        var stale = await runner.RunAsync(new ActionRequest { Intent = "take the named gate-to-market route", RoleEntityIds = new Dictionary<string, string> { ["traveller"] = Traveller, ["origin"] = initial.Legs[1].FromId, ["destination"] = initial.Legs[1].ToId, ["route"] = initial.Legs[1].RouteId, ["world"] = Root }, Input = "{}", Seed = 1414 });
        Assert.False(stale.Ok); Assert.Equal(Market, (await world.GetEntityAsync(Traveller))!.ContainerId); Assert.Contains("\"currentMinute\":30", (await world.GetEntityAsync(Root))!.Components.Single(x => x.DefinitionId == "game.core.world.clock").Data);
    }

    private async Task<Fixture> FixtureAsync()
    {
        Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Fixture Outpost", Outpost); await world.SetComponentAsync(Outpost, "game.core.world.location", """{"kind":"site","status":"active","summary":"A distant fixture outpost.","visibility":"party"}"""); await world.MoveAsync(Outpost, "region.feature-01.fixture", "location");
        await world.CreateEntityAsync("Fixture Unreachable", Unreachable); await world.SetComponentAsync(Unreachable, "game.core.world.location", """{"kind":"site","status":"active","summary":"An unreachable fixture site.","visibility":"party"}"""); await world.MoveAsync(Unreachable, "region.feature-01.fixture", "location");
        await LinkAsync(world, Observatory, Outpost); await RouteAsync(world, "route.feature-14.market-to-observatory", Market, Observatory); await RouteAsync(world, "route.feature-14.observatory-to-outpost", Observatory, Outpost);
        return new(db, world, new MechanicStore(db), new JourneyPlanReader(world));
    }
    private static async Task LinkAsync(WorldStore world, string a, string b) { var from = string.CompareOrdinal(a, b) < 0 ? a : b; var to = from == a ? b : a; await world.RelateAsync(from, to, "game.core.world.location.connected-to", "{}"); }
    private static async Task RouteAsync(WorldStore world, string id, string from, string to) { await world.CreateEntityAsync(id, id); await world.SetComponentAsync(id, "game.core.world.route", """{"status":"active","summary":"A fixture on-foot route.","visibility":"party","mode":"on-foot","durationMinutes":30}"""); await world.SetComponentAsync(id, "game.core.world.route.availability", """{"status":"open"}"""); await world.RelateAsync(id, Root, "game.core.world.route.in-world", "{}"); await world.RelateAsync(id, from, "game.core.world.route.from", "{}"); await world.RelateAsync(id, to, "game.core.world.route.to", "{}"); }
    private static async Task<State> StateAsync(WorldStore world) { var traveller = (await world.GetEntityAsync(Traveller))!; var root = (await world.GetEntityAsync(Root))!; return new(traveller.ContainerId, traveller.ContainerSlot, root.Components.Single(x => x.DefinitionId == "game.core.world.clock").Data); }
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
    private sealed record State(string? ContainerId, string ContainerSlot, string Clock);
    private sealed record Fixture(DantesRoleplayDbContext Db, WorldStore World, MechanicStore Mechanics, JourneyPlanReader Planner);
}
