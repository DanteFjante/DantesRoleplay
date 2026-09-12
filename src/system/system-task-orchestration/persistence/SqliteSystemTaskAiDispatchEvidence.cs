using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    /// <summary>Persists the full bounded request data, never a truncated prompt. No provider is called.</summary>
    internal Task<SystemTaskAiAccountingResult> StageRecordAiProviderDispatchAsync(string reference,
        SystemTaskAttemptIdentity attempt, SystemInnerWorkerResolvedProfile profile, AiProviderCallDescriptor call,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction, () => RecordAiDispatchCoreAsync(reference, attempt, profile,
            "provider", call.ProviderId, call.Request.Model, AiProviderRequestJson(profile, call),
            connection, transaction, cancellationToken), result => result.Accepted, cancellationToken);

    internal Task<SystemTaskAiAccountingResult> StageRecordAiToolDispatchAsync(string reference,
        SystemTaskAttemptIdentity attempt, SystemInnerWorkerResolvedProfile profile, AiToolDispatchDescriptor dispatch,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction, () => RecordAiDispatchCoreAsync(reference, attempt, profile,
            "tool", null, null, InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                dispatch.DispatchOrdinal, dispatch.Definition, dispatch.Invocation
            }, AiJson)), connection, transaction, cancellationToken), result => result.Accepted, cancellationToken);

    /// <summary>Only the trusted in-process provider hook supplies this observation. It grants no lease.</summary>
    internal Task<SystemTaskAiAccountingResult> StageRecordAiProviderOutcomeAsync(string reference,
        SystemTaskAttemptIdentity attempt, AiProviderCallObservation outcome, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction, () => RecordAiOutcomeCoreAsync(reference, attempt, "provider",
            outcome.Kind, OutcomeFingerprint(new { outcome.Kind, outcome.Response, outcome.FailureCode }),
            outcome.Kind == AiDispatchCompletionKind.NotStarted ? 0 : outcome.Response?.Usage?.InputTokens,
            outcome.Kind == AiDispatchCompletionKind.NotStarted ? 0 : outcome.Response?.Usage?.OutputTokens,
            outcome.Kind == AiDispatchCompletionKind.NotStarted ? 0 : outcome.Response?.Usage?.TotalTokens,
            0, outcome.Kind == AiDispatchCompletionKind.NotStarted || outcome.Response?.Usage?.IsComplete == true,
            connection, transaction, cancellationToken), result => result.Accepted, cancellationToken);

    /// <summary>Counts actual entry, including throwing/cancelled tools; suggestions are never calls.</summary>
    internal Task<SystemTaskAiAccountingResult> StageRecordAiToolOutcomeAsync(string reference,
        SystemTaskAttemptIdentity attempt, AiToolDispatchObservation outcome, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction, () => RecordAiOutcomeCoreAsync(reference, attempt, "tool",
            outcome.Kind, OutcomeFingerprint(new { outcome.Kind, outcome.Result, outcome.FailureCode }), 0, 0, 0,
            outcome.Kind == AiDispatchCompletionKind.NotStarted ? 0 : 1, true,
            connection, transaction, cancellationToken), result => result.Accepted, cancellationToken);

    private async Task<SystemTaskAiAccountingResult> RecordAiDispatchCoreAsync(string reference,
        SystemTaskAttemptIdentity attempt, SystemInnerWorkerResolvedProfile profile, string kind, string? provider,
        string? model, string requestJson, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var reservation = await ReadAiReservationAsync(connection, transaction, reference, cancellationToken);
        if (reservation is null || reservation.Evidence.Attempt != attempt) return AiRejected("INNER_AI_DISPATCH_IDENTITY_MISMATCH");
        var task = await ReadSnapshotAsync(connection, transaction, reservation.Evidence.Task, cancellationToken);
        var ceiling = await ReadAiCeilingAsync(connection, transaction, reservation.Evidence.Task.TaskId, cancellationToken);
        if (task is null || ceiling is null || ceiling.Purpose != SystemTaskPurposeNames.Get(task.Request.Purpose)
            || !AiScopeMatches(profile.Worker.InvocationHost, task.Request) || !AiSubjectMatches(profile, task.Request)
            || task.Request.InputJson != profile.Worker.InputJson
            || ceiling.Fingerprint != AiEnrollmentFingerprint(profile)) return AiRejected("INNER_AI_DISPATCH_SCOPE_MISMATCH");
        if ((kind == "provider") != (reservation.Evidence.ProviderTokens > 0)) return AiRejected("INNER_AI_DISPATCH_KIND_MISMATCH");
        if (provider is not null && (Required(provider, 200, nameof(provider)) != provider || provider.Any(char.IsControl)))
            throw new InteractionContractException("INNER_AI_PROVIDER_ID_INVALID", "The provider identity is invalid.");
        if (model is not null && (Required(model, 200, nameof(model)) != model || model.Any(char.IsControl)))
            throw new InteractionContractException("INNER_AI_MODEL_ID_INVALID", "The model identity is invalid.");
        var requestHash = AiHash(requestJson);
        var payloadHash = AiDispatchFingerprint(reference, attempt, kind, provider, model, requestHash,
            ceiling.ProfileFingerprint, ceiling.SchemaFingerprint);
        var existing = await ScalarStringAsync(connection, transaction, """
            SELECT payload_fingerprint FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference AND sequence=0
            """, cancellationToken, ("$reference", reference));
        if (existing is not null) return existing == payloadHash
            ? new(true, "INNER_AI_DISPATCH_EXISTING", reservation.Evidence) : AiRejected("INNER_AI_DISPATCH_CONFLICT");
        if (profile.Worker.InvocationHost.Budget.DeadlineUtc <= UtcNow() || reservation.DeadlineUtc <= UtcNow())
            return AiRejected("INNER_AI_DEADLINE_EXPIRED");
        if (reservation.State != "reserved" || !await AiOwnsAttemptAsync(connection, transaction, task, attempt, cancellationToken))
            return AiRejected("INNER_AI_LEASE_STALE");
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_ai_dispatch_evidence(record_reference,sequence,event_reference,payload_fingerprint,
                kind,dispatch_kind,request_fingerprint,provider_id,model_id,profile_fingerprint,schema_fingerprint,
                request_json,observed_at_utc)
            VALUES($reference,0,$event,$payload,'dispatch',$kind,$request,$provider,$model,$profile,$schema,$json,$now)
            """, cancellationToken, ("$reference", reference), ("$event", AiDispatchEvent(reference)),
            ("$payload", payloadHash), ("$kind", kind), ("$request", requestHash), ("$provider", provider), ("$model", model),
            ("$profile", ceiling.ProfileFingerprint), ("$schema", ceiling.SchemaFingerprint), ("$json", requestJson), ("$now", ToDb(UtcNow())));
        return new(true, "INNER_AI_DISPATCH_RECORDED", reservation.Evidence);
    }

    private async Task<SystemTaskAiAccountingResult> RecordAiOutcomeCoreAsync(string reference,
        SystemTaskAttemptIdentity attempt, string kind, AiDispatchCompletionKind completion, string responseHash,
        long? input, long? output, long? total, long tools, bool complete, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var reservation = await ReadAiReservationAsync(connection, transaction, reference, cancellationToken);
        if (reservation is null || reservation.Evidence.Attempt != attempt) return AiRejected("INNER_AI_USAGE_IDENTITY_MISMATCH");
        var dispatch = await ReadAiDispatchAsync(connection, transaction, reference, cancellationToken);
        if (dispatch is null) return AiRejected("INNER_AI_DISPATCH_EVIDENCE_UNAVAILABLE");
        if (dispatch.Kind != kind) return AiRejected("INNER_AI_DISPATCH_KIND_MISMATCH");
        var report = new SystemInnerWorkerAiUsageReport(reference, attempt, input, output, tools, total, complete);
        var payloadHash = AiUsageFingerprint(reference, attempt, kind, completion, responseHash, input, output,
            total, tools, complete, dispatch.RequestFingerprint, dispatch.ProfileFingerprint, dispatch.SchemaFingerprint);
        var existing = await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE record_reference=$reference AND payload_fingerprint=$hash
            """, cancellationToken, ("$reference", reference), ("$hash", payloadHash));
        if (existing != 0)
        {
            // Older partial evidence redelivery cannot replace or reduce a newer retained charge.
            return new(true, "INNER_AI_OBSERVATION_EXISTING", reservation.Evidence, await ReadCurrentAiUsageAsync(
                connection, transaction, reservation, cancellationToken));
        }
        var previous = await ReadAiUsageRowsAsync(connection, transaction, reference, cancellationToken);
        if (previous.Count >= 16) return AiRejected("INNER_AI_OBSERVATION_LIMIT");
        if (!AiUsageIsMonotonic(previous, report, completion)) return AiRejected("INNER_AI_USAGE_CONFLICT");
        var sequence = previous.Count + 1;
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_ai_dispatch_evidence(record_reference,sequence,event_reference,payload_fingerprint,
                kind,dispatch_kind,request_fingerprint,provider_id,model_id,profile_fingerprint,schema_fingerprint,
                response_fingerprint,input_tokens,output_tokens,total_tokens,observed_tool_calls,is_complete,completion_kind,observed_at_utc)
            VALUES($reference,$sequence,$event,$payload,'usage',$kind,$request,$provider,$model,$profile,$schema,
                $response,$input,$output,$total,$tools,$complete,$completion,$now)
            """, cancellationToken, ("$reference", reference), ("$sequence", sequence),
            ("$event", AiUsageEvent(reference, payloadHash)),
            ("$payload", payloadHash), ("$kind", kind), ("$request", dispatch.RequestFingerprint),
            ("$provider", dispatch.Provider), ("$model", dispatch.Model), ("$profile", dispatch.ProfileFingerprint),
            ("$schema", dispatch.SchemaFingerprint), ("$response", responseHash), ("$input", input), ("$output", output),
            ("$total", total), ("$tools", tools), ("$complete", complete ? 1 : 0),
            ("$completion", AiCompletionName(completion)), ("$now", ToDb(UtcNow())));
        // Read the just-persisted evidence back for settlement. The numbers in a later caller's
        // report can only select/confirm retained evidence; they can never create accounting facts.
        return await ReconcileAiCoreAsync(reservation, report, connection, transaction, cancellationToken);
    }

    private static string OutcomeFingerprint<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AiJson);
        if (bytes.Length > 1_048_576)
            throw new InteractionContractException("INNER_AI_RESPONSE_EVIDENCE_TOO_LARGE", "The provider outcome exceeds the bounded evidence hashing input.");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static string AiProviderRequestJson(SystemInnerWorkerResolvedProfile profile, AiProviderCallDescriptor call)
    {
        if (AiHash(InteractionCanonicalJson.CanonicalizeObject(call.Request.ResponseSchemaJson)) != profile.OutputSchemaFingerprint)
            throw new InteractionContractException("INNER_AI_DISPATCH_SCHEMA_MISMATCH", "The actual provider request changed the enrolled output schema.");
        // All request data fields are retained. ToolExecutor is a host delegate, always stripped
        // from the frozen descriptor; it is neither provider payload nor serializable authority.
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            call.Round, call.Request.Model, call.Request.Messages, call.Request.Kind, call.Request.Reasoning,
            call.Request.ResponseSchemaJson, call.Request.Tools, call.Request.MaximumOutputTokens,
            call.Request.MaximumToolCalls, call.Request.MaximumResponseBytes, call.Request.MaximumDuration
        }, AiJson));
    }

    private static string AiCompletionName(AiDispatchCompletionKind value) => value switch
    {
        AiDispatchCompletionKind.Returned => "returned", AiDispatchCompletionKind.Threw => "threw",
        AiDispatchCompletionKind.Cancelled => "cancelled", AiDispatchCompletionKind.NotStarted => "not-started",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
