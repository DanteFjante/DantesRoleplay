using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteTriggerScheduleStatusReader(
    DantesRoleplayDbContext db,
    ITriggerClock clock) : ITriggerScheduleStatusReader
{
    public async Task<TriggerScheduleStatusView?> GetAsync(
        ApplicationIdentifier applicationId,
        string triggerId,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        triggerId = OneTimeTriggerDefinition.Create(applicationId, triggerId, 1,
            DateTimeOffset.UnixEpoch, TriggerMisfirePolicy.FireOnce).Id;
        if (version is < 1)
            throw new TriggerSchedulingContractException("INVALID_TRIGGER_VERSION", "The trigger version must be positive.");
        var current = await db.OneTimeTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId, cancellationToken);
        if (current is null) return null;
        var wantedVersion = version ?? current.CurrentVersion;
        var definition = await db.OneTimeTriggers.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId &&
            value.Version == wantedVersion, cancellationToken);
        return definition is null ? null : await ProjectAsync(definition, current.CurrentVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<TriggerScheduleStatusView>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        limit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var definitions = await (
            from current in db.OneTimeTriggerCurrent.AsNoTracking()
            join definition in db.OneTimeTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.ApplicationId == applicationId.Value
            orderby definition.DueAtUtc, definition.Id
            select definition).Take(limit).ToListAsync(cancellationToken);
        var result = new List<TriggerScheduleStatusView>(definitions.Count);
        foreach (var definition in definitions)
            result.Add(await ProjectAsync(definition, definition.Version, cancellationToken));
        return result;
    }

    private async Task<TriggerScheduleStatusView> ProjectAsync(
        OneTimeTriggerRecord definition,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        var fireId = FireId(definition);
        var receipt = await db.TriggerFireReceipts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == fireId, cancellationToken);
        var work = await db.TriggerFireWork.AsNoTracking()
            .SingleOrDefaultAsync(value => value.FireId == fireId, cancellationToken);
        var notificationId = await db.TriggerNotificationLinks.AsNoTracking()
            .Where(value => value.FireId == fireId)
            .Select(value => value.NotificationId)
            .SingleOrDefaultAsync(cancellationToken);
        var status = definition.Version != currentVersion
            ? TriggerScheduleStatus.Superseded
            : definition.Lifecycle == "cancelled"
                ? TriggerScheduleStatus.Cancelled
                : receipt?.Disposition == "due" || work?.State == "completed"
                    ? TriggerScheduleStatus.Completed
                    : receipt?.Disposition == "missed" || work?.State == "missed"
                        ? TriggerScheduleStatus.Missed
                        : UtcNow() >= new DateTimeOffset(definition.DueAtUtc, TimeSpan.Zero)
                            ? TriggerScheduleStatus.Due
                            : TriggerScheduleStatus.Scheduled;
        return new(
            ApplicationIdentifier.Parse(definition.ApplicationId), definition.Id, definition.Version,
            new DateTimeOffset(definition.DueAtUtc, TimeSpan.Zero), status,
            receipt?.Id ?? (work is null ? null : fireId), notificationId,
            work?.AttemptCount ?? 0, work?.FailureKind);
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }

    private static string FireId(OneTimeTriggerRecord definition) =>
        TriggerSchedulingFingerprint.Fire(OneTimeTriggerDefinition.Create(
            ApplicationIdentifier.Parse(definition.ApplicationId), definition.Id, definition.Version,
            new DateTimeOffset(definition.DueAtUtc, TimeSpan.Zero),
            definition.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
            TriggerFireTarget.NotificationOnly,
            definition.Lifecycle == "active" ? TriggerLifecycle.Active : TriggerLifecycle.Cancelled));
}
