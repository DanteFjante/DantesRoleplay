using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private sealed record AiDispatchRow(string Kind, string RequestFingerprint, string? Provider, string? Model,
        string ProfileFingerprint, string SchemaFingerprint);
    private sealed record AiUsageRow(int Sequence, long? Input, long? Output, long? Total, long Tools,
        bool Complete, string Completion);

    /// <summary>Reports select retained host observations; caller numbers alone cannot settle a hold.</summary>
    internal Task<SystemTaskAiAccountingResult> StageReconcileAiUsageAsync(InteractionInvocationHost host,
        SystemInnerWorkerAiUsageReport report, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default) => InCallerTransactionAsync(connection, transaction, async () =>
        {
            var reservation = await ReadAiReservationAsync(connection, transaction, report.ReservationRecordReference, cancellationToken);
            if (reservation is null || reservation.Evidence.Attempt != report.Attempt) return AiRejected("INNER_AI_USAGE_IDENTITY_MISMATCH");
            var task = await ReadSnapshotAsync(connection, transaction, reservation.Evidence.Task, cancellationToken);
            if (task is null || !AiScopeMatches(host, task.Request)) return AiRejected("INNER_AI_USAGE_SCOPE_MISMATCH");
            return await ReconcileAiCoreAsync(reservation, report, connection, transaction, cancellationToken);
        }, result => result.Accepted, cancellationToken);

    private async Task<SystemTaskAiAccountingResult> ReconcileAiCoreAsync(AiReservationRow reservation,
        SystemInnerWorkerAiUsageReport report, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = await ReadAiUsageRowsAsync(connection, transaction, reservation.Evidence.RecordReference, cancellationToken);
        var observed = rows.LastOrDefault();
        if (observed is null)
        {
            var unknown = SystemInnerWorkerAiUsageReconciliation.Calculate(reservation.Evidence,
                new(reservation.Evidence.RecordReference, reservation.Evidence.Attempt, null, null, 0));
            await ApplyAiChargeAsync(connection, transaction, reservation, unknown, null, cancellationToken);
            return new(true, "INNER_AI_USAGE_EVIDENCE_UNAVAILABLE", reservation.Evidence, unknown);
        }
        if (observed.Input != report.InputTokens || observed.Output != report.OutputTokens || observed.Total != report.TotalTokens
            || observed.Tools != report.ToolCalls || observed.Complete != report.IsComplete)
            return AiRejected("INNER_AI_USAGE_EVIDENCE_MISMATCH");
        var calculation = CalculateRetainedAiUsage(reservation, rows);
        await ApplyAiChargeAsync(connection, transaction, reservation, calculation, observed.Sequence, cancellationToken);
        return new(true, calculation.Code, reservation.Evidence, calculation);
    }

    private Task ApplyAiChargeAsync(SqliteConnection connection, SqliteTransaction transaction,
        AiReservationRow reservation, SystemInnerWorkerAiUsageReconciliation calculation, int? sequence,
        CancellationToken cancellationToken) => ExecuteAsync(connection, transaction, """
            UPDATE system_task_ai_reservation SET status=$status,charged_provider_tokens=$tokens,
                charged_tool_calls=$tools,settled_evidence_sequence=$sequence,updated_at_utc=$now
            WHERE record_reference=$reference
            """, cancellationToken, ("$status", !calculation.UsageKnown ? "unknown" : calculation.ExceededReservation ? "exceeded" : "settled"),
            ("$tokens", calculation.ChargedProviderTokens), ("$tools", calculation.ChargedToolCalls), ("$sequence", sequence),
            ("$now", ToDb(UtcNow())), ("$reference", reservation.Evidence.RecordReference));

    private static async Task<SystemInnerWorkerAiUsageReconciliation?> ReadCurrentAiUsageAsync(SqliteConnection connection,
        SqliteTransaction transaction, AiReservationRow reservation, CancellationToken cancellationToken)
    {
        var rows = await ReadAiUsageRowsAsync(connection, transaction, reservation.Evidence.RecordReference, cancellationToken);
        return rows.Count == 0 ? null : CalculateRetainedAiUsage(reservation, rows);
    }

    private static SystemInnerWorkerAiUsageReconciliation CalculateRetainedAiUsage(AiReservationRow reservation,
        IReadOnlyList<AiUsageRow> rows)
    {
        var latest = rows[^1];
        var report = new SystemInnerWorkerAiUsageReport(reservation.Evidence.RecordReference, reservation.Evidence.Attempt,
            latest.Input, latest.Output, latest.Tools, latest.Total, latest.Complete);
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(reservation.Evidence, report);
        // A complete measured overrun is accounted usage. It consumes the remaining ceiling but
        // is not uncertain evidence, does not retain an in-flight slot, and cannot block cleanup.
        if (result.UsageKnown) return result with { RequiresReconciliation = false };
        var lowerBound = AiObservedLowerBound(rows);
        var tokens = Math.Max(result.ChargedProviderTokens, lowerBound);
        var tools = Math.Max(result.ChargedToolCalls, rows.Max(value => value.Tools));
        return result with
        {
            ChargedProviderTokens = tokens,
            ChargedToolCalls = tools,
            ExceededReservation = result.ExceededReservation || lowerBound > reservation.Evidence.ProviderTokens
                || tools > reservation.Evidence.ToolCalls
        };
    }

    private static bool AiUsageIsMonotonic(IReadOnlyList<AiUsageRow> previous,
        SystemInnerWorkerAiUsageReport report, AiDispatchCompletionKind completion)
    {
        if (previous.Count == 0) return true;
        // A different observation after an authoritative final total cannot replace that total.
        // Exact payload duplicates are handled before this check without appending another row.
        if (previous.Any(value => value.Complete && value.Total.HasValue)) return false;
        if (completion == AiDispatchCompletionKind.NotStarted) return false;
        if (report.InputTokens is { } input && input < previous.Max(value => value.Input ?? 0)) return false;
        if (report.OutputTokens is { } output && output < previous.Max(value => value.Output ?? 0)) return false;
        if (report.ToolCalls < previous.Max(value => value.Tools)) return false;
        if (report.TotalTokens is { } total && total < AiObservedLowerBound(previous)) return false;
        return true;
    }

    private static long AiObservedLowerBound(IReadOnlyList<AiUsageRow> rows) => Math.Max(
        rows.Max(value => value.Total ?? 0), checked(rows.Max(value => value.Input ?? 0) + rows.Max(value => value.Output ?? 0)));

    private static async Task<AiDispatchRow?> ReadAiDispatchAsync(SqliteConnection connection, SqliteTransaction transaction,
        string reference, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT dispatch_kind,request_fingerprint,provider_id,model_id,profile_fingerprint,schema_fingerprint,request_json,
                payload_fingerprint,event_reference
            FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference AND sequence=0 AND kind='dispatch'
            """, ("$reference", reference));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (AiHash(reader.GetString(6)) != reader.GetString(1))
            throw new SystemTaskException("INNER_AI_DISPATCH_EVIDENCE_INVALID", "The retained dispatch bytes do not match their fingerprint.");
        var result = new AiDispatchRow(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5));
        var attempt = await ReadAiEvidenceAttemptAsync(connection, transaction, reference, cancellationToken);
        if (reader.GetString(7) != AiDispatchFingerprint(reference, attempt, result.Kind, result.Provider, result.Model,
                result.RequestFingerprint, result.ProfileFingerprint, result.SchemaFingerprint)
            || reader.GetString(8) != AiDispatchEvent(reference))
            throw new SystemTaskException("INNER_AI_DISPATCH_EVIDENCE_INVALID", "The retained dispatch identity does not match its fingerprint.");
        return result;
    }

    private static async Task<IReadOnlyList<AiUsageRow>> ReadAiUsageRowsAsync(SqliteConnection connection,
        SqliteTransaction transaction, string reference, CancellationToken cancellationToken)
    {
        var dispatch = await ReadAiDispatchAsync(connection, transaction, reference, cancellationToken);
        var attempt = await ReadAiEvidenceAttemptAsync(connection, transaction, reference, cancellationToken);
        var result = new List<AiUsageRow>();
        await using var command = Command(connection, transaction, """
            SELECT sequence,input_tokens,output_tokens,total_tokens,observed_tool_calls,is_complete,completion_kind,
                dispatch_kind,request_fingerprint,profile_fingerprint,schema_fingerprint,provider_id,model_id,
                payload_fingerprint,response_fingerprint,event_reference
            FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference AND sequence>0 ORDER BY sequence LIMIT 17
            """, ("$reference", reference));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (result.Count == 16 || reader.GetInt32(0) != result.Count + 1 || dispatch is null
                || reader.GetString(7) != dispatch.Kind || reader.GetString(8) != dispatch.RequestFingerprint
                || reader.GetString(9) != dispatch.ProfileFingerprint || reader.GetString(10) != dispatch.SchemaFingerprint
                || (reader.IsDBNull(11) ? null : reader.GetString(11)) != dispatch.Provider
                || (reader.IsDBNull(12) ? null : reader.GetString(12)) != dispatch.Model)
                throw new SystemTaskException("INNER_AI_USAGE_EVIDENCE_INVALID", "Usage evidence does not retain the exact bounded dispatch identity.");
            var row = new AiUsageRow(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt32(5) == 1, reader.GetString(6));
            var completion = AiCompletionValue(row.Completion);
            var hash = AiUsageFingerprint(reference, attempt, dispatch.Kind, completion, reader.GetString(14),
                row.Input, row.Output, row.Total, row.Tools, row.Complete, dispatch.RequestFingerprint,
                dispatch.ProfileFingerprint, dispatch.SchemaFingerprint);
            var report = new SystemInnerWorkerAiUsageReport(reference, attempt, row.Input, row.Output, row.Tools, row.Total, row.Complete);
            if (reader.GetString(13) != hash || reader.GetString(15) != AiUsageEvent(reference, hash)
                || !AiUsageIsMonotonic(result, report, completion))
                throw new SystemTaskException("INNER_AI_USAGE_EVIDENCE_INVALID", "The retained usage does not match its fingerprint or ordered evidence.");
            result.Add(row);
        }
        return result.AsReadOnly();
    }
}
