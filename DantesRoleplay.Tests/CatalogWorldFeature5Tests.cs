using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogWorldFeature5Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-05-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_places_one_closed_initial_clock_only_on_the_world_root()
    {
        Copy(RepositoryCatalog(), _copy);
        var contents = await CatalogReader.ReadAsync(_copy);
        var rootFile = contents.Entities.Single(e => e.Id == "world.feature-01.fixture");
        AssertClock(rootFile.Components.Single(c => c.DefinitionId == "game.core.world.clock").Data);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions());
        Assert.False(imported.Aborted); Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.time"));
        var root = (await world.GetEntityAsync("world.feature-01.fixture"))!;
        AssertClock(root.Components.Single(c => c.DefinitionId == "game.core.world.clock").Data);
        foreach (var id in new[] { "region.feature-01.fixture", "location.feature-01.gate", "traveller.feature-02.fixture", "faction.feature-03.fixture", "fact.feature-04.toll-ledger" })
            Assert.DoesNotContain((await world.GetEntityAsync(id))!.Components, c => c.DefinitionId == "game.core.world.clock");
    }

    [Fact]
    public void Closed_clock_contract_rejects_wrong_shape_and_bounds()
    {
        Assert.Throws<InvalidOperationException>(() => AssertClock("{}"));
        Assert.Throws<InvalidOperationException>(() => AssertClock("""{"calendarId":" x","currentMinute":0,"revision":0}"""));
        Assert.Throws<InvalidOperationException>(() => AssertClock("""{"calendarId":"x","currentMinute":-1,"revision":0}"""));
        Assert.Throws<InvalidOperationException>(() => AssertClock("""{"calendarId":"x","currentMinute":0,"revision":2147483648}"""));
        Assert.Throws<InvalidOperationException>(() => AssertClock("""{"calendarId":"x","currentMinute":0,"revision":0,"date":"bad"}"""));
    }

    [Fact]
    public async Task Clock_advance_is_bounded_replayable_and_correlated_to_one_structural_event()
    {
        Copy(RepositoryCatalog(), _copy); await using var db=_fixture.CreateContext(); var world=new WorldStore(db); var mechanics=new MechanicStore(db);
        Assert.False((await new CatalogImporter(db,mechanics,new ProcedureStore(db),world,new EventTypeStore(db)).ApplyAsync(_copy,new CatalogImportOptions())).Aborted);
        var runner=new ActionRunner(db,mechanics,new ProjectionResolver(db),new JintMechanicEngine(),new EffectApplier(db,world,null,new EventLedger(db)),new OperationLog(db),new MechanicComposer(mechanics,new ProjectionResolver(db),new JintMechanicEngine()));
        var result=await Run(runner,"{\"minutes\":60}"); Assert.True(result.Ok,result.Error?.Why); Assert.Equal(1,result.AppliedCount);
        AssertClockEquals((await world.GetEntityAsync("world.feature-01.fixture"))!,60,1);
        var ledger = new EventLedger(db); var events = await ledger.FindAsync(rootOperationId: result.OperationId);
        Assert.Equal(new[] { "world.component.replaced", "game.core.world.clock.advanced" }, events.Select(e => e.TypeId));
        var advanced = (await ledger.GetAsync(events[1].Id))!;
        Assert.Equal("world.feature-01.fixture", advanced.Scope); Assert.Equal(new[] { "world.feature-01.fixture" }, advanced.EntityIds);
        using (var payload = JsonDocument.Parse(advanced.PayloadJson))
        {
            var root = payload.RootElement;
            Assert.Equal("world.feature-01.fixture", root.GetProperty("worldId").GetString()); Assert.Equal("lantern-compact-epoch", root.GetProperty("calendarId").GetString());
            Assert.Equal(0, root.GetProperty("beforeMinute").GetInt64()); Assert.Equal(60, root.GetProperty("afterMinute").GetInt64());
            Assert.Equal(0, root.GetProperty("beforeRevision").GetInt64()); Assert.Equal(1, root.GetProperty("afterRevision").GetInt64());
        }
        foreach(var bad in new[]{"{}","{\"minutes\":0}","{\"minutes\":1441}","{\"minutes\":60,\"reason\":\"x\"}"}){var fail=await Run(runner,bad);Assert.False(fail.Ok);AssertClockEquals((await world.GetEntityAsync("world.feature-01.fixture"))!,60,1);}
        await world.SetComponentAsync("world.feature-01.fixture","game.core.world.clock","""{"calendarId":"lantern-compact-epoch","currentMinute":1000000000,"revision":1}""");
        Assert.False((await Run(runner,"{\"minutes\":1}")).Ok); AssertClockEquals((await world.GetEntityAsync("world.feature-01.fixture"))!,1000000000,1);
    }

    private static async Task<ActionRunResult> Run(ActionRunner runner,string input)=>await runner.RunAsync(new ActionRequest{Intent="advance world time",RoleEntityIds=new Dictionary<string,string>{{"world","world.feature-01.fixture"}},Input=input,Seed=505});
    private static void AssertClockEquals(EntitySnapshot root,long minute,long revision){using var d=JsonDocument.Parse(root.Components.Single(c=>c.DefinitionId=="game.core.world.clock").Data);Assert.Equal(minute,d.RootElement.GetProperty("currentMinute").GetInt64());Assert.Equal(revision,d.RootElement.GetProperty("revision").GetInt64());}

    private static void AssertClock(string json)
    {
        using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || r.EnumerateObject().Count() != 3 || !r.TryGetProperty("calendarId", out var id) || !r.TryGetProperty("currentMinute", out var minute) || !r.TryGetProperty("revision", out var revision) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString() != id.GetString()!.Trim() || id.GetString()!.Length > 100 || !minute.TryGetInt64(out var m) || m is < 0 or > 1000000000 || !revision.TryGetInt64(out var v) || v is < 0 or > 2147483647) throw new InvalidOperationException("Invalid closed clock state.");
    }
    private static string RepositoryCatalog() { for (var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent) if(File.Exists(Path.Combine(d.FullName,"DantesRoleplay.slnx"))) return Path.Combine(d.FullName,"catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source,string target) { Directory.CreateDirectory(target); foreach(var d in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target,Path.GetRelativePath(source,d))); foreach(var f in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) File.Copy(f,Path.Combine(target,Path.GetRelativePath(source,f))); }
}
