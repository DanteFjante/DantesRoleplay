using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Actions;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class WorldModeAwareItineraryTests : IDisposable
{
    private const string Root = "world.feature-01.fixture", Traveller = "traveller.feature-02.fixture", Gate = "location.feature-01.gate", Observatory = "location.feature-01.observatory", Portal = "teleport-gate.feature-15.gate-to-observatory", Dragon = "mount.feature-13.dragon";
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-16-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Planner_prefers_fixed_portal_without_writing_and_fingerprints_the_result()
    {
        var world = await ImportAsync(); var before = await StateAsync(world); var result = await new ModeAwareItineraryReader(world).ReadAsync(new(Root, Traveller, Observatory));
        Assert.True(result.Ok, result.ErrorMessage); var plan = result.Projection!;
        Assert.Equal("ready", plan.Status); Assert.Equal(0, plan.EstimatedTotalMinutes); Assert.NotNull(plan.ItineraryFingerprint); var leg = Assert.Single(plan.Legs);
        Assert.Equal((0, "portal", Gate, Observatory, Portal, 0), (leg.Index, leg.Mode, leg.FromLocationId, leg.ToLocationId, leg.RouteOrPortalId, leg.EstimatedMinutes)); Assert.Null(leg.ConveyanceId);
        Assert.Equal(before, await StateAsync(world));
    }

    [Fact]
    public async Task Planner_uses_only_a_valid_selected_co_located_conveyance()
    {
        var world = await ImportAsync(); await world.SetComponentAsync(Portal, "game.core.world.teleport-gate", """{"kind":"fixed-portal","status":"disabled","summary":"A disabled fixture gate.","visibility":"party"}""");
        var reader = new ModeAwareItineraryReader(world); var unavailable = await reader.ReadAsync(new(Root, Traveller, Observatory, AerialConveyanceId: "missing.conveyance"));
        Assert.True(unavailable.Ok); Assert.Equal("unavailable-resource", unavailable.Projection!.Status);
        var result = await reader.ReadAsync(new(Root, Traveller, Observatory, AerialConveyanceId: Dragon));
        Assert.True(result.Ok, result.ErrorMessage); var leg = Assert.Single(result.Projection!.Legs);
        Assert.Equal(("air", Dragon, 20), (leg.Mode, leg.ConveyanceId, leg.EstimatedMinutes));
    }

    [Fact]
    public async Task Advance_rejects_stale_plan_executes_one_owner_leg_and_replans()
    {
        var world = await ImportAsync(); var db = _fixture.CreateContext(); var planner = new ModeAwareItineraryReader(world);
        var plan = (await planner.ReadAsync(new(Root, Traveller, Observatory))).Projection!; var log = new OperationLog(db);
        var runner = new ActionRunner(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), log, new MechanicComposer(new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine()));
        var tool = new ItineraryAdvanceTools(); var stale = await tool.AdvanceAsync(planner, runner, log, new(Root, Traveller, Observatory, "wrong", 0), ["procedure.game.core.world.itinerary"]);
        Assert.False(stale.Ok); Assert.Equal("STALE_ITINERARY", stale.Error?.Code); Assert.Equal(Gate, (await world.GetEntityAsync(Traveller))!.ContainerId);
        var result = await tool.AdvanceAsync(planner, runner, log, new(Root, Traveller, Observatory, plan.ItineraryFingerprint!, 0), ["procedure.game.core.world.itinerary"]);
        Assert.True(result.Ok, result.Error?.Why); Assert.Equal(Observatory, (await world.GetEntityAsync(Traveller))!.ContainerId); Assert.Equal("{\"calendarId\":\"lantern-compact-epoch\",\"currentMinute\":0,\"revision\":0}", (await world.GetEntityAsync(Root))!.Components.Single(x => x.DefinitionId == "game.core.world.clock").Data);
    }

    private async Task<WorldStore> ImportAsync()
    {
        Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        return world;
    }
    private static async Task<(string? Location, string Clock)> StateAsync(WorldStore world) { var traveller = (await world.GetEntityAsync(Traveller))!; var root = (await world.GetEntityAsync(Root))!; return (traveller.ContainerId, root.Components.Single(x => x.DefinitionId == "game.core.world.clock").Data); }
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
}
