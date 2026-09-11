using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    /// <summary>
    /// Stages an already resolved candidate-validation worker in the caller's writer transaction.
    /// The caller owns current candidate, Read, Validate, and optional causation resolution; this
    /// method persists and verifies that exact closed shape without creating authority or dispatch.
    /// </summary>
    internal Task<SystemTaskEnqueueResult> StageEnqueueValidationAsync(SystemInnerWorkerResolvedProfile profile,
        bool propagateCancellation, string? causationOperationId, string? causalCommandId,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default) =>
        InCallerTransactionAsync(connection, transaction,
            () => EnqueueValidationCoreAsync(profile, propagateCancellation, causationOperationId, causalCommandId,
                connection, transaction, cancellationToken),
            result => result.Disposition is SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing,
            cancellationToken);

    private async Task<SystemTaskEnqueueResult> EnqueueValidationCoreAsync(SystemInnerWorkerResolvedProfile profile,
        bool propagateCancellation, string? causationOperationId, string? causalCommandId,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var host = profile.Worker.InvocationHost;
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ApplicationCandidateValidation validation
            || host.Profile != InteractionExecutionProfile.ReadOnly || host.StateSpaceId is not null
            || host.StateRevision is not null || validation.Candidate.ApplicationId != host.ApplicationRevision.ApplicationId
            || profile.ToolBindings.Count != 0 || profile.AiBudget.ToolCalls != 0
            || profile.ReadAuthorityProvenance is null)
            return Rejected("SYSTEM_TASK_VALIDATION_SCOPE_INVALID",
                "Candidate validation requires its exact read-only application worker profile.");

        SystemTaskValidationCausation? causation = null;
        if ((causationOperationId is null) != (causalCommandId is null))
            return Rejected("SYSTEM_TASK_VALIDATION_CAUSATION_INVALID",
                "Validation causation requires both an operation and its causal command identity.");
        if (causationOperationId is not null)
        {
            if (!ValidOperationId(causationOperationId)
                || !ValidCausalCommandId(causalCommandId))
                return Rejected("SYSTEM_TASK_VALIDATION_CAUSATION_INVALID",
                    "Validation causation requires an exact operation and causal command identity.");
            causation = new(causationOperationId, causalCommandId!);
        }

        var taskId = NewTaskId(host.CommandId);
        var existing = await FindByCommandAsync(connection, transaction, host.CommandId, cancellationToken);
        if (existing is not null)
        {
            var retainedAdmitted = await ScalarLongAsync(connection, transaction,
                "SELECT admitted_operations FROM system_task_lifecycle WHERE task_id=$task", cancellationToken,
                ("$task", existing.Value.TaskId));
            if (retainedAdmitted is < 1 or > SystemTaskLifecycleLimits.MaximumRootOperations)
                return Rejected("SYSTEM_TASK_VALIDATION_PROOF_INVALID", "The retained validation admission proof is invalid.");
            var replayPayload = ValidationAdmissionPayload(profile, propagateCancellation, causation, (int)retainedAdmitted);
            var replayFingerprint = InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain, replayPayload);
            return StringComparer.Ordinal.Equals(existing.Value.PayloadFingerprint, replayFingerprint)
                ? new(SystemTaskEnqueueDisposition.Existing, new(existing.Value.TaskId, host.CommandId),
                    "SYSTEM_TASK_ALREADY_ENQUEUED", "The equivalent durable validation task already exists.")
                : new(SystemTaskEnqueueDisposition.Conflict, null, "SYSTEM_TASK_COMMAND_CONFLICT",
                    "The command identity is already bound to a different durable payload.");
        }

        var admitted = Math.Min(host.Budget.RemainingOperations, SystemTaskLifecycleLimits.MaximumRootOperations);
        if (admitted < 1)
            return Rejected("SYSTEM_TASK_BUDGET_EXHAUSTED", "The task has no remaining operation allowance.");
        var payloadJson = ValidationAdmissionPayload(profile, propagateCancellation, causation, admitted);
        if (System.Text.Encoding.UTF8.GetByteCount(payloadJson) > 65_536)
            return Rejected("SYSTEM_TASK_VALIDATION_PROOF_TOO_LARGE",
                "The validation admission proof exceeds its retained evidence bound.");
        var payloadFingerprint = InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain, payloadJson);

        var now = UtcNow();
        if (host.Budget.DeadlineUtc <= now)
            return Rejected("SYSTEM_TASK_DEADLINE_EXPIRED", "The task deadline has already expired.");
        var activeCount = await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE state IN ('queued','running','waiting','retry')", cancellationToken);
        if (activeCount >= SystemTaskLifecycleLimits.MaximumQueuedTasks)
            return Rejected("SYSTEM_TASK_QUEUE_FULL", "The durable task queue is at its configured bound.");

        var candidate = validation.Candidate;
        var basesJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
            host.ApplicationRevision.BaseApplications.Select(value => value.ToString())));
        string rootTaskId = taskId;
        string? parentTaskId = null;
        var parentDepth = 0;
        var rootBudget = admitted;
        if (host.ParentCommandId is not null)
        {
            if (StringComparer.Ordinal.Equals(host.ParentCommandId, host.CommandId))
                return Rejected("SYSTEM_TASK_PARENT_SELF", "A task cannot be its own parent.");
            var parent = await FindValidationParentAsync(connection, transaction, host.ParentCommandId, cancellationToken);
            if (parent is null)
                return Rejected("SYSTEM_TASK_PARENT_UNKNOWN", "The declared validation parent task does not exist.");
            if (parent.Depth >= SystemTaskLifecycleLimits.MaximumParentDepth)
                return Rejected("SYSTEM_TASK_PARENT_DEPTH", "The durable task parent depth is at its configured bound.");
            if (host.Budget.DeadlineUtc > parent.DeadlineUtc || admitted > parent.AdmittedOperations)
                return Rejected("SYSTEM_TASK_CHILD_BUDGET_EXPANDED", "A child task cannot expand its parent deadline or allowance.");
            var parentSnapshot = await ReadSnapshotByTaskAsync(connection, transaction, parent.TaskId, cancellationToken);
            if (!ValidationScopeMatches(parent, host, candidate, basesJson)
                || parentSnapshot is null || !ValidationRequestScopeMatches(parentSnapshot.Request, host, candidate, basesJson))
                return Rejected("SYSTEM_TASK_PARENT_SCOPE_MISMATCH",
                    "A validation child must retain its parent's exact candidate, principal, and application scope.");
            var childCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM system_task_lifecycle WHERE parent_task_id=$parent", cancellationToken,
                ("$parent", parent.TaskId));
            if (childCount >= SystemTaskLifecycleLimits.MaximumChildrenPerTask)
                return Rejected("SYSTEM_TASK_FANOUT_LIMIT", "The parent task is at its child fan-out bound.");
            var descendantCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM system_task_lifecycle WHERE root_task_id=$root AND task_id<>$root", cancellationToken,
                ("$root", parent.RootTaskId));
            if (descendantCount >= SystemTaskLifecycleLimits.MaximumDescendantsPerRoot)
                return Rejected("SYSTEM_TASK_DESCENDANT_LIMIT", "The root task is at its descendant bound.");
            rootTaskId = parent.RootTaskId;
            parentTaskId = parent.TaskId;
            parentDepth = parent.Depth + 1;
            rootBudget = parent.RootMaximumOperations;
        }

        var ancestorIds = parentTaskId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await ReadAncestorIdsAsync(connection, transaction, parentTaskId, cancellationToken);
        foreach (var dependency in profile.Worker.DependencyHandles)
        {
            if (StringComparer.Ordinal.Equals(dependency.CommandId, host.CommandId)
                || StringComparer.Ordinal.Equals(dependency.TaskId, taskId))
                return Rejected("SYSTEM_TASK_DEPENDENCY_SELF", "A task cannot depend on itself.");
            if (!await FindHandleAsync(connection, transaction, dependency, cancellationToken))
                return Rejected("SYSTEM_TASK_DEPENDENCY_UNKNOWN",
                    "Every dependency must already exist with the exact durable handle.");
            if (!await ValidationDependencyMatchesAsync(connection, transaction, dependency, host, candidate,
                basesJson, cancellationToken))
                return Rejected("SYSTEM_TASK_DEPENDENCY_SCOPE_MISMATCH",
                    "A validation dependency must have the same candidate, principal, and application scope.");
            var dependencySnapshot = await ReadSnapshotAsync(connection, transaction, dependency, cancellationToken);
            if (dependencySnapshot is null
                || !ValidationRequestScopeMatches(dependencySnapshot.Request, host, candidate, basesJson))
                return Rejected("SYSTEM_TASK_DEPENDENCY_SCOPE_MISMATCH",
                    "A validation dependency must retain an intact admission proof for the same scope.");
            if (ancestorIds.Contains(dependency.TaskId))
                return Rejected("SYSTEM_TASK_DEPENDENCY_ANCESTOR", "A task cannot depend on its parent ancestry.");
        }

        // A durable child is already bounded by the persisted ancestor ledger. A fresh root alone
        // reserves the caller's current allowance, once, immediately before durable SQL staging.
        if (parentTaskId is null && !host.Budget.TryTransferOperations(admitted, out _))
            return Rejected("SYSTEM_TASK_BUDGET_EXHAUSTED", "The task has no remaining operation allowance.");

        if (parentTaskId is null)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_root_budget(root_task_id,maximum_operations,consumed_operations)
                VALUES($root,$maximum,0)
                """, cancellationToken, ("$root", rootTaskId), ("$maximum", rootBudget));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_lifecycle(
                task_id,command_id,payload_fingerprint,admission_payload_json,parent_task_id,parent_command_id,
                root_task_id,parent_depth,propagate_cancellation,state,purpose,principal_reference,authentication_method,
                application_id,application_revision,application_fingerprint,base_applications_json,
                activation_revision,activation_fingerprint,activation_application_revision,activation_application_fingerprint,
                state_space_id,grant_reference,state_revision,execution_profile,admitted_operations,deadline_utc,
                definition_id,definition_version,definition_fingerprint,candidate_id,candidate_revision,candidate_fingerprint,
                causation_operation_id,input_json,checkpoint_name,completion_handler,correlation_id,checkpoint_state_json,wake_json,
                attempt_count,consecutive_failures,consumed_operations,fencing_counter,cancel_requested,cancel_acknowledged,
                created_at_utc,updated_at_utc)
            VALUES($task,$command,$payload,$proof,$parent,$parentCommand,$root,$depth,$propagate,'queued','application-validation',
                $principal,$authentication,$application,$applicationRevision,$applicationFingerprint,$bases,
                NULL,NULL,NULL,NULL,NULL,$grant,NULL,'read-only',$admitted,$deadline,NULL,NULL,NULL,
                $candidate,$candidateRevision,$candidateFingerprint,$causation,$input,NULL,NULL,NULL,NULL,NULL,0,0,0,0,0,0,$now,$now)
            """, cancellationToken, ("$task", taskId), ("$command", host.CommandId), ("$payload", payloadFingerprint),
            ("$proof", payloadJson), ("$parent", parentTaskId), ("$parentCommand", host.ParentCommandId),
            ("$root", rootTaskId), ("$depth", parentDepth), ("$propagate", propagateCancellation ? 1 : 0),
            ("$principal", host.Principal.PrincipalId), ("$authentication", host.Principal.AuthenticationMethod),
            ("$application", host.ApplicationRevision.ApplicationId.ToString()),
            ("$applicationRevision", host.ApplicationRevision.Revision),
            ("$applicationFingerprint", host.ApplicationRevision.Fingerprint), ("$bases", basesJson),
            ("$grant", host.GrantReference), ("$admitted", admitted), ("$deadline", ToDb(host.Budget.DeadlineUtc)),
            ("$candidate", candidate.CandidateId), ("$candidateRevision", candidate.Revision),
            ("$candidateFingerprint", candidate.ContentFingerprint), ("$causation", causation?.OperationId),
            ("$input", profile.Worker.InputJson), ("$now", ToDb(now)));
        foreach (var dependency in profile.Worker.DependencyHandles)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_dependency(task_id,dependency_task_id,dependency_command_id)
                VALUES($task,$dependency,$command)
                """, cancellationToken, ("$task", taskId), ("$dependency", dependency.TaskId),
                ("$command", dependency.CommandId));
        return new(SystemTaskEnqueueDisposition.Created, new(taskId, host.CommandId),
            "SYSTEM_TASK_ENQUEUED", "The durable validation task was enqueued.");
    }

    private static bool ValidationScopeMatches(ValidationParentRow parent, InteractionInvocationHost host,
        ApplicationCandidateReference candidate, string basesJson) =>
        parent.Purpose == "application-validation"
        && parent.PrincipalReference == host.Principal.PrincipalId
        && parent.AuthenticationMethod == host.Principal.AuthenticationMethod
        && parent.ApplicationId == host.ApplicationRevision.ApplicationId.ToString()
        && parent.ApplicationRevision == host.ApplicationRevision.Revision
        && parent.ApplicationFingerprint == host.ApplicationRevision.Fingerprint
        && parent.BaseApplicationsJson == basesJson && parent.StateSpaceId is null && parent.StateRevision is null
        && parent.ExecutionProfile == "read-only" && parent.GrantReference == host.GrantReference
        && parent.CandidateId == candidate.CandidateId && parent.CandidateRevision == candidate.Revision
        && parent.CandidateFingerprint == candidate.ContentFingerprint;

    private static bool ValidationRequestScopeMatches(SystemTaskStoredRequest request,
        InteractionInvocationHost host, ApplicationCandidateReference candidate, string basesJson) =>
        request.Purpose == SystemTaskPurpose.ApplicationValidation && request.Candidate == candidate
        && request.SelectedDefinition is null && request.ActivationOrigin is null
        && request.Invocation.PrincipalReference == host.Principal.PrincipalId
        && request.Invocation.AuthenticationMethod == host.Principal.AuthenticationMethod
        && request.Invocation.ApplicationId == host.ApplicationRevision.ApplicationId.ToString()
        && request.Invocation.ApplicationRevision == host.ApplicationRevision.Revision
        && request.Invocation.ApplicationFingerprint == host.ApplicationRevision.Fingerprint
        && request.Invocation.BaseApplicationsJson == basesJson && request.Invocation.StateSpaceId is null
        && request.Invocation.StateRevision is null && request.Invocation.Profile == InteractionExecutionProfile.ReadOnly
        && request.Invocation.GrantReference == host.GrantReference;

    private static async Task<ValidationParentRow?> FindValidationParentAsync(SqliteConnection connection,
        SqliteTransaction transaction, string parentCommandId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT task.task_id,task.root_task_id,task.parent_depth,task.deadline_utc,task.admitted_operations,
                budget.maximum_operations,task.purpose,task.principal_reference,task.authentication_method,
                task.application_id,task.application_revision,task.application_fingerprint,task.base_applications_json,
                task.state_space_id,task.grant_reference,task.state_revision,task.execution_profile,
                task.candidate_id,task.candidate_revision,task.candidate_fingerprint
            FROM system_task_lifecycle AS task
            JOIN system_task_root_budget AS budget ON budget.root_task_id=task.root_task_id
            WHERE task.command_id=$command AND task.state IN ('queued','running','waiting','retry')
                AND task.cancel_requested=0
            """, ("$command", parentCommandId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), ParseDb(reader.GetString(3)),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetString(9), reader.GetInt32(10), reader.GetString(11), reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15), reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetInt32(18),
                reader.IsDBNull(19) ? null : reader.GetString(19))
            : null;
    }

    private static async Task<bool> ValidationDependencyMatchesAsync(SqliteConnection connection,
        SqliteTransaction transaction, SystemTaskDurableHandle handle, InteractionInvocationHost host,
        ApplicationCandidateReference candidate, string basesJson, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_lifecycle
            WHERE task_id=$task AND command_id=$command AND purpose='application-validation'
                AND principal_reference=$principal AND authentication_method=$authentication
                AND application_id=$application AND application_revision=$revision
                AND application_fingerprint=$fingerprint AND base_applications_json=$bases
                AND state_space_id IS NULL AND state_revision IS NULL AND execution_profile='read-only'
                AND candidate_id=$candidate AND candidate_revision=$candidateRevision
                AND candidate_fingerprint=$candidateFingerprint
            """, cancellationToken, ("$task", handle.TaskId), ("$command", handle.CommandId),
            ("$principal", host.Principal.PrincipalId), ("$authentication", host.Principal.AuthenticationMethod),
            ("$application", host.ApplicationRevision.ApplicationId.ToString()),
            ("$revision", host.ApplicationRevision.Revision), ("$fingerprint", host.ApplicationRevision.Fingerprint),
            ("$bases", basesJson), ("$candidate", candidate.CandidateId), ("$candidateRevision", candidate.Revision),
            ("$candidateFingerprint", candidate.ContentFingerprint)) == 1;

    private sealed record ValidationParentRow(string TaskId, string RootTaskId, int Depth, DateTime DeadlineUtc,
        int AdmittedOperations, int RootMaximumOperations, string Purpose, string PrincipalReference,
        string AuthenticationMethod, string ApplicationId, int ApplicationRevision, string ApplicationFingerprint,
        string BaseApplicationsJson, string? StateSpaceId, string GrantReference, string? StateRevision,
        string ExecutionProfile, string? CandidateId, int? CandidateRevision, string? CandidateFingerprint);
}
