using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteRecurringTriggerStatusReader(
    DantesRoleplayDbContext db,
    ITriggerClock clock) : IRecurringTriggerStatusReader
{
    public async Task<RecurringTriggerStatusView?> GetAsync(
        ApplicationIdentifier applicationId,
        string triggerId,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        triggerId = OneTimeTriggerDefinition.Create(applicationId, triggerId, 1,
            DateTimeOffset.UnixEpoch, TriggerMisfirePolicy.FireOnce).Id;
        if (version is < 1)
            throw new TriggerSchedulingContractException("INVALID_TRIGGER_VERSION",
                "The trigger version must be positive.");
        var current = await db.RecurringTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId, cancellationToken);
        if (current is null) return null;
        var wanted = version ?? current.CurrentVersion;
        var definition = await db.RecurringTriggers.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId &&
            value.Version == wanted, cancellationToken);
        return definition is null ? null : await ProjectAsync(definition, current.CurrentVersion,
            cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringTriggerStatusView>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        limit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var definitions = await (from current in db.RecurringTriggerCurrent.AsNoTracking()
            join definition in db.RecurringTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.ApplicationId == applicationId.Value
            orderby definition.Id
            select definition).Take(limit).ToListAsync(cancellationToken);
        var result = new List<RecurringTriggerStatusView>(definitions.Count);
        foreach (var definition in definitions)
            result.Add(await ProjectAsync(definition, definition.Version, cancellationToken));
        return result;
    }

    private async Task<RecurringTriggerStatusView> ProjectAsync(
        RecurringTriggerRecord definition,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        var isCurrent = definition.Version == currentVersion;
        var state = isCurrent
            ? await db.RecurringTriggerState.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.CurrentVersion == definition.Version, cancellationToken)
            : null;
        var work = await db.RecurringTriggerFireWork.AsNoTracking()
            .Where(value => value.ApplicationId == definition.ApplicationId &&
                value.TriggerId == definition.Id && value.TriggerVersion == definition.Version &&
                (value.State == "ready" || value.State == "retry" || value.State == "leased"))
            .OrderByDescending(value => value.OccurrenceAtUtc).FirstOrDefaultAsync(cancellationToken);
        var notificationId = await db.RecurringTriggerNotificationLinks.AsNoTracking()
            .Where(value => value.ApplicationId == definition.ApplicationId &&
                value.TriggerId == definition.Id && value.TriggerVersion == definition.Version)
            .OrderByDescending(value => value.OccurrenceAtUtc).Select(value => value.NotificationId)
            .FirstOrDefaultAsync(cancellationToken);
        var next = state?.NextOccurrenceAtUtc is { } nextUtc
            ? new DateTimeOffset(DateTime.SpecifyKind(nextUtc, DateTimeKind.Utc)) : (DateTimeOffset?)null;
        var status = !isCurrent ? RecurringTriggerStatus.Superseded
            : definition.Lifecycle == "paused" ? RecurringTriggerStatus.Paused
            : definition.Lifecycle == "cancelled" ? RecurringTriggerStatus.Cancelled
            : next is null ? RecurringTriggerStatus.Completed
            : next <= UtcNow() ? RecurringTriggerStatus.Due
            : RecurringTriggerStatus.Scheduled;
        return new RecurringTriggerStatusView(ApplicationIdentifier.Parse(definition.ApplicationId),
            definition.Id, definition.Version, status, next,
            state?.LastOccurrenceAtUtc is { } last
                ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)) : null,
            state?.LastDisposition, state?.LastFailureKind, notificationId,
            work?.AttemptCount ?? 0, work?.FailureKind);
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC",
                "The trigger clock must use UTC.");
        return now;
    }
}
