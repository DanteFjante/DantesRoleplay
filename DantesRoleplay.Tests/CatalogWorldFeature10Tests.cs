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
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Feature 10 Slice 1 fixes one scheduled route closure without adding its reaction or denial.</summary>
public sealed class CatalogWorldFeature10Tests : IDisposable
{
    private const string Condition = "condition.feature-10.gate-market-closure";
    private const string Route = "route.feature-08.gate-to-market-on-foot";
    private const string Root = "world.feature-01.fixture";
    private const string Gate = "location.feature-01.gate";
    private const string Market = "location.feature-01.market";
    private const string Traveller = "traveller.feature-02.fixture";
    private const string ConditionComponent = "game.core.world.condition";
    private const string AvailabilityComponent = "game.core.world.route.availability";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-10-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Fresh_import_has_one_scheduled_closure_with_exact_scope_and_open_route_availability()
    {
        Copy(Catalog(), _copy); var contents = await CatalogReader.ReadAsync(_copy); AssertFixture(contents);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.condition"));

        var condition = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(Condition));
        AssertCondition(Assert.Single(condition.Components, component => component.DefinitionId == ConditionComponent).Data, "scheduled", 60, 180);
        var route = Assert.IsType<EntitySnapshot>(await world.GetEntityAsync(Route));
        AssertAvailability(Assert.Single(route.Components, component => component.DefinitionId == AvailabilityComponent).Data, "open");
        AssertConditionLinks((await world.GetRelationshipsAsync(Condition, includeIncoming: false)).Select(link => new Link(link.FromEntityId, link.ToEntityId, link.Kind, link.Data)));
    }

    [Fact]
    public void Closed_condition_availability_interval_and_scope_conventions_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertCondition("{}", "scheduled", 60, 180));
        Assert.Throws<InvalidOperationException>(() => AssertCondition("""{"kind":"route-closure","status":"scheduled","summary":" closure","source":"notice","visibility":"party","startMinute":60,"endMinute":180}""", "scheduled", 60, 180));
        Assert.Throws<InvalidOperationException>(() => AssertCondition("""{"kind":"weather","status":"scheduled","summary":"closure","source":"notice","visibility":"party","startMinute":60,"endMinute":180}""", "scheduled", 60, 180));
        Assert.Throws<InvalidOperationException>(() => AssertCondition("""{"kind":"route-closure","status":"scheduled","summary":"closure","source":"notice","visibility":"party","startMinute":60,"endMinute":60}""", "scheduled", 60, 60));
        Assert.Throws<InvalidOperationException>(() => AssertCondition("""{"kind":"route-closure","status":"scheduled","summary":"closure","source":"notice","visibility":"party","startMinute":181,"endMinute":180}""", "scheduled", 181, 180));
        Assert.Throws<InvalidOperationException>(() => AssertCondition("""{"kind":"route-closure","status":"scheduled","summary":"closure","source":"notice","visibility":"party","startMinute":0,"endMinute":1000000001}""", "scheduled", 0, 1000000001));
        AssertCondition("""{"kind":"route-closure","status":"scheduled","summary":"closure","source":"notice","visibility":"party","startMinute":0,"endMinute":1}""", "scheduled", 0, 1);
        Assert.Throws<InvalidOperationException>(() => AssertAvailability("{}", "open"));
        Assert.Throws<InvalidOperationException>(() => AssertAvailability("""{"status":"blocked"}""", "open"));
        Assert.Throws<InvalidOperationException>(() => AssertConditionLinks([new(Condition, Root, "game.core.world.condition.in-world", "{}")]));
        Assert.Throws<InvalidOperationException>(() => AssertConditionLinks([new(Condition, Root, "game.core.world.condition.in-world", "{}"), new(Condition, Route, "game.core.world.condition.affects", "{}"), new(Condition, Route, "game.core.world.condition.affects", "{}")]));
        Assert.Throws<InvalidOperationException>(() => AssertConditionLinks([new(Condition, Root, "game.core.world.condition.in-world", "{}"), new(Condition, Condition, "game.core.world.condition.affects", "{}")]));
        Assert.Throws<InvalidOperationException>(() => AssertConditionLinks([new(Condition, Root, "game.core.world.condition.in-world", "{\"scope\":true}"), new(Condition, Route, "game.core.world.condition.affects", "{}")]));
        Assert.Throws<InvalidOperationException>(() => AssertConditionLinks([new(Route, Root, "game.core.world.condition.in-world", "{}"), new(Route, Condition, "game.core.world.condition.affects", "{}")]));
    }

    [Fact]
    public async Task Isolated_feature_8_journey_keeps_initial_condition_open_and_changes_no_unrelated_world_state()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var before = await StateAsync(world); var runner = Runner(db, world, mechanics);

        var result = await runner.RunAsync(new ActionRequest { Intent = "take the named gate-to-market route", RoleEntityIds = new Dictionary<string, string> { ["traveller"] = Traveller, ["origin"] = Gate, ["destination"] = Market, ["route"] = Route, ["world"] = Root }, Input = "{}", Seed = 1010 });

        Assert.True(result.Ok, result.Error?.Why); Assert.Equal(2, result.AppliedCount);
        var after = await StateAsync(world);
        Assert.Equal(Market, after.TravellerContainer); Assert.Equal("presence", after.TravellerSlot); AssertClock(after.Clock, 30, 1);
        Assert.Equal(before.Route, after.Route); Assert.Equal(before.Availability, after.Availability); Assert.Equal(before.Condition, after.Condition);
        Assert.Equal(before.GateAnchor, after.GateAnchor); Assert.Equal(before.GateEdges, after.GateEdges);
    }

    [Fact]
    public async Task Clock_boundaries_reconcile_once_deny_closed_travel_and_restore_the_route_after_expiry()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var subscription = await new SubscriptionStore(db).GetAsync("subscription.game.core.world.condition.sync-route-closure");
        Assert.NotNull(subscription); Assert.Equal("{\"condition\":\"condition.feature-10.gate-market-closure\",\"route\":\"route.feature-08.gate-to-market-on-foot\"}", subscription!.FixedRoleEntityIdsJson);
        var runner = ReactiveRunner(db, world, mechanics);

        var start = await ClockAsync(runner, 60);
        Assert.True(start.Ok, start.Error?.Why); AssertCondition(Component((await world.GetEntityAsync(Condition))!, ConditionComponent), "active", 60, 180); AssertAvailability(Component((await world.GetEntityAsync(Route))!, AvailabilityComponent), "closed");
        var startEvents = await new EventLedger(db).FindAsync(rootOperationId: start.OperationId);
        Assert.Equal(new[] { 0, 0, 1, 1 }, startEvents.Select(e => e.Depth)); Assert.Equal(2, Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync()).EffectCount);

        var blockedBefore = await StateAsync(world); var blocked = await JourneyAsync(runner); var blockedAfter = await StateAsync(world);
        Assert.False(blocked.Ok); Assert.Equal(blockedBefore.TravellerContainer, blockedAfter.TravellerContainer); Assert.Equal(blockedBefore.TravellerSlot, blockedAfter.TravellerSlot); Assert.Equal(blockedBefore.Clock, blockedAfter.Clock); Assert.Equal(blockedBefore.Route, blockedAfter.Route); Assert.Equal(blockedBefore.Availability, blockedAfter.Availability); Assert.Equal(blockedBefore.Condition, blockedAfter.Condition); Assert.Equal(blockedBefore.GateAnchor, blockedAfter.GateAnchor); Assert.Equal(blockedBefore.GateEdges, blockedAfter.GateEdges);

        var end = await ClockAsync(runner, 120);
        Assert.True(end.Ok, end.Error?.Why); AssertCondition(Component((await world.GetEntityAsync(Condition))!, ConditionComponent), "expired", 60, 180); AssertAvailability(Component((await world.GetEntityAsync(Route))!, AvailabilityComponent), "open");
        var reopened = await JourneyAsync(runner);
        Assert.True(reopened.Ok, reopened.Error?.Why); Assert.Equal(Market, (await world.GetEntityAsync(Traveller))!.ContainerId); AssertClock(Component((await world.GetEntityAsync(Root))!, "game.core.world.clock"), 210, 3);
        var executions = await db.EventExecutions.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { 0, 2, 2 }, executions.Select(execution => execution.EffectCount).OrderBy(count => count));
    }

    [Fact]
    public async Task Skipped_interval_and_administrative_correction_reconcile_from_the_resulting_clock_without_a_scheduler()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var runner = ReactiveRunner(db, world, mechanics);

        var skipped = await ClockAsync(runner, 180);
        Assert.True(skipped.Ok, skipped.Error?.Why); AssertCondition(Component((await world.GetEntityAsync(Condition))!, ConditionComponent), "expired", 60, 180); AssertAvailability(Component((await world.GetEntityAsync(Route))!, AvailabilityComponent), "open");

        var correction = await ReactiveApplier(db, world).ApplyAsync([new Effect { Type = EffectType.ComponentSet, EntityId = Root, DefinitionId = "game.core.world.clock", Data = """{"calendarId":"lantern-compact-epoch","currentMinute":60,"revision":2}""" }], rootOperationId: "feature-10-correction");
        Assert.True(correction.Applied); Assert.Equal(1, correction.Count); AssertClock(Component((await world.GetEntityAsync(Root))!, "game.core.world.clock"), 60, 2);
        AssertCondition(Component((await world.GetEntityAsync(Condition))!, ConditionComponent), "active", 60, 180); AssertAvailability(Component((await world.GetEntityAsync(Route))!, AvailabilityComponent), "closed");
        var correctionEvents = await new EventLedger(db).FindAsync(rootOperationId: "feature-10-correction");
        Assert.Equal(new[] { 0, 1, 1 }, correctionEvents.Select(e => e.Depth));
    }

    [Fact]
    public async Task Corrupt_fixed_condition_aborts_the_source_clock_action_without_partial_clock_or_reaction_state()
    {
        Copy(Catalog(), _copy); await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        var malformed = """{"kind":"route-closure","status":"scheduled","summary":"A scheduled closure will temporarily bar the maintained gate-to-market road.","source":"Reviewed gate maintenance notice.","visibility":"party","startMinute":180,"endMinute":60}""";
        await world.SetComponentAsync(Condition, ConditionComponent, malformed);

        var result = await ClockAsync(ReactiveRunner(db, world, mechanics), 1);

        Assert.False(result.Ok); AssertClock(Component((await world.GetEntityAsync(Root))!, "game.core.world.clock"), 0, 0); Assert.Equal(malformed, Component((await world.GetEntityAsync(Condition))!, ConditionComponent)); AssertAvailability(Component((await world.GetEntityAsync(Route))!, AvailabilityComponent), "open");
        Assert.Empty(await new EventLedger(db).FindAsync(rootOperationId: result.OperationId)); Assert.Empty(await db.EventExecutions.AsNoTracking().ToListAsync());
    }

    private static void AssertFixture(CatalogContents contents)
    {
        Assert.Contains(contents.Components, component => component.Id == ConditionComponent && !string.IsNullOrWhiteSpace(component.Schema));
        Assert.Contains(contents.Components, component => component.Id == AvailabilityComponent && !string.IsNullOrWhiteSpace(component.Schema));
        var condition = contents.Entities.Single(entity => entity.Id == Condition);
        AssertCondition(condition.Components.Single(component => component.DefinitionId == ConditionComponent).Data, "scheduled", 60, 180);
        var route = contents.Entities.Single(entity => entity.Id == Route);
        AssertAvailability(route.Components.Single(component => component.DefinitionId == AvailabilityComponent).Data, "open");
        AssertConditionLinks(contents.Relationships!.Relationships.Where(link => link.From == Condition).Select(link => new Link(link.From, link.To, link.Kind, link.Data)));
    }

    private static CatalogMechanicTestHarness Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static CatalogMechanicTestHarness ReactiveRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), ReactiveApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static EffectApplier ReactiveApplier(DantesRoleplayDbContext db, WorldStore world) =>
        new(db, world,
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db),
            new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)));
    private static Task<ActionRunResult> ClockAsync(CatalogMechanicTestHarness runner, int minutes) => runner.RunAsync(new ActionRequest { Intent = "advance world time", RoleEntityIds = new Dictionary<string, string> { ["world"] = Root }, Input = $"{{\"minutes\":{minutes}}}", Seed = 1011 });
    private static Task<ActionRunResult> JourneyAsync(CatalogMechanicTestHarness runner) => runner.RunAsync(new ActionRequest { Intent = "take the named gate-to-market route", RoleEntityIds = new Dictionary<string, string> { ["traveller"] = Traveller, ["origin"] = Gate, ["destination"] = Market, ["route"] = Route, ["world"] = Root }, Input = "{}", Seed = 1010 });

    private static async Task<State> StateAsync(WorldStore world)
    {
        var traveller = (await world.GetEntityAsync(Traveller))!; var route = (await world.GetEntityAsync(Route))!; var condition = (await world.GetEntityAsync(Condition))!; var root = (await world.GetEntityAsync(Root))!; var gate = (await world.GetEntityAsync(Gate))!;
        return new(traveller.ContainerId, traveller.ContainerSlot, Component(root, "game.core.world.clock"), Component(route, "game.core.world.route"), Component(route, AvailabilityComponent), Component(condition, ConditionComponent), Component(gate, "game.core.world.map.anchor"), (await world.GetRelationshipsAsync(Gate)).Select(edge => $"{edge.FromEntityId}|{edge.ToEntityId}|{edge.Kind}|{edge.Data}").OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string Component(EntitySnapshot entity, string id) => entity.Components.Single(component => component.DefinitionId == id).Data;
    private static void AssertClock(string json, long minute, long revision) { using var document = JsonDocument.Parse(json); var clock = document.RootElement; if (clock.GetProperty("currentMinute").GetInt64() != minute || clock.GetProperty("revision").GetInt64() != revision) throw new InvalidOperationException("Clock does not match expected journey result."); }
    private static void AssertCondition(string json, string expectedStatus, long expectedStart, long expectedEnd)
    {
        using var document = JsonDocument.Parse(json); var condition = document.RootElement;
        if (condition.ValueKind != JsonValueKind.Object || condition.EnumerateObject().Count() != 7) throw new InvalidOperationException("Condition data must be closed.");
        var kind = Text(condition, "kind", 20); var status = Text(condition, "status", 10); var summary = Text(condition, "summary", 1000); var source = Text(condition, "source", 500); var visibility = Text(condition, "visibility", 10);
        if (kind != "route-closure" || status is not ("scheduled" or "active" or "expired") || status != expectedStatus || summary != summary.Trim() || source != source.Trim() || visibility is not ("public" or "party" or "gm") || !Integer(condition, "startMinute", out var start) || !Integer(condition, "endMinute", out var end) || start is < 0 or > 1000000000 || end is < 1 or > 1000000000 || start >= end || start != expectedStart || end != expectedEnd) throw new InvalidOperationException("Condition data is invalid.");
    }
    private static void AssertAvailability(string json, string expectedStatus)
    {
        using var document = JsonDocument.Parse(json); var availability = document.RootElement;
        if (availability.ValueKind != JsonValueKind.Object || availability.EnumerateObject().Count() != 1 || !availability.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() is not ("open" or "closed") || status.GetString() != expectedStatus) throw new InvalidOperationException("Route availability is invalid.");
    }
    private static void AssertConditionLinks(IEnumerable<Link> candidates)
    {
        var links = candidates.ToArray();
        if (links.Length != 2 || links.Any(link => link.From != Condition || link.Data != "{}")) throw new InvalidOperationException("Condition links must be two empty-data links from the condition.");
        var world = links.SingleOrDefault(link => link.Kind == "game.core.world.condition.in-world"); var route = links.SingleOrDefault(link => link.Kind == "game.core.world.condition.affects");
        if (world is null || route is null || world.To != Root || route.To != Route || world.To == route.To) throw new InvalidOperationException("Condition scope is invalid.");
    }
    private static bool Integer(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.TryGetInt64(out value);
    }
    private static string Text(JsonElement root, string name, int maximum) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString() == value.GetString()!.Trim() && value.GetString()!.Length <= maximum ? value.GetString()! : throw new InvalidOperationException($"{name} is invalid.");
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        WorldFeatureFixture.RestoreRelationships(source, target);
    }
    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record State(string? TravellerContainer, string TravellerSlot, string Clock, string Route, string Availability, string Condition, string GateAnchor, string[] GateEdges);
}
