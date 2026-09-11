using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>Verified retained facts for one completed, single-call candidate reviewer run.</summary>
internal sealed record SystemTaskCompletedValidationProof(
    SystemTaskLifecycleSnapshot Snapshot,
    SystemTaskAttemptIdentity Attempt,
    string AdmissionPayloadJson,
    string EnrollmentFingerprint,
    string ProfileFingerprint,
    string SchemaFingerprint,
    SystemInnerWorkerAiBudget Budget);

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    internal async Task<SystemTaskCompletedValidationProof?> ReadCompletedValidationProofAsync(
        SystemTaskDurableHandle handle, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(connection, transaction, handle, cancellationToken);
        if (snapshot is null || snapshot.State != SystemTaskLifecycleState.Completed
            || snapshot.Request.Purpose != SystemTaskPurpose.ApplicationValidation
            || snapshot.Request.Candidate is null || snapshot.ResultJson is null
            || snapshot.CompletionEvidenceReference is null || snapshot.AttemptCount != 1
            || snapshot.CancellationRequested || snapshot.CancellationAcknowledged
            || await HasUnresolvedAiAccountingAsync(connection, transaction, handle.TaskId, cancellationToken)) return null;

        string? admission;
        await using (var command = Command(connection, transaction,
            "SELECT admission_payload_json FROM system_task_lifecycle WHERE task_id=$task AND command_id=$command",
            ("$task", handle.TaskId), ("$command", handle.CommandId)))
            admission = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (admission is null) return null; // ReadSnapshotAsync already verified its canonical hash and retained fields.

        var references = new List<string>();
        await using (var command = Command(connection, transaction,
            "SELECT record_reference FROM system_task_ai_reservation WHERE task_id=$task ORDER BY record_reference LIMIT 2",
            ("$task", handle.TaskId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) references.Add(reader.GetString(0));
        if (references.Count != 1) return null;
        var reservation = await ReadAiReservationAsync(connection, transaction, references[0], cancellationToken);
        if (reservation is null || reservation.State is not ("settled" or "exceeded")
            || reservation.Evidence.Task != handle || reservation.Evidence.ProviderTokens < 1
            || reservation.Evidence.ToolCalls != 0) return null;
        var dispatch = await ReadAiDispatchAsync(connection, transaction, references[0], cancellationToken);
        var usage = await ReadAiUsageRowsAsync(connection, transaction, references[0], cancellationToken);
        if (dispatch?.Kind != "provider" || usage.Count == 0 || !usage[^1].Complete
            || usage[^1].Completion != "returned" || usage[^1].Tools != 0) return null;
        var ceiling = await ReadAiCeilingAsync(connection, transaction, handle.TaskId, cancellationToken);
        if (ceiling is null || ceiling.Purpose != SystemTaskPurposeNames.Get(SystemTaskPurpose.ApplicationValidation)
            || ceiling.Budget.ToolCalls != 0
            || ceiling.ProfileFingerprint != SystemInnerWorkerCandidateReviewer.ProfileVersion.Fingerprint
            || dispatch.ProfileFingerprint != ceiling.ProfileFingerprint
            || dispatch.SchemaFingerprint != ceiling.SchemaFingerprint) return null;

        var attempt = reservation.Evidence.Attempt;
        await using (var command = Command(connection, transaction, """
            SELECT ordinal,fencing_counter,lease_token,state,completed_at_utc
            FROM system_task_attempt WHERE task_id=$task AND attempt_id=$attempt
            """, ("$task", handle.TaskId), ("$attempt", attempt.AttemptId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != snapshot.AttemptCount
                || reader.GetInt64(1) != snapshot.FencingCounter || reader.GetString(2) != attempt.LeaseToken
                || reader.GetString(3) != "completed" || reader.IsDBNull(4)) return null;
        }
        return new(snapshot, attempt, admission, ceiling.Fingerprint, ceiling.ProfileFingerprint,
            ceiling.SchemaFingerprint, ceiling.Budget);
    }
}
