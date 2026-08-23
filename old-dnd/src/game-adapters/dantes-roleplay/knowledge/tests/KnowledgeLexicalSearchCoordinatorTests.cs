using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeLexicalSearchCoordinatorTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Market = "location.feature-01.market";
    private const string OldToll = "fact.knowledge-slice3.market-toll-old";
    private const string NewToll = "fact.knowledge-slice3.market-toll-new";
    private const string Fact = "game.core.world.fact";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-slice4-{Guid.NewGuid():n}");
    private readonly string _indexPath = Path.Combine(Path.GetTempPath(), $"knowledge-slice4-{Guid.NewGuid():n}.db");
    private readonly List<DantesRoleplayDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
        foreach (var path in new[] { _indexPath, $"{_indexPath}-shm", $"{_indexPath}-wal" }) if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task Rebuild_and_search_follow_the_canonical_timeline()
    {
        var (world, coordinator) = await ImportAsync();

        Assert.True(await coordinator.RebuildWorldAsync(World) >= 4);

        var before = await coordinator.SearchAsync(Request(119));
        Assert.True(before.Ok);
        var old = Assert.Single(before.Hits, hit => hit.KnowledgeId == OldToll);
        Assert.Equal("The market toll was one silver piece before the council revision.", old.Summary);

        var after = await coordinator.SearchAsync(Request(120));
        Assert.True(after.Ok);
        Assert.Contains(after.Hits, hit => hit.KnowledgeId == NewToll);

        // The derived index is deliberately left stale. Hydration must still prevent an archived
        // canonical record from being returned.
        await world.SetComponentAsync(OldToll, Fact, "{\"status\":\"archived\",\"summary\":\"The market toll was one silver piece before the council revision.\",\"provenance\":\"Reviewed historical fixture record.\",\"visibility\":\"gm\"}");
        var stale = await coordinator.SearchAsync(Request(119));
        Assert.DoesNotContain(stale.Hits, hit => hit.KnowledgeId == OldToll);
    }

    [Fact]
    public async Task Index_supports_replaceable_world_projection_and_incremental_upsert()
    {
        var index = new SqliteKnowledgeLexicalIndex(ConnectionString());
        var first = Document("fact.slice4.harbour-fee", "Harbour fee is one silver piece.");
        await index.ReplaceWorldAsync(World, [first]);

        Assert.Equal([first.KnowledgeId], (await index.SearchAsync(new(World, "harbour fee"))).Select(candidate => candidate.KnowledgeId));

        var revised = first with { Text = "Harbour fee is two silver pieces.", ContentHash = new string('b', 64) };
        await index.UpsertAsync([revised]);
        Assert.Equal([revised.KnowledgeId], (await index.SearchAsync(new(World, "two silver"))).Select(candidate => candidate.KnowledgeId));

        await index.ReplaceWorldAsync(World, []);
        Assert.Empty(await index.SearchAsync(new(World, "harbour fee")));
    }

    [Fact]
    public async Task Index_applies_an_explicit_allowlist_before_ranking_and_limit()
    {
        var index = new SqliteKnowledgeLexicalIndex(ConnectionString());
        var allowed = Document("fact.slice4.allowed", "Harbour fee is one silver piece.");
        var denied = Document("fact.slice4.denied", "Harbour fee is two silver pieces.") with { ContentHash = new string('b', 64) };
        await index.ReplaceWorldAsync(World, [allowed, denied]);

        var result = await index.SearchAsync(new(World, "harbour fee", Limit: 1, AllowedKnowledgeIds: [allowed.KnowledgeId]));

        Assert.Equal([allowed.KnowledgeId], result.Select(candidate => candidate.KnowledgeId));
        Assert.Empty(await index.SearchAsync(new(World, "harbour fee", AllowedKnowledgeIds: [])));
    }

    private async Task<(WorldStore World, IKnowledgeLexicalSearchCoordinator Coordinator)> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        _contexts.Add(db);
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var timeline = new KnowledgeTimelineCoordinator(db, world);
        return (world, new KnowledgeLexicalSearchCoordinator(
            timeline,
            new SqliteKnowledgeLexicalIndex(ConnectionString()),
            new KnowledgeSearchDocumentSource(world)));
    }

    private static KnowledgeLexicalSearchRequest Request(long minute) => new(World, "market toll", ["fact"], [Market], AsOfMinute: minute);
    private static KnowledgeLexicalDocument Document(string id, string text) => new(id, World, "fact", "active", Market, "open", null, null, new string('a', 64), text);
    private string ConnectionString() => $"Data Source={_indexPath};Pooling=False";
    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f))); }
}
