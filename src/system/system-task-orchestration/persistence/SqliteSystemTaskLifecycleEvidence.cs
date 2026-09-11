using System.Data;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    /// <summary>
    /// Readback for recovery, including uncertain calls and earlier recorded completions. Evidence
    /// is inert: reading a stored JSON result does not authorize a retry or establish a commit.
    /// The runtime/operation owner must reconcile pending identities against authoritative receipts.
    /// </summary>
    internal async Task<SystemTaskLifecycleEvidence?> InspectEvidenceAsync(SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var snapshot = await ReadSnapshotAsync(connection, transaction, handle, cancellationToken);
        if (snapshot is null) return null;

        var attempts = new List<SystemTaskAttemptEvidence>();
        await using (var command = Command(connection, transaction, """
            SELECT attempt_id, ordinal, fencing_counter, state, failure_code, safe_message,
                   started_at_utc, completed_at_utc
            FROM system_task_attempt WHERE task_id = $task ORDER BY ordinal LIMIT 16
            """, ("$task", handle.TaskId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                attempts.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    ParseDb(reader.GetString(6)), reader.IsDBNull(7) ? null : ParseDb(reader.GetString(7))));
        }
        var calls = new List<SystemTaskHostCallEvidence>();
        await using (var command = Command(connection, transaction, """
            SELECT operation_id, request_fingerprint, request_json, status, completion_json,
                   attempt_id, fencing_counter, started_at_utc, completed_at_utc
            FROM system_task_host_call WHERE task_id = $task ORDER BY started_at_utc, operation_id LIMIT 16
            """, ("$task", handle.TaskId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                calls.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetInt64(6),
                    ParseDb(reader.GetString(7)), reader.IsDBNull(8) ? null : ParseDb(reader.GetString(8))));
        }
        var checkpoints = new List<SystemTaskCheckpointEvidence>();
        await using (var command = Command(connection, transaction, """
            SELECT sequence, checkpoint_name, completion_handler, correlation_id, state_json, status,
                   wake_json, created_at_utc, woken_at_utc
            FROM system_task_checkpoint WHERE task_id = $task ORDER BY sequence LIMIT 16
            """, ("$task", handle.TaskId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                checkpoints.Add(new(reader.GetInt32(0), new(reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4)), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), ParseDb(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : ParseDb(reader.GetString(8))));
        }
        await transaction.CommitAsync(cancellationToken);
        return new(snapshot, attempts.AsReadOnly(), calls.AsReadOnly(), checkpoints.AsReadOnly());
    }
}

internal sealed record SystemTaskLifecycleEvidence(SystemTaskLifecycleSnapshot Snapshot,
    IReadOnlyList<SystemTaskAttemptEvidence> Attempts, IReadOnlyList<SystemTaskHostCallEvidence> HostCalls,
    IReadOnlyList<SystemTaskCheckpointEvidence> Checkpoints);

internal sealed record SystemTaskCheckpointEvidence(int Sequence, SystemTaskCheckpoint Checkpoint,
    string Status, string? WakeJson, DateTime CreatedAtUtc, DateTime? WokenAtUtc);

internal sealed record SystemTaskAttemptEvidence(string AttemptId, int Ordinal, long FencingCounter,
    string State, string? FailureCode, string? SafeMessage, DateTime StartedAtUtc, DateTime? CompletedAtUtc);

internal sealed record SystemTaskHostCallEvidence(string OperationId, string RequestFingerprint,
    string RequestJson, string State, string? CompletionJson, string AttemptId, long FencingCounter,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc);
