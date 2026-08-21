using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeFactAnswerLiveTests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"knowledge-answer-{Guid.NewGuid():n}");
    private readonly string _indexPath = Path.Combine(Path.GetTempPath(), $"knowledge-answer-{Guid.NewGuid():n}.db");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
        foreach (var path in new[] { _indexPath, $"{_indexPath}-shm", $"{_indexPath}-wal" })
            if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task Live_qwen3_8b_answers_only_from_hydrated_candidates_when_enabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION"),
                "1",
                StringComparison.Ordinal)) return;
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var options = new KnowledgeRetrievalOptions { Vector = new() { Enabled = false } };
        var source = new KnowledgeSearchDocumentSource(world);
        var hybrid = new KnowledgeHybridSearchCoordinator(
            new KnowledgeTimelineCoordinator(db, world),
            source,
            new SqliteKnowledgeLexicalIndex($"Data Source={_indexPath};Pooling=False"),
            new DisabledEmbedding(),
            new DisabledVector(),
            options);
        await hybrid.RebuildWorldAsync(World);
        var answer = new KnowledgeFactAnswerCoordinator(
            hybrid,
            new OllamaStructuredCompletionProvider(new HttpClient(), new()
            {
                Enabled = true,
                Model = "qwen3:8b",
                Timeout = TimeSpan.FromMinutes(2)
            }));

        var result = await answer.AnswerAsync(new(World, "market archive", ["fact"], CandidateLimit: 4));

        Assert.True(result.Ok);
        Assert.Equal("local-model", result.Mode);
        Assert.False(result.Unknown);
        Assert.NotEmpty(result.Statements);
        var available = result.Candidates.Select(candidate => candidate.KnowledgeId).ToHashSet(StringComparer.Ordinal);
        Assert.All(result.Statements.SelectMany(statement => statement.Citations), citation => Assert.Contains(citation.KnowledgeId, available));
    }

    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }

    private sealed class DisabledEmbedding : ITextEmbeddingProvider
    {
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class DisabledVector : IKnowledgeVectorIndex
    {
        public Task<IReadOnlyDictionary<string, string>> ReadContentHashesAsync(KnowledgeVectorGeneration generation, string worldId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task ReplaceWorldAsync(KnowledgeVectorGeneration generation, string worldId, IReadOnlyList<KnowledgeVectorDocument> documents, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task UpsertAsync(KnowledgeVectorGeneration generation, IReadOnlyList<KnowledgeVectorDocument> documents, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<KnowledgeVectorCandidate>> SearchAsync(KnowledgeVectorGeneration generation, string worldId, float[] query, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task MarkGenerationStaleAsync(string generationId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task MarkOtherGenerationsStaleAsync(string activeGenerationId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
