using DantesRoleplay.DataAccess;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class GraphProjectionReaderTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_graph_is_component_filtered_ordered_and_read_only()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db);
        var reader = new GraphProjectionReader(world);

        var result = await reader.ReadAsync(new GraphQuery("root", ["selected"], 1, ["linked"], 1));

        Assert.True(result.Ok, result.ErrorMessage);
        var graph = result.Projection!;
        Assert.Equal("root", graph.RootId);
        Assert.Equal(new[] { "child", "peer", "root" }, graph.Nodes.Select(node => node.Id));
        Assert.All(graph.Nodes, node => Assert.All(node.Components, component => Assert.Equal("selected", component.DefinitionId)));
        var child = Assert.Single(graph.Nodes, node => node.Id == "child");
        Assert.Equal("root", child.ContainerId);
        Assert.Equal("slot-a", child.ContainerSlot);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(("root", "peer", "linked"), (edge.FromEntityId, edge.ToEntityId, edge.Kind));
        Assert.Null(graph.Truncated);
        Assert.Equal(3, await db.Entities.CountAsync());
        Assert.Equal(3, await db.Components.CountAsync());
        Assert.Empty(db.Operations);
    }

    [Fact]
    public async Task A_graph_rejects_bad_selection_before_returning_any_nodes()
    {
        await using var db = _fixture.CreateContext();
        var reader = new GraphProjectionReader(await SeedAsync(db));

        var empty = await reader.ReadAsync(new GraphQuery("root", [], 0, [], 0));
        var duplicate = await reader.ReadAsync(new GraphQuery("root", ["selected", "selected"], 0, [], 0));
        var unknown = await reader.ReadAsync(new GraphQuery("root", ["unknown"], 0, [], 0));

        Assert.Equal("INVALID_GRAPH_QUERY", empty.ErrorCode);
        Assert.Equal("INVALID_GRAPH_QUERY", duplicate.ErrorCode);
        Assert.Equal("UNKNOWN_GRAPH_COMPONENT", unknown.ErrorCode);
        Assert.Null(empty.Projection);
        Assert.Null(duplicate.Projection);
        Assert.Null(unknown.Projection);
        Assert.Equal(3, await db.Entities.CountAsync());
        Assert.Empty(db.Operations);
    }

    [Fact]
    public async Task A_graph_caps_nodes_deterministically_and_rejects_dangling_relationships()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db);
        await world.CreateEntityAsync("Second child", "second-child");
        await world.SetComponentAsync("second-child", "selected", "{\"value\":\"second\"}");
        await world.MoveAsync("second-child", "root", "slot-b");
        var reader = new GraphProjectionReader(world);

        var capped = await reader.ReadAsync(new GraphQuery("root", ["selected"], 1, [], 0, MaxNodes: 2));

        Assert.True(capped.Ok, capped.ErrorMessage);
        Assert.Equal(new[] { "child", "root" }, capped.Projection!.Nodes.Select(node => node.Id));
        Assert.Equal("maxNodes", capped.Projection.Truncated!.Limit);

        Assert.True(await world.DeleteEntityAsync("peer"));
        var dangling = await reader.ReadAsync(new GraphQuery("root", ["selected"], 0, ["linked"], 1));

        Assert.Equal("DANGLING_GRAPH_RELATIONSHIP", dangling.ErrorCode);
        Assert.Null(dangling.Projection);
    }

    [Fact]
    public async Task Relationship_cycles_are_deduplicated_and_edge_caps_are_explicit()
    {
        await using var db = _fixture.CreateContext();
        var world = await SeedAsync(db);
        await world.RelateAsync("peer", "root", "linked", "{\"kind\":\"return\"}");
        var reader = new GraphProjectionReader(world);

        var cyclic = await reader.ReadAsync(new GraphQuery("root", ["selected"], 0, ["linked"], 2));
        var capped = await reader.ReadAsync(new GraphQuery("root", ["selected"], 0, ["linked"], 2, MaxEdges: 0));

        Assert.True(cyclic.Ok, cyclic.ErrorMessage);
        Assert.Equal(new[] { "peer", "root" }, cyclic.Projection!.Nodes.Select(node => node.Id));
        Assert.Equal(2, cyclic.Projection.Edges.Count);
        Assert.Null(cyclic.Projection.Truncated);
        Assert.True(capped.Ok, capped.ErrorMessage);
        Assert.Empty(capped.Projection!.Edges);
        Assert.Equal("maxEdges", capped.Projection.Truncated!.Limit);
    }

    private static async Task<WorldStore> SeedAsync(DantesRoleplayDbContext db)
    {
        var world = new WorldStore(db);
        await world.DefineComponentAsync("selected", "Selected", "Included by a graph test.");
        await world.DefineComponentAsync("hidden", "Hidden", "Excluded by a graph test.");
        await world.CreateEntityAsync("Root", "root");
        await world.CreateEntityAsync("Child", "child");
        await world.CreateEntityAsync("Peer", "peer");
        await world.SetComponentAsync("root", "selected", "{\"value\":\"root\"}");
        await world.SetComponentAsync("root", "hidden", "{\"value\":\"hidden\"}");
        await world.SetComponentAsync("child", "selected", "{\"value\":\"child\"}");
        await world.MoveAsync("child", "root", "slot-a");
        await world.RelateAsync("root", "peer", "linked", "{\"kind\":\"test\"}");
        return world;
    }
}
