using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

internal sealed class SystemTaskLifecycleSchemaFixture : IAsyncDisposable
{
    internal const string Sql = """
        -- Test-only FK principal key. The operation owner retains the full ledger and receipt semantics;
        -- lifecycle fixture conformance makes no claim over that schema.
        CREATE TABLE operation (
            Id TEXT NOT NULL PRIMARY KEY
        );

        CREATE TABLE system_task_root_budget (
            root_task_id TEXT NOT NULL PRIMARY KEY,
            maximum_operations INTEGER NOT NULL CHECK (maximum_operations BETWEEN 1 AND 16),
            consumed_operations INTEGER NOT NULL DEFAULT 0 CHECK (consumed_operations BETWEEN 0 AND maximum_operations)
        );

        CREATE TABLE system_task_lifecycle (
            task_id TEXT NOT NULL PRIMARY KEY,
            command_id TEXT NOT NULL UNIQUE,
            payload_fingerprint TEXT NOT NULL,
            parent_task_id TEXT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE RESTRICT,
            parent_command_id TEXT NULL,
            root_task_id TEXT NOT NULL REFERENCES system_task_root_budget(root_task_id) ON DELETE RESTRICT,
            parent_depth INTEGER NOT NULL CHECK (parent_depth BETWEEN 0 AND 16),
            propagate_cancellation INTEGER NOT NULL CHECK (propagate_cancellation IN (0, 1)),
            state TEXT NOT NULL CHECK (state IN ('queued','running','waiting','retry','completed','failed','cancelled','indeterminate')),
            purpose TEXT NOT NULL DEFAULT 'procedure-workflow' CHECK (purpose IN ('procedure-workflow','application-validation')),
            principal_reference TEXT NOT NULL,
            authentication_method TEXT NOT NULL,
            application_id TEXT NOT NULL,
            application_revision INTEGER NOT NULL,
            application_fingerprint TEXT NOT NULL,
            activation_revision INTEGER NULL,
            activation_fingerprint TEXT NULL,
            activation_application_revision INTEGER NULL,
            activation_application_fingerprint TEXT NULL,
            admission_payload_json TEXT NULL,
            base_applications_json TEXT NOT NULL,
            state_space_id TEXT NULL,
            grant_reference TEXT NOT NULL,
            state_revision TEXT NULL,
            execution_profile TEXT NOT NULL CHECK (execution_profile IN ('read-only','atomic','workflow')),
            admitted_operations INTEGER NOT NULL CHECK (admitted_operations BETWEEN 1 AND 16),
            deadline_utc TEXT NOT NULL,
            definition_id TEXT NULL,
            definition_version INTEGER NULL,
            definition_fingerprint TEXT NULL,
            candidate_id TEXT NULL,
            candidate_revision INTEGER NULL,
            candidate_fingerprint TEXT NULL,
            causation_operation_id TEXT NULL REFERENCES operation(Id) ON DELETE RESTRICT,
            input_json TEXT NOT NULL,
            checkpoint_name TEXT NULL,
            completion_handler TEXT NULL,
            correlation_id TEXT NULL,
            checkpoint_state_json TEXT NULL,
            wake_json TEXT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count BETWEEN 0 AND 16),
            consecutive_failures INTEGER NOT NULL DEFAULT 0 CHECK (consecutive_failures BETWEEN 0 AND 3),
            consumed_operations INTEGER NOT NULL DEFAULT 0 CHECK (consumed_operations BETWEEN 0 AND admitted_operations),
            fencing_counter INTEGER NOT NULL DEFAULT 0 CHECK (fencing_counter >= 0),
            lease_owner TEXT NULL,
            lease_token TEXT NULL,
            lease_expires_at_utc TEXT NULL,
            next_attempt_at_utc TEXT NULL,
            cancel_requested INTEGER NOT NULL DEFAULT 0 CHECK (cancel_requested IN (0, 1)),
            cancel_acknowledged INTEGER NOT NULL DEFAULT 0 CHECK (cancel_acknowledged IN (0, 1)),
            result_json TEXT NULL,
            completion_evidence_reference TEXT NULL,
            evidence_json TEXT NULL,
            error_code TEXT NULL,
            safe_message TEXT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            UNIQUE (task_id, purpose),
            CHECK ((activation_revision IS NULL AND activation_fingerprint IS NULL
                    AND activation_application_revision IS NULL AND activation_application_fingerprint IS NULL)
                OR (activation_revision IS NOT NULL AND activation_revision > 0
                    AND activation_fingerprint IS NOT NULL AND length(activation_fingerprint) = 64
                    AND activation_application_revision IS NOT NULL AND activation_application_revision > 0
                    AND activation_application_fingerprint IS NOT NULL AND length(activation_application_fingerprint) = 64)),
            CHECK (admission_payload_json IS NULL OR (json_valid(admission_payload_json) = 1
                AND json_type(admission_payload_json) = 'object'
                AND length(CAST(admission_payload_json AS BLOB)) <= 65536)),
            CHECK ((purpose = 'procedure-workflow'
                    AND ((activation_revision IS NULL AND admission_payload_json IS NULL)
                        OR (activation_revision IS NOT NULL AND admission_payload_json IS NOT NULL)))
                OR (purpose = 'application-validation'
                    AND activation_revision IS NULL AND admission_payload_json IS NOT NULL)),
            CHECK ((purpose = 'procedure-workflow'
                    AND state_space_id IS NOT NULL AND length(trim(state_space_id)) BETWEEN 1 AND 200
                    AND state_revision IS NOT NULL AND length(trim(state_revision)) BETWEEN 1 AND 200
                    AND definition_id IS NOT NULL AND length(definition_id) BETWEEN 1 AND 200
                    AND definition_version IS NOT NULL AND definition_version > 0
                    AND definition_fingerprint IS NOT NULL AND length(definition_fingerprint) = 64
                    AND definition_fingerprint NOT GLOB '*[^0-9A-F]*'
                    AND candidate_id IS NULL AND candidate_revision IS NULL AND candidate_fingerprint IS NULL
                    AND causation_operation_id IS NULL)
                OR (purpose = 'application-validation'
                    AND state_space_id IS NULL AND state_revision IS NULL
                    AND definition_id IS NULL AND definition_version IS NULL AND definition_fingerprint IS NULL
                    AND activation_revision IS NULL AND activation_fingerprint IS NULL
                    AND activation_application_revision IS NULL AND activation_application_fingerprint IS NULL
                    AND candidate_id IS NOT NULL AND length(candidate_id) = 32
                    AND candidate_id NOT GLOB '*[^0-9a-f]*'
                    AND candidate_revision IS NOT NULL AND candidate_revision > 0
                    AND candidate_fingerprint IS NOT NULL AND length(candidate_fingerprint) = 64
                    AND candidate_fingerprint NOT GLOB '*[^0-9A-F]*'
                    AND (causation_operation_id IS NULL OR (length(causation_operation_id) = 32
                        AND causation_operation_id NOT GLOB '*[^0-9a-f]*')))),
            CHECK ((parent_task_id IS NULL AND parent_depth = 0 AND task_id = root_task_id)
                OR (parent_task_id IS NOT NULL AND parent_depth > 0 AND task_id <> root_task_id)),
            CHECK ((checkpoint_name IS NULL AND completion_handler IS NULL AND correlation_id IS NULL AND checkpoint_state_json IS NULL)
                OR (checkpoint_name IS NOT NULL AND completion_handler IS NOT NULL AND correlation_id IS NOT NULL AND checkpoint_state_json IS NOT NULL)),
            CHECK ((state = 'running' AND lease_owner IS NOT NULL AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL)
                OR (state <> 'running' AND lease_owner IS NULL AND lease_token IS NULL AND lease_expires_at_utc IS NULL))
        );
        CREATE INDEX ix_system_task_lifecycle_claim
            ON system_task_lifecycle(state, next_attempt_at_utc, lease_expires_at_utc, created_at_utc);
        CREATE INDEX ix_system_task_lifecycle_parent ON system_task_lifecycle(parent_task_id);
        CREATE INDEX ix_system_task_lifecycle_root ON system_task_lifecycle(root_task_id);
        CREATE INDEX ix_system_task_lifecycle_correlation ON system_task_lifecycle(correlation_id, state);
        CREATE INDEX ix_system_task_lifecycle_causation_operation ON system_task_lifecycle(causation_operation_id);

        CREATE TABLE system_task_dependency (
            task_id TEXT NOT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE CASCADE,
            dependency_task_id TEXT NOT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE RESTRICT,
            dependency_command_id TEXT NOT NULL,
            PRIMARY KEY (task_id, dependency_task_id),
            CHECK (task_id <> dependency_task_id)
        );
        CREATE INDEX ix_system_task_dependency_target
            ON system_task_dependency(dependency_task_id);

        CREATE TABLE system_task_attempt (
            task_id TEXT NOT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE CASCADE,
            attempt_id TEXT NOT NULL UNIQUE,
            ordinal INTEGER NOT NULL CHECK (ordinal BETWEEN 1 AND 16),
            fencing_counter INTEGER NOT NULL CHECK (fencing_counter >= 1),
            lease_token TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('running','waiting','retry','completed','failed','cancelled','indeterminate','lease-expired')),
            failure_code TEXT NULL,
            safe_message TEXT NULL,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            PRIMARY KEY (task_id, ordinal)
        );

        CREATE TABLE system_task_checkpoint (
            task_id TEXT NOT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE CASCADE,
            sequence INTEGER NOT NULL CHECK (sequence BETWEEN 1 AND 16),
            checkpoint_name TEXT NOT NULL,
            completion_handler TEXT NOT NULL,
            correlation_id TEXT NOT NULL,
            state_json TEXT NOT NULL,
            status TEXT NOT NULL CHECK (status IN ('waiting','woken')),
            wake_json TEXT NULL,
            created_at_utc TEXT NOT NULL,
            woken_at_utc TEXT NULL,
            PRIMARY KEY (task_id, sequence),
            UNIQUE (task_id, correlation_id),
            CHECK ((status = 'waiting' AND wake_json IS NULL AND woken_at_utc IS NULL)
                OR (status = 'woken' AND wake_json IS NOT NULL AND woken_at_utc IS NOT NULL))
        );

        CREATE TABLE system_task_host_call (
            task_id TEXT NOT NULL REFERENCES system_task_lifecycle(task_id) ON DELETE CASCADE,
            operation_id TEXT NOT NULL,
            request_fingerprint TEXT NOT NULL,
            request_json TEXT NOT NULL,
            status TEXT NOT NULL CHECK (status IN ('pending','completed')),
            completion_json TEXT NULL,
            attempt_id TEXT NOT NULL,
            fencing_counter INTEGER NOT NULL CHECK (fencing_counter >= 1),
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            PRIMARY KEY (task_id, operation_id),
            CHECK ((status = 'pending' AND completion_json IS NULL AND completed_at_utc IS NULL)
                OR (status = 'completed' AND completion_json IS NOT NULL AND completed_at_utc IS NOT NULL))
        );
        """;

    private readonly string _directory;

    private SystemTaskLifecycleSchemaFixture(string directory, string connectionString, MutableTimeProvider timeProvider)
    {
        _directory = directory;
        ConnectionString = connectionString;
        TimeProvider = timeProvider;
    }

    internal string ConnectionString { get; }
    internal MutableTimeProvider TimeProvider { get; }
    internal SqliteSystemTaskLifecycleStore CreateStore() => new(ConnectionString, TimeProvider);

    internal static async Task<SystemTaskLifecycleSchemaFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dantes-roleplay-task-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lifecycle.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;" + Sql;
        await command.ExecuteNonQueryAsync();
        return new(directory, connectionString,
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)));
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}

internal sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
{
    private long _utcTicks = value.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    internal void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
