using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.TriggerScheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemTaskTriggerAdmissionDecision(bool Accepted, bool Transient, string Code)
{
    internal static SystemTaskTriggerAdmissionDecision Allow() => new(true, false, "SYSTEM_TASK_TRIGGER_AUTHORIZED");
    internal static SystemTaskTriggerAdmissionDecision Deny(string code, bool transient = false) => new(false, transient, code);
}

internal sealed record SystemTaskTriggerCausalReservationRequest(
    string CausalAllowanceId, string FireId, int MaximumOperationsPerFire);

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

    internal Task<SystemTaskTriggerAdmissionDecision> StageTriggerAsync(
        SystemInnerWorkerProcedurePreparationResult preparation,
        CancellationToken cancellationToken = default) =>
        StageTriggerAsync(preparation, null, cancellationToken);

    internal async Task<SystemTaskTriggerAdmissionDecision> StageTriggerAsync(
        SystemInnerWorkerProcedurePreparationResult preparation,
        SystemTaskTriggerCausalReservationRequest? causalReservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var worker = preparation.Worker;
        if (worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow)
            return SystemTaskTriggerAdmissionDecision.Deny("INNER_WORKER_SUBJECT_UNSUPPORTED");
        var request = new SystemTaskDurableSubmissionRequest(worker.InvocationHost,
            workflow.ProcedureVersion, worker.InputJson, dependencyHandles: worker.DependencyHandles);
        if (db.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction transaction ||
            db.Database.GetDbConnection() is not SqliteConnection connection ||
            !ReferenceEquals(transaction.Connection, connection))
            return SystemTaskTriggerAdmissionDecision.Deny("SYSTEM_TASK_TRIGGER_TRANSACTION_REQUIRED", true);
        if (request.InvocationHost.Budget.DeadlineUtc <= timeProvider.GetUtcNow().UtcDateTime)
            return SystemTaskTriggerAdmissionDecision.Deny("SYSTEM_TASK_DEADLINE_EXPIRED");
        if (!CurrentScope(request.InvocationHost))
            return SystemTaskTriggerAdmissionDecision.Deny("INVOCATION_SCOPE_STALE");
        var probeHost = WithMaximumOperations(request.InvocationHost, 1);
        var authorization = await AuthorizeCoreAsync(probeHost, request.SelectedDefinition,
            DantesRoleplay.Authorization.StandingGrantCapability.Execute, null, null, cancellationToken);
        if (authorization.Failure is not null || authorization.CurrentActivation is null ||
            authorization.Decision?.Grant is not { } grant)
            return SystemTaskTriggerAdmissionDecision.Deny(
                authorization.Failure?.Code ?? "SYSTEM_TASK_TARGET_UNAVAILABLE",
                authorization.Failure?.Tag == InteractionInvocationResultTag.Unavailable);
        var maximumOperations = causalReservation is null
            ? request.InvocationHost.Budget.MaximumOperations
            : Math.Min(causalReservation.MaximumOperationsPerFire, grant.MaximumOperations);
        if (maximumOperations != request.InvocationHost.Budget.MaximumOperations)
        {
            var narrowedHost = WithMaximumOperations(request.InvocationHost, maximumOperations);
            var narrowedWorker = new SystemInnerWorkerRequest(narrowedHost, workflow.ProcedureVersion,
                worker.InputJson, worker.ResultSchemaJson, worker.DependencyHandles);
            preparation = preparation with { Worker = narrowedWorker };
            worker = narrowedWorker;
            request = new SystemTaskDurableSubmissionRequest(narrowedHost, workflow.ProcedureVersion,
                narrowedWorker.InputJson, dependencyHandles: narrowedWorker.DependencyHandles);
        }
        var profile = preparation.BindAuthority(new(
            grant.GrantReference, grant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            grant.ContentFingerprint));
        var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, timeProvider);
        var savepoint = "trigger_causal_admission_" + Guid.NewGuid().ToString("N");
        await transaction.SaveAsync(savepoint, cancellationToken);
        InteractionInvocationResult staged;
        try
        {
            if (causalReservation is not null)
            {
                var reservation = await StageCausalReservationAsync(causalReservation,
                    request.InvocationHost.CommandId, maximumOperations, cancellationToken);
                if (!reservation.Accepted)
                {
                    await transaction.RollbackAsync(savepoint, CancellationToken.None);
                    await transaction.ReleaseAsync(savepoint, CancellationToken.None);
                    return reservation;
                }
            }
            staged = await StageInnerWorkerAsync(request, profile, authorization.CurrentActivation,
                allowEphemeralParent: false, store, connection, transaction, cancellationToken);
            if (staged.Tag != InteractionInvocationResultTag.Pending)
                await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
        }
        catch
        {
            await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
            throw;
        }
        return staged.Tag switch
        {
            InteractionInvocationResultTag.Pending => SystemTaskTriggerAdmissionDecision.Allow(),
            InteractionInvocationResultTag.Unavailable =>
                SystemTaskTriggerAdmissionDecision.Deny(staged.Code, transient: true),
            _ => SystemTaskTriggerAdmissionDecision.Deny(staged.Code)
        };
    }

    private async Task<SystemTaskTriggerAdmissionDecision> StageCausalReservationAsync(
        SystemTaskTriggerCausalReservationRequest request, string commandId, int operations,
        CancellationToken cancellationToken)
    {
        if (request.MaximumOperationsPerFire is < 1 or > 16 || operations is < 1 or > 16)
            return SystemTaskTriggerAdmissionDecision.Deny("TRIGGER_CAUSAL_RESERVATION_INVALID");
        var canonical = InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
        {
            request.CausalAllowanceId, request.FireId, request.MaximumOperationsPerFire, commandId
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/trigger-causal-reservation/v1", canonical);
        var existing = await db.TriggerCausalReservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CausalAllowanceId == request.CausalAllowanceId && value.FireId == request.FireId,
            cancellationToken);
        if (existing is not null)
            return existing.CommandId == commandId && existing.RequestFingerprint == fingerprint &&
                   existing.Operations == operations
                ? SystemTaskTriggerAdmissionDecision.Allow()
                : SystemTaskTriggerAdmissionDecision.Deny("TRIGGER_CAUSAL_RESERVATION_CONFLICT");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_causal_allowance
            SET ReservedOperations = ReservedOperations + {operations}, UpdatedAtUtc = {now}
            WHERE Id = {request.CausalAllowanceId}
              AND ReservedOperations + {operations} <= MaximumOperations
            """, cancellationToken);
        if (changed != 1)
            return SystemTaskTriggerAdmissionDecision.Deny("TRIGGER_CAUSAL_BUDGET_EXHAUSTED");
        var trackedAllowance = db.ChangeTracker.Entries<TriggerCausalAllowanceRecord>()
            .SingleOrDefault(value => value.Entity.Id == request.CausalAllowanceId);
        if (trackedAllowance is not null)
            await trackedAllowance.ReloadAsync(cancellationToken);
        db.TriggerCausalReservations.Add(new TriggerCausalReservationRecord
        {
            CausalAllowanceId = request.CausalAllowanceId, FireId = request.FireId,
            Operations = operations, CommandId = commandId, RequestFingerprint = fingerprint,
            ReservedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return SystemTaskTriggerAdmissionDecision.Allow();
    }

    private static InteractionInvocationHost WithMaximumOperations(
        InteractionInvocationHost host, int maximumOperations) =>
        new(host.Principal, host.ApplicationRevision, host.StateSpaceId!, host.GrantReference,
            host.CommandId, host.StateRevision!, host.Profile,
            new InteractionInvocationBudget(maximumOperations, host.Budget.DeadlineUtc),
            host.ParentCommandId);

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
