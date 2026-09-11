using System.Data;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Procedure-job admission and polling over the caller's scoped authority owners. Registration is
/// supplied by the coordinator only when the real grant policy, target resolver and runner exist.
/// </summary>
internal sealed partial class SqliteSystemTaskDurableService(
    DantesRoleplayDbContext db,
    IStandingGrantPolicy policy,
    IStandingGrantTargetResolver targets,
    IStateSpaceRegistry stateSpaces,
    TimeProvider timeProvider) : ISystemTaskDurableService
{
    internal sealed record InnerWorkerAuthorityResolution(
        SystemInnerWorkerResolvedProfile? Profile,
        InteractionInvocationResult? Failure);

    internal Task<InteractionInvocationResult> SubmitInnerWorkerAsync(
        DataAccess.Composition.SystemInnerWorkerProcedurePreparationResult preparation,
        CancellationToken cancellationToken = default) =>
        SubmitInnerWorkerCoreAsync(preparation, allowEphemeralParent: false, cancellationToken);

    internal Task<InteractionInvocationResult> SubmitEphemeralRootInnerWorkerAsync(
        DataAccess.Composition.SystemInnerWorkerProcedurePreparationResult preparation,
        CancellationToken cancellationToken = default) =>
        SubmitInnerWorkerCoreAsync(preparation, allowEphemeralParent: true, cancellationToken);

    private Task<InteractionInvocationResult> SubmitInnerWorkerCoreAsync(
        DataAccess.Composition.SystemInnerWorkerProcedurePreparationResult preparation,
        bool allowEphemeralParent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var worker = preparation.Worker;
        if (worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow)
            return Task.FromResult(InteractionInvocationResult.Failed("INNER_WORKER_SUBJECT_UNSUPPORTED",
                "Only exact procedure workflow subjects can be admitted here."));
        if (allowEphemeralParent && (worker.InvocationHost.ParentCommandId is null
            || StringComparer.Ordinal.Equals(worker.InvocationHost.ParentCommandId,
                worker.InvocationHost.CommandId)))
            return Task.FromResult(InteractionInvocationResult.Failed(
                "SYSTEM_TASK_EPHEMERAL_PARENT_INVALID",
                "A trusted ephemeral parent command is required."));
        var request = new SystemTaskDurableSubmissionRequest(worker.InvocationHost, workflow.ProcedureVersion,
            worker.InputJson, dependencyHandles: worker.DependencyHandles);
        return InOwnedTransactionAsync(worker.InvocationHost, write: true, async (store, connection, transaction) =>
        {
            var authorization = await AuthorizeCoreAsync(worker.InvocationHost, workflow.ProcedureVersion,
                StandingGrantCapability.Execute, null, null, cancellationToken);
            if (authorization.Failure is not null) return authorization.Failure;
            if (authorization.CurrentActivation is null || authorization.Decision?.Grant is not { } grant)
                return TargetUnavailable();
            var profile = preparation.BindAuthority(new(
                grant.GrantReference, grant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                grant.ContentFingerprint));
            return await StageInnerWorkerAsync(request, profile, authorization.CurrentActivation,
                allowEphemeralParent, store, connection, transaction, cancellationToken);
        }, cancellationToken);
    }

    private static async Task<InteractionInvocationResult> StageInnerWorkerAsync(
        SystemTaskDurableSubmissionRequest request,
        SystemInnerWorkerResolvedProfile profile,
        StandingGrantActivationOrigin activationOrigin,
        bool allowEphemeralParent,
        SqliteSystemTaskLifecycleStore store,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var savepoint = "inner_worker_admission_" + Guid.NewGuid().ToString("N");
        await transaction.SaveAsync(savepoint, cancellationToken);
        try
        {
            var staged = await store.StageEnqueueInnerWorkerAsync(request, profile, false,
                connection, transaction, cancellationToken, activationOrigin,
                allowEphemeralParent);
            if (staged.Disposition is not (SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing))
            {
                await transaction.RollbackAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseAsync(savepoint, CancellationToken.None);
                return InteractionInvocationResult.Failed(staged.Code, staged.SafeMessage);
            }
            var enrollment = await store.StageEnrollAiBudgetAsync(staged.Handle!, profile,
                connection, transaction, cancellationToken);
            if (!enrollment.Accepted)
            {
                await transaction.RollbackAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseAsync(savepoint, CancellationToken.None);
                return InteractionInvocationResult.Unavailable(enrollment.Code,
                    "The focused worker accounting could not be admitted.");
            }
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
            return InteractionInvocationResult.Pending(staged.Handle!);
        }
        catch
        {
            await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
            throw;
        }
    }

    public Task<InteractionInvocationResult> SubmitAsync(SystemTaskDurableSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InvocationHost.Profile != InteractionExecutionProfile.Workflow)
            return Task.FromResult(InteractionInvocationResult.Unavailable("SYSTEM_TASK_PROFILE_UNSUPPORTED",
                "Durable tasks require the workflow execution profile."));
        return InOwnedTransactionAsync(request.InvocationHost, write: true, async (store, connection, transaction) =>
        {
            var authorization = await AuthorizeCoreAsync(request.InvocationHost, request.SelectedDefinition,
                StandingGrantCapability.Execute, null, null, cancellationToken);
            if (authorization.Failure is not null) return authorization.Failure;
            if (authorization.CurrentActivation is null) return TargetUnavailable();
            var staged = await store.StageEnqueueAsync(request, false, connection, transaction,
                cancellationToken, authorization.CurrentActivation);
            return staged.Disposition is SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing
                ? InteractionInvocationResult.Pending(staged.Handle!)
                : InteractionInvocationResult.Failed(staged.Code, staged.SafeMessage);
        }, cancellationToken);
    }

    public Task<InteractionInvocationResult> GetAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return InOwnedTransactionAsync(invocationHost, write: false, async (store, connection, transaction) =>
        {
            var snapshot = await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken);
            if (snapshot is null || !TaskScopeMatches(invocationHost, snapshot.Request)) return NotAuthorized();
            var denied = await AuthorizeAsync(invocationHost, snapshot.Request.WorkflowDefinition,
                StandingGrantCapability.ReadTask, TaskTarget(snapshot.Request), cancellationToken, snapshot.Request.ActivationOrigin);
            if (denied is not null) return denied;

            // Journal payloads are inert diagnostics. Until the runtime supplies authoritative
            // receipt reconciliation, no outcome may silently drop or promote earlier effects.
            var calls = await ReconcileHostCallsAsync(handle, connection, transaction, cancellationToken);
            if (!calls.Reconciled)
                return UnreconciledHostCalls("The task requires authoritative host-call receipt reconciliation.", calls);
            if (await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(connection, transaction, handle.TaskId, cancellationToken))
                return InteractionInvocationResult.Unavailable("INNER_AI_RECONCILIATION_REQUIRED", "AI usage must be reconciled before returning a task outcome.");
            return PollResult(snapshot, calls.PreviousCommits);
        }, cancellationToken);
    }

    public Task<InteractionInvocationResult> CancelAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (invocationHost.Profile == InteractionExecutionProfile.ReadOnly)
            return Task.FromResult(InteractionInvocationResult.Unavailable("SYSTEM_TASK_PROFILE_UNSUPPORTED",
                "The read-only execution profile cannot request cancellation."));
        return InOwnedTransactionAsync(invocationHost, write: true, async (store, connection, transaction) =>
        {
            var snapshot = await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken);
            if (snapshot is null || !TaskScopeMatches(invocationHost, snapshot.Request)) return NotAuthorized();
            var denied = await AuthorizeAsync(invocationHost, snapshot.Request.WorkflowDefinition,
                StandingGrantCapability.CancelTask, TaskTarget(snapshot.Request), cancellationToken, snapshot.Request.ActivationOrigin);
            if (denied is not null) return denied;
            var affected = await store.ReadCancellationTargetsAsync(handle, true, connection, transaction, cancellationToken);
            foreach (var child in affected.Where(value => value.Request.Handle != handle))
            {
                if (!TaskScopeMatches(invocationHost, child.Request)) return NotAuthorized();
                denied = await AuthorizeAsync(invocationHost, child.Request.WorkflowDefinition,
                    StandingGrantCapability.CancelTask, TaskTarget(child.Request), cancellationToken, child.Request.ActivationOrigin);
                if (denied is not null) return denied;
            }
            await store.StageCancellationAsync(handle, true, connection, transaction, cancellationToken);
            var updated = (await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken))!;
            var calls = await ReconcileHostCallsAsync(handle, connection, transaction, cancellationToken);
            if (!calls.Reconciled)
                return UnreconciledHostCalls(
                    "Cancellation was processed; the task still requires authoritative host-call receipt reconciliation.", calls);
            if (await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(connection, transaction, handle.TaskId, cancellationToken))
                return InteractionInvocationResult.Unavailable("INNER_AI_RECONCILIATION_REQUIRED", "Cancellation was processed; AI usage still requires reconciliation.");
            return updated.State switch
            {
                SystemTaskLifecycleState.Cancelled => InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED",
                    "The task acknowledged cancellation.", calls.PreviousCommits),
                SystemTaskLifecycleState.Running => InteractionInvocationResult.Pending(handle, calls.PreviousCommits),
                // CancelTask alone does not permit disclosing an already completed result.
                _ => InteractionInvocationResult.Failed("SYSTEM_TASK_NOT_CANCELLABLE",
                    "The task is already terminal and cannot acknowledge a new cancellation request.")
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Rechecks the exact current procedure, activation and Execute grant inside the caller's
    /// existing transaction. The retained profile and lease are correlation only.
    /// </summary>
    internal async Task<InteractionInvocationResult?> ReauthorizeInnerWorkerAsync(
        SystemInnerWorkerResolvedProfile profile,
        SystemTaskStoredRequest retained,
        CancellationToken cancellationToken = default)
    {
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow
            || retained.Purpose != SystemTaskPurpose.ProcedureWorkflow
            || retained.SelectedDefinition != workflow.ProcedureVersion
            || retained.ActivationOrigin is null)
            return NotAuthorized();
        var authorization = await AuthorizeCoreAsync(profile.Worker.InvocationHost, workflow.ProcedureVersion,
            StandingGrantCapability.Execute, null, null, cancellationToken);
        if (authorization.Failure is not null) return authorization.Failure;
        if (authorization.CurrentActivation != retained.ActivationOrigin
            || authorization.Decision?.Grant is not { } grant
            || grant.GrantReference != profile.AuthorityProvenance.Reference
            || grant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) != profile.AuthorityProvenance.GrantRevision
            || grant.ContentFingerprint != profile.AuthorityProvenance.GrantFingerprint)
            return InteractionInvocationResult.Unavailable("INNER_WORKER_AUTHORITY_STALE",
                "The focused worker's procedure or grant selection is no longer current.");
        return null;
    }

    internal async Task<InnerWorkerAuthorityResolution> ResolveCurrentInnerWorkerProfileAsync(
        DataAccess.Composition.SystemInnerWorkerProcedurePreparationResult preparation,
        SystemTaskStoredRequest retained,
        CancellationToken cancellationToken = default)
    {
        if (preparation.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow
            || retained.Purpose != SystemTaskPurpose.ProcedureWorkflow
            || retained.SelectedDefinition != workflow.ProcedureVersion
            || retained.ActivationOrigin is null)
            return new(null, NotAuthorized());
        var authorization = await AuthorizeCoreAsync(preparation.Worker.InvocationHost, workflow.ProcedureVersion,
            StandingGrantCapability.Execute, null, null, cancellationToken);
        if (authorization.Failure is not null) return new(null, authorization.Failure);
        if (authorization.CurrentActivation != retained.ActivationOrigin
            || authorization.Decision?.Grant is not { } grant)
            return new(null, InteractionInvocationResult.Unavailable("INNER_WORKER_AUTHORITY_STALE",
                "The focused worker's procedure or grant selection is no longer current."));
        return new(preparation.BindAuthority(new(grant.GrantReference,
            grant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), grant.ContentFingerprint)), null);
    }

    private async Task<InteractionInvocationResult> InOwnedTransactionAsync(InteractionInvocationHost host, bool write,
        Func<SqliteSystemTaskLifecycleStore, SqliteConnection, SqliteTransaction, Task<InteractionInvocationResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.StateSpaceId is not { } stateSpaceId || host.StateRevision is not { } stateRevision)
            return InteractionInvocationResult.Failed("INVOCATION_STATE_SCOPE_REQUIRED",
                "Durable tasks require a state scope.");
        // Public Pending cannot escape an uncommitted caller transaction. Effect/scheduler owners
        // use the internal staging API and publish their result after their own commit boundary.
        if (db.Database.CurrentTransaction is not null)
            return InteractionInvocationResult.Unavailable("SYSTEM_TASK_OUTER_TRANSACTION_ACTIVE",
                "This operation requires its own completed transaction boundary.");
        if (cancellationToken.IsCancellationRequested || host.Budget.DeadlineUtc <= timeProvider.GetUtcNow().UtcDateTime)
            return Cancelled();
        if (!host.Budget.TryConsumeOperation())
            return InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");

        var opened = false;
        try
        {
            if (db.Database.GetDbConnection() is not SqliteConnection connection)
                return InteractionInvocationResult.Unavailable("SYSTEM_TASK_STORAGE_UNAVAILABLE", "The durable task storage is unavailable.");
            if (connection.State != ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(cancellationToken);
                opened = true;
            }
            await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: !write);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var currentState = stateSpaces.Get(stateSpaceId);
            if (currentState is null || currentState.ApplicationRevision.ApplicationId != host.ApplicationRevision.ApplicationId
                || currentState.ApplicationRevision.Revision != host.ApplicationRevision.Revision
                || currentState.ApplicationRevision.Fingerprint != host.ApplicationRevision.Fingerprint
                || !currentState.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)
                || InteractionStateRevision.From(currentState) != stateRevision)
                return InteractionInvocationResult.Failed("INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
            var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, timeProvider);
            var result = await action(store, connection, transaction);
            cancellationToken.ThrowIfCancellationRequested();
            if (host.Budget.DeadlineUtc <= timeProvider.GetUtcNow().UtcDateTime) return Cancelled();
            // Cancellation is checked before COMMIT. Once it begins, finish the local commit so
            // cancellation cannot turn a committed enqueue into a false pre-admission rejection.
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (InteractionContractException)
        {
            return InteractionInvocationResult.Failed("SYSTEM_TASK_REQUEST_INVALID", "The durable task request is invalid.");
        }
        catch (SystemTaskException)
        {
            return InteractionInvocationResult.Failed("SYSTEM_TASK_SCOPE_INVALID", "The durable task graph cannot be used in this scope.");
        }
        catch
        {
            return InteractionInvocationResult.Unavailable("SYSTEM_TASK_SERVICE_UNAVAILABLE",
                "The durable task service could not establish an outcome; retain the original command identity for retry.");
        }
        finally
        {
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<InteractionInvocationResult?> AuthorizeAsync(InteractionInvocationHost host,
        SystemTaskSelectedDefinition selection, StandingGrantCapability capability,
        StandingGrantTaskTarget? task, CancellationToken cancellationToken, StandingGrantActivationOrigin? origin = null) =>
        (await AuthorizeCoreAsync(host, selection, capability, task, origin, cancellationToken)).Failure;

    private async Task<(InteractionInvocationResult? Failure, StandingGrantActivationOrigin? CurrentActivation,
        StandingGrantDecision? Decision)> AuthorizeCoreAsync(
        InteractionInvocationHost host, SystemTaskSelectedDefinition selection, StandingGrantCapability capability,
        StandingGrantTaskTarget? task, StandingGrantActivationOrigin? origin, CancellationToken cancellationToken)
    {
        if (origin is not null && (task is null || capability is not (StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask)))
            return (NotAuthorized(), null, null);
        var definition = new StandingGrantDefinitionReference(selection.ExactDefinitionId, "procedure", selection.Version, selection.Fingerprint);
        // Provenance chooses the lookup; permission failures never broaden it. Legacy rows have
        // no inferred origin and can use only the exact current definition selection.
        var resolution = origin is null
            ? await targets.ResolveAsync(host, definition, cancellationToken)
            : await targets.ResolveRetainedAsync(host, origin, definition, cancellationToken);
        if (resolution.Status == StandingGrantTargetResolutionStatus.Denied) return (NotAuthorized(), null, null);
        if (resolution.Status != StandingGrantTargetResolutionStatus.Available || resolution.Target is not { } target)
            return (TargetUnavailable(), null, null);
        if (target.DefinitionId != selection.ExactDefinitionId || target.Revision != selection.Version
            || target.ContentFingerprint != selection.Fingerprint || target.Kind != "procedure"
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId || target.Candidate is not null
            || target.RetainedActivation != origin)
            return (TargetUnavailable(), null, null);
        if (origin is null && resolution.CurrentActivation is { } current
            && (current.ActivationRevision < 1 || !IsActivationHash(current.ActivationFingerprint)
                || current.ApplicationRevision != host.ApplicationRevision.Revision
                || current.ApplicationFingerprint != host.ApplicationRevision.Fingerprint))
            return (TargetUnavailable(), null, null);
        var requirement = new StandingGrantRequirement(capability, StandingGrantScope.StateSpace, [target], [], task);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        var decision = await policy.EvaluateAsync(host, requirement, cancellationToken);
        if (!decision.Allowed)
            return (decision.Code.Contains("UNAVAILABLE", StringComparison.Ordinal)
                ? InteractionInvocationResult.Unavailable("SYSTEM_TASK_AUTHORIZATION_UNAVAILABLE", "Current task authorization is unavailable.")
                : NotAuthorized(), null, decision);
        if (decision.Grant is not { } grant || grant.GrantReference != host.GrantReference
            || grant.PrincipalReference != host.Principal.PrincipalId || grant.ApplicationId != host.ApplicationRevision.ApplicationId
            || grant.Scope != StandingGrantScope.StateSpace || grant.StateSpaceId != host.StateSpaceId)
            return (NotAuthorized(), null, decision);
        return (null, origin is null ? resolution.CurrentActivation : null, decision);
    }

    private static bool IsActivationHash(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static InteractionInvocationResult TargetUnavailable() => InteractionInvocationResult.Unavailable(
        "SYSTEM_TASK_TARGET_UNAVAILABLE", "The selected task definition and its activation provenance cannot currently be resolved.");

    private static bool TaskScopeMatches(InteractionInvocationHost host, SystemTaskStoredRequest task) =>
        task.Invocation.PrincipalReference == host.Principal.PrincipalId
        && task.Invocation.ApplicationId == host.ApplicationRevision.ApplicationId.Value
        && task.Invocation.StateSpaceId == host.StateSpaceId;

    private static StandingGrantTaskTarget TaskTarget(SystemTaskStoredRequest task) => new(task.Handle,
        task.Invocation.PrincipalReference, ApplicationIdentifier.Parse(task.Invocation.ApplicationId),
        task.Invocation.StateSpaceId ?? throw new InvalidDataException("A workflow task is missing its state scope."),
        task.WorkflowDefinition);

    private static async Task<bool> HasUnreconciledHostCallsAsync(SystemTaskDurableHandle handle, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
        => !(await ReconcileHostCallsAsync(handle, connection, transaction, cancellationToken)).Reconciled;

    private static async Task<HostCallReconciliation> ReconcileHostCallsAsync(SystemTaskDurableHandle handle,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT call.operation_id, call.request_fingerprint, call.status, call.completion_json,
                   task.completion_evidence_reference
            FROM system_task_host_call AS call
            JOIN system_task_lifecycle AS task ON task.task_id = call.task_id
            WHERE call.task_id = $task
            ORDER BY call.started_at_utc, call.operation_id
            LIMIT 17
            """;
        command.Parameters.AddWithValue("$task", handle.TaskId);
        var commits = new List<InteractionInvocationCommitReceipt>();
        ApplicationEcsExecutionIdentity? recovery = null;
        string? commonEvidence = null;
        var count = 0;
        var reconciled = true;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            var operation = reader.GetString(0);
            var fingerprint = reader.GetString(1);
            var terminalEvidence = reader.IsDBNull(4) ? null : reader.GetString(4);
            if (count > SystemTaskLifecycleLimits.MaximumEvidenceItems
                || reader.GetString(2) != "completed" || reader.IsDBNull(3))
            {
                reconciled = false;
                recovery ??= Recovery(operation, fingerprint);
                continue;
            }
            try
            {
                var completionJson = reader.GetString(3);
                if (InteractionCanonicalJson.CanonicalizeObject(completionJson) != completionJson)
                    throw new JsonException("The host-call completion is not canonical.");
                using var document = JsonDocument.Parse(completionJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("completionEvidenceReference", out var evidence)
                    || evidence.ValueKind != JsonValueKind.String)
                    throw new JsonException("The host-call completion has no evidence reference.");
                var currentEvidence = evidence.GetString();
                if (string.IsNullOrWhiteSpace(currentEvidence) || terminalEvidence != currentEvidence
                    || commonEvidence is not null && commonEvidence != currentEvidence)
                    throw new JsonException("The host-call evidence does not match the terminal outcome.");
                commonEvidence ??= currentEvidence;
                if (!root.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object
                    || !commit.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String)
                    throw new JsonException("The host-call commit disposition is unavailable.");
                switch (status.GetString())
                {
                    case "committed":
                        if (!commit.TryGetProperty("Receipt", out var receipt) || receipt.ValueKind != JsonValueKind.Object)
                            throw new JsonException("The host-call commit receipt is unavailable.");
                        commits.Add(ParseReceipt(receipt));
                        break;
                    case "not-applicable":
                    case "not-committed":
                        if (commit.TryGetProperty("Receipt", out var absent) && absent.ValueKind != JsonValueKind.Null)
                            throw new JsonException("The host-call commit disposition conflicts with its receipt.");
                        break;
                    default:
                        throw new JsonException("The host-call commit disposition requires reconciliation.");
                }
            }
            catch (Exception error) when (error is JsonException or InteractionContractException)
            {
                reconciled = false;
                recovery ??= Recovery(operation, fingerprint);
            }
        }
        return new(reconciled, commits.AsReadOnly(), recovery, commonEvidence);
    }

    private static InteractionInvocationCommitReceipt ParseReceipt(JsonElement value)
    {
        if (value.EnumerateObject().Count() != 4
            || !value.TryGetProperty("OperationId", out var operation) || operation.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("RequestFingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("Effects", out var effects) || effects.ValueKind != JsonValueKind.Array || effects.GetArrayLength() != 0
            || !value.TryGetProperty("EffectDetailsAvailable", out var available)
            || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || available.GetBoolean())
            throw new JsonException("The host-call commit receipt is invalid.");
        return new InteractionInvocationCommitReceipt(operation.GetString()!, fingerprint.GetString()!, [], false).Validate();
    }

    private static ApplicationEcsExecutionIdentity? Recovery(string operation, string fingerprint)
    {
        try
        {
            new InteractionInvocationCommitReceipt(operation, fingerprint, []).Validate();
            return new(operation, fingerprint);
        }
        catch (InteractionContractException) { return null; }
    }

    private static InteractionInvocationResult UnreconciledHostCalls(
        string message, HostCallReconciliation calls) =>
        calls.PreviousCommits.Count == 0 && calls.RecoveryIdentity is null
            ? InteractionInvocationResult.Unavailable("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE", message)
            : InteractionInvocationResult.Failed("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE", message,
                calls.PreviousCommits, calls.RecoveryIdentity);

    private static InteractionInvocationResult PollResult(SystemTaskLifecycleSnapshot snapshot,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits = null) => snapshot.State switch
    {
        SystemTaskLifecycleState.Completed when snapshot.ResultJson is not null && snapshot.CompletionEvidenceReference is not null =>
            InteractionInvocationResult.CompletedComputation(snapshot.ResultJson, snapshot.CompletionEvidenceReference, previousCommits),
        SystemTaskLifecycleState.Queued or SystemTaskLifecycleState.Running or SystemTaskLifecycleState.Waiting or SystemTaskLifecycleState.Retry =>
            InteractionInvocationResult.Pending(snapshot.Request.Handle, previousCommits),
        SystemTaskLifecycleState.Cancelled => InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED", "The task acknowledged cancellation.", previousCommits),
        SystemTaskLifecycleState.Failed => InteractionInvocationResult.Failed(snapshot.ErrorCode ?? "SYSTEM_TASK_FAILED", "The task failed.", previousCommits),
        _ => InteractionInvocationResult.Unavailable("SYSTEM_TASK_RECOVERY_REQUIRED", "The task requires recovery before its outcome can be established.")
    };

    private sealed record HostCallReconciliation(bool Reconciled,
        IReadOnlyList<InteractionInvocationCommitReceipt> PreviousCommits,
        ApplicationEcsExecutionIdentity? RecoveryIdentity,
        string? CompletionEvidenceReference);

    private static InteractionInvocationResult NotAuthorized() => InteractionInvocationResult.Failed(
        "SYSTEM_TASK_NOT_AUTHORIZED", "The durable task is not available to this caller in this scope.");

    private static InteractionInvocationResult Cancelled() => InteractionInvocationResult.Cancelled(
        "SYSTEM_TASK_CANCELLED", "The durable task request was cancelled or its deadline elapsed.");
}
