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

public sealed class CatalogWorldFeature15Tests : IDisposable
{
    private const string Portal = "teleport-gate.feature-15.gate-to-observatory", Root = "world.feature-01.fixture", Gate = "location.feature-01.gate", Observatory = "location.feature-01.observatory", ComponentId = "game.core.world.teleport-gate";
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-15-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_has_one_way_fixed_portal_with_exact_scope_and_destination()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy); AssertFixture(contents);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var portal = (await world.GetEntityAsync(Portal))!; Assert.Equal(Gate, portal.ContainerId); Assert.Equal("presence", portal.ContainerSlot); AssertPortal(Component(portal, ComponentId)); AssertLinks(await world.GetRelationshipsAsync(Portal, includeIncoming: false));
    }

    [Fact]
    public void Closed_portal_state_and_links_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertPortal("{}")); Assert.Throws<InvalidOperationException>(() => AssertPortal("""{"kind":"fixed-portal","status":"active","summary":" portal","visibility":"party"}""")); Assert.Throws<InvalidOperationException>(() => AssertPortal("""{"kind":"network","status":"active","summary":"portal","visibility":"party"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Portal, Root, "game.core.world.teleport-gate.in-world", "{}")])); Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Portal, Root, "game.core.world.teleport-gate.in-world", "{}"), new(Portal, Gate, "game.core.world.teleport-gate.to", "{}")])); Assert.Throws<InvalidOperationException>(() => AssertLinks([new(Portal, Root, "game.core.world.teleport-gate.in-world", "{}"), new(Portal, Observatory, "game.core.world.teleport-gate.to", "{\"key\":1}")]));
    }

    [Fact]
    public async Task Replacing_portal_state_does_not_change_clock_routes_or_traveller()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var traveller = (await world.GetEntityAsync("traveller.feature-02.fixture"))!; var root = (await world.GetEntityAsync(Root))!; var route = (await world.GetEntityAsync("route.feature-08.gate-to-market-on-foot"))!;
        await world.SetComponentAsync(Portal, ComponentId, """{"kind":"fixed-portal","status":"disabled","summary":"A reviewed one-way portal from the fixture gate to the fixture observatory.","visibility":"party"}""");
        Assert.Equal(Gate, traveller.ContainerId); Assert.Equal("{\"calendarId\":\"lantern-compact-epoch\",\"currentMinute\":0,\"revision\":0}", Component(root, "game.core.world.clock")); Assert.Equal("on-foot", JsonDocument.Parse(Component(route, "game.core.world.route")).RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Active_portal_moves_only_the_co_located_traveller_and_never_changes_clock()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db); Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = new CatalogMechanicTestHarness(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var result = await runner.RunAsync(new ActionRequest { Intent = "cross the gate to observatory portal", RoleEntityIds = new Dictionary<string,string> { ["traveller"]="traveller.feature-02.fixture", ["portal"]=Portal, ["origin"]=Gate, ["destination"]=Observatory, ["world"]=Root }, Input="{}", Seed=1515 });
        Assert.True(result.Ok,result.Error?.Why); Assert.Equal(1,result.AppliedCount); Assert.Single(result.Output.Effects); Assert.Equal(Observatory,(await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId); Assert.Equal(Gate,(await world.GetEntityAsync(Portal))!.ContainerId); Assert.Equal("{\"calendarId\":\"lantern-compact-epoch\",\"currentMinute\":0,\"revision\":0}",Component((await world.GetEntityAsync(Root))!,"game.core.world.clock"));
        var stale=await runner.RunAsync(new ActionRequest { Intent="cross the gate to observatory portal", RoleEntityIds=new Dictionary<string,string>{{"traveller","traveller.feature-02.fixture"},{"portal",Portal},{"origin",Gate},{"destination",Observatory},{"world",Root}},Input="{}",Seed=1515 }); Assert.False(stale.Ok); Assert.Equal(Observatory,(await world.GetEntityAsync("traveller.feature-02.fixture"))!.ContainerId);
    }

    private static void AssertFixture(CatalogContents c) { Assert.Contains(c.Components, x => x.Id == ComponentId && !string.IsNullOrWhiteSpace(x.Schema)); var p = c.Entities.Single(x => x.Id == Portal); Assert.Equal(Gate, p.ContainerId); AssertPortal(p.Components.Single(x => x.DefinitionId == ComponentId).Data); AssertLinks(c.Relationships!.Relationships.Where(x => x.From == Portal).Select(x => new RelationshipView(x.From, x.To, x.Kind, x.Data))); }
    private static void AssertPortal(string json) { using var d = JsonDocument.Parse(json); var x = d.RootElement; if (x.ValueKind != JsonValueKind.Object || x.EnumerateObject().Count() != 4 || Text(x, "kind", 20) != "fixed-portal" || Text(x, "status", 10) is not ("active" or "disabled" or "archived") || Text(x, "visibility", 10) is not ("public" or "party" or "gm")) throw new InvalidOperationException("Portal is invalid."); Text(x, "summary", 1000); }
    private static void AssertLinks(IEnumerable<RelationshipView> edges) { var list = edges.ToArray(); if (list.Length != 2 || list.Any(x => x.FromEntityId != Portal || x.Data != "{}") || list.Count(x => x.Kind == "game.core.world.teleport-gate.in-world" && x.ToEntityId == Root) != 1 || list.Count(x => x.Kind == "game.core.world.teleport-gate.to" && x.ToEntityId == Observatory) != 1) throw new InvalidOperationException("Portal links are invalid."); }
    private static string Text(JsonElement x, string name, int max) => x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) && v.GetString() == v.GetString()!.Trim() && v.GetString()!.Length <= max ? v.GetString()! : throw new InvalidOperationException($"{name} invalid.");
    private static string Component(EntitySnapshot e, string id) => e.Components.Single(x => x.DefinitionId == id).Data;
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string s, string t) { Directory.CreateDirectory(t); foreach (var d in Directory.EnumerateDirectories(s, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(t, Path.GetRelativePath(s, d))); foreach (var f in Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(t, Path.GetRelativePath(s, f))); }
}
