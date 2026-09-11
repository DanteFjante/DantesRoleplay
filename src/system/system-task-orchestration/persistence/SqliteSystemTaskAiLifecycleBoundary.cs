using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private const string AiRecoveryCode = "INNER_AI_RECONCILIATION_REQUIRED";
    private const string AiRecoveryMessage = "AI dispatch usage requires reconciliation before further execution.";

    private static async Task<bool> HasAiAccountingTablesAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken) => await ScalarLongAsync(connection, transaction,
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='system_task_ai_reservation'", cancellationToken) == 1;

    internal static async Task<bool> HasUnresolvedAiAccountingAsync(SqliteConnection connection, SqliteTransaction transaction,
        string taskId, CancellationToken cancellationToken)
    {
        if (!await HasAiAccountingTablesAsync(connection, transaction, cancellationToken)) return false;
        return await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_ai_reservation WHERE task_id=$task AND status IN ('reserved','unknown')
            """, cancellationToken, ("$task", taskId)) > 0;
    }

    private async Task<bool> MarkAiIndeterminateAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, DateTime now, CancellationToken cancellationToken, bool requireCancellation = false)
    {
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state='indeterminate',error_code=$code,safe_message=$message,lease_owner=NULL,lease_token=NULL,
            lease_expires_at_utc=NULL,updated_at_utc=$now,completed_at_utc=$now
            """, now, cancellationToken, requireCancellation, true, ("$code", AiRecoveryCode), ("$message", AiRecoveryMessage));
        if (changed != 1) return false;
        await NormalizeAiReservationsAsync(connection, transaction, cancellationToken);
        await CompleteAttemptAsync(connection, transaction, lease, "indeterminate", AiRecoveryCode, AiRecoveryMessage, now, cancellationToken);
        return true;
    }

    private static Task MarkExpiredAiTasksAsync(SqliteConnection connection, SqliteTransaction transaction,
        DateTime now, CancellationToken cancellationToken) => ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle AS task
            SET state='indeterminate',error_code=$code,safe_message=$message,lease_owner=NULL,lease_token=NULL,
                lease_expires_at_utc=NULL,updated_at_utc=$now,completed_at_utc=$now
            WHERE task.state='running' AND task.lease_expires_at_utc<=$now
                AND EXISTS(SELECT 1 FROM system_task_ai_reservation AS reservation
                    WHERE reservation.task_id=task.task_id AND reservation.status IN ('reserved','unknown'))
                AND NOT EXISTS(SELECT 1 FROM system_task_host_call AS call
                    WHERE call.task_id=task.task_id AND call.status='pending')
            """, cancellationToken, ("$code", AiRecoveryCode), ("$message", AiRecoveryMessage), ("$now", ToDb(now)));
}
