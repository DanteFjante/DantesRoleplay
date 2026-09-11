using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.DataAccess.Composition;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Durable lifecycle storage over coordinator-owned tables. This component never creates or
/// upgrades schema; callers must supply a database at the reviewed migration boundary.
/// </summary>
internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private const string FingerprintDomain = "dantes-roleplay/system-task-durable-payload/v1";
    private const string ValidationFingerprintDomain = "dantes-roleplay/system-task-durable-payload/application-validation/v1";
    private const string HostCallFingerprintDomain = "dantes-roleplay/system-task-host-call/v1";
    private const string PendingHostCallCode = "SYSTEM_TASK_HOST_CALL_PENDING";
    private const string PendingHostCallMessage = "A host call has an uncertain commit and must be reconciled before recovery.";
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    internal SqliteSystemTaskLifecycleStore(string connectionString, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    private async Task<SystemTaskEnqueueResult> EnqueueCoreAsync(
        SystemTaskDurableSubmissionRequest request,
        bool propagateCancellation, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default, StandingGrantActivationOrigin? activationOrigin = null,
        SystemInnerWorkerResolvedProfile? innerWorkerProfile = null, bool allowEphemeralParent = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InvocationHost.StateSpaceId is not { } stateSpaceId
            || request.InvocationHost.StateRevision is not { } stateRevision)
            return Rejected("INVOCATION_STATE_SCOPE_REQUIRED", "Durable tasks require a state scope.");
        if (activationOrigin is not null && !ValidActivationOrigin(request, activationOrigin))
            return Rejected("SYSTEM_TASK_ACTIVATION_ORIGIN_INVALID",
                "The retained activation origin is invalid or does not match the admitted application revision.");
        var now = UtcNow();
        var dependencyPairs = request.DependencyHandles
            .OrderBy(value => value.TaskId, StringComparer.Ordinal)
            .Select(value => new { value.TaskId, value.CommandId }).ToArray();
        var payloadJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            principal = request.InvocationHost.Principal.PrincipalId,
            authenticationMethod = request.InvocationHost.Principal.AuthenticationMethod,
            application = request.InvocationHost.ApplicationRevision.ApplicationId.ToString(),
            applicationRevision = request.InvocationHost.ApplicationRevision.Revision,
            applicationFingerprint = request.InvocationHost.ApplicationRevision.Fingerprint,
            baseApplications = InteractionCanonicalJson.Fingerprint(FingerprintDomain + "/bases",
                InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                    request.InvocationHost.ApplicationRevision.BaseApplications.Select(value => value.ToString())))),
            StateSpaceId = stateSpaceId,
            request.InvocationHost.GrantReference,
            StateRevision = stateRevision,
            profile = InteractionExecutionProfileNames.Get(request.InvocationHost.Profile),
            request.InvocationHost.CommandId,
            request.InvocationHost.ParentCommandId,
            originatingParentMode = allowEphemeralParent ? "ephemeral-root" : null,
            // The requested ceiling is immutable; remaining allowance changes as siblings run.
            // Admission persists the remaining allowance separately from command equivalence.
            maximumOperations = request.InvocationHost.Budget.MaximumOperations,
            deadlineUtc = request.InvocationHost.Budget.DeadlineUtc.ToString("O"),
            definitionId = request.SelectedDefinition.ExactDefinitionId,
            definitionVersion = request.SelectedDefinition.Version,
            definitionFingerprint = request.SelectedDefinition.Fingerprint,
            inputFingerprint = InteractionCanonicalJson.Fingerprint(FingerprintDomain + "/input", request.InputJson),
            initialCheckpoint = request.Checkpoint is null ? null : new
            {
                request.Checkpoint.Checkpoint,
                request.Checkpoint.CompletionHandler,
                request.Checkpoint.CorrelationId,
                stateFingerprint = InteractionCanonicalJson.Fingerprint(FingerprintDomain + "/checkpoint", request.Checkpoint.StateJson)
            },
            dependencies = dependencyPairs,
            propagateCancellation
        }));
        if (activationOrigin is not null)
        {
            using var legacyPayload = JsonDocument.Parse(payloadJson);
            var extendedPayload = legacyPayload.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            extendedPayload.Add("activationOrigin", JsonSerializer.SerializeToElement(new
            {
                activationRevision = activationOrigin.ActivationRevision,
                activationFingerprint = activationOrigin.ActivationFingerprint,
                applicationRevision = activationOrigin.ApplicationRevision,
                applicationFingerprint = activationOrigin.ApplicationFingerprint
            }));
            payloadJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(extendedPayload));
        }
        if (innerWorkerProfile is not null)
        {
            if (activationOrigin is null
                || innerWorkerProfile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow
                || workflow.ProcedureVersion != request.SelectedDefinition
                || innerWorkerProfile.Worker.InvocationHost != request.InvocationHost
                || innerWorkerProfile.Worker.InputJson != request.InputJson
                || !innerWorkerProfile.Worker.DependencyHandles.SequenceEqual(request.DependencyHandles))
                return Rejected("INNER_WORKER_ADMISSION_SCOPE_MISMATCH",
                    "The focused worker profile does not match the durable submission.");
            using var retainedPayload = JsonDocument.Parse(payloadJson);
            var extendedPayload = retainedPayload.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            extendedPayload.Add("innerWorker", JsonSerializer.SerializeToElement(new
            {
                format = SystemInnerWorkerAssignmentV1.Format,
                resultSchemaJson = innerWorkerProfile.Worker.ResultSchemaJson,
                innerWorkerProfile.OutputSchemaFingerprint,
                innerWorkerProfile.ProfileVersion,
                profile = new
                {
                    innerWorkerProfile.Profile.Id,
                    innerWorkerProfile.Profile.Name,
                    innerWorkerProfile.Profile.Identity,
                    innerWorkerProfile.Profile.Instructions
                },
                toolBindings = innerWorkerProfile.ToolBindings.Select(value => new
                {
                    value.Definition,
                    value.CapabilityVersion
                }),
                innerWorkerProfile.RequiredContextReferences,
                contextEvidence = innerWorkerProfile.ManualContext,
                innerWorkerProfile.AuthorityProvenance,
                innerWorkerProfile.AiBudget,
                enrollmentFingerprint = SqliteSystemTaskLifecycleStore.AiEnrollmentFingerprint(innerWorkerProfile)
            }));
            payloadJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(extendedPayload));
        }
        var payloadFingerprint = InteractionCanonicalJson.Fingerprint(FingerprintDomain, payloadJson);
        var taskId = NewTaskId(request.InvocationHost.CommandId);

        var existing = await FindByCommandAsync(connection, transaction, request.InvocationHost.CommandId, cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.Value.PayloadFingerprint, payloadFingerprint)
                ? new(SystemTaskEnqueueDisposition.Existing,
                    new(existing.Value.TaskId, request.InvocationHost.CommandId),
                    "SYSTEM_TASK_ALREADY_ENQUEUED", "The equivalent durable task already exists.")
                : new(SystemTaskEnqueueDisposition.Conflict, null,
                    "SYSTEM_TASK_COMMAND_CONFLICT", "The command identity is already bound to a different durable payload.");
        }

        // Replays only read the existing handle. New work alone is subject to current
        // admission limits, after the same transaction has resolved command identity.
        if (request.InvocationHost.Profile != InteractionExecutionProfile.Workflow)
            return Rejected("SYSTEM_TASK_PROFILE_UNSUPPORTED", "Durable tasks require the workflow execution profile.");
        if (request.InvocationHost.Budget.DeadlineUtc <= now)
            return Rejected("SYSTEM_TASK_DEADLINE_EXPIRED", "The task deadline has already expired.");
        var admitted = Math.Min(request.InvocationHost.Budget.RemainingOperations, SystemTaskLifecycleLimits.MaximumRootOperations);
        if (admitted < 1)
            return Rejected("SYSTEM_TASK_BUDGET_EXHAUSTED", "The task has no remaining operation allowance.");

        var activeCount = await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE state IN ('queued','running','waiting','retry')", cancellationToken);
        if (activeCount >= SystemTaskLifecycleLimits.MaximumQueuedTasks)
            return Rejected("SYSTEM_TASK_QUEUE_FULL", "The durable task queue is at its configured bound.");

        string rootTaskId = taskId;
        string? parentTaskId = null;
        var parentDepth = 0;
        var rootBudget = admitted;
        if (request.InvocationHost.ParentCommandId is not null)
        {
            if (StringComparer.Ordinal.Equals(request.InvocationHost.ParentCommandId, request.InvocationHost.CommandId))
                return Rejected("SYSTEM_TASK_PARENT_SELF", "A task cannot be its own parent.");
            var parent = await FindParentAsync(connection, transaction, request.InvocationHost.ParentCommandId, cancellationToken);
            if (parent is null && !allowEphemeralParent)
                return Rejected("SYSTEM_TASK_PARENT_UNKNOWN", "The declared parent task does not exist.");
            if (parent is null)
            {
                // A trusted workflow-service host may preserve its ephemeral causal command while
                // admitting a new durable root. The immutable payload fingerprints this mode, so
                // a later durable row with the same command cannot reinterpret a replay's ancestry.
            }
            else if (parent.Depth >= SystemTaskLifecycleLimits.MaximumParentDepth)
                return Rejected("SYSTEM_TASK_PARENT_DEPTH", "The durable task parent depth is at its configured bound.");
            else if (request.InvocationHost.Budget.DeadlineUtc > parent.DeadlineUtc || admitted > parent.AdmittedOperations)
                return Rejected("SYSTEM_TASK_CHILD_BUDGET_EXPANDED", "A child task cannot expand its parent deadline or allowance.");
            if (parent is not null)
            {
                var requestedBases = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                    request.InvocationHost.ApplicationRevision.BaseApplications.Select(value => value.ToString())));
                if (!StringComparer.Ordinal.Equals(parent.PrincipalReference, request.InvocationHost.Principal.PrincipalId) ||
                    !StringComparer.Ordinal.Equals(parent.ApplicationId, request.InvocationHost.ApplicationRevision.ApplicationId.ToString()) ||
                    parent.ApplicationRevision != request.InvocationHost.ApplicationRevision.Revision ||
                    !StringComparer.Ordinal.Equals(parent.ApplicationFingerprint, request.InvocationHost.ApplicationRevision.Fingerprint) ||
                    !StringComparer.Ordinal.Equals(parent.BaseApplicationsJson, requestedBases) ||
                    !StringComparer.Ordinal.Equals(parent.StateSpaceId, stateSpaceId) ||
                    !StringComparer.Ordinal.Equals(parent.GrantReference, request.InvocationHost.GrantReference) ||
                    !StringComparer.Ordinal.Equals(parent.StateRevision, stateRevision) ||
                    !StringComparer.Ordinal.Equals(parent.ExecutionProfile, InteractionExecutionProfileNames.Get(request.InvocationHost.Profile)))
                    return Rejected("SYSTEM_TASK_PARENT_SCOPE_MISMATCH", "A child task must retain its parent's exact principal and invocation scope.");
                var childCount = await ScalarLongAsync(connection, transaction,
                    "SELECT COUNT(*) FROM system_task_lifecycle WHERE parent_task_id = $parent", cancellationToken,
                    ("$parent", parent.TaskId));
                if (childCount >= SystemTaskLifecycleLimits.MaximumChildrenPerTask)
                    return Rejected("SYSTEM_TASK_FANOUT_LIMIT", "The parent task is at its child fan-out bound.");
                var descendantCount = await ScalarLongAsync(connection, transaction,
                    "SELECT COUNT(*) FROM system_task_lifecycle WHERE root_task_id = $root AND task_id <> $root", cancellationToken,
                    ("$root", parent.RootTaskId));
                if (descendantCount >= SystemTaskLifecycleLimits.MaximumDescendantsPerRoot)
                    return Rejected("SYSTEM_TASK_DESCENDANT_LIMIT", "The root task is at its descendant bound.");
                rootTaskId = parent.RootTaskId;
                parentTaskId = parent.TaskId;
                parentDepth = parent.Depth + 1;
                rootBudget = parent.RootMaximumOperations;
            }
        }

        var ancestorIds = parentTaskId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await ReadAncestorIdsAsync(connection, transaction, parentTaskId, cancellationToken);
        var dependencyBases = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
            request.InvocationHost.ApplicationRevision.BaseApplications.Select(value => value.ToString())));
        foreach (var dependency in request.DependencyHandles)
        {
            if (StringComparer.Ordinal.Equals(dependency.CommandId, request.InvocationHost.CommandId) ||
                StringComparer.Ordinal.Equals(dependency.TaskId, taskId))
                return Rejected("SYSTEM_TASK_DEPENDENCY_SELF", "A task cannot depend on itself.");
            var stored = await FindHandleAsync(connection, transaction, dependency, cancellationToken);
            if (!stored)
                return Rejected("SYSTEM_TASK_DEPENDENCY_UNKNOWN", "Every dependency must already exist with the exact durable handle.");
            if (!await DependencyMatchesScopeAsync(connection, transaction, dependency,
                request.InvocationHost.Principal.PrincipalId,
                request.InvocationHost.ApplicationRevision.ApplicationId.ToString(),
                request.InvocationHost.ApplicationRevision.Revision,
                request.InvocationHost.ApplicationRevision.Fingerprint,
                dependencyBases, stateSpaceId, cancellationToken))
                return Rejected("SYSTEM_TASK_DEPENDENCY_SCOPE_MISMATCH", "A dependency must have the same principal, application revision, and state scope.");
            if (ancestorIds.Contains(dependency.TaskId))
                return Rejected("SYSTEM_TASK_DEPENDENCY_ANCESTOR", "A task cannot depend on its parent ancestry.");
        }

        if (parentTaskId is null)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_root_budget(root_task_id, maximum_operations, consumed_operations)
                VALUES ($root, $maximum, 0)
                """, cancellationToken, ("$root", rootTaskId), ("$maximum", rootBudget));
        }

        var basesJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
            request.InvocationHost.ApplicationRevision.BaseApplications.Select(value => value.ToString())));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_lifecycle(
                task_id, command_id, payload_fingerprint, admission_payload_json, parent_task_id, parent_command_id, root_task_id, parent_depth,
                propagate_cancellation, state, principal_reference, authentication_method,
                application_id, application_revision, application_fingerprint, base_applications_json,
                activation_revision, activation_fingerprint, activation_application_revision,
                activation_application_fingerprint,
                state_space_id, grant_reference, state_revision, execution_profile,
                admitted_operations, deadline_utc, definition_id, definition_version,
                definition_fingerprint, input_json, checkpoint_name, completion_handler,
                correlation_id, checkpoint_state_json, wake_json, attempt_count, consecutive_failures,
                consumed_operations, fencing_counter,
                cancel_requested, cancel_acknowledged, created_at_utc, updated_at_utc)
            VALUES (
                $task, $command, $payload, $admissionPayload, $parent, $parentCommand, $root, $depth, $propagate, 'queued',
                $principal, $authentication, $application, $applicationRevision, $applicationFingerprint,
                $bases, $activationRevision, $activationFingerprint, $activationApplicationRevision,
                $activationApplicationFingerprint, $stateSpace, $grant, $stateRevision, $profile, $admitted, $deadline,
                $definition, $definitionVersion, $definitionFingerprint, $input,
                $checkpoint, $handler, $correlation, $checkpointState, NULL, 0, 0, 0, 0, 0, 0, $now, $now)
            """, cancellationToken,
            ("$task", taskId), ("$command", request.InvocationHost.CommandId), ("$payload", payloadFingerprint),
            ("$admissionPayload", activationOrigin is null ? null : payloadJson),
            ("$parent", parentTaskId), ("$parentCommand", request.InvocationHost.ParentCommandId),
            ("$root", rootTaskId), ("$depth", parentDepth), ("$propagate", propagateCancellation ? 1 : 0),
            ("$principal", request.InvocationHost.Principal.PrincipalId), ("$authentication", request.InvocationHost.Principal.AuthenticationMethod),
            ("$application", request.InvocationHost.ApplicationRevision.ApplicationId.ToString()),
            ("$applicationRevision", request.InvocationHost.ApplicationRevision.Revision),
            ("$applicationFingerprint", request.InvocationHost.ApplicationRevision.Fingerprint), ("$bases", basesJson),
            ("$activationRevision", activationOrigin?.ActivationRevision),
            ("$activationFingerprint", activationOrigin?.ActivationFingerprint),
            ("$activationApplicationRevision", activationOrigin?.ApplicationRevision),
            ("$activationApplicationFingerprint", activationOrigin?.ApplicationFingerprint),
            ("$stateSpace", stateSpaceId), ("$grant", request.InvocationHost.GrantReference),
            ("$stateRevision", stateRevision), ("$profile", InteractionExecutionProfileNames.Get(request.InvocationHost.Profile)),
            ("$admitted", admitted), ("$deadline", ToDb(request.InvocationHost.Budget.DeadlineUtc)),
            ("$definition", request.SelectedDefinition.ExactDefinitionId), ("$definitionVersion", request.SelectedDefinition.Version),
            ("$definitionFingerprint", request.SelectedDefinition.Fingerprint), ("$input", request.InputJson),
            ("$checkpoint", request.Checkpoint?.Checkpoint), ("$handler", request.Checkpoint?.CompletionHandler),
            ("$correlation", request.Checkpoint?.CorrelationId), ("$checkpointState", request.Checkpoint?.StateJson),
            ("$now", ToDb(now)));
        foreach (var dependency in request.DependencyHandles)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO system_task_dependency(task_id, dependency_task_id, dependency_command_id) VALUES ($task, $dependency, $command)",
                cancellationToken, ("$task", taskId), ("$dependency", dependency.TaskId), ("$command", dependency.CommandId));
        return new(SystemTaskEnqueueDisposition.Created, new(taskId, request.InvocationHost.CommandId),
            "SYSTEM_TASK_ENQUEUED", "The durable task was enqueued.");
    }

    internal Task<SystemTaskLease?> ClaimNextAsync(string workerId, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) => ClaimNextCoreAsync(workerId, leaseDuration, null, cancellationToken);

    internal Task<SystemTaskLease?> ClaimNextValidationAsync(string workerId, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) => ClaimNextCoreAsync(workerId, leaseDuration,
            SystemTaskPurpose.ApplicationValidation, cancellationToken);

    internal Task<SystemTaskLease?> ClaimNextWorkflowAsync(string workerId, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) => ClaimNextCoreAsync(workerId, leaseDuration,
            SystemTaskPurpose.ProcedureWorkflow, cancellationToken);

    private async Task<SystemTaskLease?> ClaimNextCoreAsync(string workerId, TimeSpan leaseDuration,
        SystemTaskPurpose? purpose, CancellationToken cancellationToken)
    {
        ValidateWorker(workerId);
        ValidateLeaseDuration(leaseDuration);
        var now = UtcNow();
        var expires = now.Add(leaseDuration);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await NormalizeBlockedAsync(connection, transaction, now, cancellationToken);
            var accountingFilter = await HasAiAccountingTablesAsync(connection, transaction, cancellationToken)
                ? "AND NOT EXISTS (SELECT 1 FROM system_task_ai_reservation_ancestor AS membership JOIN system_task_ai_reservation AS reservation ON reservation.record_reference=membership.record_reference WHERE membership.ancestor_task_id=task.root_task_id AND reservation.status='unknown')"
                : "";
            var purposeFilter = purpose is null ? "" : "AND task.purpose=$purpose";
            var parameters = new List<(string Name, object? Value)>
            {
                ("$now", ToDb(now)), ("$maximumAttempts", SystemTaskLifecycleLimits.MaximumRootOperations),
                ("$maximumFailures", SystemTaskLifecycleLimits.MaximumAttemptsPerTask)
            };
            if (purpose is not null) parameters.Add(("$purpose", SystemTaskPurposeNames.Get(purpose.Value)));
            var taskId = await ScalarStringAsync(connection, transaction, $"""
                SELECT task_id
                FROM system_task_lifecycle AS task
                WHERE task.state IN ('queued','retry')
                  AND task.cancel_requested = 0
                  AND task.deadline_utc > $now
                  AND (task.state <> 'retry' OR task.next_attempt_at_utc <= $now)
                  AND task.attempt_count < $maximumAttempts
                  AND task.consecutive_failures < $maximumFailures
                  {purposeFilter}
                  {accountingFilter}
                  AND NOT EXISTS (
                      SELECT 1 FROM system_task_dependency AS edge
                      JOIN system_task_lifecycle AS dependency ON dependency.task_id = edge.dependency_task_id
                      WHERE edge.task_id = task.task_id AND dependency.state <> 'completed')
                ORDER BY task.created_at_utc, task.task_id
                LIMIT 1
                """, cancellationToken, parameters.ToArray());
            if (taskId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (!await ConsumeOperationAsync(connection, transaction, taskId, cancellationToken))
            {
                await FinishWithoutLeaseAsync(connection, transaction, taskId, "failed",
                    "SYSTEM_TASK_BUDGET_EXHAUSTED", "The root operation budget is exhausted.", now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var attemptId = "attempt." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            await ExecuteAsync(connection, transaction, """
                UPDATE system_task_attempt
                SET state = 'lease-expired', completed_at_utc = $now,
                    failure_code = 'SYSTEM_TASK_LEASE_EXPIRED',
                    safe_message = 'The worker lease expired before a durable outcome was recorded.'
                WHERE task_id = $task AND state = 'running'
                """, cancellationToken, ("$task", taskId), ("$now", ToDb(now)));
            var changed = await ExecuteAsync(connection, transaction, """
                UPDATE system_task_lifecycle
                SET state = 'running', attempt_count = attempt_count + 1,
                    fencing_counter = fencing_counter + 1, lease_owner = $worker,
                    lease_token = $token, lease_expires_at_utc = $expires,
                    next_attempt_at_utc = NULL, wake_json = wake_json, updated_at_utc = $now
                WHERE task_id = $task
                """, cancellationToken, ("$worker", workerId), ("$token", token),
                ("$expires", ToDb(expires)), ("$now", ToDb(now)), ("$task", taskId));
            if (changed != 1) throw new InvalidOperationException("The selected durable task disappeared during claim.");
            var attempt = await ReadAttemptOrdinalAndFenceAsync(connection, transaction, taskId, cancellationToken);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_attempt(task_id, attempt_id, ordinal, fencing_counter, lease_token, state, started_at_utc)
                VALUES ($task, $attempt, $ordinal, $fence, $token, 'running', $now)
                """, cancellationToken, ("$task", taskId), ("$attempt", attemptId), ("$ordinal", attempt.Ordinal),
                ("$fence", attempt.Fence), ("$token", token), ("$now", ToDb(now)));
            var lease = await ReadLeaseAsync(connection, transaction, taskId, attemptId, token, expires, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return lease;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal async Task<bool> RenewAsync(SystemTaskLease lease, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateLeaseDuration(leaseDuration);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        return await ExecuteAsync(connection, null, """
            UPDATE system_task_lifecycle
            SET lease_expires_at_utc = $expires, updated_at_utc = $now
            WHERE task_id = $task AND state = 'running' AND attempt_count = $ordinal
              AND fencing_counter = $fence AND lease_token = $token AND lease_expires_at_utc > $now
              AND cancel_requested = 0 AND deadline_utc > $now
            """, cancellationToken, ("$expires", ToDb(now.Add(leaseDuration))), ("$now", ToDb(now)),
            ("$task", lease.Request.Handle.TaskId), ("$ordinal", AttemptOrdinal(lease)),
            ("$fence", lease.Attempt.FencingCounter), ("$token", lease.Attempt.LeaseToken)) == 1;
    }

    internal async Task<bool> SaveWaitingAsync(SystemTaskLease lease, SystemTaskCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await HasPendingHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await MarkPendingIndeterminateAsync(connection, transaction, lease, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        if (await HasUnresolvedAiAccountingAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await MarkAiIndeterminateAsync(connection, transaction, lease, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var priorCorrelation = await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_checkpoint WHERE task_id = $task AND correlation_id = $correlation",
            cancellationToken, ("$task", lease.Request.Handle.TaskId), ("$correlation", checkpoint.CorrelationId));
        var checkpointCount = await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_checkpoint WHERE task_id = $task", cancellationToken,
            ("$task", lease.Request.Handle.TaskId));
        if (priorCorrelation != 0 || checkpointCount >= SystemTaskLifecycleLimits.MaximumEvidenceItems)
        {
            var changedToFailed = await ExecuteFencedAsync(connection, transaction, lease, """
                state = 'failed', error_code = 'SYSTEM_TASK_CHECKPOINT_CONFLICT',
                safe_message = 'The checkpoint correlation was reused or the checkpoint history bound was reached.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
                updated_at_utc = $now, completed_at_utc = $now
                """, now, cancellationToken);
            if (changedToFailed == 1)
                await CompleteAttemptAsync(connection, transaction, lease, "failed", "SYSTEM_TASK_CHECKPOINT_CONFLICT",
                    "The checkpoint correlation was reused or the checkpoint history bound was reached.", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state = 'waiting', checkpoint_name = $checkpoint, completion_handler = $handler,
            correlation_id = $correlation, checkpoint_state_json = $stateJson, wake_json = NULL,
            consecutive_failures = 0,
            lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL, updated_at_utc = $now
            """, now, cancellationToken, ("$checkpoint", checkpoint.Checkpoint),
            ("$handler", checkpoint.CompletionHandler), ("$correlation", checkpoint.CorrelationId),
            ("$stateJson", checkpoint.StateJson));
        if (changed == 1)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO system_task_checkpoint(task_id, sequence, checkpoint_name, completion_handler,
                    correlation_id, state_json, status, created_at_utc)
                VALUES ($task, $sequence, $checkpoint, $handler, $correlation, $stateJson, 'waiting', $now)
                """, cancellationToken, ("$task", lease.Request.Handle.TaskId), ("$sequence", checkpointCount + 1),
                ("$checkpoint", checkpoint.Checkpoint), ("$handler", checkpoint.CompletionHandler),
                ("$correlation", checkpoint.CorrelationId), ("$stateJson", checkpoint.StateJson), ("$now", ToDb(now)));
            await CompleteAttemptAsync(connection, transaction, lease, "waiting", null, null, now, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    internal async Task<int> WakeAsync(SystemTaskDurableHandle handle, string correlationId, string completionJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var correlation = Required(correlationId, 200, nameof(correlationId));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(completionJson);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var changed = await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'queued', wake_json = $completion, updated_at_utc = $now
            WHERE task_id = $task AND command_id = $command AND state = 'waiting' AND correlation_id = $correlation
              AND cancel_requested = 0 AND deadline_utc > $now
            """, cancellationToken, ("$completion", canonical), ("$now", ToDb(now)),
            ("$task", handle.TaskId), ("$command", handle.CommandId), ("$correlation", correlation));
        if (changed == 1)
            await ExecuteAsync(connection, transaction, """
                UPDATE system_task_checkpoint SET status = 'woken', wake_json = $completion, woken_at_utc = $now
                WHERE task_id = $task AND correlation_id = $correlation AND status = 'waiting'
                """, cancellationToken, ("$completion", canonical), ("$now", ToDb(now)),
                ("$task", handle.TaskId), ("$correlation", correlation));
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    internal async Task<bool> CompleteAsync(SystemTaskLease lease, SystemTaskTerminalOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var result = InteractionCanonicalJson.Canonicalize(outcome.DataJson);
        var evidenceReference = Required(outcome.CompletionEvidenceReference, 200, nameof(outcome.CompletionEvidenceReference));
        var evidence = ValidateEvidence(outcome.Evidence);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await HasPendingHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await MarkPendingIndeterminateAsync(connection, transaction, lease, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        if (await HasUnresolvedAiAccountingAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await MarkAiIndeterminateAsync(connection, transaction, lease, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state = 'completed', result_json = $result, completion_evidence_reference = $evidenceReference,
            evidence_json = $evidence, error_code = NULL, safe_message = NULL,
            lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
            updated_at_utc = $now, completed_at_utc = $now
            """, now, cancellationToken, ("$result", result), ("$evidenceReference", evidenceReference),
            ("$evidence", evidence));
        if (changed == 1)
            await CompleteAttemptAsync(connection, transaction, lease, "completed", null, null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    internal async Task<bool> FailAsync(SystemTaskLease lease, SystemTaskFailureKind failureKind,
        string code, string safeMessage, CancellationToken cancellationToken = default)
    {
        var boundedCode = Required(code, 200, nameof(code));
        var boundedMessage = Required(safeMessage, InteractionContractLimits.SafeEvidenceText, nameof(safeMessage));
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var pending = await HasPendingHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken);
        var pendingAi = await HasUnresolvedAiAccountingAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken);
        if (pending)
        {
            boundedCode = PendingHostCallCode;
            boundedMessage = PendingHostCallMessage;
        }
        else if (pendingAi)
        {
            boundedCode = AiRecoveryCode;
            boundedMessage = AiRecoveryMessage;
        }
        var failures = await ScalarLongAsync(connection, transaction,
            "SELECT consecutive_failures FROM system_task_lifecycle WHERE task_id = $task", cancellationToken,
            ("$task", lease.Request.Handle.TaskId));
        var transientRetry = failureKind == SystemTaskFailureKind.Transient && !pending && !pendingAi &&
            failures + 1 < SystemTaskLifecycleLimits.MaximumAttemptsPerTask &&
            AttemptOrdinal(lease) < SystemTaskLifecycleLimits.MaximumRootOperations && lease.Request.Invocation.DeadlineUtc > now;
        var state = pending || pendingAi || failureKind == SystemTaskFailureKind.Indeterminate
            ? "indeterminate" : transientRetry ? "retry" : "failed";
        var next = transientRetry ? ToDb(now.AddSeconds(failures == 0 ? 5 : 30)) : null;
        var terminal = state is "failed" or "indeterminate" ? ToDb(now) : null;
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state = $state, next_attempt_at_utc = $next, error_code = $code, safe_message = $message,
            consecutive_failures = consecutive_failures + 1,
            lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
            updated_at_utc = $now, completed_at_utc = $terminal
            """, now, cancellationToken, false, lease.Request.Invocation.DeadlineUtc <= now,
            ("$state", state), ("$next", next), ("$code", boundedCode),
            ("$message", boundedMessage), ("$terminal", terminal));
        if (changed == 1)
        {
            if (pendingAi) await NormalizeAiReservationsAsync(connection, transaction, cancellationToken);
            await CompleteAttemptAsync(connection, transaction, lease, state, boundedCode, boundedMessage, now, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    internal async Task<bool> AcknowledgeCancellationAsync(SystemTaskLease lease,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await HasPendingHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            var unresolved = await MarkPendingIndeterminateAsync(connection, transaction, lease, now,
                cancellationToken, requireCancellation: true);
            await transaction.CommitAsync(cancellationToken);
            return unresolved;
        }
        if (await HasUnresolvedAiAccountingAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            var unresolved = await MarkAiIndeterminateAsync(connection, transaction, lease, now,
                cancellationToken, requireCancellation: true);
            await transaction.CommitAsync(cancellationToken);
            return unresolved;
        }
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state = 'cancelled', cancel_acknowledged = 1, error_code = 'SYSTEM_TASK_CANCELLED',
            safe_message = 'Cancellation was acknowledged by the worker.',
            lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
            updated_at_utc = $now, completed_at_utc = $now
            """, now, cancellationToken, true, false);
        if (changed == 1)
            await CompleteAttemptAsync(connection, transaction, lease, "cancelled", "SYSTEM_TASK_CANCELLED",
                "Cancellation was acknowledged by the worker.", now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    internal async Task<SystemTaskLifecycleSnapshot?> ReadAsync(SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadSnapshotAsync(connection, null, handle, cancellationToken);
    }

    internal async Task<SystemTaskHostCallJournalResult> BeginHostCallAsync(SystemTaskLease lease,
        string operationId, string requestJson, CancellationToken cancellationToken = default)
    {
        var operation = Required(operationId, InteractionContractLimits.IdempotencyKey, nameof(operationId));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(requestJson);
        var fingerprint = InteractionCanonicalJson.Fingerprint(HostCallFingerprintDomain, canonical);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await OwnsLeaseAsync(connection, transaction, lease, now, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new(SystemTaskHostCallDisposition.Conflict, operation, fingerprint, null);
        }
        var existing = await ReadHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, operation, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            if (!StringComparer.Ordinal.Equals(existing.Value.RequestFingerprint, fingerprint))
                return new(SystemTaskHostCallDisposition.Conflict, operation, fingerprint, null);
            return new(existing.Value.Status == "completed" ? SystemTaskHostCallDisposition.Completed : SystemTaskHostCallDisposition.ExistingPending,
                operation, fingerprint, existing.Value.CompletionJson);
        }
        if (await HasPendingHostCallAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new(SystemTaskHostCallDisposition.BlockedByPending, operation, fingerprint, null);
        }
        if (!await ConsumeOperationAsync(connection, transaction, lease.Request.Handle.TaskId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new(SystemTaskHostCallDisposition.BudgetExhausted, operation, fingerprint, null);
        }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO system_task_host_call(task_id, operation_id, request_fingerprint, request_json,
                status, attempt_id, fencing_counter, started_at_utc)
            VALUES ($task, $operation, $fingerprint, $request, 'pending', $attempt, $fence, $now)
            """, cancellationToken, ("$task", lease.Request.Handle.TaskId), ("$operation", operation),
            ("$fingerprint", fingerprint), ("$request", canonical), ("$attempt", lease.Attempt.AttemptId),
            ("$fence", lease.Attempt.FencingCounter), ("$now", ToDb(now)));
        await transaction.CommitAsync(cancellationToken);
        return new(SystemTaskHostCallDisposition.NewPending, operation, fingerprint, null);
    }

    internal async Task<bool> CompleteHostCallAsync(SystemTaskLease lease, string operationId,
        string requestFingerprint, string completionJson, CancellationToken cancellationToken = default)
    {
        var operation = Required(operationId, InteractionContractLimits.IdempotencyKey, nameof(operationId));
        var fingerprint = Required(requestFingerprint, 64, nameof(requestFingerprint));
        var completion = InteractionCanonicalJson.Canonicalize(completionJson);
        var now = UtcNow();
        await using var connection = await OpenAsync(cancellationToken);
        return await ExecuteAsync(connection, null, """
            UPDATE system_task_host_call
            SET status = 'completed', completion_json = $completion, completed_at_utc = $now
            WHERE task_id = $task AND operation_id = $operation AND request_fingerprint = $fingerprint
              AND status = 'pending' AND attempt_id = $attempt AND fencing_counter = $fence
              AND EXISTS (
                  SELECT 1 FROM system_task_lifecycle AS task
                  WHERE task.task_id = system_task_host_call.task_id AND task.state = 'running'
                    AND task.attempt_count = $ordinal AND task.fencing_counter = $fence
                    AND task.lease_token = $token AND task.lease_expires_at_utc > $now)
            """, cancellationToken, ("$completion", completion), ("$now", ToDb(now)),
            ("$task", lease.Request.Handle.TaskId), ("$operation", operation), ("$fingerprint", fingerprint),
            ("$attempt", lease.Attempt.AttemptId), ("$fence", lease.Attempt.FencingCounter),
            ("$ordinal", AttemptOrdinal(lease)), ("$token", lease.Attempt.LeaseToken)) == 1;
    }

    private async Task NormalizeBlockedAsync(SqliteConnection connection, SqliteTransaction transaction,
        DateTime now, CancellationToken cancellationToken)
    {
        var hasAiAccounting = await HasAiAccountingTablesAsync(connection, transaction, cancellationToken);
        if (hasAiAccounting) await NormalizeAiReservationsAsync(connection, transaction, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_attempt
            SET state = 'lease-expired', completed_at_utc = $now,
                failure_code = 'SYSTEM_TASK_LEASE_EXPIRED',
                safe_message = 'The worker lease expired before a durable outcome was recorded.'
            WHERE state = 'running' AND task_id IN (
                SELECT task_id FROM system_task_lifecycle
                WHERE state = 'running' AND lease_expires_at_utc <= $now)
            """, cancellationToken, ("$now", ToDb(now)));
        if (hasAiAccounting) await MarkExpiredAiTasksAsync(connection, transaction, now, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle AS task
            SET state = 'indeterminate', completed_at_utc = $now, updated_at_utc = $now,
                error_code = 'SYSTEM_TASK_HOST_CALL_PENDING',
                safe_message = 'A host call has an uncertain commit and must be reconciled before recovery.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE task.state = 'running' AND task.lease_expires_at_utc <= $now
              AND EXISTS (SELECT 1 FROM system_task_host_call AS call
                  WHERE call.task_id = task.task_id AND call.status = 'pending')
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'cancelled', cancel_acknowledged = 1, completed_at_utc = $now,
                updated_at_utc = $now, error_code = 'SYSTEM_TASK_CANCELLED',
                safe_message = 'Cancellation was acknowledged after the worker lease expired.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE state = 'running' AND lease_expires_at_utc <= $now AND cancel_requested = 1
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'failed', completed_at_utc = $now, updated_at_utc = $now,
                error_code = 'SYSTEM_TASK_DEADLINE_EXPIRED', safe_message = 'The durable task deadline expired.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE state = 'running' AND lease_expires_at_utc <= $now AND deadline_utc <= $now
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'failed', completed_at_utc = $now, updated_at_utc = $now,
                error_code = 'SYSTEM_TASK_ATTEMPTS_EXHAUSTED',
                safe_message = 'The worker lease recovery limit was exhausted.',
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
                consecutive_failures = consecutive_failures + 1
            WHERE state = 'running' AND lease_expires_at_utc <= $now
              AND consecutive_failures + 1 >= $maximumFailures
            """, cancellationToken, ("$maximumFailures", SystemTaskLifecycleLimits.MaximumAttemptsPerTask), ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'queued', updated_at_utc = $now, lease_owner = NULL, lease_token = NULL,
                lease_expires_at_utc = NULL, consecutive_failures = consecutive_failures + 1,
                error_code = 'SYSTEM_TASK_LEASE_EXPIRED',
                safe_message = 'The expired worker lease was recovered for another bounded attempt.'
            WHERE state = 'running' AND lease_expires_at_utc <= $now
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'cancelled', cancel_acknowledged = 1, completed_at_utc = $now,
                updated_at_utc = $now, error_code = 'SYSTEM_TASK_CANCELLED',
                safe_message = 'Cancellation was acknowledged before execution.'
            WHERE cancel_requested = 1 AND state IN ('queued','waiting','retry')
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle
            SET state = 'failed', completed_at_utc = $now, updated_at_utc = $now,
                error_code = 'SYSTEM_TASK_DEADLINE_EXPIRED', safe_message = 'The durable task deadline expired.'
            WHERE state IN ('queued','waiting','retry') AND deadline_utc <= $now
            """, cancellationToken, ("$now", ToDb(now)));
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle AS task
            SET state = 'failed', completed_at_utc = $now, updated_at_utc = $now,
                error_code = 'SYSTEM_TASK_DEPENDENCY_FAILED', safe_message = 'A durable dependency did not complete successfully.'
            WHERE task.state IN ('queued','waiting','retry') AND EXISTS (
                SELECT 1 FROM system_task_dependency AS edge
                JOIN system_task_lifecycle AS dependency ON dependency.task_id = edge.dependency_task_id
                WHERE edge.task_id = task.task_id AND dependency.state IN ('failed','cancelled','indeterminate'))
            """, cancellationToken, ("$now", ToDb(now)));
    }

    private static Task<int> ExecuteFencedAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, string assignments, DateTime now, CancellationToken cancellationToken,
        params (string Name, object? Value)[] values) =>
        ExecuteFencedAsync(connection, transaction, lease, assignments, now, cancellationToken, false, false, values);

    private static async Task<int> ExecuteFencedAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, string assignments, DateTime now, CancellationToken cancellationToken,
        bool requireCancellation, bool allowElapsedDeadline, params (string Name, object? Value)[] values)
    {
        var parameters = new List<(string, object?)>(values)
        {
            ("$task", lease.Request.Handle.TaskId), ("$ordinal", AttemptOrdinal(lease)),
            ("$fence", lease.Attempt.FencingCounter), ("$token", lease.Attempt.LeaseToken), ("$now", ToDb(now))
        };
        var sql = $"""
            UPDATE system_task_lifecycle SET {assignments}
            WHERE task_id = $task AND state = 'running' AND attempt_count = $ordinal
              AND fencing_counter = $fence AND lease_token = $token AND lease_expires_at_utc > $now
              {(requireCancellation ? "AND cancel_requested = 1" : allowElapsedDeadline
                  ? "AND cancel_requested = 0" : "AND cancel_requested = 0 AND deadline_utc > $now")}
            """;
        return await ExecuteAsync(connection, transaction, sql, cancellationToken, [.. parameters]);
    }

    private static async Task<bool> OwnsLeaseAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, DateTime now, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_lifecycle
            WHERE task_id = $task AND state = 'running' AND attempt_count = $ordinal
              AND fencing_counter = $fence AND lease_token = $token AND lease_expires_at_utc > $now
              AND cancel_requested = 0 AND deadline_utc > $now
            """, cancellationToken, ("$task", lease.Request.Handle.TaskId), ("$ordinal", AttemptOrdinal(lease)),
            ("$fence", lease.Attempt.FencingCounter), ("$token", lease.Attempt.LeaseToken), ("$now", ToDb(now))) == 1;

    private static async Task<SystemTaskLease> ReadLeaseAsync(SqliteConnection connection, SqliteTransaction transaction,
        string taskId, string attemptId, string token, DateTime expires, CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotByTaskAsync(connection, transaction, taskId, cancellationToken)
            ?? throw new InvalidOperationException("The claimed durable task could not be read.");
        return new(snapshot.Request,
            new(snapshot.Request.Handle.CommandId, attemptId, token, snapshot.FencingCounter, DateTime.SpecifyKind(expires, DateTimeKind.Utc)),
            snapshot.AttemptCount, snapshot.Checkpoint, snapshot.WakeJson, snapshot.CancellationRequested);
    }

    private static async Task<SystemTaskLifecycleSnapshot?> ReadSnapshotAsync(SqliteConnection connection,
        SqliteTransaction? transaction, SystemTaskDurableHandle handle, CancellationToken cancellationToken)
    {
        var result = await ReadSnapshotByTaskAsync(connection, transaction, handle.TaskId, cancellationToken);
        return result is not null && StringComparer.Ordinal.Equals(result.Request.Handle.CommandId, handle.CommandId) ? result : null;
    }

    private static async Task<SystemTaskLifecycleSnapshot?> ReadSnapshotByTaskAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string taskId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "SELECT * FROM system_task_lifecycle WHERE task_id = $task", ("$task", taskId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var commandId = reader.GetString(reader.GetOrdinal("command_id"));
        var handle = new SystemTaskDurableHandle(taskId, commandId);
        var invocation = new SystemTaskStoredInvocation(
            reader.GetString(reader.GetOrdinal("principal_reference")), reader.GetString(reader.GetOrdinal("authentication_method")),
            reader.GetString(reader.GetOrdinal("application_id")), reader.GetInt32(reader.GetOrdinal("application_revision")),
            reader.GetString(reader.GetOrdinal("application_fingerprint")), reader.GetString(reader.GetOrdinal("base_applications_json")),
            NullableString(reader, "state_space_id"), reader.GetString(reader.GetOrdinal("grant_reference")),
            NullableString(reader, "state_revision"), ParseProfile(reader.GetString(reader.GetOrdinal("execution_profile"))),
            commandId, NullableString(reader, "parent_command_id"), reader.GetInt32(reader.GetOrdinal("admitted_operations")),
            ParseDb(reader.GetString(reader.GetOrdinal("deadline_utc"))));
        var purpose = SystemTaskPurposeNames.Parse(reader.GetString(reader.GetOrdinal("purpose")));
        var definitionId = NullableString(reader, "definition_id");
        var definitionVersionOrdinal = reader.GetOrdinal("definition_version");
        var definitionFingerprint = NullableString(reader, "definition_fingerprint");
        var definitionVersion = reader.IsDBNull(definitionVersionOrdinal) ? (int?)null : reader.GetInt32(definitionVersionOrdinal);
        if ((definitionId is null) != (definitionVersion is null) || (definitionId is null) != (definitionFingerprint is null))
            throw new InvalidDataException("The retained task definition identity is incomplete.");
        var definition = definitionId is null ? null : new SystemTaskSelectedDefinition(definitionId,
            definitionVersion!.Value, definitionFingerprint!);
        var candidateId = NullableString(reader, "candidate_id");
        var candidateRevisionOrdinal = reader.GetOrdinal("candidate_revision");
        var candidateFingerprint = NullableString(reader, "candidate_fingerprint");
        var candidateRevision = reader.IsDBNull(candidateRevisionOrdinal) ? (int?)null : reader.GetInt32(candidateRevisionOrdinal);
        if ((candidateId is null) != (candidateRevision is null) || (candidateId is null) != (candidateFingerprint is null))
            throw new InvalidDataException("The retained validation candidate identity is incomplete.");
        var candidate = candidateId is null ? null : new DantesRoleplay.ApplicationActivation.ApplicationCandidateReference(
            DantesRoleplay.Applications.ApplicationIdentifier.Parse(invocation.ApplicationId), candidateId,
            candidateRevision!.Value, candidateFingerprint!);
        var causationOperationId = NullableString(reader, "causation_operation_id");
        var purposeShapeValid = purpose switch
        {
            SystemTaskPurpose.ProcedureWorkflow => definition is not null && candidate is null
                && causationOperationId is null && invocation.StateSpaceId is not null
                && invocation.StateRevision is not null && invocation.Profile == InteractionExecutionProfile.Workflow,
            SystemTaskPurpose.ApplicationValidation => definition is null && candidate is not null
                && invocation.StateSpaceId is null && invocation.StateRevision is null
                && invocation.Profile == InteractionExecutionProfile.ReadOnly,
            _ => false
        };
        if (!purposeShapeValid)
            throw new InvalidDataException("The retained task does not match its closed purpose shape.");
        var checkpointName = NullableString(reader, "checkpoint_name");
        var checkpoint = checkpointName is null ? null : new SystemTaskCheckpoint(checkpointName,
            reader.GetString(reader.GetOrdinal("completion_handler")), reader.GetString(reader.GetOrdinal("correlation_id")),
            reader.GetString(reader.GetOrdinal("checkpoint_state_json")));
        var dependencies = await ReadDependenciesAsync(connection, transaction, taskId, cancellationToken);
        var activationOrigin = purpose == SystemTaskPurpose.ProcedureWorkflow
            ? ReadActivationOrigin(reader, invocation, definition
                ?? throw new InvalidDataException("A workflow task is missing its selected definition."), dependencies)
            : ReadValidationActivationOrigin(reader);
        var causation = purpose == SystemTaskPurpose.ApplicationValidation
            ? VerifyValidationAdmission(reader, invocation, candidate
                ?? throw new InvalidDataException("A validation task is missing its candidate."), dependencies)
            : null;
        var request = new SystemTaskStoredRequest(handle, invocation, definition,
            reader.GetString(reader.GetOrdinal("input_json")), dependencies,
            reader.GetInt64(reader.GetOrdinal("propagate_cancellation")) != 0, activationOrigin,
            candidate, causation, purpose);
        return new(request, ParseState(reader.GetString(reader.GetOrdinal("state"))), checkpoint,
            NullableString(reader, "wake_json"), reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.GetInt64(reader.GetOrdinal("fencing_counter")), reader.GetInt64(reader.GetOrdinal("cancel_requested")) != 0,
            reader.GetInt64(reader.GetOrdinal("cancel_acknowledged")) != 0, NullableString(reader, "result_json"),
            NullableString(reader, "completion_evidence_reference"), NullableString(reader, "error_code"),
            NullableString(reader, "safe_message"), ParseEvidence(NullableString(reader, "evidence_json")),
            ParseDb(reader.GetString(reader.GetOrdinal("created_at_utc"))), ParseDb(reader.GetString(reader.GetOrdinal("updated_at_utc"))),
            NullableDate(reader, "completed_at_utc"));
    }

    private static async Task<IReadOnlyList<SystemTaskDurableHandle>> ReadDependenciesAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string taskId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT dependency_task_id, dependency_command_id FROM system_task_dependency
            WHERE task_id = $task ORDER BY dependency_task_id
            """, ("$task", taskId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<SystemTaskDurableHandle>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0), reader.GetString(1)));
        return result.AsReadOnly();
    }

    private static async Task<HashSet<string>> ReadAncestorIdsAsync(SqliteConnection connection,
        SqliteTransaction transaction, string parentTaskId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            WITH RECURSIVE ancestors(task_id, parent_task_id, depth) AS (
                SELECT task_id, parent_task_id, 1 FROM system_task_lifecycle WHERE task_id = $parent
                UNION ALL
                SELECT task.task_id, task.parent_task_id, ancestors.depth + 1
                FROM system_task_lifecycle AS task JOIN ancestors ON task.task_id = ancestors.parent_task_id
                WHERE ancestors.depth <= $maximum)
            SELECT task_id FROM ancestors
            """, ("$parent", parentTaskId), ("$maximum", SystemTaskLifecycleLimits.MaximumParentDepth));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<(string TaskId, string PayloadFingerprint)?> FindByCommandAsync(SqliteConnection connection,
        SqliteTransaction transaction, string commandId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "SELECT task_id, payload_fingerprint FROM system_task_lifecycle WHERE command_id = $command", ("$command", commandId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    private static async Task<ParentRow?> FindParentAsync(
        SqliteConnection connection, SqliteTransaction transaction, string parentCommandId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT task.task_id, task.root_task_id, task.parent_depth, task.deadline_utc,
                   task.admitted_operations, budget.maximum_operations, task.principal_reference,
                   task.application_id, task.application_revision, task.application_fingerprint,
                   task.base_applications_json, task.state_space_id, task.grant_reference,
                   task.state_revision, task.execution_profile
            FROM system_task_lifecycle AS task JOIN system_task_root_budget AS budget ON budget.root_task_id = task.root_task_id
            WHERE task.command_id = $command AND task.state IN ('queued','running','waiting','retry')
              AND task.cancel_requested = 0
            """, ("$command", parentCommandId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ParentRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), ParseDb(reader.GetString(3)),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8),
                reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14))
            : null;
    }

    private static async Task<bool> FindHandleAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE task_id = $task AND command_id = $command", cancellationToken,
            ("$task", handle.TaskId), ("$command", handle.CommandId)) == 1;

    private static async Task<bool> DependencyMatchesScopeAsync(SqliteConnection connection,
        SqliteTransaction transaction, SystemTaskDurableHandle handle, string principalReference,
        string applicationId, int applicationRevision, string applicationFingerprint,
        string baseApplicationsJson, string stateSpaceId, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, """
            SELECT COUNT(*) FROM system_task_lifecycle
            WHERE task_id = $task AND command_id = $command AND principal_reference = $principal
              AND application_id = $application AND application_revision = $revision
              AND application_fingerprint = $fingerprint AND base_applications_json = $bases
              AND state_space_id = $stateSpace
            """, cancellationToken, ("$task", handle.TaskId), ("$command", handle.CommandId),
            ("$principal", principalReference), ("$application", applicationId), ("$revision", applicationRevision),
            ("$fingerprint", applicationFingerprint), ("$bases", baseApplicationsJson), ("$stateSpace", stateSpaceId)) == 1;

    private static async Task<(int Ordinal, long Fence)> ReadAttemptOrdinalAndFenceAsync(SqliteConnection connection,
        SqliteTransaction transaction, string taskId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "SELECT attempt_count, fencing_counter FROM system_task_lifecycle WHERE task_id = $task", ("$task", taskId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Durable task attempt identity was unavailable.");
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private static async Task<(string Status, string RequestFingerprint, string? CompletionJson)?> ReadHostCallAsync(
        SqliteConnection connection, SqliteTransaction transaction, string taskId, string operationId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            SELECT status, request_fingerprint, completion_json FROM system_task_host_call
            WHERE task_id = $task AND operation_id = $operation
            """, ("$task", taskId), ("$operation", operationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)) : null;
    }

    private static async Task<bool> HasPendingHostCallAsync(SqliteConnection connection, SqliteTransaction transaction,
        string taskId, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction,
            "SELECT COUNT(*) FROM system_task_host_call WHERE task_id = $task AND status = 'pending'", cancellationToken,
            ("$task", taskId)) > 0;

    private static async Task<bool> ConsumeOperationAsync(SqliteConnection connection,
        SqliteTransaction transaction, string taskId, CancellationToken cancellationToken)
    {
        // Taking the root row first serializes all descendants that share the root allowance.
        var rootChanged = await ExecuteAsync(connection, transaction, """
            UPDATE system_task_root_budget
            SET consumed_operations = consumed_operations + 1
            WHERE root_task_id = (SELECT root_task_id FROM system_task_lifecycle WHERE task_id = $task)
              AND consumed_operations < maximum_operations
            """, cancellationToken, ("$task", taskId));
        if (rootChanged != 1) return false;

        var unavailable = await ScalarLongAsync(connection, transaction, """
            WITH RECURSIVE ancestry(task_id, parent_task_id) AS (
                SELECT task_id, parent_task_id FROM system_task_lifecycle WHERE task_id = $task
                UNION ALL
                SELECT parent.task_id, parent.parent_task_id FROM system_task_lifecycle AS parent
                JOIN ancestry AS child ON parent.task_id = child.parent_task_id)
            SELECT COUNT(*) FROM ancestry
            JOIN system_task_lifecycle AS task ON task.task_id = ancestry.task_id
            WHERE task.consumed_operations >= task.admitted_operations
            """, cancellationToken, ("$task", taskId));
        if (unavailable != 0)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE system_task_root_budget SET consumed_operations = consumed_operations - 1
                WHERE root_task_id = (SELECT root_task_id FROM system_task_lifecycle WHERE task_id = $task)
                """, cancellationToken, ("$task", taskId));
            return false;
        }

        await ExecuteAsync(connection, transaction, """
            WITH RECURSIVE ancestry(task_id, parent_task_id) AS (
                SELECT task_id, parent_task_id FROM system_task_lifecycle WHERE task_id = $task
                UNION ALL
                SELECT parent.task_id, parent.parent_task_id FROM system_task_lifecycle AS parent
                JOIN ancestry AS child ON parent.task_id = child.parent_task_id)
            UPDATE system_task_lifecycle SET consumed_operations = consumed_operations + 1
            WHERE task_id IN (SELECT task_id FROM ancestry)
            """, cancellationToken, ("$task", taskId));
        return true;
    }

    private static async Task<bool> MarkPendingIndeterminateAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, DateTime now, CancellationToken cancellationToken, bool requireCancellation = false)
    {
        var changed = await ExecuteFencedAsync(connection, transaction, lease, """
            state = 'indeterminate', error_code = $pendingCode, safe_message = $pendingMessage,
            lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL,
            updated_at_utc = $now, completed_at_utc = $now
            """, now, cancellationToken, requireCancellation, true,
            ("$pendingCode", PendingHostCallCode), ("$pendingMessage", PendingHostCallMessage));
        if (changed == 1)
            await CompleteAttemptAsync(connection, transaction, lease, "indeterminate",
                PendingHostCallCode, PendingHostCallMessage, now, cancellationToken);
        return changed == 1;
    }

    private static async Task CompleteAttemptAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskLease lease, string state, string? code, string? message, DateTime now, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, """
            UPDATE system_task_attempt SET state = $state, failure_code = $code,
                safe_message = $message, completed_at_utc = $now
            WHERE task_id = $task AND attempt_id = $attempt AND fencing_counter = $fence AND state = 'running'
            """, cancellationToken, ("$state", state), ("$code", code), ("$message", message),
            ("$now", ToDb(now)), ("$task", lease.Request.Handle.TaskId),
            ("$attempt", lease.Attempt.AttemptId), ("$fence", lease.Attempt.FencingCounter));

    private static Task FinishWithoutLeaseAsync(SqliteConnection connection, SqliteTransaction transaction,
        string taskId, string state, string code, string message, DateTime now, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction, """
            UPDATE system_task_lifecycle SET state = $state, error_code = $code, safe_message = $message,
                completed_at_utc = $now, updated_at_utc = $now WHERE task_id = $task
            """, cancellationToken, ("$state", state), ("$code", code), ("$message", message),
            ("$now", ToDb(now)), ("$task", taskId));

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarStringAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (string)value;
    }

    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static int AttemptOrdinal(SystemTaskLease lease) => lease.AttemptOrdinal;

    private static string NewTaskId(string commandId)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("dantes-roleplay/system-task-id/v1\0" + commandId));
        return "task." + Convert.ToHexStringLower(bytes)[..32];
    }

    private static string ToDb(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");
    private static DateTime ParseDb(string value) => DateTime.SpecifyKind(DateTime.Parse(value,
        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), DateTimeKind.Utc);
    private static DateTime? NullableDate(SqliteDataReader reader, string name) =>
        NullableString(reader, name) is { } value ? ParseDb(value) : null;
    private static string? NullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static InteractionExecutionProfile ParseProfile(string value) => value switch
    {
        "read-only" => InteractionExecutionProfile.ReadOnly,
        "atomic" => InteractionExecutionProfile.Atomic,
        "workflow" => InteractionExecutionProfile.Workflow,
        _ => throw new InvalidDataException("The stored task execution profile is invalid.")
    };

    private static SystemTaskLifecycleState ParseState(string value) => value switch
    {
        "queued" => SystemTaskLifecycleState.Queued,
        "running" => SystemTaskLifecycleState.Running,
        "waiting" => SystemTaskLifecycleState.Waiting,
        "retry" => SystemTaskLifecycleState.Retry,
        "completed" => SystemTaskLifecycleState.Completed,
        "failed" => SystemTaskLifecycleState.Failed,
        "cancelled" => SystemTaskLifecycleState.Cancelled,
        "indeterminate" => SystemTaskLifecycleState.Indeterminate,
        _ => throw new InvalidDataException("The stored durable task state is invalid.")
    };

    private static string ValidateEvidence(IReadOnlyList<string>? values)
    {
        var evidence = values?.ToArray() ?? [];
        if (evidence.Length > SystemTaskLifecycleLimits.MaximumEvidenceItems)
            throw new ArgumentException("The task evidence exceeds its bound.", nameof(values));
        if (evidence.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > InteractionContractLimits.SafeEvidenceText))
            throw new ArgumentException("Task evidence contains an invalid item.", nameof(values));
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(evidence));
    }

    private static IReadOnlyList<string> ParseEvidence(string? json) => json is null
        ? Array.Empty<string>()
        : Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(json) ?? []);

    private static bool ValidActivationOrigin(SystemTaskDurableSubmissionRequest request,
        StandingGrantActivationOrigin origin) => ValidActivationOrigin(
            request.InvocationHost.ApplicationRevision.Revision,
            request.InvocationHost.ApplicationRevision.Fingerprint, origin);

    private static bool ValidActivationOrigin(int applicationRevision, string applicationFingerprint,
        StandingGrantActivationOrigin origin) => origin.ActivationRevision > 0
        && origin.ApplicationRevision > 0
        && origin.ApplicationRevision == applicationRevision
        && StringComparer.Ordinal.Equals(origin.ApplicationFingerprint, applicationFingerprint)
        && IsUpperHash(origin.ActivationFingerprint) && IsUpperHash(origin.ApplicationFingerprint);

    private static bool IsUpperHash(string? value) => value is { Length: 64 }
        && value.All(char.IsAsciiHexDigitUpper);

    private static StandingGrantActivationOrigin? ReadActivationOrigin(SqliteDataReader reader,
        SystemTaskStoredInvocation invocation, SystemTaskSelectedDefinition definition,
        IReadOnlyList<SystemTaskDurableHandle> dependencies)
    {
        var activationRevisionOrdinal = reader.GetOrdinal("activation_revision");
        var activationFingerprintOrdinal = reader.GetOrdinal("activation_fingerprint");
        var applicationRevisionOrdinal = reader.GetOrdinal("activation_application_revision");
        var applicationFingerprintOrdinal = reader.GetOrdinal("activation_application_fingerprint");
        var absent = reader.IsDBNull(activationRevisionOrdinal) && reader.IsDBNull(activationFingerprintOrdinal)
            && reader.IsDBNull(applicationRevisionOrdinal) && reader.IsDBNull(applicationFingerprintOrdinal);
        if (absent)
        {
            if (NullableString(reader, "admission_payload_json") is not null)
                throw new InvalidDataException("A legacy workflow cannot acquire admission provenance after creation.");
            return null;
        }
        if (reader.IsDBNull(activationRevisionOrdinal) || reader.IsDBNull(activationFingerprintOrdinal)
            || reader.IsDBNull(applicationRevisionOrdinal) || reader.IsDBNull(applicationFingerprintOrdinal))
            throw new InvalidDataException("The stored retained activation origin is incomplete.");
        var origin = new StandingGrantActivationOrigin(reader.GetInt32(activationRevisionOrdinal),
            reader.GetString(activationFingerprintOrdinal), reader.GetInt32(applicationRevisionOrdinal),
            reader.GetString(applicationFingerprintOrdinal));
        if (!ValidActivationOrigin(invocation.ApplicationRevision, invocation.ApplicationFingerprint, origin))
            throw new InvalidDataException("The stored retained activation origin is invalid or does not match the admitted application revision.");
        VerifyActivationAdmission(reader, invocation, definition, dependencies, origin);
        return origin;
    }

    private static StandingGrantActivationOrigin? ReadValidationActivationOrigin(SqliteDataReader reader)
    {
        if (!reader.IsDBNull(reader.GetOrdinal("activation_revision"))
            || !reader.IsDBNull(reader.GetOrdinal("activation_fingerprint"))
            || !reader.IsDBNull(reader.GetOrdinal("activation_application_revision"))
            || !reader.IsDBNull(reader.GetOrdinal("activation_application_fingerprint")))
            throw new InvalidDataException("A validation task cannot retain workflow activation provenance.");
        return null;
    }

    private static string Required(string value, int maximum, string parameter) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maximum
            ? throw new ArgumentException($"{parameter} is required and may contain at most {maximum} characters.", parameter)
            : value.Trim();

    private static void ValidateWorker(string workerId)
    {
        var value = Required(workerId, 128, nameof(workerId));
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or ':' or '-')))
            throw new ArgumentException("The worker identity contains unsupported characters.", nameof(workerId));
    }

    private static void ValidateLeaseDuration(TimeSpan value)
    {
        if (value < TimeSpan.FromSeconds(5) || value > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(value), "A lease must be between five seconds and thirty minutes.");
    }

    private static SystemTaskEnqueueResult Rejected(string code, string message) =>
        new(SystemTaskEnqueueDisposition.Rejected, null, code, message);

    private sealed record ParentRow(string TaskId, string RootTaskId, int Depth, DateTime DeadlineUtc,
        int AdmittedOperations, int RootMaximumOperations, string PrincipalReference, string ApplicationId,
        int ApplicationRevision, string ApplicationFingerprint, string BaseApplicationsJson, string StateSpaceId,
        string GrantReference, string StateRevision, string ExecutionProfile);
}
