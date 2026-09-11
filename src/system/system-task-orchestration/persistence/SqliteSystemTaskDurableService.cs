using System.Data;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
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
    public Task<InteractionInvocationResult> SubmitAsync(SystemTaskDurableSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InvocationHost.Profile != InteractionExecutionProfile.Workflow)
            return Task.FromResult(InteractionInvocationResult.Unavailable("SYSTEM_TASK_PROFILE_UNSUPPORTED",
                "Durable tasks require the workflow execution profile."));
        return InOwnedTransactionAsync(request.InvocationHost, write: true, async (store, connection, transaction) =>
        {
            var denied = await AuthorizeAsync(request.InvocationHost, request.SelectedDefinition,
                StandingGrantCapability.Execute, null, cancellationToken);
            if (denied is not null) return denied;
            var staged = await store.StageEnqueueAsync(request, false, connection, transaction, cancellationToken);
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
            var denied = await AuthorizeAsync(invocationHost, snapshot.Request.SelectedDefinition,
                StandingGrantCapability.ReadTask, TaskTarget(snapshot.Request), cancellationToken);
            if (denied is not null) return denied;

            // Journal payloads are inert diagnostics. Until the runtime supplies authoritative
            // receipt reconciliation, no outcome may silently drop or promote earlier effects.
            if (await HasHostCallsAsync(handle, connection, transaction, cancellationToken))
                return InteractionInvocationResult.Unavailable("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE",
                    "The task requires authoritative host-call receipt reconciliation.");
            if (await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(connection, transaction, handle.TaskId, cancellationToken))
                return InteractionInvocationResult.Unavailable("INNER_AI_RECONCILIATION_REQUIRED", "AI usage must be reconciled before returning a task outcome.");
            return PollResult(snapshot);
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
            var denied = await AuthorizeAsync(invocationHost, snapshot.Request.SelectedDefinition,
                StandingGrantCapability.CancelTask, TaskTarget(snapshot.Request), cancellationToken);
            if (denied is not null) return denied;
            var affected = await store.ReadCancellationTargetsAsync(handle, true, connection, transaction, cancellationToken);
            foreach (var child in affected.Where(value => value.Request.Handle != handle))
            {
                if (!TaskScopeMatches(invocationHost, child.Request)) return NotAuthorized();
                denied = await AuthorizeAsync(invocationHost, child.Request.SelectedDefinition,
                    StandingGrantCapability.CancelTask, TaskTarget(child.Request), cancellationToken);
                if (denied is not null) return denied;
            }
            await store.StageCancellationAsync(handle, true, connection, transaction, cancellationToken);
            var updated = (await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken))!;
            if (await HasHostCallsAsync(handle, connection, transaction, cancellationToken))
                return InteractionInvocationResult.Unavailable("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE",
                    "Cancellation was processed; the task still requires authoritative host-call receipt reconciliation.");
            if (await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(connection, transaction, handle.TaskId, cancellationToken))
                return InteractionInvocationResult.Unavailable("INNER_AI_RECONCILIATION_REQUIRED", "Cancellation was processed; AI usage still requires reconciliation.");
            return updated.State switch
            {
                SystemTaskLifecycleState.Cancelled => InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED",
                    "The task acknowledged cancellation."),
                SystemTaskLifecycleState.Running => InteractionInvocationResult.Pending(handle),
                // CancelTask alone does not permit disclosing an already completed result.
                _ => InteractionInvocationResult.Failed("SYSTEM_TASK_NOT_CANCELLABLE",
                    "The task is already terminal and cannot acknowledge a new cancellation request.")
            };
        }, cancellationToken);
    }

    private async Task<InteractionInvocationResult> InOwnedTransactionAsync(InteractionInvocationHost host, bool write,
        Func<SqliteSystemTaskLifecycleStore, SqliteConnection, SqliteTransaction, Task<InteractionInvocationResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
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
            var currentState = stateSpaces.Get(host.StateSpaceId);
            if (currentState is null || currentState.ApplicationRevision.ApplicationId != host.ApplicationRevision.ApplicationId
                || currentState.ApplicationRevision.Revision != host.ApplicationRevision.Revision
                || currentState.ApplicationRevision.Fingerprint != host.ApplicationRevision.Fingerprint
                || !currentState.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)
                || InteractionStateRevision.From(currentState) != host.StateRevision)
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
        StandingGrantTaskTarget? task, CancellationToken cancellationToken)
    {
        var resolution = await targets.ResolveAsync(host,
            new(selection.ExactDefinitionId, "procedure", selection.Version, selection.Fingerprint), cancellationToken);
        if (resolution.Status == StandingGrantTargetResolutionStatus.Denied) return NotAuthorized();
        if (resolution.Status != StandingGrantTargetResolutionStatus.Available || resolution.Target is not { } target)
            return InteractionInvocationResult.Unavailable("SYSTEM_TASK_TARGET_UNAVAILABLE", "The selected task definition cannot currently be resolved.");
        if (target.DefinitionId != selection.ExactDefinitionId || target.Revision != selection.Version
            || target.ContentFingerprint != selection.Fingerprint || target.Kind != "procedure"
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId)
            return InteractionInvocationResult.Unavailable("SYSTEM_TASK_TARGET_UNAVAILABLE", "The selected task definition cannot currently be resolved.");
        var requirement = new StandingGrantRequirement(capability, StandingGrantScope.StateSpace, [target], [], task);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        var decision = await policy.EvaluateAsync(host, requirement, cancellationToken);
        if (!decision.Allowed)
            return decision.Code.Contains("UNAVAILABLE", StringComparison.Ordinal)
                ? InteractionInvocationResult.Unavailable("SYSTEM_TASK_AUTHORIZATION_UNAVAILABLE", "Current task authorization is unavailable.")
                : NotAuthorized();
        if (decision.Grant is not { } grant || grant.GrantReference != host.GrantReference
            || grant.PrincipalReference != host.Principal.PrincipalId || grant.ApplicationId != host.ApplicationRevision.ApplicationId
            || grant.Scope != StandingGrantScope.StateSpace || grant.StateSpaceId != host.StateSpaceId)
            return NotAuthorized();
        return null;
    }

    private static bool TaskScopeMatches(InteractionInvocationHost host, SystemTaskStoredRequest task) =>
        task.Invocation.PrincipalReference == host.Principal.PrincipalId
        && task.Invocation.ApplicationId == host.ApplicationRevision.ApplicationId.Value
        && task.Invocation.StateSpaceId == host.StateSpaceId;

    private static StandingGrantTaskTarget TaskTarget(SystemTaskStoredRequest task) => new(task.Handle,
        task.Invocation.PrincipalReference, ApplicationIdentifier.Parse(task.Invocation.ApplicationId),
        task.Invocation.StateSpaceId, task.SelectedDefinition);

    private static async Task<bool> HasHostCallsAsync(SystemTaskDurableHandle handle, SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM system_task_host_call WHERE task_id = $task)";
        command.Parameters.AddWithValue("$task", handle.TaskId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static InteractionInvocationResult PollResult(SystemTaskLifecycleSnapshot snapshot) => snapshot.State switch
    {
        SystemTaskLifecycleState.Completed when snapshot.ResultJson is not null && snapshot.CompletionEvidenceReference is not null =>
            InteractionInvocationResult.CompletedComputation(snapshot.ResultJson, snapshot.CompletionEvidenceReference),
        SystemTaskLifecycleState.Queued or SystemTaskLifecycleState.Running or SystemTaskLifecycleState.Waiting or SystemTaskLifecycleState.Retry =>
            InteractionInvocationResult.Pending(snapshot.Request.Handle),
        SystemTaskLifecycleState.Cancelled => InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED", "The task acknowledged cancellation."),
        SystemTaskLifecycleState.Failed => InteractionInvocationResult.Failed(snapshot.ErrorCode ?? "SYSTEM_TASK_FAILED", "The task failed."),
        _ => InteractionInvocationResult.Unavailable("SYSTEM_TASK_RECOVERY_REQUIRED", "The task requires recovery before its outcome can be established.")
    };

    private static InteractionInvocationResult NotAuthorized() => InteractionInvocationResult.Failed(
        "SYSTEM_TASK_NOT_AUTHORIZED", "The durable task is not available to this caller in this scope.");

    private static InteractionInvocationResult Cancelled() => InteractionInvocationResult.Cancelled(
        "SYSTEM_TASK_CANCELLED", "The durable task request was cancelled or its deadline elapsed.");
}
