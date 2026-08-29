using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

/// <summary>
/// The only scheduled-notification content writer. It stages rows in the worker's scoped context;
/// the worker remains the root transaction and commits the receipt/work state with these rows.
/// </summary>
internal sealed class TriggerNotificationTransactionParticipant(
    DantesRoleplayDbContext db,
    ITriggerClock clock) : ITriggerFireTransactionParticipant
{
    public bool IsAvailable => true;

    public async Task<TriggerFireAttemptResult> StageAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Target != TriggerFireTarget.NotificationOnly)
            return TriggerFireAttemptResult.Permanent();
        return lease.ScheduleKind switch
        {
            TriggerScheduleKind.Recurring => await StageRecurringAsync(lease, cancellationToken),
            TriggerScheduleKind.Conditional => await StageConditionalAsync(lease, cancellationToken),
            TriggerScheduleKind.Observation => await StageObservationAsync(lease, cancellationToken),
            _ => await StageOneTimeAsync(lease, cancellationToken)
        };
    }

    private async Task<TriggerFireAttemptResult> StageObservationAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.ObservationId is null) return TriggerFireAttemptResult.Permanent();
        var current = await db.ObservationTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId,
            cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        var stored = await db.ObservationTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        var observation = await db.TriggerObservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == lease.ObservationId && value.ApplicationId == lease.ApplicationId.Value,
            cancellationToken);
        if (stored is null || stored.Lifecycle != "active" || stored.Target != "notification-only" ||
            observation is null || observation.SourceId != stored.SourceId ||
            observation.SourceVersion != stored.SourceVersion || observation.StructureId != stored.StructureId ||
            observation.StructureVersion != stored.StructureVersion ||
            observation.StructureHash != stored.StructureHash)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        TriggerNotificationTarget target;
        try
        {
            target = TriggerNotificationTarget.Create(stored.NotificationTopic, stored.NotificationSubject,
                stored.NotificationBody, stored.NotificationStateSpaceId,
                stored.NotificationEntities.OrderBy(value => value.Ordinal).Select(value => value.EntityId).ToArray());
        }
        catch (TriggerSchedulingContractException) { return TriggerFireAttemptResult.Permanent(); }
        if (!await LinksAreCurrentAsync(lease, target, cancellationToken))
            return TriggerFireAttemptResult.Permanent();
        return await StageNotificationAsync(lease, target, recurring: false, cancellationToken);
    }

    private async Task<TriggerFireAttemptResult> StageOneTimeAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        var current = await db.OneTimeTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId,
            cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);

        var trigger = await db.OneTimeTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion,
                cancellationToken);
        if (trigger is null || trigger.Lifecycle != "active" || trigger.Target != "notification-only" ||
            trigger.DueAtUtc != lease.OccurrenceAt.UtcDateTime)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);

        TriggerNotificationTarget target;
        try
        {
            target = TriggerNotificationTarget.Create(
                trigger.NotificationTopic,
                trigger.NotificationSubject,
                trigger.NotificationBody,
                trigger.NotificationStateSpaceId,
                trigger.NotificationEntities.OrderBy(value => value.Ordinal)
                    .Select(value => value.EntityId).ToArray());
        }
        catch (TriggerSchedulingContractException)
        {
            return TriggerFireAttemptResult.Permanent();
        }

        if (!await LinksAreCurrentAsync(lease, target, cancellationToken))
            return TriggerFireAttemptResult.Permanent();

        return await StageNotificationAsync(lease, target, recurring: false, cancellationToken);
    }

    private async Task<TriggerFireAttemptResult> StageRecurringAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        var current = await db.RecurringTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId,
            cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        var stored = await db.RecurringTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion,
                cancellationToken);
        if (stored is null || stored.Lifecycle != "active" || stored.Target != "notification-only")
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        TriggerNotificationTarget target;
        RecurringTriggerDefinition definition;
        try
        {
            target = TriggerNotificationTarget.Create(stored.NotificationTopic, stored.NotificationSubject,
                stored.NotificationBody, stored.NotificationStateSpaceId,
                stored.NotificationEntities.OrderBy(value => value.Ordinal).Select(value => value.EntityId).ToArray());
            definition = RecurringTriggerDefinition.Create(lease.ApplicationId, stored.Id, stored.Version,
                SqliteTriggerSchedulingStore.Pattern(stored), RecurringTriggerLifecycle.Active,
                stored.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
                TriggerFireTarget.NotificationOnly, target);
        }
        catch (TriggerSchedulingContractException)
        {
            return TriggerFireAttemptResult.Permanent();
        }
        var exact = RecurringScheduleEvaluator.NextOnOrAfter(definition, lease.OccurrenceAt);
        if (exact?.OccurrenceAtUtc != lease.OccurrenceAt ||
            !await LinksAreCurrentAsync(lease, target, cancellationToken))
            return TriggerFireAttemptResult.Permanent();

        return await StageNotificationAsync(lease, target, recurring: true, cancellationToken);
    }

    private async Task<TriggerFireAttemptResult> StageConditionalAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.ChangeOperationId is null) return TriggerFireAttemptResult.Permanent();
        var current = await db.ConditionalTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId,
            cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        var stored = await db.ConditionalTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        var state = await db.ConditionalTriggerState.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.TriggerId == lease.TriggerId &&
            value.CurrentVersion == lease.TriggerVersion, cancellationToken);
        if (stored is null || stored.Lifecycle != "active" || stored.Target != "notification-only" ||
            state?.LastFiredOperationId != lease.ChangeOperationId)
            return TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger);
        TriggerNotificationTarget target;
        try
        {
            target = TriggerNotificationTarget.Create(stored.NotificationTopic, stored.NotificationSubject,
                stored.NotificationBody, stored.NotificationStateSpaceId,
                stored.NotificationEntities.OrderBy(value => value.Ordinal).Select(value => value.EntityId).ToArray());
        }
        catch (TriggerSchedulingContractException) { return TriggerFireAttemptResult.Permanent(); }
        if (!await LinksAreCurrentAsync(lease, target, cancellationToken))
            return TriggerFireAttemptResult.Permanent();
        return await StageNotificationAsync(lease, target, recurring: false, cancellationToken);
    }

    private async Task<TriggerFireAttemptResult> StageNotificationAsync(
        TriggerFireLease lease,
        TriggerNotificationTarget target,
        bool recurring,
        CancellationToken cancellationToken)
    {

        var notificationId = NotificationId(lease.FireId);
        if (await db.Notifications.AsNoTracking().AnyAsync(value => value.Id == notificationId, cancellationToken) ||
            await db.TriggerNotificationLinks.AsNoTracking().AnyAsync(value =>
                value.FireId == lease.FireId || value.NotificationId == notificationId, cancellationToken) ||
            await db.RecurringTriggerNotificationLinks.AsNoTracking().AnyAsync(value =>
                value.FireId == lease.FireId || value.NotificationId == notificationId, cancellationToken) ||
            await db.ConditionalTriggerNotificationLinks.AsNoTracking().AnyAsync(value =>
                value.FireId == lease.FireId || value.NotificationId == notificationId, cancellationToken) ||
            await db.ObservationTriggerNotificationLinks.AsNoTracking().AnyAsync(value =>
                value.FireId == lease.FireId || value.NotificationId == notificationId, cancellationToken))
            return TriggerFireAttemptResult.Permanent();

        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            return TriggerFireAttemptResult.Permanent();
        var notification = new Notification
        {
            Id = notificationId,
            Topic = target.Topic,
            Subject = target.Subject,
            Body = target.Body,
            CorrelationId = notificationId,
            EventId = string.Empty,
            ExecutionId = string.Empty,
            RootOperationId = string.Empty,
            Ordinal = 0,
            CreatedAt = now.UtcDateTime,
            State = NotificationState.Unread
        };
        for (var ordinal = 0; ordinal < target.EntityIds.Count; ordinal++)
        {
            notification.Entities.Add(new NotificationEntity
            {
                NotificationId = notificationId,
                EntityId = target.EntityIds[ordinal],
                Ordinal = ordinal
            });
        }
        db.Notifications.Add(notification);
        if (lease.ScheduleKind == TriggerScheduleKind.Observation)
        {
            db.ObservationTriggerNotificationLinks.Add(new ObservationTriggerNotificationLinkRecord
            {
                FireId = lease.FireId,
                NotificationId = notificationId,
                ApplicationId = lease.ApplicationId.Value,
                TriggerId = lease.TriggerId,
                TriggerVersion = lease.TriggerVersion,
                ObservationId = lease.ObservationId!,
                CreatedAtUtc = now.UtcDateTime
            });
        }
        else if (lease.ScheduleKind == TriggerScheduleKind.Conditional)
        {
            db.ConditionalTriggerNotificationLinks.Add(new ConditionalTriggerNotificationLinkRecord
            {
                FireId = lease.FireId,
                NotificationId = notificationId,
                ApplicationId = lease.ApplicationId.Value,
                TriggerId = lease.TriggerId,
                TriggerVersion = lease.TriggerVersion,
                ChangeOperationId = lease.ChangeOperationId!,
                CreatedAtUtc = now.UtcDateTime
            });
        }
        else if (recurring)
        {
            db.RecurringTriggerNotificationLinks.Add(new RecurringTriggerNotificationLinkRecord
            {
                FireId = lease.FireId,
                NotificationId = notificationId,
                ApplicationId = lease.ApplicationId.Value,
                TriggerId = lease.TriggerId,
                TriggerVersion = lease.TriggerVersion,
                OccurrenceAtUtc = lease.OccurrenceAt.UtcDateTime,
                CreatedAtUtc = now.UtcDateTime
            });
        }
        else
        {
            db.TriggerNotificationLinks.Add(new TriggerNotificationLinkRecord
            {
                FireId = lease.FireId,
                NotificationId = notificationId,
                ApplicationId = lease.ApplicationId.Value,
                TriggerId = lease.TriggerId,
                TriggerVersion = lease.TriggerVersion,
                OccurrenceAtUtc = lease.OccurrenceAt.UtcDateTime,
                CreatedAtUtc = now.UtcDateTime
            });
        }
        return TriggerFireAttemptResult.Succeeded();
    }

    private async Task<bool> LinksAreCurrentAsync(
        TriggerFireLease lease,
        TriggerNotificationTarget target,
        CancellationToken cancellationToken)
    {
        if (target.StateSpaceId is null)
            return target.EntityIds.Count == 0;
        var stateSpace = await db.Set<ApplicationStateSpaceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == target.StateSpaceId, cancellationToken);
        if (stateSpace?.ApplicationId != lease.ApplicationId.Value)
            return false;
        var existing = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == target.StateSpaceId &&
                target.EntityIds.Contains(value.Id) && value.DeletedAtUtc == null)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        return existing.Length == target.EntityIds.Count &&
            existing.ToHashSet(StringComparer.Ordinal).SetEquals(target.EntityIds);
    }

    internal static string NotificationId(string fireId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("trigger-notification\n" + fireId));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}
