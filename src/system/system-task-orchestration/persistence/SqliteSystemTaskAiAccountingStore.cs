using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>Inert staging result. Accepted means staged, not committed or authorized to dispatch.</summary>
internal sealed record SystemTaskAiAccountingResult(bool Accepted, string Code,
    SystemInnerWorkerAiReservationEvidence? Reservation = null,
    SystemInnerWorkerAiUsageReconciliation? Usage = null);

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private const string ValidationAiEnrollmentFingerprintDomain =
        "dantes-roleplay/system-task-ai-enrollment/application-validation/v1";
    private static readonly JsonSerializerOptions AiJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The caller resolves/rechecks the profile and current grants in this same immediate writer
    /// transaction. Enqueue and enrollment must both succeed before that caller commits. A DTO
    /// construction is not profile authority; this internal core has no public route or fallback.
    /// </summary>
    internal Task<SystemTaskAiAccountingResult> StageEnrollAiBudgetAsync(SystemTaskDurableHandle handle,
        SystemInnerWorkerResolvedProfile profile, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default) => InCallerTransactionAsync(connection, transaction,
        () => EnrollAiCoreAsync(handle, profile, connection, transaction, cancellationToken), value => value.Accepted, cancellationToken);

    /// <summary>Caller reauthorizes this exact dispatch before staging, then commits before invoking it.</summary>
    internal Task<SystemTaskAiAccountingResult> StageReserveAiBudgetAsync(SystemInnerWorkerAiReservationRequest request,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction,
            () => ReserveAiCoreAsync(request, connection, transaction, cancellationToken), value => value.Accepted, cancellationToken);

    private async Task<SystemTaskAiAccountingResult> EnrollAiCoreAsync(SystemTaskDurableHandle handle,
        SystemInnerWorkerResolvedProfile profile, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var task = await ReadSnapshotAsync(connection, transaction, handle, cancellationToken);
        if (task is null) return AiRejected("INNER_AI_ENROLLMENT_SCOPE_MISMATCH");
        if (!AiSubjectPurposeMatches(profile.Worker.Subject, task.Request.Purpose))
            return AiRejected("INNER_AI_SUBJECT_UNSUPPORTED");
        if (!AiScopeMatches(profile.Worker.InvocationHost, task.Request)
            || !AiSubjectMatches(profile, task.Request)
            || task.Request.InputJson != profile.Worker.InputJson) return AiRejected("INNER_AI_ENROLLMENT_SCOPE_MISMATCH");
        if (!await ValidationAdmissionMatchesProfileAsync(connection, transaction, task.Request, profile, cancellationToken))
            return AiRejected("INNER_AI_ENROLLMENT_ADMISSION_MISMATCH");
        var fingerprint = AiEnrollmentFingerprint(profile);
        var existing = await ReadAiCeilingAsync(connection, transaction, handle.TaskId, cancellationToken);
        if (existing is not null) return existing.Purpose == SystemTaskPurposeNames.Get(task.Request.Purpose)
            && existing.Fingerprint == fingerprint
            ? new(true, "INNER_AI_ENROLLMENT_EXISTING") : AiRejected("INNER_AI_ENROLLMENT_CONFLICT");
        if (task.State != SystemTaskLifecycleState.Queued || task.AttemptCount != 0)
            return AiRejected("INNER_AI_ENROLLMENT_TOO_LATE");
        if (profile.Worker.InvocationHost.Budget.DeadlineUtc <= UtcNow()) return AiRejected("INNER_AI_DEADLINE_EXPIRED");
        var ancestry = await ReadAiAncestryAsync(connection, transaction, handle.TaskId, cancellationToken);
        foreach (var ancestor in ancestry.Skip(1))
        {
            var ceiling = await ReadAiCeilingAsync(connection, transaction, ancestor, cancellationToken);
            if (ceiling is null || ceiling.Purpose != SystemTaskPurposeNames.Get(task.Request.Purpose))
                return AiRejected("INNER_AI_ANCESTOR_NOT_ENROLLED");
            if (profile.Worker.InvocationHost.Budget.DeadlineUtc > ceiling.DeadlineUtc)
                return AiRejected("INNER_AI_DEADLINE_EXPANDED");
            ceiling.Budget.NarrowTo(profile.AiBudget);
        }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_ai_ceiling(task_id,task_purpose,enrollment_fingerprint,profile_id,profile_version,
                profile_fingerprint,grant_reference,grant_revision,grant_fingerprint,definition_id,
                definition_version,definition_fingerprint,output_schema_fingerprint,mode,maximum_provider_tokens,
                maximum_tool_calls,maximum_concurrent_provider_requests,deadline_utc,created_at_utc)
            VALUES($task,$purpose,$fingerprint,$profile,$profileVersion,$profileHash,$grant,$grantRevision,$grantHash,
                $definition,$definitionVersion,$definitionHash,$schema,$mode,$tokens,$tools,$concurrency,$deadline,$now)
            """, cancellationToken, ("$task", handle.TaskId), ("$fingerprint", fingerprint),
            ("$purpose", SystemTaskPurposeNames.Get(task.Request.Purpose)),
            ("$profile", profile.ProfileVersion.ExactDefinitionId), ("$profileVersion", profile.ProfileVersion.Version),
            ("$profileHash", profile.ProfileVersion.Fingerprint), ("$grant", profile.Worker.InvocationHost.GrantReference),
            ("$grantRevision", profile.AuthorityProvenance.GrantRevision), ("$grantHash", profile.AuthorityProvenance.GrantFingerprint),
            ("$definition", task.Request.SelectedDefinition?.ExactDefinitionId),
            ("$definitionVersion", task.Request.SelectedDefinition?.Version),
            ("$definitionHash", task.Request.SelectedDefinition?.Fingerprint), ("$schema", profile.OutputSchemaFingerprint),
            ("$mode", SystemInnerWorkerTokenBudgetModeNames.Get(profile.AiBudget.Mode)), ("$tokens", profile.AiBudget.ProviderTokens),
            ("$tools", profile.AiBudget.ToolCalls), ("$concurrency", profile.AiBudget.MaxConcurrentProviderRequests),
            ("$deadline", ToDb(profile.Worker.InvocationHost.Budget.DeadlineUtc)), ("$now", ToDb(UtcNow())));
        return new(true, "INNER_AI_ENROLLED");
    }

    private async Task<SystemTaskAiAccountingResult> ReserveAiCoreAsync(SystemInnerWorkerAiReservationRequest request,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var task = await ReadSnapshotAsync(connection, transaction, request.Task, cancellationToken);
        if (task is null || !AiScopeMatches(request.Host, task.Request)) return AiRejected("INNER_AI_RESERVATION_SCOPE_MISMATCH");
        var existingReference = await ScalarStringAsync(connection, transaction, """
            SELECT record_reference FROM system_task_ai_reservation WHERE task_id=$task AND reservation_id=$id
            """, cancellationToken, ("$task", request.Task.TaskId), ("$id", request.ReservationId));
        if (existingReference is not null)
        {
            var existing = await ReadAiReservationAsync(connection, transaction, existingReference, cancellationToken);
            return existing is not null && existing.Fingerprint == request.ReservationFingerprint
                ? new(true, "INNER_AI_RESERVATION_EXISTING", existing.Evidence) : AiRejected("INNER_AI_RESERVATION_CONFLICT");
        }
        if ((request.ProviderTokens > 0 && request.ToolCalls != 0)
            || (request.ProviderTokens == 0 && request.ToolCalls != 1)) return AiRejected("INNER_AI_DISPATCH_RESERVATION_INVALID");
        if (request.ProviderBoundFailure() is { } unavailable) return AiRejected(unavailable.Code);
        if (request.Host.Budget.DeadlineUtc <= UtcNow()) return AiRejected("INNER_AI_DEADLINE_EXPIRED");
        if (!await AiOwnsAttemptAsync(connection, transaction, task, request.Attempt, cancellationToken))
            return AiRejected("INNER_AI_LEASE_STALE");
        var ancestry = await ReadAiAncestryAsync(connection, transaction, request.Task.TaskId, cancellationToken);
        var own = await ReadAiCeilingAsync(connection, transaction, request.Task.TaskId, cancellationToken);
        if (own is null || own.Purpose != SystemTaskPurposeNames.Get(task.Request.Purpose))
            return AiRejected("INNER_AI_TASK_NOT_ENROLLED");
        if (own.Budget != request.Ceiling) return AiRejected("INNER_AI_CEILING_CHANGED");
        await NormalizeAiReservationsAsync(connection, transaction, cancellationToken);
        var heldTokens = request.ProviderTokens;
        foreach (var ancestor in ancestry)
        {
            var ceiling = await ReadAiCeilingAsync(connection, transaction, ancestor, cancellationToken);
            if (ceiling is null || ceiling.Purpose != SystemTaskPurposeNames.Get(task.Request.Purpose))
                return AiRejected("INNER_AI_ANCESTOR_NOT_ENROLLED");
            if (request.Host.Budget.DeadlineUtc > ceiling.DeadlineUtc)
                return AiRejected("INNER_AI_DEADLINE_EXPANDED");
            ceiling.Budget.NarrowTo(own.Budget);
            var balances = await ReadAiBalancesAsync(connection, transaction, ancestor, cancellationToken);
            if (balances.Blocked) return AiRejected("INNER_AI_RECONCILIATION_REQUIRED");
            if (request.ToolCalls > 0 && (balances.ChargedTokens >= ceiling.Budget.ProviderTokens
                || balances.HeldTokens >= ceiling.Budget.ProviderTokens - balances.ChargedTokens))
                return AiRejected("INNER_AI_TOKEN_THRESHOLD_REACHED");
            if (request.ProviderTokens > 0)
                heldTokens = Math.Min(heldTokens, ceiling.Budget.ProviderReservationAmount(request.ProviderTokens,
                    balances.ChargedTokens, balances.HeldTokens, balances.ActiveProviders, false));
            if (request.ToolCalls > ceiling.Budget.ToolCalls - balances.ChargedTools - balances.HeldTools)
                return AiRejected("INNER_AI_TOOL_BUDGET_EXHAUSTED");
        }
        if (!await ConsumeOperationAsync(connection, transaction, request.Task.TaskId, cancellationToken))
            return AiRejected("INNER_AI_OPERATION_BUDGET_EXHAUSTED");
        var reference = "ai-reservation." + AiHash(request.Task.TaskId + "\n" + request.ReservationId).ToLowerInvariant();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_ai_reservation(record_reference,task_id,reservation_id,request_fingerprint,
                attempt_id,fencing_counter,lease_token,lease_expires_at_utc,deadline_utc,requested_provider_tokens,
                reserved_provider_tokens,reserved_tool_calls,mode,status,created_at_utc,updated_at_utc)
            VALUES($reference,$task,$id,$fingerprint,$attempt,$fence,$token,$expiry,$deadline,$requested,$held,$tools,$mode,'reserved',$now,$now)
            """, cancellationToken, ("$reference", reference), ("$task", request.Task.TaskId), ("$id", request.ReservationId),
            ("$fingerprint", request.ReservationFingerprint), ("$attempt", request.Attempt.AttemptId),
            ("$fence", request.Attempt.FencingCounter), ("$token", request.Attempt.LeaseToken), ("$expiry", ToDb(request.Attempt.LeaseExpiresAtUtc)),
            ("$deadline", ToDb(request.Host.Budget.DeadlineUtc)),
            ("$requested", request.ProviderTokens), ("$held", heldTokens), ("$tools", request.ToolCalls),
            ("$mode", SystemInnerWorkerTokenBudgetModeNames.Get(request.Ceiling.Mode)), ("$now", ToDb(UtcNow())));
        foreach (var ancestor in ancestry)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_ai_reservation_ancestor(record_reference,ancestor_task_id) VALUES($reference,$ancestor)
                """, cancellationToken, ("$reference", reference), ("$ancestor", ancestor));
        return new(true, "INNER_AI_RESERVED", (await ReadAiReservationAsync(connection, transaction, reference, cancellationToken))!.Evidence);
    }

    private static bool AiScopeMatches(InteractionInvocationHost host, SystemTaskStoredRequest task) =>
        host.CommandId == task.Handle.CommandId && host.Principal.PrincipalId == task.Invocation.PrincipalReference
        && host.ApplicationRevision.ApplicationId.Value == task.Invocation.ApplicationId
        && host.ApplicationRevision.Revision == task.Invocation.ApplicationRevision
        && host.ApplicationRevision.Fingerprint == task.Invocation.ApplicationFingerprint
        && InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(host.ApplicationRevision.BaseApplications.Select(value => value.ToString()))) == task.Invocation.BaseApplicationsJson
        && host.StateSpaceId == task.Invocation.StateSpaceId && host.StateRevision == task.Invocation.StateRevision
        && host.GrantReference == task.Invocation.GrantReference && host.Profile == task.Invocation.Profile
        && host.Budget.DeadlineUtc <= task.Invocation.DeadlineUtc
        && (task.Purpose switch
        {
            SystemTaskPurpose.ProcedureWorkflow => host.Profile == InteractionExecutionProfile.Workflow
                && host.StateSpaceId is not null && host.StateRevision is not null,
            SystemTaskPurpose.ApplicationValidation => host.Profile == InteractionExecutionProfile.ReadOnly
                && host.StateSpaceId is null && host.StateRevision is null,
            _ => false
        });

    private static bool AiSubjectMatches(SystemInnerWorkerResolvedProfile profile, SystemTaskStoredRequest task) =>
        (profile.Worker.Subject, task.Purpose) switch
        {
            (SystemInnerWorkerSubject.ProcedureWorkflow workflow, SystemTaskPurpose.ProcedureWorkflow) =>
                task.SelectedDefinition == workflow.ProcedureVersion && task.Candidate is null,
            (SystemInnerWorkerSubject.ApplicationCandidateValidation validation, SystemTaskPurpose.ApplicationValidation) =>
                task.SelectedDefinition is null && task.Candidate == validation.Candidate,
            _ => false
        };

    private static bool AiSubjectPurposeMatches(SystemInnerWorkerSubject subject, SystemTaskPurpose purpose) =>
        (subject, purpose) is (SystemInnerWorkerSubject.ProcedureWorkflow, SystemTaskPurpose.ProcedureWorkflow)
            or (SystemInnerWorkerSubject.ApplicationCandidateValidation, SystemTaskPurpose.ApplicationValidation);

    private async Task<bool> AiOwnsAttemptAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLifecycleSnapshot task, SystemTaskAttemptIdentity attempt, CancellationToken cancellationToken)
    {
        if (attempt.StableCommandId != task.Request.Handle.CommandId) return false;
        return await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_attempt AS attempt JOIN system_task_lifecycle AS task
                ON task.task_id=attempt.task_id AND task.attempt_count=attempt.ordinal
            WHERE task.task_id=$task AND attempt.attempt_id=$attempt AND attempt.fencing_counter=$fence
                AND attempt.lease_token=$token AND task.fencing_counter=$fence AND task.lease_token=$token
                AND task.state='running' AND task.cancel_requested=0 AND task.lease_expires_at_utc>$now AND task.deadline_utc>$now
            """, cancellationToken, ("$task", task.Request.Handle.TaskId), ("$attempt", attempt.AttemptId),
            ("$fence", attempt.FencingCounter), ("$token", attempt.LeaseToken), ("$now", ToDb(UtcNow()))) == 1;
    }

    internal static string AiEnrollmentFingerprint(SystemInnerWorkerResolvedProfile profile) =>
        profile.Worker.Subject switch
        {
            SystemInnerWorkerSubject.ProcedureWorkflow workflow => AiEnrollmentFingerprint(profile, workflow.ProcedureVersion),
            SystemInnerWorkerSubject.ApplicationCandidateValidation validation =>
                InteractionCanonicalJson.Fingerprint(ValidationAiEnrollmentFingerprintDomain,
                    InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                    {
                        purpose = "application-validation",
                        candidate = validation.Candidate,
                        profile.ProfileVersion,
                        profileDefinition = profile.Profile,
                        profile.OutputSchemaFingerprint,
                        inputFingerprint = AiHash(profile.Worker.InputJson),
                        profile.ManualContext,
                        profile.RequiredContextReferences,
                        validateAuthorityProvenance = profile.AuthorityProvenance,
                        readAuthorityProvenance = profile.ReadAuthorityProvenance,
                        profile.Worker.InvocationHost.GrantReference,
                        profile.Worker.InvocationHost.Budget.DeadlineUtc,
                        profile.AiBudget
                    }, AiJson))),
            _ => throw new InteractionContractException("INNER_AI_SUBJECT_UNSUPPORTED",
                "AI accounting does not support this worker subject.")
        };

    private static string AiEnrollmentFingerprint(SystemInnerWorkerResolvedProfile profile,
        SystemTaskSelectedDefinition procedureVersion)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            profile.ProfileVersion, profileDefinition = profile.Profile,
            profile.OutputSchemaFingerprint, profile.AuthorityProvenance,
            ProcedureVersion = procedureVersion, profile.Worker.InvocationHost.GrantReference, profile.AiBudget,
            profile.ToolBindings, profile.RequiredContextReferences, contextEvidence = profile.ManualContext,
            profile.Worker.InvocationHost.Budget.DeadlineUtc,
            inputFingerprint = AiHash(profile.Worker.InputJson)
        }, AiJson));
        if (profile.Worker.DependencyInputs.Count == 0) return AiHash(canonical);
        using var document = JsonDocument.Parse(canonical);
        var payload = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        payload.Add("dependencyInputs", JsonSerializer.SerializeToElement(
            profile.Worker.DependencyInputs.Select(value => new
            {
                name = value.Name,
                handle = new { taskId = value.Handle.TaskId, commandId = value.Handle.CommandId },
                jsonPointer = value.JsonPointer
            }).ToArray(), AiJson));
        return AiHash(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(payload, AiJson)));
    }

    private static string AiHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static SystemTaskAiAccountingResult AiRejected(string code) => new(false, code);
}
