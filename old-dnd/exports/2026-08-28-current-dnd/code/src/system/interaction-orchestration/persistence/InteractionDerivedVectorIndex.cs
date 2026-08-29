using Microsoft.Data.Sqlite;
using DantesRoleplay.CatalogNavigation;

namespace DantesRoleplay.Interactions;

/// <summary>Validated location for the non-authoritative retrieval cache.</summary>
public sealed record InteractionDerivedIndexLocation
{
    private InteractionDerivedIndexLocation(string directory, string databasePath)
    {
        Directory = directory;
        DatabasePath = databasePath;
    }

    public string Directory { get; }
    public string DatabasePath { get; }

    public static InteractionDerivedIndexLocation Create(string configuredDirectory, string databaseFileName = "interaction-retrieval.sqlite")
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory) || !Path.IsPathFullyQualified(configuredDirectory))
            throw new InteractionContractException("INVALID_DERIVED_INDEX_DIRECTORY", "The derived-index directory must be an absolute configured path.");
        if (string.IsNullOrWhiteSpace(databaseFileName) || databaseFileName != Path.GetFileName(databaseFileName)
            || !databaseFileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
            throw new InteractionContractException("INVALID_DERIVED_INDEX_FILE", "The derived-index file name is invalid.");
        var directory = Path.GetFullPath(configuredDirectory);
        var root = Path.GetPathRoot(directory);
        if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InteractionContractException("INVALID_DERIVED_INDEX_DIRECTORY", "The derived-index directory may not be a filesystem root.");
        var database = Path.GetFullPath(Path.Combine(directory, databaseFileName));
        var relative = Path.GetRelativePath(directory, database);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InteractionContractException("INVALID_DERIVED_INDEX_FILE", "The derived-index file escapes its configured directory.");
        return new(directory, database);
    }
}

/// <summary>
/// Disposable SQLite vector cache. It deliberately has no EF model or kernel migration because its
/// rows are rebuildable hints, not authoritative application or receipt state.
/// </summary>
public sealed class SqliteInteractionDerivedVectorIndex(InteractionDerivedIndexLocation location) : IInteractionDerivedVectorIndex
{
    private readonly InteractionDerivedIndexLocation _location = location ?? throw new ArgumentNullException(nameof(location));

    public async Task ReplaceAsync(
        InteractionRetrievalGeneration generation,
        IReadOnlyList<InteractionVectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        Validate(generation, documents);
        Directory.CreateDirectory(_location.Directory);
        await using var connection = new SqliteConnection($"Data Source={_location.DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await DeleteAsync(connection, transaction, generation.GenerationKey, cancellationToken);
            await InsertGenerationAsync(connection, transaction, generation, cancellationToken);
            foreach (var document in documents.OrderBy(value => value.Reference.QualifiedId, StringComparer.Ordinal))
                await InsertDocumentAsync(connection, transaction, generation, document, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<InteractionVectorCandidate>> SearchAsync(
        InteractionRetrievalGeneration generation,
        float[] query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateGeneration(generation);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length != generation.Embedding.Dimensions || query.Any(value => !float.IsFinite(value)) || limit is < 1 or > 200)
            throw new InteractionContractException("INVALID_VECTOR_QUERY", "The vector query is invalid or unbounded.");
        if (!File.Exists(_location.DatabasePath))
            throw new InteractionContractException("VECTOR_INDEX_UNAVAILABLE", "The disposable vector index is not present.");

        await using var connection = new SqliteConnection($"Data Source={_location.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        if (!await GenerationExistsAsync(connection, generation, cancellationToken))
            throw new InteractionContractException("VECTOR_INDEX_STALE", "The disposable vector index has no current generation.");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.QualifiedId, v.Vector
            FROM interaction_retrieval_generations AS g
            JOIN interaction_retrieval_documents AS d ON d.GenerationKey = g.GenerationKey
            JOIN interaction_retrieval_vectors AS v ON v.GenerationKey = d.GenerationKey AND v.QualifiedId = d.QualifiedId
            WHERE g.GenerationKey = $key
              AND g.ApplicationId = $applicationId
              AND g.Lane = $lane
              AND g.CatalogFingerprint = $catalogFingerprint
              AND g.FormatVersion = $formatVersion
              AND g.Provider = $provider AND g.Model = $model AND g.Revision = $revision AND g.Dimensions = $dimensions
            """;
        BindGeneration(command, generation);
        var candidates = new List<InteractionVectorCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vector = ToVector(reader.GetFieldValue<byte[]>(1), generation.Embedding.Dimensions);
            candidates.Add(new(reader.GetString(0), Distance(query, vector)));
        }
        return Array.AsReadOnly(candidates.OrderBy(value => value.Distance).ThenBy(value => value.QualifiedId, StringComparer.Ordinal)
            .Take(limit).ToArray());
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS interaction_retrieval_generations (
                GenerationKey TEXT NOT NULL PRIMARY KEY,
                ApplicationId TEXT NOT NULL,
                Lane INTEGER NOT NULL,
                CatalogFingerprint TEXT NOT NULL,
                FormatVersion TEXT NOT NULL,
                Provider TEXT NOT NULL,
                Model TEXT NOT NULL,
                Revision TEXT NOT NULL,
                Dimensions INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS interaction_retrieval_documents (
                GenerationKey TEXT NOT NULL,
                QualifiedId TEXT NOT NULL,
                ContentFingerprint TEXT NOT NULL,
                Version INTEGER NOT NULL,
                SearchText TEXT NOT NULL,
                PRIMARY KEY (GenerationKey, QualifiedId),
                FOREIGN KEY (GenerationKey) REFERENCES interaction_retrieval_generations (GenerationKey) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS interaction_retrieval_vectors (
                GenerationKey TEXT NOT NULL,
                QualifiedId TEXT NOT NULL,
                Vector BLOB NOT NULL,
                PRIMARY KEY (GenerationKey, QualifiedId),
                FOREIGN KEY (GenerationKey, QualifiedId) REFERENCES interaction_retrieval_documents (GenerationKey, QualifiedId) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> GenerationExistsAsync(SqliteConnection connection, InteractionRetrievalGeneration generation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM interaction_retrieval_generations
            WHERE GenerationKey = $key AND ApplicationId = $applicationId AND Lane = $lane
              AND CatalogFingerprint = $catalogFingerprint AND FormatVersion = $formatVersion
              AND Provider = $provider AND Model = $model AND Revision = $revision AND Dimensions = $dimensions
            LIMIT 1;
            """;
        BindGeneration(command, generation);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task DeleteAsync(SqliteConnection connection, SqliteTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM interaction_retrieval_generations WHERE GenerationKey = $key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGenerationAsync(SqliteConnection connection, SqliteTransaction transaction, InteractionRetrievalGeneration generation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO interaction_retrieval_generations
            (GenerationKey, ApplicationId, Lane, CatalogFingerprint, FormatVersion, Provider, Model, Revision, Dimensions)
            VALUES ($key, $applicationId, $lane, $catalogFingerprint, $formatVersion, $provider, $model, $revision, $dimensions);
            """;
        BindGeneration(command, generation);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDocumentAsync(SqliteConnection connection, SqliteTransaction transaction, InteractionRetrievalGeneration generation, InteractionVectorDocument document, CancellationToken cancellationToken)
    {
        await using var documentCommand = connection.CreateCommand();
        documentCommand.Transaction = transaction;
        documentCommand.CommandText = """
            INSERT INTO interaction_retrieval_documents (GenerationKey, QualifiedId, ContentFingerprint, Version, SearchText)
            VALUES ($key, $qualifiedId, $contentFingerprint, $version, $searchText);
            """;
        documentCommand.Parameters.AddWithValue("$key", generation.GenerationKey);
        documentCommand.Parameters.AddWithValue("$qualifiedId", document.Reference.QualifiedId);
        documentCommand.Parameters.AddWithValue("$contentFingerprint", document.Reference.ContentFingerprint);
        documentCommand.Parameters.AddWithValue("$version", document.Reference.Version);
        documentCommand.Parameters.AddWithValue("$searchText", document.SearchText);
        await documentCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var vectorCommand = connection.CreateCommand();
        vectorCommand.Transaction = transaction;
        vectorCommand.CommandText = "INSERT INTO interaction_retrieval_vectors (GenerationKey, QualifiedId, Vector) VALUES ($key, $qualifiedId, $vector);";
        vectorCommand.Parameters.AddWithValue("$key", generation.GenerationKey);
        vectorCommand.Parameters.AddWithValue("$qualifiedId", document.Reference.QualifiedId);
        vectorCommand.Parameters.Add("$vector", SqliteType.Blob).Value = ToBytes(document.Vector);
        await vectorCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void BindGeneration(SqliteCommand command, InteractionRetrievalGeneration generation)
    {
        command.Parameters.AddWithValue("$key", generation.GenerationKey);
        command.Parameters.AddWithValue("$applicationId", generation.ApplicationId.Value);
        command.Parameters.AddWithValue("$lane", (int)generation.Lane);
        command.Parameters.AddWithValue("$catalogFingerprint", generation.CatalogFingerprint);
        command.Parameters.AddWithValue("$formatVersion", generation.RetrievalFormatVersion);
        command.Parameters.AddWithValue("$provider", generation.Embedding.Provider);
        command.Parameters.AddWithValue("$model", generation.Embedding.Model);
        command.Parameters.AddWithValue("$revision", generation.Embedding.Revision);
        command.Parameters.AddWithValue("$dimensions", generation.Embedding.Dimensions);
    }

    private static void Validate(InteractionRetrievalGeneration generation, IReadOnlyList<InteractionVectorDocument> documents)
    {
        ValidateGeneration(generation);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count > CatalogNavigationLimits.MaximumRecords || documents.Select(value => value.Reference.QualifiedId).Distinct(StringComparer.Ordinal).Count() != documents.Count
            || documents.Any(value => value.Reference.ApplicationId != generation.ApplicationId || value.Reference.Lane != generation.Lane
                || value.Reference.CatalogFingerprint != generation.CatalogFingerprint || value.Vector.Length != generation.Embedding.Dimensions))
            throw new InteractionContractException("INVALID_VECTOR_GENERATION", "The vector generation documents are invalid.");
    }

    private static void ValidateGeneration(InteractionRetrievalGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(generation.ApplicationId);
        ArgumentNullException.ThrowIfNull(generation.Embedding);
        if (generation.GenerationKey is not { Length: 64 } || generation.GenerationKey.Any(value => !(char.IsAsciiDigit(value) || value is >= 'A' and <= 'F'))
            || generation.CatalogFingerprint is not { Length: 64 } || generation.CatalogFingerprint.Any(value => !(char.IsAsciiDigit(value) || value is >= 'A' and <= 'F'))
            || generation.RetrievalFormatVersion != InteractionRetrievalFingerprint.FormatVersion || !Enum.IsDefined(generation.Lane)
            || string.IsNullOrWhiteSpace(generation.Embedding.Provider) || string.IsNullOrWhiteSpace(generation.Embedding.Model)
            || string.IsNullOrWhiteSpace(generation.Embedding.Revision) || generation.Embedding.Dimensions is < 1 or > InteractionRetrievalLimits.MaximumVectorDimensions)
            throw new InteractionContractException("INVALID_VECTOR_GENERATION", "The vector generation identity is invalid.");
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ToVector(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float))
            throw new InteractionContractException("VECTOR_STORAGE_INVALID", "The derived index contains an invalid vector.");
        var vector = new float[dimensions];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        if (vector.Any(value => !float.IsFinite(value)))
            throw new InteractionContractException("VECTOR_STORAGE_INVALID", "The derived index contains a non-finite vector.");
        return vector;
    }

    private static double Distance(float[] left, float[] right)
    {
        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }
        if (leftMagnitude <= 0 || rightMagnitude <= 0) return 1d;
        return 1d - dot / Math.Sqrt(leftMagnitude * rightMagnitude);
    }
}
