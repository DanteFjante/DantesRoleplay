using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeStateCoordinatorTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Fact = "fact.feature-04.toll-ledger";
    private const string Rumour = "rumour.feature-04.observatory-signal";
    private const string Secret = "secret.feature-04.oren-correspondence";
    private const string Mara = "actor.feature-03.mara-vell";
    private const string Oren = "actor.feature-03.oren-dale";
    private const string Resident = "actor.knowledge-slice1.resident";
    private const string Outsider = "actor.knowledge-slice1.outsider";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-slice1-{Guid.NewGuid():n}");
    private readonly List<DantesRoleplayDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Fixture_resolves_global_exception_region_and_faction_without_changing_feature4_components()
    {
        var coordinator = await ImportAsync();

        Assert.Equal(("known", "world-baseline", World), Value(await coordinator.ResolveAsync(Mara, Fact)));
        Assert.Equal(("unknown", "explicit-state", Outsider), Value(await coordinator.ResolveAsync(Outsider, Fact)));
        Assert.Equal(("known", "region-baseline", "region.feature-01.fixture"), Value(await coordinator.ResolveAsync(Resident, Rumour)));
        Assert.Equal(("known", "faction-baseline", "faction.feature-03.fixture"), Value(await coordinator.ResolveAsync(Mara, Secret)));
        Assert.Equal(("unknown", "derived-unknown", (string?)null), Value(await coordinator.ResolveAsync(Oren, Secret)));
    }

    [Fact]
    public async Task Governed_writes_replace_the_single_explicit_state_and_reject_invalid_scope_or_state()
    {
        var coordinator = await ImportAsync();

        var recorded = await coordinator.RecordStateAsync(new(Oren, Secret, "believed"));
        Assert.True(recorded.Recorded);
        Assert.Equal(("believed", "explicit-state", Oren), Value(await coordinator.ResolveAsync(Oren, Secret)));

        var corrected = await coordinator.RecordStateAsync(new(Oren, Secret, "doubted"));
        Assert.True(corrected.Recorded);
        Assert.Equal(("doubted", "explicit-state", Oren), Value(await coordinator.ResolveAsync(Oren, Secret)));

        var invalidState = await coordinator.RecordStateAsync(new(Oren, Secret, "certain"));
        Assert.False(invalidState.Recorded);
        Assert.Equal("INVALID_KNOWLEDGE_STATE_REQUEST", Assert.Single(invalidState.Problems).Code);

        var invalidScope = await coordinator.RecordBaselineAsync(new(Oren, Secret));
        Assert.False(invalidScope.Recorded);
        Assert.Equal("KNOWLEDGE_SCOPE_INVALID", Assert.Single(invalidScope.Problems).Code);

    }

    [Fact]
    public async Task Governed_world_baseline_is_available_to_an_outsider_only_after_its_explicit_override_is_corrected()
    {
        var coordinator = await ImportAsync();

        var before = await coordinator.ResolveAsync(Outsider, Fact);
        Assert.Equal("unknown", before.Value!.State);

        var correction = await coordinator.RecordStateAsync(new(Outsider, Fact, "known"));
        Assert.True(correction.Recorded);
        Assert.Equal(("known", "explicit-state", Outsider), Value(await coordinator.ResolveAsync(Outsider, Fact)));
    }

    private async Task<IKnowledgeStateCoordinator> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        _contexts.Add(db);
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return new KnowledgeStateCoordinator(world);
    }

    private static (string State, string SourceKind, string? SourceId) Value(EffectiveKnowledgeStateResult result)
    {
        Assert.True(result.Resolved, string.Join(" ", result.Problems.Select(problem => problem.Code)));
        var value = Assert.IsType<EffectiveKnowledgeState>(result.Value);
        return (value.State, value.SourceKind, value.SourceEntityId);
    }

    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f))); }
}
