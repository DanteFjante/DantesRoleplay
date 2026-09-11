using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

internal sealed class SystemTaskAiAccountingSchemaFixture : IAsyncDisposable
{
    internal const string Sql = """
        CREATE TABLE system_task_ai_ceiling (
            task_id TEXT NOT NULL PRIMARY KEY REFERENCES system_task_lifecycle(task_id) ON DELETE RESTRICT CHECK (length(task_id) BETWEEN 1 AND 200),
            enrollment_fingerprint TEXT NOT NULL CHECK (length(enrollment_fingerprint) = 64),
            profile_id TEXT NOT NULL CHECK (length(profile_id) BETWEEN 1 AND 200),
            profile_version INTEGER NOT NULL CHECK (profile_version > 0),
            profile_fingerprint TEXT NOT NULL CHECK (length(profile_fingerprint) = 64),
            grant_reference TEXT NOT NULL CHECK (length(grant_reference) BETWEEN 1 AND 200),
            grant_revision TEXT NOT NULL CHECK (length(grant_revision) BETWEEN 1 AND 1024),
            grant_fingerprint TEXT NOT NULL CHECK (length(grant_fingerprint) = 64),
            definition_id TEXT NOT NULL CHECK (length(definition_id) BETWEEN 1 AND 200),
            definition_version INTEGER NOT NULL CHECK (definition_version > 0),
            definition_fingerprint TEXT NOT NULL CHECK (length(definition_fingerprint) = 64),
            output_schema_fingerprint TEXT NOT NULL CHECK (length(output_schema_fingerprint) = 64),
            mode TEXT NOT NULL CHECK (mode IN ('measured-stop','hard-cap')),
            maximum_provider_tokens INTEGER NOT NULL CHECK (maximum_provider_tokens BETWEEN 1 AND 131072),
            maximum_tool_calls INTEGER NOT NULL CHECK (maximum_tool_calls BETWEEN 0 AND 16),
            maximum_concurrent_provider_requests INTEGER NOT NULL CHECK (maximum_concurrent_provider_requests BETWEEN 1 AND 4),
            deadline_utc TEXT NOT NULL CHECK (length(deadline_utc) BETWEEN 1 AND 40),
            created_at_utc TEXT NOT NULL CHECK (length(created_at_utc) BETWEEN 1 AND 40)
        );

        CREATE TABLE system_task_ai_reservation (
            record_reference TEXT NOT NULL PRIMARY KEY CHECK (length(record_reference) BETWEEN 1 AND 200),
            task_id TEXT NOT NULL REFERENCES system_task_ai_ceiling(task_id) ON DELETE RESTRICT CHECK (length(task_id) BETWEEN 1 AND 200),
            reservation_id TEXT NOT NULL CHECK (length(reservation_id) BETWEEN 1 AND 128),
            request_fingerprint TEXT NOT NULL CHECK (length(request_fingerprint) = 64),
            attempt_id TEXT NOT NULL REFERENCES system_task_attempt(attempt_id) ON DELETE RESTRICT CHECK (length(attempt_id) BETWEEN 1 AND 128),
            fencing_counter INTEGER NOT NULL CHECK (fencing_counter >= 1),
            lease_token TEXT NOT NULL CHECK (length(lease_token) BETWEEN 1 AND 128),
            lease_expires_at_utc TEXT NOT NULL CHECK (length(lease_expires_at_utc) BETWEEN 1 AND 40),
            deadline_utc TEXT NOT NULL CHECK (length(deadline_utc) BETWEEN 1 AND 40),
            requested_provider_tokens INTEGER NOT NULL CHECK (requested_provider_tokens BETWEEN 0 AND 131072),
            reserved_provider_tokens INTEGER NOT NULL CHECK (reserved_provider_tokens BETWEEN 0 AND requested_provider_tokens),
            reserved_tool_calls INTEGER NOT NULL CHECK (reserved_tool_calls BETWEEN 0 AND 16),
            mode TEXT NOT NULL CHECK (mode IN ('measured-stop','hard-cap')),
            status TEXT NOT NULL CHECK (status IN ('reserved','unknown','settled','exceeded')),
            charged_provider_tokens INTEGER NOT NULL DEFAULT 0 CHECK (charged_provider_tokens >= 0),
            charged_tool_calls INTEGER NOT NULL DEFAULT 0 CHECK (charged_tool_calls >= 0),
            settled_evidence_sequence INTEGER NULL CHECK (settled_evidence_sequence BETWEEN 1 AND 16),
            created_at_utc TEXT NOT NULL CHECK (length(created_at_utc) BETWEEN 1 AND 40),
            updated_at_utc TEXT NOT NULL CHECK (length(updated_at_utc) BETWEEN 1 AND 40),
            UNIQUE (task_id, reservation_id),
            CHECK ((requested_provider_tokens > 0 AND reserved_provider_tokens > 0 AND reserved_tool_calls = 0)
                OR (requested_provider_tokens = 0 AND reserved_provider_tokens = 0 AND reserved_tool_calls = 1))
        );
        CREATE INDEX ix_system_task_ai_reservation_attempt ON system_task_ai_reservation(attempt_id);

        CREATE TABLE system_task_ai_reservation_ancestor (
            record_reference TEXT NOT NULL REFERENCES system_task_ai_reservation(record_reference) ON DELETE RESTRICT CHECK (length(record_reference) BETWEEN 1 AND 200),
            ancestor_task_id TEXT NOT NULL REFERENCES system_task_ai_ceiling(task_id) ON DELETE RESTRICT CHECK (length(ancestor_task_id) BETWEEN 1 AND 200),
            PRIMARY KEY (record_reference, ancestor_task_id)
        );
        CREATE INDEX ix_system_task_ai_reservation_ancestor_task
            ON system_task_ai_reservation_ancestor(ancestor_task_id);

        CREATE TABLE system_task_ai_dispatch_evidence (
            record_reference TEXT NOT NULL REFERENCES system_task_ai_reservation(record_reference) ON DELETE RESTRICT CHECK (length(record_reference) BETWEEN 1 AND 200),
            sequence INTEGER NOT NULL CHECK (sequence BETWEEN 0 AND 16),
            event_reference TEXT NOT NULL UNIQUE CHECK (length(event_reference) BETWEEN 1 AND 200),
            payload_fingerprint TEXT NOT NULL CHECK (length(payload_fingerprint) = 64),
            kind TEXT NOT NULL CHECK (kind IN ('dispatch','usage')),
            dispatch_kind TEXT NOT NULL CHECK (dispatch_kind IN ('provider','tool')),
            request_fingerprint TEXT NOT NULL CHECK (length(request_fingerprint) = 64),
            provider_id TEXT NULL CHECK (provider_id IS NULL OR length(provider_id) BETWEEN 1 AND 200),
            model_id TEXT NULL CHECK (model_id IS NULL OR length(model_id) BETWEEN 1 AND 200),
            profile_fingerprint TEXT NOT NULL CHECK (length(profile_fingerprint) = 64),
            schema_fingerprint TEXT NOT NULL CHECK (length(schema_fingerprint) = 64),
            request_json TEXT NULL,
            response_fingerprint TEXT NULL CHECK (response_fingerprint IS NULL OR length(response_fingerprint) = 64),
            input_tokens INTEGER NULL CHECK (input_tokens IS NULL OR input_tokens >= 0),
            output_tokens INTEGER NULL CHECK (output_tokens IS NULL OR output_tokens >= 0),
            total_tokens INTEGER NULL CHECK (total_tokens IS NULL OR total_tokens >= 0),
            observed_tool_calls INTEGER NOT NULL DEFAULT 0 CHECK (observed_tool_calls >= 0),
            is_complete INTEGER NOT NULL DEFAULT 0 CHECK (is_complete IN (0, 1)),
            completion_kind TEXT NULL CHECK (completion_kind IS NULL OR completion_kind IN ('returned','threw','cancelled','not-started')),
            observed_at_utc TEXT NOT NULL CHECK (length(observed_at_utc) BETWEEN 1 AND 40),
            PRIMARY KEY (record_reference, sequence),
            CHECK ((dispatch_kind = 'provider' AND provider_id IS NOT NULL AND model_id IS NOT NULL)
                OR (dispatch_kind = 'tool' AND provider_id IS NULL AND model_id IS NULL)),
            CHECK ((kind = 'dispatch' AND sequence = 0 AND request_json IS NOT NULL
                    AND length(CAST(request_json AS BLOB)) <= 65536 AND response_fingerprint IS NULL
                    AND input_tokens IS NULL AND output_tokens IS NULL AND total_tokens IS NULL
                    AND observed_tool_calls = 0 AND is_complete = 0 AND completion_kind IS NULL)
                OR (kind = 'usage' AND sequence BETWEEN 1 AND 16 AND request_json IS NULL
                    AND response_fingerprint IS NOT NULL AND completion_kind IS NOT NULL)),
            CHECK (total_tokens IS NULL OR (total_tokens >= COALESCE(output_tokens, 0)
                AND COALESCE(input_tokens, 0) <= total_tokens - COALESCE(output_tokens, 0))),
            CHECK (input_tokens IS NULL OR output_tokens IS NULL
                OR input_tokens <= 9223372036854775807 - output_tokens)
        );
        """;

    private readonly string directory;

    private SystemTaskAiAccountingSchemaFixture(string directory, string connectionString)
    {
        this.directory = directory;
        ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    internal static async Task<SystemTaskAiAccountingSchemaFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dantes-roleplay-task-ai-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "accounting.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;" + SystemTaskLifecycleSchemaFixture.Sql + Sql;
        await command.ExecuteNonQueryAsync();
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
