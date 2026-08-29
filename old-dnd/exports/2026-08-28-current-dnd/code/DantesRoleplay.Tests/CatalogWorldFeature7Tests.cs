using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Feature 7 publishes trusted-GM recipes without adding world-specific query code.</summary>
public sealed class CatalogWorldFeature7Tests : IDisposable
{
    private const string Root = "world.feature-01.fixture";
    private const string Market = "location.feature-01.market";
    private const string Faction = "faction.feature-03.fixture";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-07-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_copy)) Directory.Delete(_copy, true);
    }

    [Fact]
    public async Task Imported_contract_publishes_all_four_recipes_through_the_public_graph_query_without_world_writes()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var procedures = new ProcedureStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_copy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var contract = await procedures.GetAsync("procedure.game.core.world.read");
        Assert.NotNull(contract);
        Assert.Contains("World overview", contract!.Instructions, StringComparison.Ordinal);
        Assert.Contains("Location detail", contract.Instructions, StringComparison.Ordinal);
        Assert.Contains("Faction detail", contract.Instructions, StringComparison.Ordinal);
        Assert.Contains("Knowledge detail", contract.Instructions, StringComparison.Ordinal);

        var before = await WorldCountsAsync(db);
        var operationsBefore = await db.Operations.CountAsync();

        var overview = Projection(await GraphAsync(db, procedures, world, mechanics, Root,
            ["game.core.world.root", "game.core.world.location"], 2,
            ["game.core.world.location.connected-to"], 1, 100, 100));
        Assert.Equal(["location.feature-01.gate", Market, "location.feature-01.observatory", "region.feature-01.fixture", Root], overview.Nodes.Select(n => n.Id));
        Assert.Equal(2, overview.Edges.Count);
        Assert.All(overview.Nodes, node => Assert.DoesNotContain(node.Components, c => c.DefinitionId == "game.core.world.clock"));

        var location = Projection(await GraphAsync(db, procedures, world, mechanics, Market,
            ["game.core.world.location"], 1, ["game.core.world.location.connected-to"], 1, 50, 50));
        Assert.Equal(["actor.knowledge-slice1.resident", "location.feature-01.gate", Market, "location.feature-01.observatory"], location.Nodes.Select(n => n.Id));
        Assert.Equal("region.feature-01.fixture", location.Nodes.Single(n => n.Id == Market).ContainerId);
        Assert.Equal(2, location.Edges.Count);

        var faction = Projection(await GraphAsync(db, procedures, world, mechanics, Faction,
            ["game.core.world.faction", "game.core.world.motive"], 0,
            ["game.core.world.faction.member", "game.core.world.faction.controls", "game.core.world.faction.allied-with", "game.core.world.faction.opposed-to"], 1, 40, 50));
        Assert.Equal(["actor.feature-03.mara-vell", Faction, Market], faction.Nodes.Select(n => n.Id));
        Assert.Equal(["game.core.world.faction.controls", "game.core.world.faction.member"], faction.Edges.Select(e => e.Kind));
        Assert.Contains(faction.Nodes.Single(n => n.Id == "actor.feature-03.mara-vell").Components, c => c.DefinitionId == "game.core.world.motive");

        var knowledge = Projection(await GraphAsync(db, procedures, world, mechanics, Root,
            ["game.core.world.fact", "game.core.world.rumour", "game.core.world.secret", "game.core.world.clue"], 0,
            ["game.core.world.knowledge.in-world", "game.core.world.knowledge.about", "game.core.world.clue.supports"], 2, 100, 150));
        Assert.Contains(knowledge.Nodes, n => n.Id == "fact.feature-04.toll-ledger");
        Assert.Contains(knowledge.Nodes, n => n.Id == "rumour.feature-04.observatory-signal");
        Assert.Contains(knowledge.Nodes, n => n.Id == "secret.feature-04.oren-correspondence");
        Assert.Equal(3, knowledge.Nodes.Count(n => n.Id.StartsWith("clue.feature-04.", StringComparison.Ordinal)));
        Assert.Equal(23, knowledge.Edges.Count);
        Assert.All(knowledge.Edges, edge => Assert.Contains(edge.Kind, new[] { "game.core.world.knowledge.in-world", "game.core.world.knowledge.about", "game.core.world.clue.supports" }));
        Assert.Null(knowledge.Truncated);

        Assert.Equal(before, await WorldCountsAsync(db));
        Assert.Equal(operationsBefore + 4, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Knowledge_recipe_reads_the_verified_reactive_clue_state_without_rewriting_the_secret()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_copy, new CatalogImportOptions())).Aborted);

        var runner = new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world,
                new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
                new EventLedger(db),
                new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db))),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var advanced = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance faction agenda",
            RoleEntityIds = new Dictionary<string, string> { ["faction"] = Faction },
            Input = "{}",
            Seed = 707
        });
        Assert.True(advanced.Ok, advanced.Error?.Why);

        var knowledge = Projection(await GraphAsync(db, procedures, world, mechanics, Root,
            ["game.core.world.fact", "game.core.world.rumour", "game.core.world.secret", "game.core.world.clue"], 0,
            ["game.core.world.knowledge.in-world", "game.core.world.knowledge.about", "game.core.world.clue.supports"], 2, 100, 150));
        Assert.Equal(("revealed", "party"), StatusAndVisibility(knowledge.Nodes.Single(n => n.Id == "clue.feature-04.oren-letter"), "game.core.world.clue"));
        Assert.Equal(("active", "gm"), StatusAndVisibility(knowledge.Nodes.Single(n => n.Id == "secret.feature-04.oren-correspondence"), "game.core.world.secret"));
    }

    private static async Task<ToolEnvelope> GraphAsync(DantesRoleplayDbContext db, IProcedureStore procedures, IWorldStore world, IMechanicStore mechanics, string id, string[] components, int containmentDepth, string[] relationships, int relationshipDepth, int maxNodes, int maxEdges) =>
        await new QueryTool().QueryAsync(procedures, world, new GraphProjectionReader(world), new JourneyPlanReader(world), new ModeAwareItineraryReader(world), null!, null!, mechanics, new EventTypeStore(db), new SubscriptionStore(db), new EventLedger(db), new OperationLog(db), new NotificationStore(db),
            "graph", id: id, componentIds: components, containmentDepth: containmentDepth, relationshipKinds: relationships, relationshipDepth: relationshipDepth, maxNodes: maxNodes, maxEdges: maxEdges);

    private static GraphProjection Projection(ToolEnvelope envelope) => Assert.IsType<GraphProjection>(Assert.IsType<ToolEnvelope>(envelope).Data);

    private static (string Status, string Visibility) StatusAndVisibility(GraphNode node, string componentId)
    {
        using var document = JsonDocument.Parse(node.Components.Single(c => c.DefinitionId == componentId).Data);
        return (document.RootElement.GetProperty("status").GetString()!, document.RootElement.GetProperty("visibility").GetString()!);
    }

    private static async Task<WorldCounts> WorldCountsAsync(DantesRoleplayDbContext db) => new(
        await db.Entities.CountAsync(), await db.Components.CountAsync(), await db.Containments.CountAsync(),
        await db.Relationships.CountAsync(), await db.Events.CountAsync(), await db.Notifications.CountAsync());

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private sealed record WorldCounts(int Entities, int Components, int Containments, int Relationships, int Events, int Notifications);
}
