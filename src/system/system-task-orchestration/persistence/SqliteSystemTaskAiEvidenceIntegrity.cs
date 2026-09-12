using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private static string AiDispatchFingerprint(string reference, SystemTaskAttemptIdentity attempt,
        string kind, string? provider, string? model, string requestHash, string profileFingerprint, string schemaFingerprint) =>
        AiHash(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            reference, attempt, kind, provider, model, requestHash, profileFingerprint, schemaFingerprint
        }, AiJson)));

    private static string AiUsageFingerprint(string reference, SystemTaskAttemptIdentity attempt,
        string kind, AiDispatchCompletionKind completion, string responseHash, long? input, long? output,
        long? total, long tools, bool complete, string requestFingerprint, string profileFingerprint, string schemaFingerprint) =>
        AiHash(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            reference, attempt, kind, completion, responseHash, input, output, total, tools, complete,
            requestFingerprint, profileFingerprint, schemaFingerprint
        }, AiJson)));

    private static string AiDispatchEvent(string reference) => "ai-dispatch." + AiHash(reference).ToLowerInvariant();
    private static string AiUsageEvent(string reference, string payloadHash) =>
        "ai-observation." + AiHash(reference + "\n" + payloadHash).ToLowerInvariant();

    private static AiDispatchCompletionKind AiCompletionValue(string value) => value switch
    {
        "returned" => AiDispatchCompletionKind.Returned, "threw" => AiDispatchCompletionKind.Threw,
        "cancelled" => AiDispatchCompletionKind.Cancelled, "not-started" => AiDispatchCompletionKind.NotStarted,
        _ => throw new SystemTaskException("INNER_AI_USAGE_EVIDENCE_INVALID", "The retained completion kind is invalid.")
    };

    private static async Task<SystemTaskAttemptIdentity> ReadAiEvidenceAttemptAsync(SqliteConnection connection,
        SqliteTransaction transaction, string reference, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT task.command_id,reservation.attempt_id,reservation.lease_token,reservation.fencing_counter,
                reservation.lease_expires_at_utc,
                attempt.task_id=reservation.task_id AND attempt.fencing_counter=reservation.fencing_counter
                    AND attempt.lease_token=reservation.lease_token
            FROM system_task_ai_reservation AS reservation
            JOIN system_task_lifecycle AS task ON task.task_id=reservation.task_id
            JOIN system_task_attempt AS attempt ON attempt.attempt_id=reservation.attempt_id
            WHERE reservation.record_reference=$reference
            """, ("$reference", reference));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(5) != 1)
            throw new SystemTaskException("INNER_AI_ATTEMPT_MISMATCH", "The retained evidence attempt does not match its task and fence.");
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), ParseDb(reader.GetString(4)));
    }

    private static async Task ValidateAiSettlementCacheAsync(SqliteConnection connection, SqliteTransaction transaction,
        AiReservationRow reservation, CancellationToken cancellationToken)
    {
        var rows = await ReadAiUsageRowsAsync(connection, transaction, reservation.Evidence.RecordReference, cancellationToken);
        var valid = false;
        if (rows.Count == 0)
        {
            valid = reservation.SettledSequence is null && (reservation.State switch
            {
                "reserved" => reservation.ChargedTokens == 0 && reservation.ChargedTools == 0,
                "unknown" => reservation.ChargedTokens == reservation.Evidence.ProviderTokens
                    && reservation.ChargedTools == reservation.Evidence.ToolCalls,
                _ => false
            });
        }
        else
        {
            var usage = CalculateRetainedAiUsage(reservation, rows);
            var state = !usage.UsageKnown ? "unknown" : usage.ExceededReservation ? "exceeded" : "settled";
            valid = reservation.State == state && reservation.ChargedTokens == usage.ChargedProviderTokens
                && reservation.ChargedTools == usage.ChargedToolCalls && reservation.SettledSequence == rows[^1].Sequence;
        }
        if (!valid)
            throw new SystemTaskException("INNER_AI_SETTLEMENT_EVIDENCE_INVALID", "The retained settlement does not match its authoritative usage evidence.");
    }
}
