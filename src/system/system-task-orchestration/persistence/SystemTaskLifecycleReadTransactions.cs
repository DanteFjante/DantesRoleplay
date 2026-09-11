using System.Data;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    /// <summary>Reads through the caller's transaction without changing its outcome or lifetime.</summary>
    internal Task<SystemTaskLifecycleSnapshot?> ReadInTransactionAsync(SystemTaskDurableHandle handle,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ValidateReadTransaction(connection, transaction);
        return ReadSnapshotAsync(connection, transaction, handle, cancellationToken);
    }

    /// <summary>
    /// Resolves the exact cancellation set through declared parent propagation edges. Dependencies
    /// are intentionally outside this graph. Returned rows are inert retained state.
    /// </summary>
    internal async Task<IReadOnlyList<SystemTaskLifecycleSnapshot>> ReadCancellationTargetsAsync(
        SystemTaskDurableHandle handle, bool propagate, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ValidateReadTransaction(connection, transaction);
        var taskIds = new List<string>();
        await using (var command = Command(connection, transaction, """
            WITH RECURSIVE targets(task_id) AS (
                SELECT task_id FROM system_task_lifecycle
                WHERE task_id = $task AND command_id = $command
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
            JOIN system_task_lifecycle AS root ON root.task_id = $task AND root.command_id = $command
            ORDER BY target.task_id
            LIMIT 66
            """, ("$task", handle.TaskId), ("$command", handle.CommandId),
            ("$propagate", propagate ? 1 : 0)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(1) != 1)
                    throw new SystemTaskException("SYSTEM_TASK_CANCELLATION_SCOPE_MISMATCH",
                        "A cancellation edge crosses the task's retained invocation scope.");
                taskIds.Add(reader.GetString(0));
            }
        }

        if (taskIds.Count > SystemTaskLifecycleLimits.MaximumDescendantsPerRoot + 1)
            throw new SystemTaskException("SYSTEM_TASK_DESCENDANT_LIMIT",
                "The cancellation graph exceeds its retained descendant bound.");
        var snapshots = new List<SystemTaskLifecycleSnapshot>(taskIds.Count);
        foreach (var taskId in taskIds)
            snapshots.Add(await ReadSnapshotByTaskAsync(connection, transaction, taskId, cancellationToken)
                ?? throw new InvalidDataException("A cancellation target disappeared inside the caller transaction."));
        return snapshots.AsReadOnly();
    }

    private static void ValidateReadTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new ArgumentException("The caller must supply its open SQLite connection and that connection's active transaction.");
    }
}
