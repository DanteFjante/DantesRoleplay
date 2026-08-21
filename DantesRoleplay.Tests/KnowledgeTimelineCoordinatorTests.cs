using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeTimelineCoordinatorTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string OldToll = "fact.knowledge-slice3.market-toll-old";
    private const string NewToll = "fact.knowledge-slice3.market-toll-new";
    private const string ClaimA = "rumour.knowledge-slice3.observatory-a";
    private const string ClaimB = "rumour.knowledge-slice3.observatory-b";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-slice3-{Guid.NewGuid():n}");
    private readonly List<DantesRoleplayDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task As_of_read_preserves_adjacent_superseded_history()
    {
        var timeline = await ImportAsync();

        var before = await timeline.ReadAsOfAsync(World, 119);
        Assert.True(before.Read);
        Assert.Equal("effective", Record(before, OldToll).TemporalStatus);
        Assert.Equal("not-yet-effective", Record(before, NewToll).TemporalStatus);

        var after = await timeline.ReadAsOfAsync(World, 120);
        Assert.Equal("historical", Record(after, OldToll).TemporalStatus);
        var current = Record(after, NewToll);
        Assert.Equal("effective", current.TemporalStatus);
        Assert.Equal([OldToll], current.SupersedesKnowledgeIds);
    }

    [Fact]
    public async Task Contradictions_are_canonical_and_remain_contested_without_selecting_truth()
    {
        var timeline = await ImportAsync();

        var history = await timeline.ReadAsOfAsync(World, 0);
        Assert.True(Record(history, ClaimA).Contested);
        Assert.True(Record(history, ClaimB).Contested);
        Assert.Equal([ClaimB], Record(history, ClaimA).ContradictsKnowledgeIds);
        Assert.Equal([ClaimA], Record(history, ClaimB).ContradictsKnowledgeIds);

        var replay = await timeline.RecordContradictionAsync(new(ClaimB, ClaimA));
        Assert.True(replay.Recorded);
        Assert.Equal("replayed", replay.Status);
        Assert.Equal((ClaimA, ClaimB), (replay.FromKnowledgeId, replay.ToKnowledgeId));
    }

    [Fact]
    public async Task Invalid_future_validity_and_reverse_supersession_are_rejected_without_rewriting_records()
    {
        var timeline = await ImportAsync();

        var future = await timeline.RecordValidityAsync(new(OldToll, 1, 120));
        Assert.False(future.Recorded);
        Assert.Equal("KNOWLEDGE_VALIDITY_FUTURE", Assert.Single(future.Problems).Code);

        var reverse = await timeline.RecordSupersessionAsync(new(OldToll, NewToll));
        Assert.False(reverse.Recorded);
        Assert.Equal("KNOWLEDGE_SUPERSESSION_INTERVAL_INVALID", Assert.Single(reverse.Problems).Code);

        var history = await timeline.ReadAsOfAsync(World, 119);
        Assert.Equal("effective", Record(history, OldToll).TemporalStatus);
        Assert.Equal("not-yet-effective", Record(history, NewToll).TemporalStatus);
    }

    private async Task<IKnowledgeTimelineCoordinator> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        _contexts.Add(db);
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return new KnowledgeTimelineCoordinator(db, world);
    }

    private static KnowledgeTimelineProjection Record(KnowledgeHistoryResult history, string id) => Assert.Single(history.Records, record => record.KnowledgeId == id);
    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f))); }
}
