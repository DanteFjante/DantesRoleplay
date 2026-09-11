using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

/// <summary>
/// Routes each closed trigger target inside the worker-owned transaction. Workflow enqueue and
/// trigger evidence commit together; notification handling remains with its existing owner.
/// </summary>
internal sealed class SystemTaskTriggerTransactionParticipant(
    DantesRoleplayDbContext db,
    TriggerNotificationTransactionParticipant notifications,
    SqliteSystemTaskDurableService durableTasks,
    ISystemInnerWorkerProcedureResolver procedureWorkers) : ITriggerFireTransactionParticipant
{
    public bool IsAvailable => notifications.IsAvailable;

    public Task<TriggerFireAttemptResult> StageAsync(TriggerFireLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return lease.Target == TriggerFireTarget.NotificationOnly
            ? notifications.StageAsync(lease, cancellationToken)
            : StageWorkflowAsync(lease, cancellationToken);
    }

    private async Task<TriggerFireAttemptResult> StageWorkflowAsync(TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.Target != TriggerFireTarget.ProcedureWorkflow || lease.AdmittedAt is not { } admittedAt)
            return TriggerFireAttemptResult.Permanent();
        try
        {
            var target = lease.ScheduleKind switch
            {
                TriggerScheduleKind.OneTime => await OneTimeAsync(lease, admittedAt, cancellationToken),
                TriggerScheduleKind.Observation => await ObservationAsync(lease, admittedAt, cancellationToken),
                _ => null
            };
            if (target is null)
                return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
            var request = new SystemInnerWorkerRequest(target.Submission.InvocationHost,
                target.Submission.SelectedDefinition, target.Submission.InputJson,
                target.ResultSchemaJson, target.Submission.DependencyHandles);
            var preparation = await procedureWorkers.ResolveAsync(request, cancellationToken);
            var staged = await durableTasks.StageTriggerAsync(preparation, cancellationToken);
            return staged.Accepted
                ? TriggerFireAttemptResult.Succeeded()
                : staged.Transient
                    ? TriggerFireAttemptResult.Transient(TriggerFireFailureKind.HandlerUnavailable)
                    : TriggerFireAttemptResult.Permanent();
        }
        catch (OperationCanceledException) { throw; }
        catch (Microsoft.Data.Sqlite.SqliteException) { return TriggerFireAttemptResult.Transient(TriggerFireFailureKind.TransientDatabase); }
        catch (DbUpdateException) { return TriggerFireAttemptResult.Transient(TriggerFireFailureKind.TransientDatabase); }
        catch { return TriggerFireAttemptResult.Permanent(); }
    }

    private async Task<SystemTaskDurableTriggerTarget?> OneTimeAsync(TriggerFireLease lease,
        DateTimeOffset admittedAt, CancellationToken cancellationToken)
    {
        var current = await db.OneTimeTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId, cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion) return null;
        var row = await db.OneTimeTriggers.AsNoTracking().Include(value => value.WorkflowBinding)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        if (row is null || row.Target != "procedure-workflow" || row.Lifecycle != "active" ||
            row.DueAtUtc != lease.OccurrenceAt.UtcDateTime || row.WorkflowBinding is null)
            return null;
        return TriggerProcedureWorkflowBindingPersistence.Materialize(row.Id, row.Version, lease.FireId,
            admittedAt, row.WorkflowBinding);
    }

    private async Task<SystemTaskDurableTriggerTarget?> ObservationAsync(TriggerFireLease lease,
        DateTimeOffset admittedAt, CancellationToken cancellationToken)
    {
        if (lease.ObservationId is null) return null;
        var current = await db.ObservationTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId, cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion) return null;
        var row = await db.ObservationTriggers.AsNoTracking().Include(value => value.WorkflowBinding)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        var observation = await db.TriggerObservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == lease.ObservationId && value.ApplicationId == lease.ApplicationId.Value,
            cancellationToken);
        if (row is null || row.Target != "procedure-workflow" || row.Lifecycle != "active" ||
            row.WorkflowBinding is null || observation is null || observation.SourceId != row.SourceId ||
            observation.SourceVersion != row.SourceVersion || observation.StructureId != row.StructureId ||
            observation.StructureVersion != row.StructureVersion || observation.StructureHash != row.StructureHash)
            return null;
        return TriggerProcedureWorkflowBindingPersistence.Materialize(row.Id, row.Version, lease.FireId,
            admittedAt, row.WorkflowBinding);
    }
}
