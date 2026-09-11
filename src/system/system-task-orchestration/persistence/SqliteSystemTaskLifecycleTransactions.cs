using System.Data;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    internal async Task<SystemTaskEnqueueResult> EnqueueAsync(SystemTaskDurableSubmissionRequest request,
        bool propagateCancellation = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await StageEnqueueAsync(request, propagateCancellation, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>Stages a job under the caller's current grant/effect transaction; never publishes Pending.</summary>
    internal Task<SystemTaskEnqueueResult> StageEnqueueAsync(SystemTaskDurableSubmissionRequest request,
        bool propagateCancellation, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default) => InCallerTransactionAsync(connection, transaction,
            () => EnqueueCoreAsync(request, propagateCancellation, connection, transaction, cancellationToken),
            result => result.Disposition is SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing,
            cancellationToken);

    internal async Task<bool> RequestCancellationAsync(SystemTaskDurableHandle handle, bool propagate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await StageCancellationAsync(handle, propagate, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>The caller authorizes cancellation in this transaction before staging it.</summary>
    internal Task<bool> StageCancellationAsync(SystemTaskDurableHandle handle, bool propagate,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction,
            () => CancelCoreAsync(handle, propagate, connection, transaction, cancellationToken), result => result, cancellationToken);

    private static async Task<T> InCallerTransactionAsync<T>(SqliteConnection connection, SqliteTransaction transaction,
        Func<Task<T>> stage, Func<T, bool> accepted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new ArgumentException("The caller must supply its open SQLite connection and that connection's active transaction.");
        var savepoint = "system_task_" + Guid.NewGuid().ToString("N");
        await transaction.SaveAsync(savepoint, cancellationToken);
        try
        {
            var result = await stage();
            if (!accepted(result)) await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> CancelCoreAsync(SystemTaskDurableHandle handle, bool propagate,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!await FindHandleAsync(connection, transaction, handle, cancellationToken)) return false;
        var targets = new List<string>();
        await using (var command = Command(connection, transaction, """
            WITH RECURSIVE targets(task_id) AS (
                SELECT task_id FROM system_task_lifecycle WHERE task_id = $task
                UNION
                SELECT child.task_id FROM system_task_lifecycle AS child
                JOIN targets AS parent ON child.parent_task_id = parent.task_id
                WHERE $propagate = 1 AND child.propagate_cancellation = 1)
            SELECT target.task_id,
                target.principal_reference = root.principal_reference
                AND target.application_id = root.application_id
                AND target.application_revision = root.application_revision
                AND target.application_fingerprint = root.application_fingerprint
                AND target.base_applications_json = root.base_applications_json
                AND target.state_space_id = root.state_space_id
                AND target.state_revision = root.state_revision
                AND target.grant_reference = root.grant_reference
                AND target.execution_profile = root.execution_profile AS scope_matches
            FROM targets JOIN system_task_lifecycle AS target ON target.task_id = targets.task_id
            JOIN system_task_lifecycle AS root ON root.task_id = $task
            LIMIT 66
            """, ("$task", handle.TaskId), ("$propagate", propagate ? 1 : 0)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(1) != 1)
                    throw new SystemTaskException("SYSTEM_TASK_CANCELLATION_SCOPE_MISMATCH", "A cancellation edge crosses the task's retained invocation scope.");
                targets.Add(reader.GetString(0));
            }
        }
        if (targets.Count > SystemTaskLifecycleLimits.MaximumDescendantsPerRoot + 1)
            throw new SystemTaskException("SYSTEM_TASK_DESCENDANT_LIMIT", "The cancellation graph exceeds its retained descendant bound.");
        var parameters = targets.Select((value, index) => ($"$target{index}", (object?)value)).ToList();
        parameters.Add(("$now", ToDb(UtcNow())));
        var selection = string.Join(",", Enumerable.Range(0, targets.Count).Select(index => $"$target{index}"));
        await ExecuteAsync(connection, transaction, $"""
            UPDATE system_task_lifecycle SET cancel_requested = 1, updated_at_utc = $now
            WHERE task_id IN ({selection}) AND state IN ('queued','running','waiting','retry')
            """, cancellationToken, [.. parameters]);
        await ExecuteAsync(connection, transaction, $"""
            UPDATE system_task_lifecycle
            SET state = 'cancelled', cancel_acknowledged = 1, completed_at_utc = $now,
                updated_at_utc = $now, error_code = 'SYSTEM_TASK_CANCELLED',
                safe_message = 'Cancellation was acknowledged before execution.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE task_id IN ({selection}) AND cancel_requested = 1 AND state IN ('queued','waiting','retry')
            """, cancellationToken, [.. parameters]);
        return true;
    }
}
