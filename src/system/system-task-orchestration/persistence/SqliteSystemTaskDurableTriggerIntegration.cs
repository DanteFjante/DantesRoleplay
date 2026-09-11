using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemTaskTriggerAdmissionDecision(bool Accepted, bool Transient, string Code)
{
    internal static SystemTaskTriggerAdmissionDecision Allow() => new(true, false, "SYSTEM_TASK_TRIGGER_AUTHORIZED");
    internal static SystemTaskTriggerAdmissionDecision Deny(string code, bool transient = false) => new(false, transient, code);
}

internal sealed partial class SqliteSystemTaskDurableService
{
    internal async Task<SystemTaskTriggerAdmissionDecision> AuthorizeTriggerBindingAsync(
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition definition,
        ApplicationIdentifier owningApplication,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            return SystemTaskTriggerAdmissionDecision.Deny("SYSTEM_TASK_TRIGGER_TRANSACTION_REQUIRED", true);
        if (host.ApplicationRevision.ApplicationId != owningApplication || !CurrentScope(host))
            return SystemTaskTriggerAdmissionDecision.Deny("INVOCATION_SCOPE_STALE");
        var authorization = await AuthorizeCoreAsync(host, definition,
            DantesRoleplay.Authorization.StandingGrantCapability.Execute, null, null, cancellationToken);
        if (authorization.Failure is null && authorization.CurrentActivation is not null)
            return SystemTaskTriggerAdmissionDecision.Allow();
        return SystemTaskTriggerAdmissionDecision.Deny(
            authorization.Failure?.Code ?? "SYSTEM_TASK_TARGET_UNAVAILABLE",
            authorization.Failure?.Tag == InteractionInvocationResultTag.Unavailable);
    }

    internal async Task<SystemTaskTriggerAdmissionDecision> StageTriggerAsync(
        SystemTaskDurableSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (db.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction transaction ||
            db.Database.GetDbConnection() is not SqliteConnection connection ||
            !ReferenceEquals(transaction.Connection, connection))
            return SystemTaskTriggerAdmissionDecision.Deny("SYSTEM_TASK_TRIGGER_TRANSACTION_REQUIRED", true);
        if (request.InvocationHost.Budget.DeadlineUtc <= timeProvider.GetUtcNow().UtcDateTime)
            return SystemTaskTriggerAdmissionDecision.Deny("SYSTEM_TASK_DEADLINE_EXPIRED");
        if (!CurrentScope(request.InvocationHost))
            return SystemTaskTriggerAdmissionDecision.Deny("INVOCATION_SCOPE_STALE");
        var authorization = await AuthorizeCoreAsync(request.InvocationHost, request.SelectedDefinition,
            DantesRoleplay.Authorization.StandingGrantCapability.Execute, null, null, cancellationToken);
        if (authorization.Failure is not null || authorization.CurrentActivation is null)
            return SystemTaskTriggerAdmissionDecision.Deny(
                authorization.Failure?.Code ?? "SYSTEM_TASK_TARGET_UNAVAILABLE",
                authorization.Failure?.Tag == InteractionInvocationResultTag.Unavailable);
        var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, timeProvider);
        var staged = await store.StageEnqueueAsync(request, false, connection, transaction, cancellationToken,
            authorization.CurrentActivation);
        return staged.Disposition switch
        {
            SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing =>
                SystemTaskTriggerAdmissionDecision.Allow(),
            SystemTaskEnqueueDisposition.Rejected => SystemTaskTriggerAdmissionDecision.Deny(staged.Code),
            _ => SystemTaskTriggerAdmissionDecision.Deny(staged.Code)
        };
    }

    internal async Task<SystemTaskTriggerAdmissionDecision> ReauthorizeExecutionAsync(
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (!CurrentScope(host))
            return SystemTaskTriggerAdmissionDecision.Deny("INVOCATION_SCOPE_STALE");
        var authorization = await AuthorizeCoreAsync(host, definition,
            DantesRoleplay.Authorization.StandingGrantCapability.Execute, null, null, cancellationToken);
        return authorization.Failure is null && authorization.CurrentActivation is not null
            ? SystemTaskTriggerAdmissionDecision.Allow()
            : SystemTaskTriggerAdmissionDecision.Deny(
                authorization.Failure?.Code ?? "SYSTEM_TASK_TARGET_UNAVAILABLE",
                authorization.Failure?.Tag == InteractionInvocationResultTag.Unavailable);
    }

    private bool CurrentScope(InteractionInvocationHost host)
    {
        if (host.StateSpaceId is not { } stateSpaceId || host.StateRevision is not { } stateRevision)
            return false;
        var current = stateSpaces.Get(stateSpaceId);
        return current is not null && current.ApplicationRevision.ApplicationId == host.ApplicationRevision.ApplicationId
            && current.ApplicationRevision.Revision == host.ApplicationRevision.Revision
            && current.ApplicationRevision.Fingerprint == host.ApplicationRevision.Fingerprint
            && current.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)
            && DantesRoleplay.Interactions.InteractionStateRevision.From(current) == stateRevision;
    }
}
