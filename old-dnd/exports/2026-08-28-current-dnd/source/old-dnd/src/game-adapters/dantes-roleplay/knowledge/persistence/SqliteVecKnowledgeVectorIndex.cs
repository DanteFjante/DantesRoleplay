using System.Runtime.InteropServices;
using DantesRoleplay.Retrieval;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.DataAccess.Retrieval;

/// <summary>
/// Optional persistent derived index. Tables are created lazily only after the pinned native
/// extension loads, so normal migrations and FTS-only play never depend on sqlite-vec.
/// </summary>
public sealed class SqliteVecKnowledgeVectorIndex(
    string connectionString,
    SqliteVecOptions options) : IKnowledgeVectorIndex
{
    private const int MaximumDocumentsPerBatch = 1_000;
    private const int MaximumDocumentsPerWorld = 10_000;
    private readonly SqliteVecExtensionProbe _probe = new(options);

    public async Task<IReadOnlyDictionary<string, string>> ReadContentHashesAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        CancellationToken cancellationToken = default)
    {
        ValidateGeneration(generation);
        Nonblank(worldId, nameof(worldId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, null, generation.Embedding.Dimensions, cancellationToken);
        await RequireActiveGenerationAsync(connection, generation, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT knowledge_id, content_hash
            FROM knowledge_vector_document
            WHERE generation_id = $generation AND world_id = $world
            ORDER BY knowledge_id;
            """;
        command.Parameters.AddWithValue("$generation", generation.Id);
        command.Parameters.AddWithValue("$world", worldId);
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0), reader.GetString(1));
        return results;
    }

    public async Task ReplaceWorldAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        IReadOnlyList<KnowledgeVectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ValidateGeneration(generation);
        Nonblank(worldId, nameof(worldId));
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count > MaximumDocumentsPerWorld)
            throw new ArgumentOutOfRangeException(nameof(documents),
                $"A world vector replacement may contain at most {MaximumDocumentsPerWorld} documents.");
        if (documents.Any(document => document.WorldId != worldId))
            throw new ArgumentException("Every replacement document must belong to worldId.", nameof(documents));
        if (documents.Select(document => document.KnowledgeId).Distinct(StringComparer.Ordinal).Count() != documents.Count)
            throw new ArgumentException("A replacement may name one knowledge id only once.", nameof(documents));
        foreach (var document in documents) ValidateDocument(generation, document);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, generation.Embedding.Dimensions, cancellationToken);
        await EnsureGenerationAsync(connection, transaction, generation, cancellationToken);
        var table = VectorTable(generation.Embedding.Dimensions);

        var rowIds = new List<long>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT row_id FROM knowledge_vector_document
                WHERE generation_id = $generation AND world_id = $world;
                """;
            read.Parameters.AddWithValue("$generation", generation.Id);
            read.Parameters.AddWithValue("$world", worldId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rowIds.Add(reader.GetInt64(0));
        }
        foreach (var rowId in rowIds)
            await DeleteVectorAsync(connection, transaction, table, rowId, cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM knowledge_vector_document
                WHERE generation_id = $generation AND world_id = $world;
                """;
            delete.Parameters.AddWithValue("$generation", generation.Id);
            delete.Parameters.AddWithValue("$world", worldId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var document in documents)
        {
            var rowId = await InsertDocumentAsync(
                connection, transaction, generation.Id, document, cancellationToken);
            await InsertVectorAsync(
                connection, transaction, table, rowId, generation.Id, document, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        KnowledgeVectorGeneration generation,
        IReadOnlyList<KnowledgeVectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ValidateGeneration(generation);
        if (documents.Count > MaximumDocumentsPerBatch)
            throw new ArgumentOutOfRangeException(nameof(documents),
                $"A vector upsert batch may contain at most {MaximumDocumentsPerBatch} documents.");
        foreach (var document in documents) ValidateDocument(generation, document);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, generation.Embedding.Dimensions, cancellationToken);
        await EnsureGenerationAsync(connection, transaction, generation, cancellationToken);

        var table = VectorTable(generation.Embedding.Dimensions);
        foreach (var document in documents)
        {
            var rowId = await FindDocumentRowIdAsync(
                connection, transaction, generation.Id, document.KnowledgeId, cancellationToken);
            if (rowId is null)
            {
                rowId = await InsertDocumentAsync(
                    connection, transaction, generation.Id, document, cancellationToken);
            }
            else
            {
                await DeleteVectorAsync(connection, transaction, table, rowId.Value, cancellationToken);
                await UpdateDocumentAsync(
                    connection, transaction, rowId.Value, document, cancellationToken);
            }

            await InsertVectorAsync(
                connection, transaction, table, rowId.Value, generation.Id, document, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeVectorCandidate>> SearchAsync(
        KnowledgeVectorGeneration generation,
        string worldId,
        float[] query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateGeneration(generation);
        Nonblank(worldId, nameof(worldId));
        if (query.Length != generation.Embedding.Dimensions || query.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Query vector must be finite and match the generation dimensions.", nameof(query));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, null, generation.Embedding.Dimensions, cancellationToken);
        await RequireActiveGenerationAsync(connection, generation, cancellationToken);

        var table = VectorTable(generation.Embedding.Dimensions);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT d.knowledge_id, nearest.distance
            FROM (
                SELECT rowid, distance
                FROM {table}
                WHERE embedding MATCH $query
                  AND generation_id = $generation
                  AND world_id = $world
                  AND k = $limit
            ) AS nearest
            JOIN knowledge_vector_document AS d ON d.row_id = nearest.rowid
            WHERE d.generation_id = $generation
              AND d.world_id = $world
            ORDER BY nearest.distance, d.knowledge_id;
            """;
        command.Parameters.AddWithValue("$query", VectorBytes(query));
        command.Parameters.AddWithValue("$generation", generation.Id);
        command.Parameters.AddWithValue("$world", worldId);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<KnowledgeVectorCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new(reader.GetString(0), reader.GetDouble(1)));
        return results;
    }

    public async Task MarkGenerationStaleAsync(
        string generationId,
        CancellationToken cancellationToken = default)
    {
        Nonblank(generationId, nameof(generationId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureRegistrySchemaAsync(connection, null, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE knowledge_vector_generation
            SET stale_at = $stale
            WHERE id = $id AND stale_at IS NULL;
            """;
        command.Parameters.AddWithValue("$stale", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", generationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkOtherGenerationsStaleAsync(
        string activeGenerationId,
        CancellationToken cancellationToken = default)
    {
        Nonblank(activeGenerationId, nameof(activeGenerationId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureRegistrySchemaAsync(connection, null, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE knowledge_vector_generation
            SET stale_at = $stale
            WHERE id <> $active AND stale_at IS NULL;
            """;
        command.Parameters.AddWithValue("$stale", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$active", activeGenerationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var status = await _probe.CheckAsync(connection, cancellationToken);
            if (!status.Ready)
                throw new InvalidOperationException($"{status.ErrorCode}: {status.ErrorMessage}");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        int dimensions,
        CancellationToken cancellationToken)
    {
        await EnsureRegistrySchemaAsync(connection, transaction, cancellationToken);
        var table = VectorTable(dimensions);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = $"""
            CREATE VIRTUAL TABLE IF NOT EXISTS {table} USING vec0(
                generation_id TEXT PARTITION KEY,
                world_id TEXT PARTITION KEY,
                embedding FLOAT[{dimensions}] DISTANCE_METRIC=cosine
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureRegistrySchemaAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS knowledge_vector_generation (
                id TEXT PRIMARY KEY,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                revision TEXT NOT NULL,
                dimensions INTEGER NOT NULL CHECK (dimensions > 0),
                created_at TEXT NOT NULL,
                stale_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS knowledge_vector_document (
                row_id INTEGER PRIMARY KEY AUTOINCREMENT,
                generation_id TEXT NOT NULL,
                knowledge_id TEXT NOT NULL,
                world_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                UNIQUE (generation_id, knowledge_id),
                FOREIGN KEY (generation_id) REFERENCES knowledge_vector_generation(id)
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_vector_document_world
                ON knowledge_vector_document(generation_id, world_id, knowledge_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureGenerationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        KnowledgeVectorGeneration generation,
        CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT provider, model, revision, dimensions, stale_at
            FROM knowledge_vector_generation WHERE id = $id;
            """;
        read.Parameters.AddWithValue("$id", generation.Id);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var matches = reader.GetString(0) == generation.Embedding.Provider &&
                          reader.GetString(1) == generation.Embedding.Model &&
                          reader.GetString(2) == generation.Embedding.Revision &&
                          reader.GetInt32(3) == generation.Embedding.Dimensions;
            var stale = !reader.IsDBNull(4);
            if (!matches) throw new InvalidOperationException(
                "The vector generation ID already exists with a different embedding identity.");
            if (stale) throw new InvalidOperationException("The vector generation is stale.");
            return;
        }
        await reader.DisposeAsync();

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO knowledge_vector_generation
              (id, provider, model, revision, dimensions, created_at, stale_at)
            VALUES ($id, $provider, $model, $revision, $dimensions, $created, NULL);
            """;
        insert.Parameters.AddWithValue("$id", generation.Id);
        insert.Parameters.AddWithValue("$provider", generation.Embedding.Provider);
        insert.Parameters.AddWithValue("$model", generation.Embedding.Model);
        insert.Parameters.AddWithValue("$revision", generation.Embedding.Revision);
        insert.Parameters.AddWithValue("$dimensions", generation.Embedding.Dimensions);
        insert.Parameters.AddWithValue("$created", generation.CreatedAt.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RequireActiveGenerationAsync(
        SqliteConnection connection,
        KnowledgeVectorGeneration generation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider, model, revision, dimensions, stale_at
            FROM knowledge_vector_generation WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", generation.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The vector generation is not indexed.");
        if (!reader.IsDBNull(4)) throw new InvalidOperationException("The vector generation is stale.");
        if (reader.GetString(0) != generation.Embedding.Provider ||
            reader.GetString(1) != generation.Embedding.Model ||
            reader.GetString(2) != generation.Embedding.Revision ||
            reader.GetInt32(3) != generation.Embedding.Dimensions)
            throw new InvalidOperationException("The requested embedding identity does not match the stored generation.");
    }

    private static async Task<long?> FindDocumentRowIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        string knowledgeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT row_id FROM knowledge_vector_document
            WHERE generation_id = $generation AND knowledge_id = $knowledge;
            """;
        command.Parameters.AddWithValue("$generation", generationId);
        command.Parameters.AddWithValue("$knowledge", knowledgeId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private static async Task<long> InsertDocumentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        KnowledgeVectorDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO knowledge_vector_document
              (generation_id, knowledge_id, world_id, content_hash)
            VALUES ($generation, $knowledge, $world, $hash);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$generation", generationId);
        command.Parameters.AddWithValue("$knowledge", document.KnowledgeId);
        command.Parameters.AddWithValue("$world", document.WorldId);
        command.Parameters.AddWithValue("$hash", document.ContentHash);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpdateDocumentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long rowId,
        KnowledgeVectorDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE knowledge_vector_document
            SET world_id = $world, content_hash = $hash
            WHERE row_id = $row;
            """;
        command.Parameters.AddWithValue("$world", document.WorldId);
        command.Parameters.AddWithValue("$hash", document.ContentHash);
        command.Parameters.AddWithValue("$row", rowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        long rowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE rowid = $row;";
        command.Parameters.AddWithValue("$row", rowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        long rowId,
        string generationId,
        KnowledgeVectorDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {table}(rowid, generation_id, world_id, embedding)
            VALUES ($row, $generation, $world, $embedding);
            """;
        command.Parameters.AddWithValue("$row", rowId);
        command.Parameters.AddWithValue("$generation", generationId);
        command.Parameters.AddWithValue("$world", document.WorldId);
        command.Parameters.AddWithValue("$embedding", VectorBytes(document.Vector));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] VectorBytes(float[] vector) =>
        MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private static string VectorTable(int dimensions)
    {
        if (dimensions is < 1 or > 16_384) throw new ArgumentOutOfRangeException(nameof(dimensions));
        return $"knowledge_vector_{dimensions}";
    }

    private static void ValidateGeneration(KnowledgeVectorGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        Nonblank(generation.Id, nameof(generation.Id));
        Nonblank(generation.Embedding.Provider, nameof(generation.Embedding.Provider));
        Nonblank(generation.Embedding.Model, nameof(generation.Embedding.Model));
        Nonblank(generation.Embedding.Revision, nameof(generation.Embedding.Revision));
        _ = VectorTable(generation.Embedding.Dimensions);
    }

    private static void ValidateDocument(
        KnowledgeVectorGeneration generation,
        KnowledgeVectorDocument document)
    {
        Nonblank(document.KnowledgeId, nameof(document.KnowledgeId));
        Nonblank(document.WorldId, nameof(document.WorldId));
        if (document.ContentHash.Length != 64 ||
            document.ContentHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("ContentHash must be exactly 64 hexadecimal characters.",
                nameof(document));
        if (document.Vector.Length != generation.Embedding.Dimensions ||
            document.Vector.Any(value => !float.IsFinite(value)))
            throw new ArgumentException(
                "Every document vector must be finite and match the generation dimensions.",
                nameof(document));
    }

    private static void Nonblank(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500)
            throw new ArgumentException("Value must be nonblank and at most 500 characters.", parameter);
    }
}
