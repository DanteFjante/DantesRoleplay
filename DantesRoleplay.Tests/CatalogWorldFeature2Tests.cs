using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// World Feature 2 Slice 2 proves real action-runner movement over the catalog-owned Feature 1
/// graph. Movement is valid only when frozen relationship evidence supports it.
/// </summary>
public sealed class CatalogWorldFeature2Tests : IDisposable
{
    private const string Traveller = "traveller.feature-02.fixture";
    private const string Gate = "location.feature-01.gate";
    private const string Market = "location.feature-01.market";
    private const string Observatory = "location.feature-01.observatory";
    private const string TravellerComponent = "game.core.world.traveller";
    private const string MoveMechanic = "mechanic.game.core.world.location.move";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"world-feature-02-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_contains_the_active_traveller_and_adjacent_move_mechanic()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        var traveller = await world.GetEntityAsync(Traveller);
        Assert.NotNull(traveller);
        Assert.Equal(Gate, traveller!.ContainerId);
        Assert.Equal("presence", traveller.ContainerSlot);
        Assert.Equal("""{"status":"active"}""", Assert.Single(traveller.Components, c => c.DefinitionId == TravellerComponent).Data);

        var mechanic = await new MechanicStore(db).GetAsync(MoveMechanic);
        Assert.NotNull(mechanic);
        Assert.Equal(MechanicStatus.Active, mechanic!.Status);
        Assert.Contains("includeRelationships", mechanic.Requirements, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fresh_catalog_sessions_move_across_each_fixture_edge_deterministically_and_emit_the_structural_event()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        var first = await RunSessionAsync(_catalogCopy);
        var second = await RunSessionAsync(_catalogCopy);

        Assert.Equal(first.FirstOutput, second.FirstOutput);
        Assert.Equal(first.SecondOutput, second.SecondOutput);
        Assert.Equal(first.FirstEffects, second.FirstEffects);
        Assert.Equal(first.SecondEffects, second.SecondEffects);
        Assert.Equal(Market, first.AfterFirstContainer);
        Assert.Equal(Observatory, first.AfterSecondContainer);
        Assert.Equal("presence", first.AfterFirstSlot);
        Assert.Equal("presence", first.AfterSecondSlot);
        Assert.Equal("world.containment.moved", first.FirstEventType);
        Assert.Equal("world.containment.moved", first.SecondEventType);
    }

    [Fact]
    public async Task Disconnected_corrupt_and_stale_requests_leave_the_fixture_unchanged()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var baseline = await StateAsync(world);
        var disconnected = await RunAsync(runner, Gate, Observatory, "{}");
        Assert.False(disconnected.Ok);
        Assert.Equal(0, disconnected.AppliedCount);
        AssertStateEqual(baseline, await StateAsync(world));

        var invalidInput = await RunAsync(runner, Gate, Market, """{"route":"invented"}""");
        Assert.False(invalidInput.Ok);
        Assert.Equal(0, invalidInput.AppliedCount);
        AssertStateEqual(baseline, await StateAsync(world));

        await world.RelateAsync(Market, Gate, "game.core.world.location.connected-to", "{}");
        var duplicateEdge = await RunAsync(runner, Gate, Market, "{}");
        Assert.False(duplicateEdge.Ok);
        Assert.Equal(0, duplicateEdge.AppliedCount);
        var afterDuplicate = await StateAsync(world);
        Assert.Equal(Gate, afterDuplicate.TravellerContainer);
        Assert.Equal(baseline.TravellerComponent, afterDuplicate.TravellerComponent);

        await world.UnrelateAsync(Market, Gate, "game.core.world.location.connected-to");
        var accepted = await RunAsync(runner, Gate, Market, "{}");
        Assert.True(accepted.Ok, accepted.Error?.Why);
        Assert.Equal(1, accepted.AppliedCount);
        Assert.Equal(Market, (await world.GetEntityAsync(Traveller))!.ContainerId);

        var stale = await RunAsync(runner, Gate, Market, "{}");
        Assert.False(stale.Ok);
        Assert.Equal(0, stale.AppliedCount);
        Assert.Equal(Market, (await world.GetEntityAsync(Traveller))!.ContainerId);
    }

    [Fact]
    public async Task Missing_or_inactive_traveller_state_is_rejected_without_a_move()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = CreateRunner(db, world, mechanics);

        await world.SetComponentAsync(Traveller, TravellerComponent, """{"status":"inactive"}""");
        var result = await RunAsync(runner, Gate, Market, "{}");

        Assert.False(result.Ok);
        Assert.Equal(0, result.AppliedCount);
        var traveller = await world.GetEntityAsync(Traveller);
        Assert.Equal(Gate, traveller!.ContainerId);
        Assert.Equal("""{"status":"inactive"}""", Assert.Single(traveller.Components, c => c.DefinitionId == TravellerComponent).Data);
    }

    private static async Task<SessionTranscript> RunSessionAsync(string catalog)
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(catalog, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var first = await RunAsync(runner, Gate, Market, "{}");
        Assert.True(first.Ok, first.Error?.Why);
        Assert.Equal(MoveMechanic, first.Mechanic!.Id);
        Assert.Equal(1, first.AppliedCount);
        AssertMoveData(first.Output.Data, Gate, Market);
        var afterFirst = await world.GetEntityAsync(Traveller);
        var firstEvent = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: first.OperationId));
        Assert.Equal(first.OperationId, firstEvent.RootOperationId);

        var second = await RunAsync(runner, Market, Observatory, "{}");
        Assert.True(second.Ok, second.Error?.Why);
        Assert.Equal(1, second.AppliedCount);
        AssertMoveData(second.Output.Data, Market, Observatory);
        var afterSecond = await world.GetEntityAsync(Traveller);
        var secondEvent = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: second.OperationId));
        Assert.Equal(second.OperationId, secondEvent.RootOperationId);

        return new SessionTranscript(
            first.Output.Data,
            JsonSerializer.Serialize(first.Output.Effects),
            second.Output.Data,
            JsonSerializer.Serialize(second.Output.Effects),
            afterFirst!.ContainerId,
            afterFirst.ContainerSlot,
            afterSecond!.ContainerId,
            afterSecond.ContainerSlot,
            firstEvent.TypeId,
            secondEvent.TypeId);
    }

    private static async Task<ActionRunResult> RunAsync(ActionRunner runner, string origin, string destination, string input) =>
        await runner.RunAsync(new ActionRequest
        {
            Intent = "move to a connected location",
            RoleEntityIds = new Dictionary<string, string>
            {
                ["traveller"] = Traveller,
                ["origin"] = origin,
                ["destination"] = destination
            },
            Input = input,
            Seed = 903
        });

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world, null, new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static void AssertMoveData(string json, string origin, string destination)
    {
        using var data = JsonDocument.Parse(json);
        Assert.Equal("adjacent-world-movement", data.RootElement.GetProperty("test").GetString());
        Assert.Equal(Traveller, data.RootElement.GetProperty("travellerId").GetString());
        Assert.Equal(origin, data.RootElement.GetProperty("originId").GetString());
        Assert.Equal(destination, data.RootElement.GetProperty("destinationId").GetString());
        Assert.Equal("presence", data.RootElement.GetProperty("previousSlot").GetString());
        Assert.Equal("presence", data.RootElement.GetProperty("currentSlot").GetString());
        Assert.Equal("game.core.world.location.connected-to", data.RootElement.GetProperty("adjacencyKind").GetString());
    }

    private static async Task<FixtureState> StateAsync(WorldStore world)
    {
        var traveller = (await world.GetEntityAsync(Traveller))!;
        var edges = await world.GetRelationshipsAsync(Gate);
        return new FixtureState(
            traveller.ContainerId,
            traveller.ContainerSlot,
            Assert.Single(traveller.Components, c => c.DefinitionId == TravellerComponent).Data,
            edges.Select(edge => $"{edge.FromEntityId}|{edge.ToEntityId}|{edge.Kind}|{edge.Data}").OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static void AssertStateEqual(FixtureState expected, FixtureState actual)
    {
        Assert.Equal(expected.TravellerContainer, actual.TravellerContainer);
        Assert.Equal(expected.TravellerSlot, actual.TravellerSlot);
        Assert.Equal(expected.TravellerComponent, actual.TravellerComponent);
        Assert.Equal(expected.GateRelationships, actual.GateRelationships);
    }

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
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record FixtureState(string? TravellerContainer, string TravellerSlot, string TravellerComponent, string[] GateRelationships);
    private sealed record SessionTranscript(
        string FirstOutput,
        string FirstEffects,
        string SecondOutput,
        string SecondEffects,
        string? AfterFirstContainer,
        string AfterFirstSlot,
        string? AfterSecondContainer,
        string AfterSecondSlot,
        string FirstEventType,
        string SecondEventType);
}
