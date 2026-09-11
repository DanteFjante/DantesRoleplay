using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskAiAccountingModelTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Ef_model_matches_the_independent_ai_fixture_schema()
    {
        await using var model = await ModelDatabase.CreateAsync();
        await using var fixture = await SystemTaskAiAccountingSchemaFixture.CreateAsync();
        await using var modelConnection = await model.OpenAsync();
        await using var fixtureConnection = await fixture.OpenAsync();
        var tables = await StringsAsync(fixtureConnection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'system_task_ai_%' ORDER BY name");

        Assert.Equal(new[]
        {
            "system_task_ai_ceiling", "system_task_ai_dispatch_evidence",
            "system_task_ai_reservation", "system_task_ai_reservation_ancestor"
        }, tables);
        foreach (var table in tables)
        {
            Assert.Equal((await ReadColumnsAsync(fixtureConnection, table)).OrderBy(value => value, StringComparer.Ordinal),
                (await ReadColumnsAsync(modelConnection, table)).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(await ReadIndexesAsync(fixtureConnection, table), await ReadIndexesAsync(modelConnection, table));
            Assert.Equal((await ReadForeignKeysAsync(fixtureConnection, table)).OrderBy(value => value, StringComparer.Ordinal),
                (await ReadForeignKeysAsync(modelConnection, table)).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(ExtractChecks(await CreateSqlAsync(fixtureConnection, table)),
                ExtractChecks(await CreateSqlAsync(modelConnection, table)));
        }
    }

    [Fact]
    public async Task Model_schema_accepts_exact_dispatch_and_rejects_utf8_overflow_and_token_overflow()
    {
        await using var database = await ModelDatabase.CreateAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        var store = new SqliteSystemTaskLifecycleStore(database.ConnectionString, time);
        var request = new SystemTaskDurableSubmissionRequest(new(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", "command.ai.model", "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, time.GetUtcNow().AddHours(1).UtcDateTime)),
            new("fixture-app.jobs.procedure-one", 1, Hash), "{}");
        var handle = (await store.EnqueueAsync(request)).Handle!;
        var lease = (await store.ClaimNextAsync("worker.ai", TimeSpan.FromMinutes(1)))!;
        await using var connection = await database.OpenAsync();
        await InsertCeilingAsync(connection, handle.TaskId);
        await InsertReservationAsync(connection, "reservation.record.1", "reservation.1", lease);
        await InsertReservationAsync(connection, "reservation.record.2", "reservation.2", lease);

        await ExecuteAsync(connection, """
            INSERT INTO system_task_ai_dispatch_evidence(
                record_reference, sequence, event_reference, payload_fingerprint, kind, dispatch_kind,
                request_fingerprint, provider_id, model_id, profile_fingerprint, schema_fingerprint,
                request_json, observed_at_utc)
            VALUES ('reservation.record.1', 0, 'event.dispatch.1', $hash, 'dispatch', 'provider',
                $hash, 'provider.1', 'model.1', $hash, $hash, '{}', $now)
            """, ("$hash", Hash), ("$now", time.GetUtcNow().ToString("O")));

        var oversized = "\"" + new string('\u00e9', 32_769) + "\"";
        var utf8 = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO system_task_ai_dispatch_evidence(
                record_reference, sequence, event_reference, payload_fingerprint, kind, dispatch_kind,
                request_fingerprint, provider_id, model_id, profile_fingerprint, schema_fingerprint,
                request_json, observed_at_utc)
            VALUES ('reservation.record.2', 0, 'event.dispatch.2', $hash, 'dispatch', 'provider',
                $hash, 'provider.1', 'model.1', $hash, $hash, $json, $now)
            """, ("$hash", Hash), ("$json", oversized), ("$now", time.GetUtcNow().ToString("O"))));
        Assert.Equal(19, utf8.SqliteErrorCode);

        var overflow = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO system_task_ai_dispatch_evidence(
                record_reference, sequence, event_reference, payload_fingerprint, kind, dispatch_kind,
                request_fingerprint, provider_id, model_id, profile_fingerprint, schema_fingerprint,
                response_fingerprint, input_tokens, output_tokens, total_tokens, is_complete,
                completion_kind, observed_at_utc)
            VALUES ('reservation.record.1', 1, 'event.usage.1', $hash, 'usage', 'provider',
                $hash, 'provider.1', 'model.1', $hash, $hash, $hash,
                9223372036854775807, 1, 9223372036854775807, 1, 'returned', $now)
            """, ("$hash", Hash), ("$now", time.GetUtcNow().ToString("O"))));
        Assert.Equal(19, overflow.SqliteErrorCode);
    }

    private static async Task InsertCeilingAsync(SqliteConnection connection, string taskId) =>
        await ExecuteAsync(connection, """
            INSERT INTO system_task_ai_ceiling(
                task_id, enrollment_fingerprint, profile_id, profile_version, profile_fingerprint,
                grant_reference, grant_revision, grant_fingerprint, definition_id, definition_version,
                definition_fingerprint, output_schema_fingerprint, mode, maximum_provider_tokens,
                maximum_tool_calls, maximum_concurrent_provider_requests, deadline_utc, created_at_utc)
            VALUES ($task, $hash, 'profile.1', 1, $hash, 'grant.1', 'revision.1', $hash,
                'fixture-app.jobs.procedure-one', 1, $hash, $hash, 'hard-cap', 4096, 4, 1, $now, $now)
            """, ("$task", taskId), ("$hash", Hash), ("$now", DateTime.UtcNow.ToString("O")));

    private static async Task InsertReservationAsync(SqliteConnection connection, string record, string reservation,
        SystemTaskLease lease) => await ExecuteAsync(connection, """
            INSERT INTO system_task_ai_reservation(
                record_reference, task_id, reservation_id, request_fingerprint, attempt_id,
                fencing_counter, lease_token, lease_expires_at_utc, deadline_utc, requested_provider_tokens,
                reserved_provider_tokens, reserved_tool_calls, mode, status, created_at_utc, updated_at_utc)
            VALUES ($record, $task, $reservation, $hash, $attempt, $fence, $lease, $expiry, $deadline,
                100, 100, 0, 'hard-cap', 'reserved', $now, $now)
            """, ("$record", record), ("$task", lease.Request.Handle.TaskId), ("$reservation", reservation),
            ("$hash", Hash), ("$attempt", lease.Attempt.AttemptId), ("$fence", lease.Attempt.FencingCounter),
            ("$lease", lease.Attempt.LeaseToken), ("$expiry", lease.Attempt.LeaseExpiresAtUtc.ToString("O")),
            ("$deadline", lease.Request.Invocation.DeadlineUtc.ToString("O")),
            ("$now", DateTime.UtcNow.ToString("O")));

    private static async Task ExecuteAsync(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add($"{reader.GetString(1)}:{reader.GetString(2)}:{reader.GetInt64(3)}:{(reader.IsDBNull(4) ? "" : reader.GetString(4))}:{reader.GetInt64(5)}");
        return values;
    }

    private static async Task<IReadOnlyList<string>> ReadIndexesAsync(SqliteConnection connection, string table)
    {
        await using var list = connection.CreateCommand();
        list.CommandText = $"PRAGMA index_list(\"{table}\")";
        await using var reader = await list.ExecuteReaderAsync();
        var headers = new List<(string Name, long Unique)>();
        while (await reader.ReadAsync()) headers.Add((reader.GetString(1), reader.GetInt64(2)));
        var values = new List<string>();
        foreach (var header in headers)
        {
            await using var info = connection.CreateCommand();
            info.CommandText = $"PRAGMA index_info(\"{header.Name}\")";
            await using var columns = await info.ExecuteReaderAsync();
            var names = new List<string>();
            while (await columns.ReadAsync()) names.Add(columns.GetString(2));
            var name = header.Name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal) ? "auto" : header.Name;
            values.Add($"{name}:{header.Unique}:{string.Join(',', names)}");
        }
        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static async Task<IReadOnlyList<string>> ReadForeignKeysAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add($"{reader.GetString(3)}>{reader.GetString(2)}.{reader.GetString(4)}:{reader.GetString(6)}");
        return values;
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<string> CreateSqlAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table";
        command.Parameters.AddWithValue("$table", table);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static IReadOnlyList<string> ExtractChecks(string sql)
    {
        var normalized = Normalize(sql);
        var checks = new List<string>();
        var offset = 0;
        while ((offset = normalized.IndexOf("check(", offset, StringComparison.Ordinal)) >= 0)
        {
            var start = offset + 6;
            var depth = 1;
            var end = start;
            for (; end < normalized.Length && depth > 0; end++)
            {
                if (normalized[end] == '(') depth++;
                else if (normalized[end] == ')') depth--;
            }
            checks.Add(TrimOuterParentheses(normalized[start..(end - 1)]));
            offset = end;
        }
        checks.Sort(StringComparer.Ordinal);
        return checks;
    }

    private static string TrimOuterParentheses(string value)
    {
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')')
        {
            var depth = 0;
            var wraps = true;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '(') depth++;
                else if (value[index] == ')') depth--;
                if (depth == 0 && index < value.Length - 1) { wraps = false; break; }
            }
            if (!wraps) break;
            value = value[1..^1];
        }
        return value;
    }

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            if (!char.IsWhiteSpace(character) && character != '"') result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }

    private sealed class ModelDatabase(string directory, string connectionString) : IAsyncDisposable
    {
        internal string ConnectionString { get; } = connectionString;

        internal static async Task<ModelDatabase> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dantes-roleplay-ai-model-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "model.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
            await using var context = new ModelContext(connectionString);
            Assert.True(await context.Database.EnsureCreatedAsync());
            return new(directory, connectionString);
        }

        internal async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON");
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ModelContext(string connectionString) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite(connectionString);
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SystemTaskLifecycleModelConfiguration.Configure(modelBuilder);
            SystemTaskAiAccountingModelConfiguration.Configure(modelBuilder);
        }
    }
}
