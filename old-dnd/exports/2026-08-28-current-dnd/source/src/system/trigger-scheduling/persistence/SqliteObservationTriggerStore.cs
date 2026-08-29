using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteObservationTriggerStore(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore components,
    IEnumerable<IObservationMatchAdapter> adapters,
    ITriggerClock clock) : IObservationTriggerStore
{
    public async Task<TriggerSchedulingWriteResult<StoredObservationTrigger>> AppendAsync(
        ObservationTriggerDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ResolveAdapter(definition.Adapter).Validate(definition);
        var existing = await ExistingAsync(definition, cancellationToken);
        if (existing is not null)
        {
            var projected = Project(existing);
            return Same(definition, projected)
                ? TriggerSchedulingWriteResult<StoredObservationTrigger>.Replay(projected)
                : TriggerSchedulingWriteResult<StoredObservationTrigger>.Conflict();
        }
        await ValidateScopeAsync(definition, cancellationToken);
        var now = UtcNow();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var current = await db.ObservationTriggerCurrent.SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id,
                cancellationToken);
            if ((current is null && definition.Version != 1) ||
                (current is not null && definition.Version != current.CurrentVersion + 1))
                throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_VERSION_GAP",
                    "Observation trigger revisions must be appended without gaps.");
            var row = Row(definition, now);
            db.ObservationTriggers.Add(row);
            if (current is null)
                db.ObservationTriggerCurrent.Add(new ObservationTriggerCurrentRecord
                { ApplicationId = definition.ApplicationId.Value, Id = definition.Id, CurrentVersion = definition.Version });
            else current.CurrentVersion = definition.Version;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return TriggerSchedulingWriteResult<StoredObservationTrigger>.Appended(Project(row));
        }
        catch (DbUpdateException)
        {
            if (transaction is null) throw;
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { db.ChangeTracker.Clear(); }
            existing = await ExistingAsync(definition, CancellationToken.None);
            if (existing is null) throw;
            var projected = Project(existing);
            return Same(definition, projected)
                ? TriggerSchedulingWriteResult<StoredObservationTrigger>.Replay(projected)
                : TriggerSchedulingWriteResult<StoredObservationTrigger>.Conflict();
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                finally { db.ChangeTracker.Clear(); }
            }
            throw;
        }
    }

    internal IObservationMatchAdapter ResolveAdapter(ObservationMatchAdapterReference reference)
    {
        var matches = adapters.Where(value => value.Id == reference.Id && value.Version == reference.Version).ToArray();
        if (matches.Length != 1) throw new TriggerSchedulingContractException(
            "OBSERVATION_MATCH_ADAPTER_UNAVAILABLE", "The exact reviewed observation matcher is unavailable.");
        return matches[0];
    }

    private async Task ValidateScopeAsync(ObservationTriggerDefinition definition,
        CancellationToken cancellationToken)
    {
        var sourceCurrent = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.SourceId,
            cancellationToken);
        if (sourceCurrent?.CurrentVersion != definition.SourceVersion)
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_SOURCE_STALE",
                "The trigger requires the exact current source revision.");
        var source = await db.TriggerObservationSources.AsNoTracking().Include(value => value.AllowedStructures)
            .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value &&
                value.Id == definition.SourceId && value.Version == definition.SourceVersion, cancellationToken);
        if (source is null || source.Status != "enabled")
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_SOURCE_STALE",
                "The trigger source is missing or disabled.");
        if (!source.AllowedStructures.Any(value => value.StructureId == definition.StructureId &&
                value.StructureVersion == definition.StructureVersion))
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_STRUCTURE_FORBIDDEN",
                "The source does not permit the exact structure revision.");
        var structureCurrent = await db.TriggerObservationStructureCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.StructureId,
            cancellationToken);
        if (structureCurrent?.CurrentVersion != definition.StructureVersion)
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_STRUCTURE_STALE",
                "The trigger requires the exact current structure revision.");
        var structure = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.StructureId &&
            value.Version == definition.StructureVersion, cancellationToken);
        if (structure is null || structure.Status != "active" || structure.SchemaHash != definition.StructureHash)
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_STRUCTURE_STALE",
                "The trigger structure is missing, retired, or has a different exact hash.");
        if (definition.Notification.StateSpaceId is null)
        {
            if (definition.Notification.EntityIds.Count != 0) throw new TriggerSchedulingContractException(
                "OBSERVATION_TRIGGER_NOTIFICATION_SCOPE", "Notification entities require a state space.");
            return;
        }
        var stateSpace = stateSpaces.Get(definition.Notification.StateSpaceId);
        if (stateSpace?.ApplicationRevision.ApplicationId != definition.ApplicationId)
            throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_NOTIFICATION_SCOPE",
                "The notification state space is outside the application.");
        foreach (var entityId in definition.Notification.EntityIds)
            if (await components.GetEntityAsync(definition.Notification.StateSpaceId, entityId,
                    cancellationToken) is null)
                throw new TriggerSchedulingContractException("OBSERVATION_TRIGGER_NOTIFICATION_ENTITY_MISSING",
                    "A linked notification entity is missing or deleted.");
    }

    private Task<ObservationTriggerRecord?> ExistingAsync(ObservationTriggerDefinition definition,
        CancellationToken cancellationToken) => db.ObservationTriggers.AsNoTracking()
        .Include(value => value.NotificationEntities).SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id &&
            value.Version == definition.Version, cancellationToken);

    private static ObservationTriggerRecord Row(ObservationTriggerDefinition definition, DateTimeOffset now)
    {
        var row = new ObservationTriggerRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id, Version = definition.Version,
            Lifecycle = Lifecycle(definition.Lifecycle), SourceId = definition.SourceId,
            SourceVersion = definition.SourceVersion, StructureId = definition.StructureId,
            StructureVersion = definition.StructureVersion, StructureHash = definition.StructureHash,
            AdapterId = definition.Adapter.Id, AdapterVersion = definition.Adapter.Version,
            AdapterConfigurationJson = definition.AdapterConfiguration.Json,
            AdapterConfigurationHash = definition.AdapterConfiguration.Hash, Target = "notification-only",
            NotificationTopic = definition.Notification.Topic, NotificationSubject = definition.Notification.Subject,
            NotificationBody = definition.Notification.Body,
            NotificationStateSpaceId = definition.Notification.StateSpaceId, RecordedAtUtc = now.UtcDateTime
        };
        for (var ordinal = 0; ordinal < definition.Notification.EntityIds.Count; ordinal++)
            row.NotificationEntities.Add(new ObservationTriggerNotificationEntityRecord
            {
                ApplicationId = definition.ApplicationId.Value, TriggerId = definition.Id,
                TriggerVersion = definition.Version, Ordinal = ordinal,
                StateSpaceId = definition.Notification.StateSpaceId!,
                EntityId = definition.Notification.EntityIds[ordinal]
            });
        return row;
    }

    internal static ObservationTriggerDefinition Definition(ObservationTriggerRecord row) =>
        ObservationTriggerDefinition.Create(ApplicationIdentifier.Parse(row.ApplicationId), row.Id, row.Version,
            ParseLifecycle(row.Lifecycle), row.SourceId, row.SourceVersion, row.StructureId,
            row.StructureVersion, row.StructureHash,
            ObservationMatchAdapterReference.Create(row.AdapterId, row.AdapterVersion),
            row.AdapterConfigurationJson, TriggerFireTarget.NotificationOnly,
            TriggerNotificationTarget.Create(row.NotificationTopic, row.NotificationSubject,
                row.NotificationBody, row.NotificationStateSpaceId,
                row.NotificationEntities.OrderBy(value => value.Ordinal).Select(value => value.EntityId).ToArray()));

    private static StoredObservationTrigger Project(ObservationTriggerRecord row)
    {
        var definition = Definition(row);
        return new(definition.ApplicationId, definition.Id, definition.Version, definition.Lifecycle,
            definition.SourceId, definition.SourceVersion, definition.StructureId, definition.StructureVersion,
            definition.StructureHash, definition.Adapter, definition.AdapterConfiguration,
            definition.Notification, new DateTimeOffset(DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc)));
    }

    private static bool Same(ObservationTriggerDefinition value, StoredObservationTrigger stored) =>
        value.ApplicationId == stored.ApplicationId && value.Id == stored.Id && value.Version == stored.Version &&
        value.Lifecycle == stored.Lifecycle && value.SourceId == stored.SourceId &&
        value.SourceVersion == stored.SourceVersion && value.StructureId == stored.StructureId &&
        value.StructureVersion == stored.StructureVersion && value.StructureHash == stored.StructureHash &&
        value.Adapter == stored.Adapter && value.AdapterConfiguration.Hash == stored.AdapterConfiguration.Hash &&
        value.Notification.Topic == stored.Notification.Topic && value.Notification.Subject == stored.Notification.Subject &&
        value.Notification.Body == stored.Notification.Body && value.Notification.StateSpaceId == stored.Notification.StateSpaceId &&
        value.Notification.EntityIds.SequenceEqual(stored.Notification.EntityIds);

    internal static string FireId(string applicationId, string triggerId, int version, string observationId)
    {
        var bytes = Encoding.UTF8.GetBytes($"observation-trigger-fire\n{applicationId}\n{triggerId}\n{version}\n{observationId}");
        return "trigger-fire." + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
    }
    internal static string Lifecycle(ObservationTriggerLifecycle value) => value switch
    { ObservationTriggerLifecycle.Active => "active", ObservationTriggerLifecycle.Paused => "paused", _ => "cancelled" };
    internal static ObservationTriggerLifecycle ParseLifecycle(string value) => value switch
    { "active" => ObservationTriggerLifecycle.Active, "paused" => ObservationTriggerLifecycle.Paused,
        _ => ObservationTriggerLifecycle.Cancelled };
    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero) throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC",
            "The trigger clock must use UTC.");
        return now;
    }
}
