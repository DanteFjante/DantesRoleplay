using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteObservationTriggerStatusReader(DantesRoleplayDbContext db)
    : IObservationTriggerStatusReader
{
    public async Task<ObservationTriggerStatusView?> GetAsync(ApplicationIdentifier applicationId,
        string triggerId, int? version = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        triggerId = OneTimeTriggerDefinition.Create(applicationId, triggerId, 1,
            DateTimeOffset.UnixEpoch, TriggerMisfirePolicy.FireOnce).Id;
        if (version is < 1) throw new TriggerSchedulingContractException("INVALID_TRIGGER_VERSION",
            "The trigger version must be positive.");
        var current = await db.ObservationTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId, cancellationToken);
        if (current is null) return null;
        var wanted = version ?? current.CurrentVersion;
        var definition = await db.ObservationTriggers.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId && value.Version == wanted,
            cancellationToken);
        return definition is null ? null : await ProjectAsync(definition, current.CurrentVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<ObservationTriggerStatusView>> ListAsync(
        ApplicationIdentifier applicationId, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        limit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var definitions = await (from current in db.ObservationTriggerCurrent.AsNoTracking()
            join definition in db.ObservationTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.ApplicationId == applicationId.Value
            orderby definition.Id
            select definition).Take(limit).ToListAsync(cancellationToken);
        var result = new List<ObservationTriggerStatusView>(definitions.Count);
        foreach (var definition in definitions)
            result.Add(await ProjectAsync(definition, definition.Version, cancellationToken));
        return result;
    }

    private async Task<ObservationTriggerStatusView> ProjectAsync(ObservationTriggerRecord definition,
        int currentVersion, CancellationToken cancellationToken)
    {
        var current = definition.Version == currentVersion;
        var sourceCurrent = current && await db.TriggerObservationSourceCurrent.AsNoTracking().AnyAsync(value =>
            value.ApplicationId == definition.ApplicationId && value.Id == definition.SourceId &&
            value.CurrentVersion == definition.SourceVersion, cancellationToken);
        var structureCurrent = current && await db.TriggerObservationStructureCurrent.AsNoTracking().AnyAsync(value =>
            value.ApplicationId == definition.ApplicationId && value.Id == definition.StructureId &&
            value.CurrentVersion == definition.StructureVersion, cancellationToken);
        var work = await db.ObservationTriggerMatchWork.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version &&
                (value.State == "ready" || value.State == "retry" || value.State == "leased"))
            .OrderByDescending(value => value.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var failed = work is null ? await db.ObservationTriggerMatchWork.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version && value.State == "failed")
            .OrderByDescending(value => value.UpdatedAtUtc).FirstOrDefaultAsync(cancellationToken) : null;
        var receipt = await db.ObservationTriggerMatchReceipts.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version)
            .OrderByDescending(value => value.RecordedAtUtc).ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var notification = await db.ObservationTriggerNotificationLinks.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version)
            .OrderByDescending(value => value.CreatedAtUtc).Select(value => value.NotificationId)
            .FirstOrDefaultAsync(cancellationToken);
        var status = !current ? ObservationTriggerStatus.Superseded
            : definition.Lifecycle == "paused" ? ObservationTriggerStatus.Paused
            : definition.Lifecycle == "cancelled" ? ObservationTriggerStatus.Cancelled
            : !sourceCurrent ? ObservationTriggerStatus.StaleSource
            : !structureCurrent ? ObservationTriggerStatus.StaleStructure
            : ObservationTriggerStatus.Active;
        return new ObservationTriggerStatusView(ApplicationIdentifier.Parse(definition.ApplicationId),
            definition.Id, definition.Version, status, receipt?.ObservationId, receipt?.Disposition,
            notification, work?.AttemptCount ?? failed?.AttemptCount ?? 0,
            work?.FailureKind ?? failed?.FailureKind);
    }
}
