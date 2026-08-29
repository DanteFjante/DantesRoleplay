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

/// <summary>Feature 11 Slice 1 fixes one faction scope, territory controller, and observatory front.</summary>
public sealed class CatalogWorldFeature11Tests : IDisposable
{
    private const string Faction = "faction.feature-03.fixture";
    private const string Root = "world.feature-01.fixture";
    private const string Market = "location.feature-01.market";
    private const string Observatory = "location.feature-01.observatory";
    private const string Front = "front.feature-11.observatory-claim";
    private const string FrontComponent = "game.core.world.faction.front";
    private const string FactionScope = "game.core.world.faction.in-world";
    private const string Territory = "game.core.world.faction.territory-controls";
    private const string FrontScope = "game.core.world.faction.front.in-world";
    private const string FrontFaction = "game.core.world.faction.front.for-faction";
    private const string FrontContests = "game.core.world.faction.front.contests";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-11-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_has_the_confirmed_compact_scope_market_controller_and_quiet_observatory_front()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy); AssertFixture(contents);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.faction"));

        var faction = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(Faction));
        AssertFactionLinks((await world.GetRelationshipsAsync(Faction, includeIncoming: false)).Where(link => link.Kind is FactionScope or Territory).Select(ToLink));
        var front = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(Front));
        AssertFront(Component(front, FrontComponent), "quiet", 0); AssertFrontLinks((await world.GetRelationshipsAsync(Front, includeIncoming: false)).Select(ToLink));
        Assert.Equal("ready", Agenda(faction)); Assert.Equal(Root, (await world.GetEntityAsync(Root))!.Id);
    }

    [Fact]
    public void Closed_front_scope_and_territory_exclusivity_conventions_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertFront("{}", "quiet", 0));
        Assert.Throws<InvalidOperationException>(() => AssertFront("""{"status":"active","summary":" front","visibility":"gm","phase":"quiet","phaseStartedMinute":0}""", "quiet", 0));
        Assert.Throws<InvalidOperationException>(() => AssertFront("""{"status":"active","summary":"front","visibility":"gm","phase":"waiting","phaseStartedMinute":0}""", "quiet", 0));
        Assert.Throws<InvalidOperationException>(() => AssertFront("""{"status":"active","summary":"front","visibility":"gm","phase":"quiet","phaseStartedMinute":-1}""", "quiet", -1));
        Assert.Throws<InvalidOperationException>(() => AssertFactionLinks([new(Faction, Root, FactionScope, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertFactionLinks([new(Faction, Root, FactionScope, "{}"), new(Faction, Market, Territory, "{}"), new(Faction, Market, Territory, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertFactionLinks([new(Root, Faction, FactionScope, "{}"), new(Faction, Market, Territory, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertFactionLinks([new(Faction, Root, FactionScope, "{}"), new(Faction, Market, Territory, "{\"rank\":1}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertFrontLinks([new(Front, Root, FrontScope, "{}"), new(Front, Faction, FrontFaction, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertFrontLinks([new(Front, Root, FrontScope, "{}"), new(Front, Faction, FrontFaction, "{}"), new(Front, Front, FrontContests, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertTerritoryExclusivity([new(Faction, Market, Territory, "{}"), new("faction.feature-11.rival", Market, Territory, "{}") ]));
        AssertTerritoryExclusivity([new(Faction, Market, "game.core.world.faction.controls", "{}"), new(Faction, Market, Territory, "{}") ]);
    }

    [Fact]
    public async Task Replacing_the_front_changes_no_agenda_location_clock_route_condition_or_territory_state()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var before = await StateAsync(world);
        await world.SetComponentAsync(Front, FrontComponent, """{"status":"active","summary":"The Lantern Compact quietly seeks leverage over the sealed observatory.","visibility":"gm","phase":"rising","phaseStartedMinute":0}""");
        var after = await StateAsync(world);
        Assert.Equal("rising", Phase(after.Front)); Assert.Equal(before.Faction, after.Faction); Assert.Equal(before.Market, after.Market); Assert.Equal(before.Clock, after.Clock); Assert.Equal(before.Route, after.Route); Assert.Equal(before.Condition, after.Condition); Assert.Equal(before.FactionLinks, after.FactionLinks);
    }

    [Fact]
    public async Task Expected_phase_advances_one_front_with_current_clock_evidence_and_one_structural_event()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = Runner(db, world, mechanics);

        var quiet = await AdvanceAsync(runner, "quiet");
        Assert.True(quiet.Ok, quiet.Error?.Why); Assert.Equal(1, quiet.AppliedCount); AssertFront(Component((await world.GetEntityAsync(Front))!, FrontComponent), "rising", 0);
        var eventRow = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: quiet.OperationId)); Assert.Equal("world.component.replaced", eventRow.TypeId);
        await world.SetComponentAsync(Root, "game.core.world.clock", """{"calendarId":"lantern-compact-epoch","currentMinute":42,"revision":1}""");
        var rising = await AdvanceAsync(runner, "rising");
        Assert.True(rising.Ok, rising.Error?.Why); AssertFront(Component((await world.GetEntityAsync(Front))!, FrontComponent), "pressing", 42);
    }

    [Fact]
    public async Task Stale_terminal_invalid_input_and_role_mismatch_leave_front_and_world_state_unchanged()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = Runner(db, world, mechanics); var baseline = await StateAsync(world);
        foreach (var request in new[] { "{}", "{\"expectedPhase\":\"pressing\"}", "{\"expectedPhase\":\"quiet\",\"minute\":1}" })
        {
            var rejected = await runner.RunAsync(new ActionRequest { Intent = "advance the observatory front", RoleEntityIds = Roles(), Input = request, Seed = 1111 });
            Assert.False(rejected.Ok); Assert.Equal(baseline.Front, Component((await world.GetEntityAsync(Front))!, FrontComponent)); Assert.Equal(baseline.Clock, Component((await world.GetEntityAsync(Root))!, "game.core.world.clock"));
        }
        var wrongLocation = await runner.RunAsync(new ActionRequest { Intent = "advance the observatory front", RoleEntityIds = Roles(location: Market), Input = "{\"expectedPhase\":\"quiet\"}", Seed = 1111 });
        Assert.False(wrongLocation.Ok); Assert.Equal(baseline.Front, Component((await world.GetEntityAsync(Front))!, FrontComponent)); Assert.Equal(baseline.Faction, Component((await world.GetEntityAsync(Faction))!, "game.core.world.faction"));
        Assert.True((await AdvanceAsync(runner, "quiet")).Ok);
        var stale = await AdvanceAsync(runner, "quiet");
        Assert.False(stale.Ok); Assert.Equal("rising", Phase(Component((await world.GetEntityAsync(Front))!, FrontComponent)));
    }

    private static void AssertFixture(CatalogContents contents)
    {
        Assert.Contains(contents.Components, component => component.Id == FrontComponent && !string.IsNullOrWhiteSpace(component.Schema));
        var front = contents.Entities.Single(entity => entity.Id == Front); AssertFront(front.Components.Single(component => component.DefinitionId == FrontComponent).Data, "quiet", 0);
        AssertFactionLinks(contents.Relationships!.Relationships.Where(link => link.From == Faction && link.Kind is FactionScope or Territory).Select(ToLink));
        AssertFrontLinks(contents.Relationships.Relationships.Where(link => link.From == Front).Select(ToLink));
    }

    private static async Task<State> StateAsync(WorldStore world)
    {
        var faction = (await world.GetEntityAsync(Faction))!; var market = (await world.GetEntityAsync(Market))!; var root = (await world.GetEntityAsync(Root))!; var route = (await world.GetEntityAsync("route.feature-08.gate-to-market-on-foot"))!; var condition = (await world.GetEntityAsync("condition.feature-10.gate-market-closure"))!; var front = (await world.GetEntityAsync(Front))!;
        return new(Component(faction, "game.core.world.faction"), Component(market, "game.core.world.location"), Component(root, "game.core.world.clock"), Component(route, "game.core.world.route"), Component(condition, "game.core.world.condition"), Component(front, FrontComponent), (await world.GetRelationshipsAsync(Faction, includeIncoming: false)).Select(link => $"{link.FromEntityId}|{link.ToEntityId}|{link.Kind}|{link.Data}").OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static void AssertFront(string json, string expectedPhase, long expectedMinute)
    {
        using var document = JsonDocument.Parse(json); var front = document.RootElement;
        if (front.ValueKind != JsonValueKind.Object || front.EnumerateObject().Count() != 5) throw new InvalidOperationException("Front data must be closed.");
        var status = Text(front, "status", 10); var summary = Text(front, "summary", 1000); var visibility = Text(front, "visibility", 10); var phase = Text(front, "phase", 10);
        if (status is not ("active" or "resolved" or "archived") || summary != summary.Trim() || visibility is not ("public" or "party" or "gm") || phase is not ("quiet" or "rising" or "pressing") || phase != expectedPhase || !Integer(front, "phaseStartedMinute", out var minute) || minute is < 0 or > 1000000000 || minute != expectedMinute) throw new InvalidOperationException("Front data is invalid.");
    }
    private static void AssertFactionLinks(IEnumerable<Link> candidates)
    {
        var links = candidates.ToArray();
        if (links.Length != 2 || links.Any(link => link.From != Faction || link.Data != "{}") || links.Count(link => link.Kind == FactionScope && link.To == Root) != 1 || links.Count(link => link.Kind == Territory && link.To == Market) != 1) throw new InvalidOperationException("Faction scope or territory control is invalid.");
    }
    private static void AssertFrontLinks(IEnumerable<Link> candidates)
    {
        var links = candidates.ToArray();
        if (links.Length != 3 || links.Any(link => link.From != Front || link.Data != "{}") || links.Count(link => link.Kind == FrontScope && link.To == Root) != 1 || links.Count(link => link.Kind == FrontFaction && link.To == Faction) != 1 || links.Count(link => link.Kind == FrontContests && link.To == Observatory) != 1) throw new InvalidOperationException("Front scope is invalid.");
    }
    private static void AssertTerritoryExclusivity(IEnumerable<Link> candidates)
    {
        var controllers = candidates.Where(link => link.Kind == Territory && link.To == Market).ToArray();
        if (controllers.Length > 1 || controllers.Any(link => link.From == link.To || link.Data != "{}")) throw new InvalidOperationException("Territory controller must be exclusive.");
    }
    private static string Component(EntitySnapshot entity, string id) => entity.Components.Single(component => component.DefinitionId == id).Data;
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static Task<ActionRunResult> AdvanceAsync(ActionRunner runner, string expectedPhase) => runner.RunAsync(new ActionRequest { Intent = "advance the observatory front", RoleEntityIds = Roles(), Input = $"{{\"expectedPhase\":\"{expectedPhase}\"}}", Seed = 1111 });
    private static Dictionary<string, string> Roles(string? location = null) => new() { ["front"] = Front, ["faction"] = Faction, ["location"] = location ?? Observatory, ["world"] = Root };
    private static string Agenda(EntitySnapshot entity) { using var document = JsonDocument.Parse(Component(entity, "game.core.world.faction")); return document.RootElement.GetProperty("agenda").GetProperty("state").GetString()!; }
    private static string Phase(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.GetProperty("phase").GetString()!; }
    private static bool Integer(JsonElement root, string name, out long value) { value = 0; return root.TryGetProperty(name, out var element) && element.TryGetInt64(out value); }
    private static string Text(JsonElement root, string name, int maximum) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString() == value.GetString()!.Trim() && value.GetString()!.Length <= maximum ? value.GetString()! : throw new InvalidOperationException($"{name} is invalid.");
    private static Link ToLink(RelationshipEntry link) => new(link.From, link.To, link.Kind, link.Data);
    private static Link ToLink(RelationshipView link) => new(link.FromEntityId, link.ToEntityId, link.Kind, link.Data);
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record State(string Faction, string Market, string Clock, string Route, string Condition, string Front, string[] FactionLinks);
}
