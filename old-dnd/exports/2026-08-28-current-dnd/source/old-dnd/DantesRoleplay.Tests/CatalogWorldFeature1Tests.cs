using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// World Feature 1 proves authored topology through a fresh catalog import. Movement and route
/// resolution deliberately remain outside this fixture-only slice.
/// </summary>
public sealed class CatalogWorldFeature1Tests : IDisposable
{
    private const string Root = "world.feature-01.fixture";
    private const string Region = "region.feature-01.fixture";
    private const string Gate = "location.feature-01.gate";
    private const string Market = "location.feature-01.market";
    private const string Observatory = "location.feature-01.observatory";
    private const string RootComponent = "game.core.world.root";
    private const string LocationComponent = "game.core.world.location";
    private const string ConnectedTo = "game.core.world.location.connected-to";

    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"world-feature-01-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_contains_the_canonical_world_topology_fixture()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        AssertTopologyContract(contents);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.Equal(5, contents.Entities.Count(entity => entity.Id is Root or Region or Gate or Market or Observatory));

        var root = AssertEntity(await world.GetEntityAsync(Root));
        var region = AssertEntity(await world.GetEntityAsync(Region));
        var gate = AssertEntity(await world.GetEntityAsync(Gate));
        var market = AssertEntity(await world.GetEntityAsync(Market));
        var observatory = AssertEntity(await world.GetEntityAsync(Observatory));

        Assert.Null(root.ContainerId);
        Assert.Equal(Root, region.ContainerId);
        Assert.Equal("region", region.ContainerSlot);
        foreach (var location in new[] { gate, market, observatory })
        {
            Assert.Equal(Region, location.ContainerId);
            Assert.Equal("location", location.ContainerSlot);
        }

        AssertComponent(root, RootComponent, "active", "A compact fixture setting for persistent topology regression coverage.", "gm");
        AssertLocation(region, "region", "party");
        AssertLocation(gate, "settlement", "party");
        AssertLocation(market, "site", "party");
        AssertLocation(observatory, "interior", "gm");

        var edges = new[]
        {
            (await world.GetRelationshipsAsync(Gate, includeIncoming: false)).Single(),
            (await world.GetRelationshipsAsync(Market, includeIncoming: false)).Single()
        };
        Assert.Collection(edges.OrderBy(edge => edge.FromEntityId, StringComparer.Ordinal),
            edge => AssertEdge(edge, Gate, Market),
            edge => AssertEdge(edge, Market, Observatory));

        var hero = AssertEntity(await world.GetEntityAsync("creature.dnd2024.feature-10.hero"));
        Assert.DoesNotContain(hero.Components, component => component.DefinitionId.StartsWith("game.core.world.", StringComparison.Ordinal));

        var secondPlan = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .PlanAsync(_catalogCopy);
        Assert.True(secondPlan.IsClean, string.Join(", ", secondPlan.Entries.Where(entry => entry.Change != CatalogChange.Unchanged).Select(entry => entry.Id)));
    }

    [Fact]
    public void Topology_contract_rejects_invalid_component_and_edge_conventions()
    {
        Assert.Throws<InvalidOperationException>(() => AssertRootData("""{"status":"unknown","summary":"x","visibility":"gm"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRootData("""{"status":"active","summary":"   ","visibility":"gm"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertRootData("""{"status":"active","summary":"x","visibility":"gm","worldId":"duplicate"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertLocationData("""{"kind":"planet","status":"active","summary":"x","visibility":"gm"}"""));
        Assert.Throws<InvalidOperationException>(() => AssertLocationData("""{"kind":"site","status":"active","summary":null,"visibility":"gm"}"""));

        Assert.Throws<InvalidOperationException>(() => AssertEdges([(Gate, Gate, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertEdges([(Market, Gate, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertEdges([(Root, Gate, "{}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertEdges([(Gate, Market, "{\"blocked\":true}") ]));
        Assert.Throws<InvalidOperationException>(() => AssertEdges([(Gate, Market, "{}"), (Gate, Market, "{}") ]));
    }

    private static void AssertTopologyContract(CatalogContents contents)
    {
        var root = contents.Entities.Single(entity => entity.Id == Root);
        var region = contents.Entities.Single(entity => entity.Id == Region);
        var locations = new[] { Gate, Market, Observatory }
            .Select(id => contents.Entities.Single(entity => entity.Id == id))
            .ToArray();

        Assert.Null(root.ContainerId);
        Assert.Equal(Root, region.ContainerId);
        Assert.Equal("region", region.ContainerSlot);
        Assert.All(locations, location =>
        {
            Assert.Equal(Region, location.ContainerId);
            Assert.Equal("location", location.ContainerSlot);
        });

        AssertRootData(root.Components.Single(component => component.DefinitionId == RootComponent).Data);
        AssertLocationData(region.Components.Single(component => component.DefinitionId == LocationComponent).Data);
        Assert.All(locations, location => AssertLocationData(location.Components.Single(component => component.DefinitionId == LocationComponent).Data));
        AssertEdges(contents.Relationships!.Relationships
            .Where(edge => edge.Kind == ConnectedTo)
            .Select(edge => (edge.From, edge.To, edge.Data)));
    }

    private static void AssertRootData(string data) => AssertData(data, ["status", "summary", "visibility"], new Dictionary<string, string[]>
    {
        ["status"] = ["draft", "active", "archived"],
        ["visibility"] = ["public", "party", "gm"]
    });

    private static void AssertLocationData(string data) => AssertData(data, ["kind", "status", "summary", "visibility"], new Dictionary<string, string[]>
    {
        ["kind"] = ["region", "settlement", "site", "interior"],
        ["status"] = ["draft", "active", "archived"],
        ["visibility"] = ["public", "party", "gm"]
    });

    private static void AssertData(string data, IReadOnlyCollection<string> names, IReadOnlyDictionary<string, string[]> values)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Count)
            throw new InvalidOperationException("Topology data must be a closed object.");

        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) throw new InvalidOperationException($"Missing {name}.");
            if (name == "summary")
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 1000)
                    throw new InvalidOperationException("Summary is invalid.");
            }
            else if (value.ValueKind != JsonValueKind.String || !values[name].Contains(value.GetString(), StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{name} is invalid.");
            }
        }
    }

    private static void AssertEdges(IEnumerable<(string From, string To, string Data)> candidates)
    {
        var locations = new HashSet<string>([Gate, Market, Observatory], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var edges = candidates.ToArray();
        if (edges.Length != 2) throw new InvalidOperationException("The fixture must have exactly two adjacency edges.");

        foreach (var (from, to, data) in edges)
        {
            if (!locations.Contains(from) || !locations.Contains(to) || string.CompareOrdinal(from, to) >= 0 || data != "{}")
                throw new InvalidOperationException("Adjacency violates the canonical topology convention.");
            if (!seen.Add($"{from}|{to}|{ConnectedTo}")) throw new InvalidOperationException("Duplicate adjacency.");
        }
    }

    private static void AssertComponent(EntitySnapshot entity, string componentId, string status, string summary, string visibility)
    {
        var component = entity.Components.Single(candidate => candidate.DefinitionId == componentId);
        using var data = JsonDocument.Parse(component.Data);
        Assert.Equal(status, data.RootElement.GetProperty("status").GetString());
        Assert.Equal(summary, data.RootElement.GetProperty("summary").GetString());
        Assert.Equal(visibility, data.RootElement.GetProperty("visibility").GetString());
    }

    private static void AssertLocation(EntitySnapshot entity, string kind, string visibility)
    {
        var component = entity.Components.Single(candidate => candidate.DefinitionId == LocationComponent);
        using var data = JsonDocument.Parse(component.Data);
        Assert.Equal(kind, data.RootElement.GetProperty("kind").GetString());
        Assert.Equal("active", data.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.RootElement.GetProperty("summary").GetString()));
        Assert.Equal(visibility, data.RootElement.GetProperty("visibility").GetString());
    }

    private static void AssertEdge(DantesRoleplay.World.RelationshipView edge, string from, string to)
    {
        Assert.Equal(from, edge.FromEntityId);
        Assert.Equal(to, edge.ToEntityId);
        Assert.Equal(ConnectedTo, edge.Kind);
        Assert.Equal("{}", edge.Data);
    }

    private static EntitySnapshot AssertEntity(EntitySnapshot? entity) => Assert.IsType<EntitySnapshot>(entity);

    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
