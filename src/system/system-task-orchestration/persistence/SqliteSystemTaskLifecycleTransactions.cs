using System.Data;
using DantesRoleplay.Authorization;
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
        CancellationToken cancellationToken = default, StandingGrantActivationOrigin? activationOrigin = null) =>
        InCallerTransactionAsync(connection, transaction,
            () => EnqueueCoreAsync(request, propagateCancellation, connection, transaction, cancellationToken, activationOrigin),
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
        var snapshots = await ReadCancellationTargetsAsync(handle, propagate, connection, transaction, cancellationToken);
        if (snapshots.Count == 0) return false;
        var targets = snapshots.Select(value => value.Request.Handle.TaskId).ToArray();
        var parameters = targets.Select((value, index) => ($"$target{index}", (object?)value)).ToList();
        parameters.Add(("$now", ToDb(UtcNow())));
        var selection = string.Join(",", Enumerable.Range(0, targets.Length).Select(index => $"$target{index}"));
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
