using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeHybridSearchCoordinatorTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Correspondence = "secret.feature-04.oren-correspondence";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-slice5-{Guid.NewGuid():n}");
    private readonly string _indexPath = Path.Combine(Path.GetTempPath(), $"knowledge-slice5-{Guid.NewGuid():n}.db");
    private readonly List<DantesRoleplayDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
        foreach (var path in new[] { _indexPath, $"{_indexPath}-shm", $"{_indexPath}-wal" })
            if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task Semantic_candidate_improves_recall_and_is_hydrated_from_canonical_state()
    {
        var setup = await ImportAsync(new SemanticEmbeddingProvider());
        var rebuilt = await setup.Hybrid.RebuildWorldAsync(World);
        Assert.True(rebuilt.VectorReady);
        Assert.Equal(rebuilt.LexicalDocuments, rebuilt.VectorDocuments);

        var request = new KnowledgeLexicalSearchRequest(World, "concealed family papers", ["secret"]);
        Assert.Empty(await setup.Lexical.SearchAsync(request));

        var result = await setup.Hybrid.SearchAsync(request);
        Assert.True(result.Ok);
        Assert.Equal("hybrid", result.Mode);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(Correspondence, hit.KnowledgeId);
        Assert.Null(hit.LexicalRank);
        Assert.Equal(1, hit.VectorRank);
        Assert.Equal("Oren's family sealed the observatory after hiding correspondence that implicates the old council.", hit.Summary);
    }

    [Fact]
    public async Task Unavailable_embeddings_leave_complete_lexical_fallback()
    {
        var setup = await ImportAsync(new UnavailableEmbeddingProvider());
        var rebuilt = await setup.Hybrid.RebuildWorldAsync(World);
        Assert.False(rebuilt.VectorReady);
        Assert.Equal("EMBEDDING_DISABLED", rebuilt.FallbackCode);
        Assert.True(rebuilt.LexicalDocuments > 0);

        var result = await setup.Hybrid.SearchAsync(new(World, "market archive", ["fact"]));
        Assert.True(result.Ok);
        Assert.Equal("lexical", result.Mode);
        Assert.Equal("EMBEDDING_DISABLED", result.FallbackCode);
        Assert.Contains(result.Hits, hit => hit.KnowledgeId == "fact.feature-04.toll-ledger");
    }

    [Fact]
    public async Task Fusion_is_stable_and_exact_id_lookup_survives_semantic_miss()
    {
        var setup = await ImportAsync(new SemanticEmbeddingProvider());
        await setup.Hybrid.RebuildWorldAsync(World);
        var request = new KnowledgeLexicalSearchRequest(World, Correspondence, Limit: 5);

        var first = await setup.Hybrid.SearchAsync(request);
        var second = await setup.Hybrid.SearchAsync(request);

        Assert.Equal(first.Hits.Select(hit => hit.KnowledgeId), second.Hits.Select(hit => hit.KnowledgeId));
        var exact = Assert.Single(first.Hits, hit => hit.KnowledgeId == Correspondence);
        Assert.True(exact.ExactIdMatch);
    }

    [Fact]
    public async Task Synchronization_skips_unchanged_document_hashes()
    {
        var embeddings = new SemanticEmbeddingProvider();
        var setup = await ImportAsync(embeddings);

        var first = await setup.Hybrid.SynchronizeWorldAsync(World);
        Assert.True(first.VectorReady);
        Assert.Equal(first.VectorDocuments, first.EmbeddedDocuments);
        var afterFirst = embeddings.EmbeddedInputs;

        var second = await setup.Hybrid.SynchronizeWorldAsync(World);
        Assert.True(second.VectorReady);
        Assert.Equal(0, second.EmbeddedDocuments);
        Assert.Equal(afterFirst, embeddings.EmbeddedInputs);
    }

    private async Task<Setup> ImportAsync(ITextEmbeddingProvider embeddings)
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        _contexts.Add(db);
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var source = new KnowledgeSearchDocumentSource(world);
        var timeline = new KnowledgeTimelineCoordinator(db, world);
        var lexical = new SqliteKnowledgeLexicalIndex(ConnectionString());
        var hybrid = new KnowledgeHybridSearchCoordinator(
            timeline,
            source,
            lexical,
            embeddings,
            new MemoryVectorIndex(),
            new KnowledgeRetrievalOptions
            {
                Embedding = new() { Enabled = true, ExpectedDimensions = 2, MaxBatchSize = 4 },
                Vector = new() { Enabled = true, ExtensionPath = "memory-test-index" },
                BackfillBatchSize = 4,
                CandidateLimit = 20
            });
        return new(lexical, hybrid);
    }

    private string ConnectionString() => $"Data Source={_indexPath};Pooling=False";
    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }

    private sealed record Setup(IKnowledgeLexicalIndex Lexical, IKnowledgeHybridSearchCoordinator Hybrid);

    private sealed class SemanticEmbeddingProvider : ITextEmbeddingProvider
    {
        private static readonly EmbeddingProviderIdentity Identity = new("test", "semantic", "v1", 2);
        public int EmbeddedInputs { get; private set; }
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingProviderStatus(true, Identity));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            EmbeddedInputs += inputs.Count;
            return Task.FromResult(new EmbeddingBatchResult(Identity, inputs.Select(Vector).ToArray()));
        }
        private static float[] Vector(string text) =>
            text.Contains("concealed family papers", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Oren's Correspondence", StringComparison.Ordinal)
                ? [1f, 0f]
                : [0f, 1f];
    }

    private sealed class UnavailableEmbeddingProvider : ITextEmbeddingProvider
    {
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EmbeddingProviderStatus.Unavailable("EMBEDDING_DISABLED", "disabled for test"));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmbeddingBatchResult.Failure("EMBEDDING_DISABLED", "disabled for test"));
    }

    private sealed class MemoryVectorIndex : IKnowledgeVectorIndex
    {
        private readonly Dictionary<string, KnowledgeVectorDocument> _documents = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> ReadContentHashesAsync(KnowledgeVectorGeneration generation, string worldId, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string> result = _documents.Values
                .Where(document => document.WorldId == worldId)
                .ToDictionary(document => document.KnowledgeId, document => document.ContentHash, StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public Task ReplaceWorldAsync(KnowledgeVectorGeneration generation, string worldId, IReadOnlyList<KnowledgeVectorDocument> documents, CancellationToken cancellationToken = default)
        {
            foreach (var id in _documents.Values.Where(document => document.WorldId == worldId).Select(document => document.KnowledgeId).ToArray()) _documents.Remove(id);
            foreach (var document in documents) _documents[document.KnowledgeId] = document;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(KnowledgeVectorGeneration generation, IReadOnlyList<KnowledgeVectorDocument> documents, CancellationToken cancellationToken = default)
        {
            foreach (var document in documents) _documents[document.KnowledgeId] = document;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KnowledgeVectorCandidate>> SearchAsync(KnowledgeVectorGeneration generation, string worldId, float[] query, int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<KnowledgeVectorCandidate> result = _documents.Values
                .Where(document => document.WorldId == worldId)
                .Select(document => new KnowledgeVectorCandidate(document.KnowledgeId, Distance(document.Vector, query)))
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.KnowledgeId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task MarkGenerationStaleAsync(string generationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOtherGenerationsStaleAsync(string activeGenerationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        private static double Distance(float[] left, float[] right) => 1d - left.Zip(right).Sum(pair => pair.First * pair.Second);
    }
}
