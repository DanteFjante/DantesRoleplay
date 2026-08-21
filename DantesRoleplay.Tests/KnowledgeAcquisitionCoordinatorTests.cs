using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeAcquisitionCoordinatorTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Secret = "secret.feature-04.oren-correspondence";
    private const string Oren = "actor.feature-03.oren-dale";
    private const string Outsider = "actor.knowledge-slice1.outsider";
    private const string Learner = "actor.knowledge-slice2.learner";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-slice2-{Guid.NewGuid():n}");
    private readonly List<DantesRoleplayDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Fixture_keeps_interaction_sourced_knowledge_personal_and_durable()
    {
        var setup = await ImportAsync();

        Assert.Equal("believed", (await setup.States.ResolveAsync(Learner, Secret)).Value!.State);
        Assert.Equal("unknown", (await setup.States.ResolveAsync(Oren, Secret)).Value!.State);
    }

    [Fact]
    public async Task Accepted_interaction_teaches_one_actor_once_and_replay_is_idempotent()
    {
        var setup = await ImportAsync();
        var request = Request("interaction.knowledge-test.oren-outsider", "acquisition.knowledge-test.oren-outsider", "believed");

        var recorded = await setup.Acquisitions.RecordInteractionAsync(request);
        Assert.True(recorded.Recorded);
        Assert.Equal("recorded", recorded.Status);
        var acquisition = Assert.Single(recorded.Acquisitions);
        Assert.True(acquisition.StateUpdated);
        Assert.False(acquisition.Replayed);
        Assert.Equal("believed", (await setup.States.ResolveAsync(Outsider, Secret)).Value!.State);
        Assert.Equal("unknown", (await setup.States.ResolveAsync(Oren, Secret)).Value!.State);

        var replayed = await setup.Acquisitions.RecordInteractionAsync(request);
        Assert.True(replayed.Recorded);
        Assert.Equal("replayed", replayed.Status);
        Assert.True(Assert.Single(replayed.Acquisitions).Replayed);
        var sourceLinks = await setup.World.GetRelationshipsAsync(request.InteractionId, includeIncoming: true);
        Assert.Single(sourceLinks.Where(link => link.Kind == "game.core.world.knowledge.acquisition.source" && link.ToEntityId == request.InteractionId));
    }

    [Fact]
    public async Task New_sources_strengthen_but_never_weaken_an_explicit_state()
    {
        var setup = await ImportAsync();
        await setup.Acquisitions.RecordInteractionAsync(Request("interaction.knowledge-test.first", "acquisition.knowledge-test.first", "believed"));

        var strengthened = await setup.Acquisitions.RecordInteractionAsync(Request("interaction.knowledge-test.second", "acquisition.knowledge-test.second", "known"));
        Assert.True(Assert.Single(strengthened.Acquisitions).StateUpdated);
        Assert.Equal("known", (await setup.States.ResolveAsync(Outsider, Secret)).Value!.State);

        var weaker = await setup.Acquisitions.RecordInteractionAsync(Request("interaction.knowledge-test.third", "acquisition.knowledge-test.third", "familiar"));
        var result = Assert.Single(weaker.Acquisitions);
        Assert.False(result.StateUpdated);
        Assert.Equal("known", result.State);
        Assert.Equal("known", (await setup.States.ResolveAsync(Outsider, Secret)).Value!.State);
    }

    [Fact]
    public async Task Invalid_acquisition_rejects_before_creating_the_interaction()
    {
        var setup = await ImportAsync();
        var invalid = Request("interaction.knowledge-test.invalid", "acquisition.knowledge-test.invalid", "known") with
        {
            Acquisitions = [new("acquisition.knowledge-test.invalid", Outsider, "secret.not-found", "told", "known")]
        };

        var rejected = await setup.Acquisitions.RecordInteractionAsync(invalid);
        Assert.False(rejected.Recorded);
        Assert.Equal("KNOWLEDGE_NOT_FOUND", Assert.Single(rejected.Problems).Code);
        Assert.Null(await setup.World.GetEntityAsync(invalid.InteractionId));
    }

    private static RecordKnowledgeInteractionRequest Request(string interaction, string acquisition, string state) =>
        new(interaction, "Oren tells the outsider", World, "conversation", "A trusted fixture conversation.", [Oren, Outsider], [new(acquisition, Outsider, Secret, "told", state)]);

    private async Task<Setup> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        _contexts.Add(db);
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var states = new KnowledgeStateCoordinator(world);
        return new(world, states, new KnowledgeAcquisitionCoordinator(db, world, states));
    }

    private sealed record Setup(IWorldStore World, IKnowledgeStateCoordinator States, IKnowledgeAcquisitionCoordinator Acquisitions);
    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f))); }
}
