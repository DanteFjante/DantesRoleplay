using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleModelTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task EnsureCreated_schema_has_the_reviewed_columns_types_nullability_defaults_and_keys()
    {
        await using var database = await ModelDatabase.CreateAsync();
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["system_task_root_budget"] = ["root_task_id:T:1::1", "maximum_operations:I:1::0", "consumed_operations:I:1:0:0"],
            ["system_task_lifecycle"] =
            [
                "task_id:T:1::1", "command_id:T:1::0", "payload_fingerprint:T:1::0", "parent_task_id:T:0::0",
                "parent_command_id:T:0::0", "root_task_id:T:1::0", "parent_depth:I:1::0", "propagate_cancellation:I:1::0",
                "state:T:1::0", "principal_reference:T:1::0", "authentication_method:T:1::0", "application_id:T:1::0",
                "application_revision:I:1::0", "application_fingerprint:T:1::0", "base_applications_json:T:1::0",
                "state_space_id:T:1::0", "grant_reference:T:1::0", "state_revision:T:1::0", "execution_profile:T:1::0",
                "admitted_operations:I:1::0", "deadline_utc:T:1::0", "definition_id:T:1::0", "definition_version:I:1::0",
                "definition_fingerprint:T:1::0", "input_json:T:1::0", "checkpoint_name:T:0::0", "completion_handler:T:0::0",
                "correlation_id:T:0::0", "checkpoint_state_json:T:0::0", "wake_json:T:0::0", "attempt_count:I:1:0:0",
                "consecutive_failures:I:1:0:0", "consumed_operations:I:1:0:0", "fencing_counter:I:1:0:0",
                "lease_owner:T:0::0", "lease_token:T:0::0", "lease_expires_at_utc:T:0::0", "next_attempt_at_utc:T:0::0",
                "cancel_requested:I:1:0:0", "cancel_acknowledged:I:1:0:0", "result_json:T:0::0",
                "completion_evidence_reference:T:0::0", "evidence_json:T:0::0", "error_code:T:0::0", "safe_message:T:0::0",
                "created_at_utc:T:1::0", "updated_at_utc:T:1::0", "completed_at_utc:T:0::0"
            ],
            ["system_task_dependency"] = ["task_id:T:1::1", "dependency_task_id:T:1::2", "dependency_command_id:T:1::0"],
            ["system_task_attempt"] = ["task_id:T:1::1", "attempt_id:T:1::0", "ordinal:I:1::2", "fencing_counter:I:1::0", "lease_token:T:1::0", "state:T:1::0", "failure_code:T:0::0", "safe_message:T:0::0", "started_at_utc:T:1::0", "completed_at_utc:T:0::0"],
            ["system_task_checkpoint"] = ["task_id:T:1::1", "sequence:I:1::2", "checkpoint_name:T:1::0", "completion_handler:T:1::0", "correlation_id:T:1::0", "state_json:T:1::0", "status:T:1::0", "wake_json:T:0::0", "created_at_utc:T:1::0", "woken_at_utc:T:0::0"],
            ["system_task_host_call"] = ["task_id:T:1::1", "operation_id:T:1::2", "request_fingerprint:T:1::0", "request_json:T:1::0", "status:T:1::0", "completion_json:T:0::0", "attempt_id:T:1::0", "fencing_counter:I:1::0", "started_at_utc:T:1::0", "completed_at_utc:T:0::0"]
        };

        await using var connection = await database.OpenAsync();
        var tables = await StringsAsync(connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'system_task_%' ORDER BY name");
        Assert.Equal(expected.Keys.OrderBy(value => value, StringComparer.Ordinal), tables);
        foreach (var (table, columns) in expected)
            Assert.Equal(columns.OrderBy(value => value, StringComparer.Ordinal),
                (await ReadColumnsAsync(connection, table)).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnsureCreated_schema_has_only_reviewed_indexes_foreign_keys_and_checks()
    {
        await using var database = await ModelDatabase.CreateAsync();
        await using var connection = await database.OpenAsync();

        var indexes = await ReadIndexesAsync(connection, "system_task_lifecycle");
        Assert.Contains("ix_system_task_lifecycle_claim:0:state,next_attempt_at_utc,lease_expires_at_utc,created_at_utc", indexes);
        Assert.Contains("ix_system_task_lifecycle_parent:0:parent_task_id", indexes);
        Assert.Contains("ix_system_task_lifecycle_root:0:root_task_id", indexes);
        Assert.Contains("ix_system_task_lifecycle_correlation:0:correlation_id,state", indexes);
        Assert.Contains(indexes, value => value.StartsWith("sqlite_autoindex_system_task_lifecycle_", StringComparison.Ordinal)
            && value.EndsWith(":1:command_id", StringComparison.Ordinal));
        Assert.Contains("ix_system_task_dependency_target:0:dependency_task_id",
            await ReadIndexesAsync(connection, "system_task_dependency"));
        Assert.Contains(await ReadIndexesAsync(connection, "system_task_attempt"),
            value => value.StartsWith("sqlite_autoindex_system_task_attempt_", StringComparison.Ordinal)
                && value.EndsWith(":1:attempt_id", StringComparison.Ordinal));
        Assert.Contains(await ReadIndexesAsync(connection, "system_task_checkpoint"),
            value => value.StartsWith("sqlite_autoindex_system_task_checkpoint_", StringComparison.Ordinal)
                && value.EndsWith(":1:task_id,correlation_id", StringComparison.Ordinal));

        var foreignKeys = new List<string>();
        foreach (var table in new[] { "system_task_lifecycle", "system_task_dependency", "system_task_attempt", "system_task_checkpoint", "system_task_host_call" })
            foreignKeys.AddRange(await ReadForeignKeysAsync(connection, table));
        Assert.Equal(new[]
        {
            "system_task_attempt:task_id>system_task_lifecycle.task_id:CASCADE",
            "system_task_checkpoint:task_id>system_task_lifecycle.task_id:CASCADE",
            "system_task_dependency:dependency_task_id>system_task_lifecycle.task_id:RESTRICT",
            "system_task_dependency:task_id>system_task_lifecycle.task_id:CASCADE",
            "system_task_host_call:task_id>system_task_lifecycle.task_id:CASCADE",
            "system_task_lifecycle:parent_task_id>system_task_lifecycle.task_id:RESTRICT",
            "system_task_lifecycle:root_task_id>system_task_root_budget.root_task_id:RESTRICT"
        }, foreignKeys.OrderBy(value => value, StringComparer.Ordinal));

        var schema = Normalize(string.Join(" ", await StringsAsync(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name LIKE 'system_task_%' ORDER BY name")));
        foreach (var check in ExpectedChecks()) Assert.Contains(Normalize(check), schema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_created_schema_matches_the_reviewed_fixture_database()
    {
        await using var model = await ModelDatabase.CreateAsync();
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        await using var modelConnection = await model.OpenAsync();
        await using var fixtureConnection = new SqliteConnection(fixture.ConnectionString);
        await fixtureConnection.OpenAsync();
        var tables = await StringsAsync(fixtureConnection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'system_task_%' ORDER BY name");

        foreach (var table in tables)
        {
            Assert.Equal((await ReadColumnsAsync(fixtureConnection, table)).OrderBy(value => value, StringComparer.Ordinal),
                (await ReadColumnsAsync(modelConnection, table)).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(NormalizeIndexNames(await ReadIndexesAsync(fixtureConnection, table)),
                NormalizeIndexNames(await ReadIndexesAsync(modelConnection, table)));
            Assert.Equal((await ReadForeignKeysAsync(fixtureConnection, table)).OrderBy(value => value, StringComparer.Ordinal),
                (await ReadForeignKeysAsync(modelConnection, table)).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(ExtractChecks(await CreateSqlAsync(fixtureConnection, table)),
                ExtractChecks(await CreateSqlAsync(modelConnection, table)));
        }
    }

    [Fact]
    public async Task Raw_lifecycle_store_executes_against_model_created_database()
    {
        await using var database = await ModelDatabase.CreateAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        var store = new SqliteSystemTaskLifecycleStore(database.ConnectionString, time);
        var request = new SystemTaskDurableSubmissionRequest(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", "command.model", "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, time.GetUtcNow().AddHours(1).UtcDateTime)),
            new("procedure.fixture", 1, Hash), "{\"input\":true}");

        var enqueue = await store.EnqueueAsync(request);
        var lease = await store.ClaimNextAsync("worker.model", TimeSpan.FromSeconds(30));
        Assert.Equal(enqueue.Handle, lease!.Request.Handle);
        Assert.True(await store.CompleteAsync(lease, new("{\"result\":true}", "evidence.model")));
        var snapshot = await new SqliteSystemTaskLifecycleStore(database.ConnectionString, time).ReadAsync(enqueue.Handle!);
        Assert.Equal(SystemTaskLifecycleState.Completed, snapshot!.State);
        Assert.Equal("{\"result\":true}", snapshot.ResultJson);
        Assert.EndsWith("Z", snapshot.CreatedAtUtc.ToString("O"), StringComparison.Ordinal);
    }

    private static IEnumerable<string> ExpectedChecks() =>
    [
        "maximum_operations BETWEEN 1 AND 16", "consumed_operations BETWEEN 0 AND maximum_operations",
        "parent_depth BETWEEN 0 AND 16", "propagate_cancellation IN (0, 1)",
        "state IN ('queued','running','waiting','retry','completed','failed','cancelled','indeterminate')",
        "execution_profile IN ('read-only','atomic','workflow')", "admitted_operations BETWEEN 1 AND 16",
        "attempt_count BETWEEN 0 AND 16", "consecutive_failures BETWEEN 0 AND 3",
        "consumed_operations BETWEEN 0 AND admitted_operations", "fencing_counter >= 0",
        "cancel_requested IN (0, 1)", "cancel_acknowledged IN (0, 1)",
        "((parent_task_id IS NULL AND parent_depth = 0 AND task_id = root_task_id) OR (parent_task_id IS NOT NULL AND parent_depth > 0 AND task_id <> root_task_id))",
        "((checkpoint_name IS NULL AND completion_handler IS NULL AND correlation_id IS NULL AND checkpoint_state_json IS NULL) OR (checkpoint_name IS NOT NULL AND completion_handler IS NOT NULL AND correlation_id IS NOT NULL AND checkpoint_state_json IS NOT NULL))",
        "((state = 'running' AND lease_owner IS NOT NULL AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL) OR (state <> 'running' AND lease_owner IS NULL AND lease_token IS NULL AND lease_expires_at_utc IS NULL))",
        "task_id <> dependency_task_id", "ordinal BETWEEN 1 AND 16", "fencing_counter >= 1",
        "state IN ('running','waiting','retry','completed','failed','cancelled','indeterminate','lease-expired')",
        "sequence BETWEEN 1 AND 16", "status IN ('waiting','woken')",
        "((status = 'waiting' AND wake_json IS NULL AND woken_at_utc IS NULL) OR (status = 'woken' AND wake_json IS NOT NULL AND woken_at_utc IS NOT NULL))",
        "status IN ('pending','completed')",
        "((status = 'pending' AND completion_json IS NULL AND completed_at_utc IS NULL) OR (status = 'completed' AND completion_json IS NOT NULL AND completed_at_utc IS NOT NULL))"
    ];

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            var type = reader.GetString(2) == "TEXT" ? "T" : reader.GetString(2) == "INTEGER" ? "I" : reader.GetString(2);
            values.Add($"{reader.GetString(1)}:{type}:{reader.GetInt64(3)}:{(reader.IsDBNull(4) ? "" : reader.GetString(4))}:{reader.GetInt64(5)}");
        }
        return values;
    }

    private static async Task<IReadOnlyList<string>> ReadIndexesAsync(SqliteConnection connection, string table)
    {
        await using var list = connection.CreateCommand();
        list.CommandText = $"PRAGMA index_list(\"{table}\")";
        await using var reader = await list.ExecuteReaderAsync();
        var headers = new List<(string Name, long Unique)>();
        while (await reader.ReadAsync()) headers.Add((reader.GetString(1), reader.GetInt64(2)));
        var result = new List<string>();
        foreach (var header in headers)
        {
            await using var info = connection.CreateCommand();
            info.CommandText = $"PRAGMA index_info(\"{header.Name}\")";
            await using var columns = await info.ExecuteReaderAsync();
            var names = new List<string>();
            while (await columns.ReadAsync()) names.Add(columns.GetString(2));
            result.Add($"{header.Name}:{header.Unique}:{string.Join(',', names)}");
        }
        return result;
    }

    private static async Task<IReadOnlyList<string>> ReadForeignKeysAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
            result.Add($"{table}:{reader.GetString(3)}>{reader.GetString(2)}.{reader.GetString(4)}:{reader.GetString(6)}");
        return result;
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<string> CreateSqlAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table";
        command.Parameters.AddWithValue("$table", table);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static IReadOnlyList<string> NormalizeIndexNames(IReadOnlyList<string> indexes) => indexes
        .Select(value => value.StartsWith("sqlite_autoindex_", StringComparison.Ordinal)
            ? "auto:" + value[(value.IndexOf(':') + 1)..]
            : value)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

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
            var directory = Path.Combine(Path.GetTempPath(), "dantes-roleplay-task-model-" + Guid.NewGuid().ToString("N"));
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
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON";
            await command.ExecuteNonQueryAsync();
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
        protected override void OnModelCreating(ModelBuilder modelBuilder) => SystemTaskLifecycleModelConfiguration.Configure(modelBuilder);
    }
}
