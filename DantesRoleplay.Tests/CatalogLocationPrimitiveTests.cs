using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogLocationPrimitiveTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Parent = "region.feature-01.fixture";
    private const string ExistingLocation = "location.feature-01.gate";
    private const string Location = "location.feature-01.primitive-workshop";
    private const string Furnishing = "furnishing.primitive-workshop-table";
    private const string Knowledge = "fact.feature-04.primitive-workshop-charter";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(
        Path.GetTempPath(), $"location-primitives-{Guid.NewGuid():n}");

    [Fact]
    public async Task Focused_primitives_commit_authored_state_without_inventing_world_details()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(
                db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = Runner(db, world, mechanics);
        var primitiveIds = new[]
        {
            "mechanic.game.core.world.location.shell-create",
            "mechanic.game.core.world.location.place",
            "mechanic.game.core.world.location.furnishing-create",
            "mechanic.game.core.world.location.furnishing-attach",
            "mechanic.game.core.world.location.connection-add",
            "mechanic.game.core.world.location.knowledge-attach",
            "mechanic.game.core.world.location.media-attach"
        };
        var declaredComponentOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var primitiveId in primitiveIds)
        {
            var mechanic = await mechanics.GetAsync(primitiveId);
            Assert.NotNull(mechanic);
            var requirements = MechanicRequirements.Parse(mechanic.Requirements);
            Assert.NotNull(requirements.InputSchema);
            Assert.Equal(JsonValueKind.Object, requirements.InputSchema.Value.ValueKind);
            foreach (var componentId in requirements.EffectComponentIds)
                Assert.True(declaredComponentOwners.Add(componentId),
                    $"Primitive component ownership overlaps at {componentId}.");
        }

        var shell = await RunAsync(runner, "create an empty location shell", [], new
        {
            locationId = Location,
            name = "Fixture Workshop",
            kind = "interior",
            status = "active",
            summary = "A reviewed workshop used to prove primitive location authoring.",
            visibility = "gm"
        });
        AssertSuccess(shell, "mechanic.game.core.world.location.shell-create", 2);

        var placement = await RunAsync(runner, "place an existing location under its parent",
            new() { ["location"] = Location, ["parent"] = Parent }, new { });
        AssertSuccess(placement, "mechanic.game.core.world.location.place", 1);

        var createFurnishing = await RunAsync(runner, "create an unplaced location furnishing", [], new
        {
            furnishingId = Furnishing,
            name = "Copper drafting table",
            status = "active",
            summary = "A scarred copper table sized for architectural plans.",
            visibility = "party"
        });
        AssertSuccess(createFurnishing, "mechanic.game.core.world.location.furnishing-create", 2);

        var attachFurnishing = await RunAsync(runner, "attach an existing furnishing to a location",
            new() { ["location"] = Location, ["furnishing"] = Furnishing }, new { });
        AssertSuccess(attachFurnishing, "mechanic.game.core.world.location.furnishing-attach", 1);

        var connection = await RunAsync(runner, "connect two existing locations as adjacent",
            new() { ["left"] = Location, ["right"] = ExistingLocation }, new { });
        AssertSuccess(connection, "mechanic.game.core.world.location.connection-add", 1);

        await world.CreateEntityAsync("Workshop charter", Knowledge);
        await world.SetComponentAsync(Knowledge, "game.core.world.fact", JsonSerializer.Serialize(new
        {
            status = "active",
            summary = "The workshop operates under a reviewed regional charter.",
            provenance = "Location primitive acceptance fixture.",
            visibility = "party"
        }));
        await world.SetComponentAsync(Knowledge, "game.core.world.knowledge.classification",
            "{\"subjectKind\":\"location\",\"sensitivity\":\"open\"}");
        await world.RelateAsync(Knowledge, World, "game.core.world.knowledge.in-world", "{}");
        var knowledge = await RunAsync(runner, "attach existing world knowledge to a location",
            new() { ["world"] = World, ["location"] = Location, ["knowledge"] = Knowledge }, new { });
        AssertSuccess(knowledge, "mechanic.game.core.world.location.knowledge-attach", 1);

        var media = await RunAsync(runner, "attach finalized visual media to a location",
            new() { ["location"] = Location }, new
            {
                attachments = new[]
                {
                    new
                    {
                        role = "setting",
                        visibility = new[] { "player", "dm" },
                        sha256 = new string('a', 64),
                        mimeType = "image/png",
                        width = 1200,
                        height = 675,
                        alt = "The copper drafting table inside the workshop.",
                        caption = "Fixture workshop",
                        order = 0,
                        provenance = new
                        {
                            kind = "original",
                            credit = "Acceptance fixture",
                            source = "Finalized test asset receipt",
                            reviewedOn = "2026-09-03",
                            version = 1
                        }
                    }
                }
            });
        AssertSuccess(media, "mechanic.game.core.world.location.media-attach", 1);

        var location = await world.GetEntityAsync(Location);
        Assert.NotNull(location);
        Assert.Equal(Parent, location.ContainerId);
        Assert.Equal("location", location.ContainerSlot);
        Assert.Equal("Fixture Workshop", location.Name);
        Assert.Contains("reviewed workshop", Assert.Single(location.Components,
            value => value.DefinitionId == "game.core.world.location").Data);
        Assert.Contains("Finalized test asset receipt", Assert.Single(location.Components,
            value => value.DefinitionId == "game.core.media.visual").Data);

        var furnishing = await world.GetEntityAsync(Furnishing);
        Assert.NotNull(furnishing);
        Assert.Equal(Location, furnishing.ContainerId);
        Assert.Equal("furnishing", furnishing.ContainerSlot);
        Assert.Contains("architectural plans", Assert.Single(furnishing.Components,
            value => value.DefinitionId == "game.core.world.location.furnishing").Data);

        Assert.Contains(await world.GetRelationshipsAsync(Location), edge =>
            edge.Kind == "game.core.world.location.connected-to"
            && new[] { edge.FromEntityId, edge.ToEntityId }.Contains(Location)
            && new[] { edge.FromEntityId, edge.ToEntityId }.Contains(ExistingLocation));
        Assert.Contains(await world.GetRelationshipsAsync(Knowledge), edge =>
            edge.FromEntityId == Knowledge && edge.ToEntityId == Location
            && edge.Kind == "game.core.world.knowledge.about" && edge.Data == "{}");

        var duplicateConnection = await RunAsync(runner, "connect two existing locations as adjacent",
            new() { ["left"] = ExistingLocation, ["right"] = Location }, new { });
        Assert.False(duplicateConnection.Ok);
        Assert.Equal(0, duplicateConnection.AppliedCount);

        var invented = await RunAsync(runner, "create an empty location shell", [], new
        {
            locationId = "location.feature-01.invalid-primitive",
            name = "Invalid",
            kind = "interior",
            status = "draft",
            summary = "This input contains a forbidden invented field.",
            visibility = "gm",
            exits = new[] { "invented" }
        });
        Assert.False(invented.Ok);
        Assert.Equal(0, invented.AppliedCount);
        Assert.Null(await world.GetEntityAsync("location.feature-01.invalid-primitive"));
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    private static async Task<ActionRunResult> RunAsync(
        CatalogMechanicTestHarness runner,
        string intent,
        Dictionary<string, string> roles,
        object input) => await runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            RoleEntityIds = roles,
            Input = JsonSerializer.Serialize(input),
            Seed = 2903
        });

    private static void AssertSuccess(ActionRunResult result, string mechanicId, int applied)
    {
        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal(mechanicId, result.Mechanic?.Id);
        Assert.Equal(applied, result.AppliedCount);
    }

    private static CatalogMechanicTestHarness Runner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore mechanics) => new(
        db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
        new EffectApplier(db, world, null, new EventLedger(db)),
        new OperationLog(db),
        new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
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
}
