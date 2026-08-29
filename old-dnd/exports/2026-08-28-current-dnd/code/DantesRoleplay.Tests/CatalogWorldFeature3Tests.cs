using System.Text;
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

/// <summary>
/// World Feature 3 Slice 1 proves that factions, recurring motives, and their authored
/// relationship conventions survive a fresh catalog import. Agenda advancement is deliberately a
/// later slice and is not exercised here.
/// </summary>
public sealed class CatalogWorldFeature3Tests : IDisposable
{
    private const string Faction = "faction.feature-03.fixture";
    private const string Mara = "actor.feature-03.mara-vell";
    private const string Oren = "actor.feature-03.oren-dale";
    private const string Market = "location.feature-01.market";
    private const string FactionComponent = "game.core.world.faction";
    private const string MotiveComponent = "game.core.world.motive";
    private const string Member = "game.core.world.faction.member";
    private const string Controls = "game.core.world.faction.controls";
    private const string AlliedWith = "game.core.world.faction.allied-with";
    private const string OpposedTo = "game.core.world.faction.opposed-to";
    private const string AgendaMechanic = "mechanic.game.core.world.faction.agenda";

    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"world-feature-03-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_contains_the_confirmed_faction_motive_fixture_and_preserves_topology()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        AssertFoundationContract(contents);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.True(imported.ManifestUpdated);
        var definitions = await world.GetDefinitionsAsync();
        Assert.Contains(definitions, definition => definition.Id == FactionComponent && definition.UsageCount == 1);
        Assert.Contains(definitions, definition => definition.Id == MotiveComponent && definition.UsageCount >= 2);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.faction"));

        var faction = Require(await world.GetEntityAsync(Faction));
        var mara = Require(await world.GetEntityAsync(Mara));
        var oren = Require(await world.GetEntityAsync(Oren));
        AssertFactionData(Component(faction, FactionComponent));
        AssertMotiveData(Component(mara, MotiveComponent));
        AssertMotiveData(Component(oren, MotiveComponent));
        Assert.Single(faction.Components);
        Assert.Single(mara.Components);
        Assert.Single(oren.Components);

        var factionLinks = await world.GetRelationshipsAsync(Faction);
        AssertFactionConventions(factionLinks.Where(link => link.Kind is Member or Controls or AlliedWith or OpposedTo).Select(ToLink), [Faction]);
        Assert.Contains(factionLinks, link => link.FromEntityId == Faction && link.ToEntityId == Mara && link.Kind == Member && link.Data == "{}");
        Assert.Contains(factionLinks, link => link.FromEntityId == Faction && link.ToEntityId == Market && link.Kind == Controls && link.Data == "{}");

        await AssertFeatureOneUnchangedAsync(world, contents);

        var cleanPlan = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .PlanAsync(_catalogCopy);
        Assert.True(cleanPlan.IsClean, string.Join(", ", cleanPlan.Entries.Where(entry => entry.Change != CatalogChange.Unchanged).Select(entry => entry.Id)));
    }

    [Fact]
    public void Closed_component_data_and_faction_link_conventions_reject_invalid_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertFactionData("""{"status":"active","summary":"x","visibility":"gm","goals":["g"],"methods":["m"],"assets":[],"agenda":{"state":"ready","summary":"a"},"members":[]}"""));
        Assert.Throws<InvalidOperationException>(() => AssertFactionData("""{"status":"active","summary":"x","visibility":"gm","goals":[" "],"methods":["m"],"assets":[],"agenda":{"state":"ready","summary":"a"}}"""));
        Assert.Throws<InvalidOperationException>(() => AssertFactionData("""{"status":"active","summary":"x","visibility":"gm","goals":["g","g"],"methods":["m"],"assets":[],"agenda":{"state":"ready","summary":"a"}}"""));
        Assert.Throws<InvalidOperationException>(() => AssertFactionData("""{"status":"active","summary":"x","visibility":"gm","goals":["g"],"methods":["m"],"assets":[],"agenda":{"state":"waiting","summary":"a"}}"""));
        Assert.Throws<InvalidOperationException>(() => AssertMotiveData("""{"status":"active","summary":"x","visibility":"gm","factionId":"not-here"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertMotiveData("""{"status":"active","summary":"   ","visibility":"gm"}"""));

        var factions = new[] { Faction, "faction.feature-03.rival" };
        AssertFactionConventions(
        [
            new(Faction, Mara, Member, "{}"),
            new("faction.feature-03.rival", Mara, Member, "{}"),
            new(Faction, Market, Controls, "{}"),
            new("faction.feature-03.rival", Market, Controls, "{}")
        ], factions);

        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions([new(Mara, Faction, Member, "{}")], factions));
        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions([new(Faction, Faction, Controls, "{}")], factions));
        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions([new(Faction, Mara, Member, "{\"rank\":1}")], factions));
        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions([new(Faction, Mara, Member, "{}"), new(Faction, Mara, Member, "{}")], factions));
        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions([new("faction.feature-03.rival", Faction, AlliedWith, "{}")], factions));
        Assert.Throws<InvalidOperationException>(() => AssertFactionConventions(
            [new(Faction, "faction.feature-03.rival", AlliedWith, "{}"), new(Faction, "faction.feature-03.rival", OpposedTo, "{}")], factions));
    }

    [Fact]
    public async Task Fresh_catalog_sessions_advance_the_ready_agenda_once_deterministically_and_emit_the_structural_event()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var first = await RunAgendaSessionAsync(_catalogCopy);
        var second = await RunAgendaSessionAsync(_catalogCopy);

        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Effects, second.Effects);
        Assert.Equal("world.component.replaced", first.EventType);
        Assert.Equal("ready", first.PreviousState);
        Assert.Equal("advanced", first.CurrentState);
        Assert.Equal(first.Before.Replace("\"state\":\"ready\"", "\"state\":\"advanced\"", StringComparison.Ordinal), first.After);
    }

    [Fact]
    public async Task Agenda_rejects_closed_input_and_invalid_or_stale_state_without_changing_unrelated_world_data()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var ready = Component(Require(await world.GetEntityAsync(Faction)), FactionComponent);
        var protectedState = await ProtectedStateAsync(world);

        var invalidInput = await RunAgendaAsync(runner, "{\"state\":\"advanced\"}");
        Assert.False(invalidInput.Ok);
        Assert.Equal(0, invalidInput.AppliedCount);
        Assert.Equal(ready, Component(Require(await world.GetEntityAsync(Faction)), FactionComponent));
        Assert.Equal(protectedState, await ProtectedStateAsync(world));

        foreach (var corrupt in new[]
                 {
                     ready.Replace("\"status\":\"active\"", "\"status\":\"draft\"", StringComparison.Ordinal),
                     ready.Replace("\"state\":\"ready\"", "\"state\":\"advanced\"", StringComparison.Ordinal),
                     "{}",
                     ready.Replace("\"state\":\"ready\"", "\"state\":\"waiting\"", StringComparison.Ordinal)
                 })
        {
            await world.SetComponentAsync(Faction, FactionComponent, corrupt);
            var rejected = await RunAgendaAsync(runner);
            Assert.False(rejected.Ok, corrupt);
            Assert.Equal(0, rejected.AppliedCount);
            Assert.Equal(corrupt, Component(Require(await world.GetEntityAsync(Faction)), FactionComponent));
            Assert.Equal(protectedState, await ProtectedStateAsync(world));
        }

        await world.SetComponentAsync(Faction, FactionComponent, ready);
        var accepted = await RunAgendaAsync(runner);
        Assert.True(accepted.Ok, accepted.Error?.Why);
        Assert.Equal(AgendaMechanic, accepted.Mechanic!.Id);
        Assert.Equal(1, accepted.AppliedCount);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(accepted.Output.Effects).Type);
        var advanced = Component(Require(await world.GetEntityAsync(Faction)), FactionComponent);
        Assert.Equal(ready.Replace("\"state\":\"ready\"", "\"state\":\"advanced\"", StringComparison.Ordinal), advanced);
        Assert.Equal(protectedState, await ProtectedStateAsync(world));
        var eventRow = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: accepted.OperationId));
        Assert.Equal("world.component.replaced", eventRow.TypeId);
        Assert.Equal(accepted.OperationId, eventRow.RootOperationId);

        var stale = await RunAgendaAsync(runner);
        Assert.False(stale.Ok);
        Assert.Equal(0, stale.AppliedCount);
        Assert.Equal(advanced, Component(Require(await world.GetEntityAsync(Faction)), FactionComponent));
        Assert.Equal(protectedState, await ProtectedStateAsync(world));
        Assert.Empty(await new EventLedger(db).FindAsync(rootOperationId: stale.OperationId));
    }

    private static void AssertFoundationContract(CatalogContents contents)
    {
        Assert.Contains(contents.Components, component => component.Id == FactionComponent && !string.IsNullOrWhiteSpace(component.Schema));
        Assert.Contains(contents.Components, component => component.Id == MotiveComponent && !string.IsNullOrWhiteSpace(component.Schema));

        var faction = contents.Entities.Single(entity => entity.Id == Faction);
        var mara = contents.Entities.Single(entity => entity.Id == Mara);
        var oren = contents.Entities.Single(entity => entity.Id == Oren);
        AssertFactionData(faction.Components.Single(component => component.DefinitionId == FactionComponent).Data);
        AssertMotiveData(mara.Components.Single(component => component.DefinitionId == MotiveComponent).Data);
        AssertMotiveData(oren.Components.Single(component => component.DefinitionId == MotiveComponent).Data);
        AssertFactionConventions(contents.Relationships!.Relationships.Where(link => link.Kind is Member or Controls or AlliedWith or OpposedTo).Select(ToLink), [Faction]);
    }

    private static async Task AssertFeatureOneUnchangedAsync(IWorldStore world, CatalogContents contents)
    {
        foreach (var id in new[] { "world.feature-01.fixture", "region.feature-01.fixture", "location.feature-01.gate", Market, "location.feature-01.observatory" })
        {
            var expected = contents.Entities.Single(entity => entity.Id == id);
            var actual = Require(await world.GetEntityAsync(id));
            Assert.Equal(expected.ContainerId, actual.ContainerId);
            Assert.Equal(expected.ContainerSlot, actual.ContainerSlot);
            foreach (var component in expected.Components)
                Assert.Equal(component.Data, Component(actual, component.DefinitionId));
        }

        var adjacency = (await world.GetRelationshipsAsync("location.feature-01.gate"))
            .Concat(await world.GetRelationshipsAsync(Market))
            .Where(link => link.Kind == "game.core.world.location.connected-to")
            .Select(ToLink)
            .Distinct()
            .OrderBy(link => link.From, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
        [
            new Link("location.feature-01.gate", Market, "game.core.world.location.connected-to", "{}"),
            new Link(Market, "location.feature-01.observatory", "game.core.world.location.connected-to", "{}")
        ], adjacency);
    }

    private static async Task<AgendaTranscript> RunAgendaSessionAsync(string catalog)
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(catalog, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var before = Component(Require(await world.GetEntityAsync(Faction)), FactionComponent);
        var result = await RunAgendaAsync(runner);
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal(AgendaMechanic, result.Mechanic!.Id);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(result.Output.Effects).Type);
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.Equal("faction-agenda-advance", data.RootElement.GetProperty("test").GetString());
        Assert.Equal(Faction, data.RootElement.GetProperty("factionId").GetString());
        Assert.Equal("ready", data.RootElement.GetProperty("previousState").GetString());
        Assert.Equal("advanced", data.RootElement.GetProperty("currentState").GetString());
        var after = Component(Require(await world.GetEntityAsync(Faction)), FactionComponent);
        var eventRow = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: result.OperationId));
        Assert.Equal(result.OperationId, eventRow.RootOperationId);

        return new AgendaTranscript(result.Output.Data, JsonSerializer.Serialize(result.Output.Effects), before, after,
            "ready", "advanced", eventRow.TypeId);
    }

    private static async Task<ActionRunResult> RunAgendaAsync(ActionRunner runner, string input = "{}") =>
        await runner.RunAsync(new ActionRequest
        {
            Intent = "advance faction agenda",
            RoleEntityIds = new Dictionary<string, string> { ["faction"] = Faction },
            Input = input,
            Seed = 1703
        });

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world, null, new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static async Task<ProtectedState> ProtectedStateAsync(IWorldStore world)
    {
        var mara = Require(await world.GetEntityAsync(Mara));
        var oren = Require(await world.GetEntityAsync(Oren));
        var market = Require(await world.GetEntityAsync(Market));
        var links = (await world.GetRelationshipsAsync(Faction))
            .Select(ToLink)
            .OrderBy(link => link.From, StringComparer.Ordinal)
            .ThenBy(link => link.To, StringComparer.Ordinal)
            .ThenBy(link => link.Kind, StringComparer.Ordinal)
            .Select(link => $"{link.From}|{link.To}|{link.Kind}|{link.Data}");
        return new ProtectedState(Component(mara, MotiveComponent), Component(oren, MotiveComponent),
            Component(market, "game.core.world.location"), string.Join("\n", links));
    }

    private static void AssertFactionData(string data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        RequireClosedObject(root, ["status", "summary", "visibility", "goals", "methods", "assets", "agenda"]);
        RequireEnum(root, "status", "draft", "active", "archived");
        RequireText(root, "summary", 1000);
        RequireEnum(root, "visibility", "public", "party", "gm");
        RequireTextList(root, "goals", 1, 5);
        RequireTextList(root, "methods", 1, 5);
        RequireTextList(root, "assets", 0, 10);
        var agenda = root.GetProperty("agenda");
        RequireClosedObject(agenda, ["state", "summary"]);
        RequireEnum(agenda, "state", "ready", "advanced");
        RequireText(agenda, "summary", 1000);
    }

    private static void AssertMotiveData(string data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        RequireClosedObject(root, ["status", "summary", "visibility"]);
        RequireEnum(root, "status", "draft", "active", "archived");
        RequireText(root, "summary", 1000);
        RequireEnum(root, "visibility", "public", "party", "gm");
    }

    private static void AssertFactionConventions(IEnumerable<Link> candidates, IReadOnlyCollection<string> factionIds)
    {
        var links = candidates.ToArray();
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var mutual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (!exact.Add($"{link.From}|{link.To}|{link.Kind}"))
                throw new InvalidOperationException("Faction links must not duplicate an identical directed kind.");
            if (link.Data != "{}") throw new InvalidOperationException("Faction link data must be exactly {}.");

            if (link.Kind is Member or Controls)
            {
                if (!factionIds.Contains(link.From, StringComparer.Ordinal) || link.From == link.To)
                    throw new InvalidOperationException("Membership/control links require a distinct faction source.");
                continue;
            }

            if (link.Kind is not (AlliedWith or OpposedTo)
                || !factionIds.Contains(link.From, StringComparer.Ordinal)
                || !factionIds.Contains(link.To, StringComparer.Ordinal)
                || string.CompareOrdinal(link.From, link.To) >= 0)
                throw new InvalidOperationException("Alliance/opposition links require canonical distinct faction endpoints.");

            var pair = $"{link.From}|{link.To}";
            if (mutual.TryGetValue(pair, out var existing) && existing != link.Kind)
                throw new InvalidOperationException("Alliance and opposition cannot coexist for one faction pair.");
            mutual[pair] = link.Kind;
        }
    }

    private static void RequireClosedObject(JsonElement value, IReadOnlyCollection<string> names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Count || names.Any(name => !value.TryGetProperty(name, out _)))
            throw new InvalidOperationException("Data must be a closed object with exactly the declared properties.");
    }

    private static void RequireEnum(JsonElement root, string name, params string[] accepted)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || !accepted.Contains(value.GetString(), StringComparer.Ordinal))
            throw new InvalidOperationException($"{name} is invalid.");
    }

    private static void RequireText(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidOperationException($"{name} must be text.");
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.EnumerateRunes().Count() > maximum)
            throw new InvalidOperationException($"{name} must be trimmed, nonempty bounded text.");
    }

    private static void RequireTextList(JsonElement root, string name, int minimum, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"{name} must be an array.");
        var values = value.EnumerateArray().ToArray();
        if (values.Length < minimum || values.Length > maximum) throw new InvalidOperationException($"{name} has the wrong count.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            if (item.ValueKind != JsonValueKind.String) throw new InvalidOperationException($"{name} must hold text.");
            var text = item.GetString()!;
            if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.EnumerateRunes().Count() > 500 || !seen.Add(text))
                throw new InvalidOperationException($"{name} contains invalid or duplicate text.");
        }
    }

    private static string Component(EntitySnapshot entity, string definitionId) =>
        entity.Components.Single(component => component.DefinitionId == definitionId).Data;

    private static EntitySnapshot Require(EntitySnapshot? entity) => Assert.IsType<EntitySnapshot>(entity);

    private static Link ToLink(RelationshipEntry link) => new(link.From, link.To, link.Kind, link.Data);
    private static Link ToLink(RelationshipView link) => new(link.FromEntityId, link.ToEntityId, link.Kind, link.Data);

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var catalog = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(catalog)) return Path.GetDirectoryName(catalog)!;
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record ProtectedState(string MaraMotive, string OrenMotive, string MarketLocation, string FactionLinks);
    private sealed record AgendaTranscript(string Output, string Effects, string Before, string After, string PreviousState, string CurrentState, string EventType);
}
