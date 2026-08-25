using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Ecs.Tests;

public sealed class StateSpaceEdgeStoreTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Containment_is_scoped_exclusive_revision_bound_and_acyclic()
    {
        var setup = Setup();
        foreach (var id in new[] { "root", "middle", "leaf" })
            await setup.Entities.CreateEntityAsync("space", id, id);
        await setup.OtherEntities.CreateEntityAsync("other-space", "root", "Other root");
        await setup.OtherEntities.CreateEntityAsync("other-space", "leaf", "Other leaf");

        var first = await setup.Edges.MoveContainmentAsync("space", "leaf", "middle", "carried", 0);
        var second = await setup.Edges.MoveContainmentAsync("space", "leaf", "root", "present", 1);
        Assert.Equal(2, second.Revision);
        Assert.Equal("root", (await setup.Edges.GetContainmentAsync("space", "leaf"))!.ContainerEntityId);
        Assert.Null(await setup.Edges.GetContainmentAsync("other-space", "leaf"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Edges.MoveContainmentAsync("space", "leaf", "middle", "stale", first.Revision));

        await setup.Edges.MoveContainmentAsync("space", "middle", "leaf", "nested", 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Edges.MoveContainmentAsync("space", "root", "middle", "cycle", 0));
        Assert.True(await setup.Edges.RemoveContainmentAsync("space", "leaf", 2));
        Assert.False(await setup.Edges.RemoveContainmentAsync("space", "leaf", 2));
    }

    [Fact]
    public async Task Relationships_are_owner_qualified_json_preserving_and_revision_bound()
    {
        var setup = Setup();
        await setup.Entities.CreateEntityAsync("space", "from", "From");
        await setup.Entities.CreateEntityAsync("space", "to", "To");

        var first = await setup.Edges.SetRelationshipAsync(
            "space", "from", "to", "fixture-app.knows", "[1,true,null]", 0);
        var second = await setup.Edges.SetRelationshipAsync(
            "space", "from", "to", "fixture-app.knows", "{\"since\":2}", first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal("{\"since\":2}", (await setup.Edges.GetRelationshipAsync(
            "space", "from", "to", "fixture-app.knows"))!.DataJson);
        Assert.Single(await setup.Edges.ListRelationshipsAsync("space"));

        await Assert.ThrowsAsync<ArgumentException>(() => setup.Edges.SetRelationshipAsync(
            "space", "from", "to", "other-app.knows", "{}", 0));
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Edges.SetRelationshipAsync(
            "space", "from", "to", "fixture-app.invalid", "not-json", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Edges.SetRelationshipAsync(
            "space", "from", "to", "fixture-app.knows", "{}", 1));
        Assert.True(await setup.Edges.RemoveRelationshipAsync(
            "space", "from", "to", "fixture-app.knows", 2));
        Assert.Empty(await setup.Edges.ListRelationshipsAsync("space"));
    }

    private SetupResult Setup()
    {
        var db = _fixture.CreateContext();
        var app = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(app, "Fixture", "Fixture application.", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("space", revision, Manifest));
        spaces.Create(new("other-space", revision, Manifest));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        return new(
            new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator()),
            new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator()),
            new SqliteStateSpaceEdgeStore(db, spaces));
    }

    public void Dispose() => _fixture.Dispose();

    private sealed record SetupResult(
        SqliteEntityComponentStore Entities,
        SqliteEntityComponentStore OtherEntities,
        SqliteStateSpaceEdgeStore Edges);
}
