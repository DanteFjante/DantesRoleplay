using DantesRoleplay.Applications;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Ecs.Tests;

public sealed class ApplicationEntitySearchTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Name_search_matches_display_name_and_id_case_insensitively()
    {
        var setup = Setup();
        await setup.Store.CreateEntityAsync("space", "campaign.caldris.measure-of-mercy", "The Measure of Mercy");
        await setup.Store.CreateEntityAsync("space", "campaign.thalorien.brackenford", "The Waystone at Brackenford");
        await setup.Store.CreateEntityAsync("space", "actor.caldris.ganji", "Ganji");

        Assert.Equal(
            ["campaign.caldris.measure-of-mercy"],
            (await setup.Store.SearchEntitiesAsync("space", new("mercy", null, null, 50)))
                .Entities.Select(value => value.EntityId));

        Assert.Equal(
            ["campaign.caldris.measure-of-mercy", "campaign.thalorien.brackenford"],
            (await setup.Store.SearchEntitiesAsync("space", new("campaign.", null, null, 50)))
                .Entities.Select(value => value.EntityId));

        Assert.Empty((await setup.Store.SearchEntitiesAsync("space", new("nothing-here", null, null, 50))).Entities);
    }

    [Fact]
    public async Task Component_filter_selects_only_entities_carrying_that_type()
    {
        var setup = Setup();
        await setup.Store.CreateEntityAsync("space", "alpha", "Alpha");
        await setup.Store.CreateEntityAsync("space", "bravo", "Bravo");
        var root = setup.Types.Define(new(setup.Application, "fixture-app.campaign-root", "true"));
        await setup.Store.AddComponentAsync(
            new("space", "alpha", new(root.QualifiedId, root.Version, root.SchemaHash), "1", 0));

        Assert.Equal(
            ["alpha"],
            (await setup.Store.SearchEntitiesAsync("space", new(null, "fixture-app.campaign-root", null, 50)))
                .Entities.Select(value => value.EntityId));

        Assert.Empty((await setup.Store.SearchEntitiesAsync(
            "space", new("bravo", "fixture-app.campaign-root", null, 50))).Entities);
    }

    [Fact]
    public async Task Search_pages_by_cursor_and_skips_deleted_entities()
    {
        var setup = Setup();
        await setup.Store.CreateEntityAsync("space", "match.alpha", "Match Alpha");
        await setup.Store.CreateEntityAsync("space", "match.bravo", "Match Bravo");
        await setup.Store.CreateEntityAsync("space", "match.charlie", "Match Charlie");
        await setup.Store.DeleteEntityAsync("space", "match.bravo", 1);

        var first = await setup.Store.SearchEntitiesAsync("space", new("match", null, null, 1));
        Assert.Equal(["match.alpha"], first.Entities.Select(value => value.EntityId));
        Assert.Equal("match.alpha", first.NextEntityId);

        var second = await setup.Store.SearchEntitiesAsync("space", new("match", null, first.NextEntityId, 10));
        Assert.Equal(["match.charlie"], second.Entities.Select(value => value.EntityId));
        Assert.Null(second.NextEntityId);
    }

    [Fact]
    public async Task Search_requires_a_term_and_a_bounded_limit()
    {
        var setup = Setup();
        await Assert.ThrowsAsync<ArgumentException>(
            () => setup.Store.SearchEntitiesAsync("space", new(null, null, null, 10)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => setup.Store.SearchEntitiesAsync("space", new(new string('x', 201), null, null, 10)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => setup.Store.SearchEntitiesAsync("space", new("x", null, null, 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => setup.Store.SearchEntitiesAsync("space", new("x", null, null, 101)));
    }

    private SetupResult Setup()
    {
        var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(application, "fixture-app", "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("space", revision, Manifest));
        return new(application,
            new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()),
            new SqliteEntityComponentStore(db, new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()),
                new BoundedJsonSchemaValidator()));
    }

    public void Dispose() => _fixture.Dispose();

    private sealed record SetupResult(
        ApplicationIdentifier Application,
        SqliteComponentTypeRegistry Types,
        SqliteEntityComponentStore Store);
}
