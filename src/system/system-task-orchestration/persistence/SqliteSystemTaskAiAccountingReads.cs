using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private sealed record AiCeilingRow(string Purpose, string Fingerprint, SystemInnerWorkerAiBudget Budget,
        string ProfileFingerprint, string SchemaFingerprint, DateTime DeadlineUtc);
    private sealed record AiReservationRow(string Fingerprint, SystemInnerWorkerAiReservationEvidence Evidence,
        string State, long ChargedTokens, long ChargedTools, int? SettledSequence, DateTime DeadlineUtc);
    private sealed record AiBalances(long ChargedTokens, long ChargedTools, long HeldTokens, long HeldTools,
        int ActiveProviders, bool Blocked);

    private static async Task<AiCeilingRow?> ReadAiCeilingAsync(SqliteConnection connection, SqliteTransaction transaction,
        string taskId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT task_purpose,enrollment_fingerprint,maximum_provider_tokens,maximum_tool_calls,mode,
                maximum_concurrent_provider_requests,profile_fingerprint,output_schema_fingerprint,deadline_utc
            FROM system_task_ai_ceiling WHERE task_id=$task
            """, ("$task", taskId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), new(reader.GetInt32(2), reader.GetInt32(3),
                SystemInnerWorkerTokenBudgetModeNames.Parse(reader.GetString(4)), reader.GetInt32(5)), reader.GetString(6), reader.GetString(7),
                ParseDb(reader.GetString(8)))
            : null;
    }

    private static async Task<IReadOnlyList<string>> ReadAiAncestryAsync(SqliteConnection connection,
        SqliteTransaction transaction, string taskId, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        string? next = taskId;
        string? declaredRoot = null;
        while (next is not null)
        {
            if (result.Count > SystemTaskLifecycleLimits.MaximumParentDepth || result.Contains(next, StringComparer.Ordinal))
                throw new SystemTaskException("INNER_AI_ANCESTRY_INVALID", "The persisted task ancestry exceeds its bound or contains a cycle.");
            await using var command = Command(connection, transaction,
                "SELECT parent_task_id,root_task_id FROM system_task_lifecycle WHERE task_id=$task", ("$task", next));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new SystemTaskException("INNER_AI_ANCESTRY_INVALID", "A persisted ancestor is missing.");
            declaredRoot ??= reader.GetString(1);
            if (declaredRoot != reader.GetString(1))
                throw new SystemTaskException("INNER_AI_ANCESTRY_INVALID", "The persisted root and parent ancestry disagree.");
            result.Add(next);
            next = reader.IsDBNull(0) ? null : reader.GetString(0);
        }
        if (result[^1] != declaredRoot)
            throw new SystemTaskException("INNER_AI_ANCESTRY_INVALID", "The persisted root is not the last ancestor.");
        return result.AsReadOnly();
    }

    private static async Task<AiReservationRow?> ReadAiReservationAsync(SqliteConnection connection,
        SqliteTransaction transaction, string reference, CancellationToken cancellationToken)
    {
        AiReservationRow? result;
        await using (var command = Command(connection, transaction, """
            SELECT reservation.request_fingerprint,reservation.reservation_id,task.task_id,task.command_id,
                root.task_id,root.command_id,reservation.attempt_id,reservation.lease_token,reservation.fencing_counter,
                reservation.lease_expires_at_utc,reservation.reserved_provider_tokens,reservation.reserved_tool_calls,
                reservation.mode,reservation.status,reservation.charged_provider_tokens,reservation.charged_tool_calls,
                reservation.settled_evidence_sequence,
                attempt.task_id=reservation.task_id AND attempt.fencing_counter=reservation.fencing_counter
                    AND attempt.lease_token=reservation.lease_token AS attempt_matches,reservation.deadline_utc
            FROM system_task_ai_reservation AS reservation
            JOIN system_task_lifecycle AS task ON task.task_id=reservation.task_id
            JOIN system_task_lifecycle AS root ON root.task_id=task.root_task_id
            JOIN system_task_attempt AS attempt ON attempt.attempt_id=reservation.attempt_id
            WHERE reservation.record_reference=$reference
            """, ("$reference", reference)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            if (reader.GetInt32(17) != 1)
                throw new SystemTaskException("INNER_AI_ATTEMPT_MISMATCH", "The reservation attempt does not belong to its task and fence.");
            var task = new SystemTaskDurableHandle(reader.GetString(2), reader.GetString(3));
            var evidence = new SystemInnerWorkerAiReservationEvidence(reference, reader.GetString(1),
                new(reader.GetString(4), reader.GetString(5)), task,
                new(task.CommandId, reader.GetString(6), reader.GetString(7), reader.GetInt64(8), ParseDb(reader.GetString(9))),
                reader.GetInt32(10), reader.GetInt32(11), SystemInnerWorkerTokenBudgetModeNames.Parse(reader.GetString(12)));
            result = new(reader.GetString(0), evidence, reader.GetString(13), reader.GetInt64(14), reader.GetInt64(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16), ParseDb(reader.GetString(18)));
        }
        var expected = await ReadAiAncestryAsync(connection, transaction, result.Evidence.Task.TaskId, cancellationToken);
        var retained = new List<string>();
        await using (var command = Command(connection, transaction, """
            SELECT ancestor_task_id FROM system_task_ai_reservation_ancestor WHERE record_reference=$reference
            """, ("$reference", reference)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) retained.Add(reader.GetString(0));
        if (!expected.Order(StringComparer.Ordinal).SequenceEqual(retained.Order(StringComparer.Ordinal)))
            throw new SystemTaskException("INNER_AI_ANCESTOR_MEMBERSHIP_INVALID", "The reservation does not cover its exact persisted ancestry.");
        await ValidateAiSettlementCacheAsync(connection, transaction, result, cancellationToken);
        return result;
    }

    private static async Task<AiBalances> ReadAiBalancesAsync(SqliteConnection connection, SqliteTransaction transaction,
        string ancestor, CancellationToken cancellationToken)
    {
        long chargedTokens = 0, chargedTools = 0, heldTokens = 0, heldTools = 0;
        var active = 0;
        var blocked = false;
        // Discover from lifecycle ownership, then verify every reservation's membership. An
        // altered/missing membership row cannot hide a sibling's charge from the root balance.
        var references = new List<string>();
        await using (var command = Command(connection, transaction, """
            SELECT reservation.record_reference FROM system_task_ai_reservation AS reservation
            JOIN system_task_lifecycle AS task ON task.task_id=reservation.task_id
            WHERE task.root_task_id=(SELECT root_task_id FROM system_task_lifecycle WHERE task_id=$ancestor) LIMIT 17
            """, ("$ancestor", ancestor)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) references.Add(reader.GetString(0));
        if (references.Count > SystemTaskLifecycleLimits.MaximumRootOperations)
            throw new SystemTaskException("INNER_AI_RESERVATION_LIMIT", "The retained reservation count exceeds the operation bound.");
        foreach (var reference in references)
        {
            var reservation = await ReadAiReservationAsync(connection, transaction, reference, cancellationToken)
                ?? throw new SystemTaskException("INNER_AI_RESERVATION_INVALID", "The retained reservation cannot be resolved.");
            var ancestry = await ReadAiAncestryAsync(connection, transaction, reservation.Evidence.Task.TaskId, cancellationToken);
            if (!ancestry.Contains(ancestor, StringComparer.Ordinal)) continue;
            checked
            {
                if (reservation.State == "reserved")
                {
                    heldTokens += reservation.Evidence.ProviderTokens;
                    heldTools += reservation.Evidence.ToolCalls;
                    if (reservation.Evidence.ProviderTokens != 0) active++;
                }
                else
                {
                    chargedTokens += reservation.ChargedTokens;
                    chargedTools += reservation.ChargedTools;
                    blocked |= reservation.State == "unknown";
                }
            }
        }
        return new(chargedTokens, chargedTools, heldTokens, heldTools, active, blocked);
    }

    private async Task NormalizeAiReservationsAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            UPDATE system_task_ai_reservation AS reservation
            SET status='unknown',charged_provider_tokens=MAX(charged_provider_tokens,reserved_provider_tokens),
                charged_tool_calls=MAX(charged_tool_calls,reserved_tool_calls),updated_at_utc=$now
            WHERE status='reserved' AND NOT EXISTS (
                SELECT 1 FROM system_task_lifecycle AS task JOIN system_task_attempt AS attempt
                    ON attempt.task_id=task.task_id AND attempt.ordinal=task.attempt_count
                WHERE task.task_id=reservation.task_id AND attempt.attempt_id=reservation.attempt_id
                    AND task.fencing_counter=reservation.fencing_counter AND task.lease_token=reservation.lease_token
                    AND task.state='running' AND task.cancel_requested=0 AND task.lease_expires_at_utc>$now
                    AND task.deadline_utc>$now AND reservation.deadline_utc>$now)
            """, cancellationToken, ("$now", ToDb(UtcNow())));
}
