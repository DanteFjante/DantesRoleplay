using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.Retrieval;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.DataAccess.Retrieval;

/// <summary>Built-in SQLite FTS5 implementation for disposable trusted-GM knowledge projections.</summary>
public sealed class SqliteKnowledgeLexicalIndex(string connectionString) : IKnowledgeLexicalIndex
{
    private const int MaximumBatch = 10_000;
    private static readonly Regex Token = new("[\\p{L}\\p{N}_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "fact", "rumour", "secret", "clue" };

    public async Task ReplaceWorldAsync(string worldId, IReadOnlyList<KnowledgeLexicalDocument> documents, CancellationToken cancellationToken = default)
    {
        ValidId(worldId, nameof(worldId));
        Validate(documents);
        if (documents.Any(document => document.WorldId != worldId)) throw new ArgumentException("Every replacement document must belong to worldId.", nameof(documents));
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAsync(connection, transaction, cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM knowledge_lexical_fts WHERE world_id = $world;";
            delete.Parameters.AddWithValue("$world", worldId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAsync(connection, transaction, documents, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertAsync(IReadOnlyList<KnowledgeLexicalDocument> documents, CancellationToken cancellationToken = default)
    {
        Validate(documents);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAsync(connection, transaction, cancellationToken);
        foreach (var document in documents)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM knowledge_lexical_fts WHERE knowledge_id = $id;";
            delete.Parameters.AddWithValue("$id", document.KnowledgeId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAsync(connection, transaction, documents, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeLexicalCandidate>> SearchAsync(KnowledgeLexicalSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.WorldId) || !Text(request.Query, 300) || request.Limit is < 1 or > 100 || request.AsOfMinute is < 0 or > 1_000_000_000)
            throw new ArgumentException("Search requires a bounded world, query, limit, and optional world minute.", nameof(request));
        var kinds = Closed(request.Kinds, Kinds, "Kinds");
        var subjects = Identifiers(request.SubjectIds, "SubjectIds");
        var allowed = Identifiers(request.AllowedKnowledgeIds, "AllowedKnowledgeIds");
        var match = Match(request.Query);
        if (match.Length == 0) return [];

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureAsync(connection, null, cancellationToken);
        await using var command = connection.CreateCommand();
        var filters = new List<string> { "knowledge_lexical_fts MATCH $match", "world_id = $world" };
        command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$world", request.WorldId);
        if (!request.IncludeArchived) filters.Add("status <> 'archived'");
        if (request.AsOfMinute is long minute)
        {
            filters.Add("(valid_from IS NULL OR (valid_from <= $minute AND (valid_until IS NULL OR valid_until > $minute)))");
            command.Parameters.AddWithValue("$minute", minute);
        }
        In(filters, command, "kind", "kind", kinds);
        In(filters, command, "subject_id", "subject", subjects);
        if (request.AllowedKnowledgeIds is not null)
        {
            // One JSON parameter avoids SQLite's parameter limit while still constraining FTS
            // before ranking and LIMIT. An empty allowlist intentionally yields no rows.
            filters.Add("knowledge_id IN (SELECT value FROM json_each($allowed))");
            command.Parameters.AddWithValue("$allowed", JsonSerializer.Serialize(allowed));
        }
        command.CommandText = $"""
            SELECT knowledge_id, bm25(knowledge_lexical_fts) AS rank
            FROM knowledge_lexical_fts
            WHERE {string.Join(" AND ", filters)}
            ORDER BY rank, knowledge_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", request.Limit);
        var results = new List<KnowledgeLexicalCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetString(0), reader.GetDouble(1)));
        return results;
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<KnowledgeLexicalDocument> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO knowledge_lexical_fts
                  (knowledge_id, world_id, kind, status, subject_id, sensitivity, valid_from, valid_until, content_hash, text)
                VALUES ($id, $world, $kind, $status, $subject, $sensitivity, $from, $until, $hash, $text);
                """;
            insert.Parameters.AddWithValue("$id", document.KnowledgeId); insert.Parameters.AddWithValue("$world", document.WorldId);
            insert.Parameters.AddWithValue("$kind", document.Kind); insert.Parameters.AddWithValue("$status", document.Status);
            insert.Parameters.AddWithValue("$subject", document.SubjectId); insert.Parameters.AddWithValue("$sensitivity", document.Sensitivity);
            insert.Parameters.AddWithValue("$from", document.ValidFromMinute is null ? DBNull.Value : document.ValidFromMinute.Value);
            insert.Parameters.AddWithValue("$until", document.ValidUntilMinute is null ? DBNull.Value : document.ValidUntilMinute.Value);
            insert.Parameters.AddWithValue("$hash", document.ContentHash); insert.Parameters.AddWithValue("$text", document.Text);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS knowledge_lexical_fts USING fts5(
                knowledge_id UNINDEXED,
                world_id UNINDEXED,
                kind UNINDEXED,
                status UNINDEXED,
                subject_id UNINDEXED,
                sensitivity UNINDEXED,
                valid_from UNINDEXED,
                valid_until UNINDEXED,
                content_hash UNINDEXED,
                text
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static void Validate(IReadOnlyList<KnowledgeLexicalDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count > MaximumBatch) throw new ArgumentOutOfRangeException(nameof(documents));
        if (documents.Select(document => document.KnowledgeId).Distinct(StringComparer.Ordinal).Count() != documents.Count) throw new ArgumentException("A batch may name one knowledge id only once.", nameof(documents));
        foreach (var document in documents)
        {
            if (!Id(document.KnowledgeId) || !Id(document.WorldId) || !Id(document.SubjectId) || document.Kind is not ("fact" or "rumour" or "secret" or "clue") || !Text(document.Status, 40) || !Text(document.Sensitivity, 40) || !Text(document.Text, 20_000) || document.ContentHash.Length != 64 || document.ContentHash.Any(character => !Uri.IsHexDigit(character)) || document.ValidFromMinute is < 0 or > 1_000_000_000 || document.ValidUntilMinute is < 0 or > 1_000_000_000 || (document.ValidUntilMinute is long until && document.ValidFromMinute is long from && until <= from))
                throw new ArgumentException("A lexical document is malformed.", nameof(documents));
        }
    }
    private static void In(List<string> filters, SqliteCommand command, string column, string prefix, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;
        var names = new List<string>();
        for (var index = 0; index < values.Count; index++) { var name = $"${prefix}{index}"; names.Add(name); command.Parameters.AddWithValue(name, values[index]); }
        filters.Add($"{column} IN ({string.Join(", ", names)})");
    }
    private static IReadOnlyList<string> Closed(IReadOnlyList<string>? values, IReadOnlySet<string> allowed, string name) => values is null ? [] : values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Select(value => allowed.Contains(value) ? value : throw new ArgumentException($"{name} contains an unsupported value.", name)).ToArray();
    private static IReadOnlyList<string> Identifiers(IReadOnlyList<string>? values, string name) => values is null ? [] : values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Select(value => Id(value) ? value : throw new ArgumentException($"{name} contains an invalid id.", name)).ToArray();
    private static string Match(string query) => string.Join(" AND ", Token.Matches(query).Select(match => $"\"{match.Value.Replace("\"", "\"\"")}\""));
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
    private static void ValidId(string value, string parameter) { if (!Id(value)) throw new ArgumentException("Value must be a canonical id.", parameter); }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
}
