using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SqliteVecExtensionProbeTests
{
    [Fact]
    public async Task Disabled_probe_never_opens_the_connection()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");

        var status = await new SqliteVecExtensionProbe(new()).CheckAsync(connection);

        Assert.False(status.Ready);
        Assert.Equal("VECTOR_INDEX_DISABLED", status.ErrorCode);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Missing_extension_has_a_stable_recoverable_failure()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        var path = Path.Combine(Path.GetTempPath(), $"missing-vec0-{Guid.NewGuid():N}.dll");

        var status = await new SqliteVecExtensionProbe(new()
        {
            Enabled = true,
            ExtensionPath = path
        }).CheckAsync(connection);

        Assert.False(status.Ready);
        Assert.Equal("VECTOR_EXTENSION_MISSING", status.ErrorCode);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Configured_native_extension_supports_a_real_knn_query()
    {
        var path = Environment.GetEnvironmentVariable("DANTESROLEPLAY_SQLITE_VEC_EXTENSION");
        if (string.IsNullOrWhiteSpace(path)) return;

        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var status = await new SqliteVecExtensionProbe(new()
        {
            Enabled = true,
            ExtensionPath = path
        }).CheckAsync(connection);

        Assert.True(status.Ready, $"{status.ErrorCode}: {status.ErrorMessage}");
        Assert.False(string.IsNullOrWhiteSpace(status.Version));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE temp.knowledge_vectors USING vec0(embedding float[3]);
            INSERT INTO knowledge_vectors(rowid, embedding) VALUES
              (1, '[1,0,0]'),
              (2, '[0,1,0]');
            SELECT rowid
            FROM knowledge_vectors
            WHERE embedding MATCH '[0.9,0.1,0]'
            ORDER BY distance
            LIMIT 1;
            """;

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Pinned_hash_rejects_a_substituted_native_artifact_before_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"not-sqlite-vec-{Guid.NewGuid():N}.dll");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        try
        {
            await using var connection = new SqliteConnection("Filename=:memory:");
            var status = await new SqliteVecExtensionProbe(new()
            {
                Enabled = true,
                ExtensionPath = path
            }).CheckAsync(connection);

            Assert.False(status.Ready);
            Assert.Equal("VECTOR_EXTENSION_HASH_MISMATCH", status.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Persistent_index_partitions_worlds_upserts_and_survives_reopen_and_backup()
    {
        var extension = Environment.GetEnvironmentVariable("DANTESROLEPLAY_SQLITE_VEC_EXTENSION");
        if (string.IsNullOrWhiteSpace(extension)) return;

        var database = Path.Combine(Path.GetTempPath(), $"knowledge-vectors-{Guid.NewGuid():N}.db");
        var backup = Path.Combine(Path.GetTempPath(), $"knowledge-vectors-backup-{Guid.NewGuid():N}.db");
        try
        {
            var options = new SqliteVecOptions { Enabled = true, ExtensionPath = extension };
            var connectionString = $"Data Source={database};Pooling=False";
            var generation = Generation("generation.one", "digest-one");
            var index = new SqliteVecKnowledgeVectorIndex(connectionString, options);

            await index.UpsertAsync(generation,
            [
                Document("fact.sun", "world.a", 'A', [1f, 0f, 0f]),
                Document("fact.moon", "world.a", 'B', [0f, 1f, 0f]),
                Document("fact.outsider", "world.b", 'C', [1f, 0f, 0f])
            ]);

            var first = await index.SearchAsync(generation, "world.a", [0.99f, 0.01f, 0f], 10);
            Assert.Equal(["fact.sun", "fact.moon"], first.Select(x => x.KnowledgeId));
            Assert.DoesNotContain(first, x => x.KnowledgeId == "fact.outsider");

            await index.ReplaceWorldAsync(generation, "world.a",
            [
                Document("fact.moon", "world.a", 'B', [0f, 1f, 0f])
            ]);
            var afterReplace = await index.SearchAsync(generation, "world.a", [1f, 0f, 0f], 10);
            Assert.Equal(["fact.moon"], afterReplace.Select(x => x.KnowledgeId));
            var otherWorld = await index.SearchAsync(generation, "world.b", [1f, 0f, 0f], 10);
            Assert.Equal(["fact.outsider"], otherWorld.Select(x => x.KnowledgeId));

            await index.UpsertAsync(generation,
            [
                Document("fact.sun", "world.a", 'D', [0f, 1f, 0f])
            ]);
            var afterUpsert = await index.SearchAsync(generation, "world.a", [1f, 0f, 0f], 10);
            Assert.Equal(2, afterUpsert.Count);
            Assert.Equal(2, afterUpsert.Select(x => x.KnowledgeId).Distinct().Count());

            var reopened = new SqliteVecKnowledgeVectorIndex(connectionString, options);
            var afterReopen = await reopened.SearchAsync(generation, "world.a", [0f, 1f, 0f], 10);
            Assert.Equal(["fact.moon", "fact.sun"],
                afterReopen.Select(x => x.KnowledgeId).Order(StringComparer.Ordinal));

            await using (var source = new SqliteConnection(connectionString))
            await using (var destination = new SqliteConnection($"Data Source={backup};Pooling=False"))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            var copied = new SqliteVecKnowledgeVectorIndex($"Data Source={backup};Pooling=False", options);
            var afterBackup = await copied.SearchAsync(generation, "world.a", [0f, 1f, 0f], 10);
            Assert.Equal(2, afterBackup.Count);
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    [Fact]
    public async Task Stale_generation_cannot_be_searched_or_written_and_identity_cannot_drift()
    {
        var extension = Environment.GetEnvironmentVariable("DANTESROLEPLAY_SQLITE_VEC_EXTENSION");
        if (string.IsNullOrWhiteSpace(extension)) return;

        var database = Path.Combine(Path.GetTempPath(), $"knowledge-vectors-{Guid.NewGuid():N}.db");
        try
        {
            var index = new SqliteVecKnowledgeVectorIndex($"Data Source={database};Pooling=False", new()
            {
                Enabled = true,
                ExtensionPath = extension
            });
            var generation = Generation("generation.stale", "digest-one");
            var document = Document("fact.sun", "world.a", 'A', [1f, 0f, 0f]);
            await index.UpsertAsync(generation, [document]);

            var drifted = Generation(generation.Id, "different-digest");
            await Assert.ThrowsAsync<InvalidOperationException>(() => index.UpsertAsync(drifted, [document]));

            await index.MarkGenerationStaleAsync(generation.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                index.SearchAsync(generation, "world.a", [1f, 0f, 0f], 5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => index.UpsertAsync(generation, [document]));
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private static KnowledgeVectorGeneration Generation(string id, string revision) =>
        new(id, new("ollama", "qwen3-embedding:4b", revision, 3), DateTimeOffset.UtcNow);

    private static KnowledgeVectorDocument Document(
        string id,
        string world,
        char hashCharacter,
        float[] vector) => new(id, world, new string(hashCharacter, 64), vector);
}
