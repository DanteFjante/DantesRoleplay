using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteConditionalTriggerStatusReader(DantesRoleplayDbContext db)
    : IConditionalTriggerStatusReader
{
    public async Task<ConditionalTriggerStatusView?> GetAsync(ApplicationIdentifier applicationId,
        string triggerId, int? version = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        triggerId = OneTimeTriggerDefinition.Create(applicationId, triggerId, 1,
            DateTimeOffset.UnixEpoch, TriggerMisfirePolicy.FireOnce).Id;
        if (version is < 1) throw new TriggerSchedulingContractException("INVALID_TRIGGER_VERSION",
            "The trigger version must be positive.");
        var current = await db.ConditionalTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId, cancellationToken);
        if (current is null) return null;
        var wanted = version ?? current.CurrentVersion;
        var definition = await db.ConditionalTriggers.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == triggerId && value.Version == wanted,
            cancellationToken);
        return definition is null ? null : await ProjectAsync(definition, current.CurrentVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<ConditionalTriggerStatusView>> ListAsync(
        ApplicationIdentifier applicationId, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        limit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var definitions = await (from current in db.ConditionalTriggerCurrent.AsNoTracking()
            join definition in db.ConditionalTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.ApplicationId == applicationId.Value
            orderby definition.Id
            select definition).Take(limit).ToListAsync(cancellationToken);
        var result = new List<ConditionalTriggerStatusView>(definitions.Count);
        foreach (var definition in definitions)
            result.Add(await ProjectAsync(definition, definition.Version, cancellationToken));
        return result;
    }

    private async Task<ConditionalTriggerStatusView> ProjectAsync(ConditionalTriggerRecord definition,
        int currentVersion, CancellationToken cancellationToken)
    {
        var isCurrent = definition.Version == currentVersion;
        var state = isCurrent ? await db.ConditionalTriggerState.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
            value.CurrentVersion == definition.Version, cancellationToken) : null;
        var work = await db.ConditionalTriggerFireWork.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version &&
                (value.State == "ready" || value.State == "retry" || value.State == "leased"))
            .OrderByDescending(value => value.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var failed = work is null ? await db.ConditionalTriggerFireWork.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version && value.State == "failed")
            .OrderByDescending(value => value.UpdatedAtUtc).FirstOrDefaultAsync(cancellationToken) : null;
        var notification = await db.ConditionalTriggerNotificationLinks.AsNoTracking().Where(value =>
                value.ApplicationId == definition.ApplicationId && value.TriggerId == definition.Id &&
                value.TriggerVersion == definition.Version)
            .OrderByDescending(value => value.CreatedAtUtc).Select(value => value.NotificationId)
            .FirstOrDefaultAsync(cancellationToken);
        var status = !isCurrent ? ConditionalTriggerStatus.Superseded
            : definition.Lifecycle == "paused" ? ConditionalTriggerStatus.Paused
            : definition.Lifecycle == "cancelled" ? ConditionalTriggerStatus.Cancelled
            : ConditionalTriggerStatus.Active;
        return new ConditionalTriggerStatusView(ApplicationIdentifier.Parse(definition.ApplicationId),
            definition.Id, definition.Version, status, state?.CurrentTruth, state?.Armed ?? false,
            state?.EvaluationRevision ?? 0, state?.LastOperationId, state?.LastFiredOperationId,
            notification, work?.AttemptCount ?? failed?.AttemptCount ?? 0,
            work?.FailureKind ?? failed?.FailureKind);
    }
}
