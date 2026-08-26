using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteConditionalTriggerStore(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore components,
    IEnumerable<IConditionalTriggerAdapter> adapters,
    ITriggerClock clock) : IConditionalTriggerStore
{
    public async Task<TriggerSchedulingWriteResult<StoredConditionalTrigger>> AppendAsync(
        ConditionalTriggerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var adapter = ResolveAdapter(definition.Adapter);
        adapter.Validate(definition);

        var existing = await ExistingAsync(definition, cancellationToken);
        if (existing is not null)
        {
            var projected = await ProjectAsync(existing, cancellationToken);
            return Equivalent(definition, projected)
                ? TriggerSchedulingWriteResult<StoredConditionalTrigger>.Replay(projected)
                : TriggerSchedulingWriteResult<StoredConditionalTrigger>.Conflict();
        }
        await ValidateScopeAsync(definition, cancellationToken);

        var now = UtcNow();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var current = await db.ConditionalTriggerCurrent.SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id,
                cancellationToken);
            if ((current is null && definition.Version != 1) ||
                (current is not null && definition.Version != current.CurrentVersion + 1))
                throw new TriggerSchedulingContractException("CONDITIONAL_TRIGGER_VERSION_GAP",
                    $"Conditional trigger revisions must be appended without gaps (requested {definition.Version}, current {current?.CurrentVersion.ToString() ?? "none"}).");

            var snapshots = await SnapshotsAsync(definition, cancellationToken);
            var truth = definition.Lifecycle == ConditionalTriggerLifecycle.Active
                ? adapter.Evaluate(definition, snapshots) : (bool?)null;
            var armed = definition.Lifecycle == ConditionalTriggerLifecycle.Active &&
                (definition.Kind != ConditionalTriggerKind.WorldClockThreshold || truth != true);
            var row = Row(definition, now);
            db.ConditionalTriggers.Add(row);
            if (current is null)
                db.ConditionalTriggerCurrent.Add(new ConditionalTriggerCurrentRecord
                {
                    ApplicationId = definition.ApplicationId.Value,
                    Id = definition.Id,
                    CurrentVersion = definition.Version
                });
            else current.CurrentVersion = definition.Version;
            var state = await db.ConditionalTriggerState.SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId.Value && value.TriggerId == definition.Id,
                cancellationToken);
            if (state is null)
                db.ConditionalTriggerState.Add(new ConditionalTriggerStateRecord
                {
                    ApplicationId = definition.ApplicationId.Value,
                    TriggerId = definition.Id,
                    CurrentVersion = definition.Version,
                    CurrentTruth = truth,
                    Armed = armed,
                    EvaluationRevision = 0,
                    UpdatedAtUtc = now.UtcDateTime
                });
            else
            {
                state.CurrentVersion = definition.Version;
                state.CurrentTruth = truth;
                state.Armed = armed;
                state.EvaluationRevision++;
                state.LastOperationId = null;
                state.LastFiredOperationId = null;
                state.UpdatedAtUtc = now.UtcDateTime;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return TriggerSchedulingWriteResult<StoredConditionalTrigger>.Appended(
                await ProjectAsync(row, cancellationToken));
        }
        catch (DbUpdateException)
        {
            if (transaction is null) throw;
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { db.ChangeTracker.Clear(); }
            existing = await ExistingAsync(definition, CancellationToken.None);
            if (existing is null) throw;
            var projected = await ProjectAsync(existing, CancellationToken.None);
            return Equivalent(definition, projected)
                ? TriggerSchedulingWriteResult<StoredConditionalTrigger>.Replay(projected)
                : TriggerSchedulingWriteResult<StoredConditionalTrigger>.Conflict();
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

    private Task<ConditionalTriggerRecord?> ExistingAsync(ConditionalTriggerDefinition definition,
        CancellationToken cancellationToken) => db.ConditionalTriggers.AsNoTracking()
        .Include(value => value.Dependencies).Include(value => value.NotificationEntities)
        .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value &&
            value.Id == definition.Id && value.Version == definition.Version, cancellationToken);

    internal IConditionalTriggerAdapter ResolveAdapter(ConditionalTriggerAdapterReference reference)
    {
        var matches = adapters.Where(value => value.Id == reference.Id && value.Version == reference.Version).ToArray();
        if (matches.Length != 1)
            throw new TriggerSchedulingContractException("CONDITIONAL_ADAPTER_UNAVAILABLE",
                "The exact reviewed conditional adapter is unavailable.");
        return matches[0];
    }

    internal async Task<IReadOnlyList<ConditionalTriggerDependencySnapshot>> SnapshotsAsync(
        ConditionalTriggerDefinition definition,
        CancellationToken cancellationToken)
    {
        var locators = definition.Dependencies
            .Select(value => new EcsComponentLocator(value.EntityId, value.ComponentType.QualifiedTypeId)).ToArray();
        var current = await components.GetComponentsAsync(definition.StateSpaceId, locators, cancellationToken);
        var byKey = current.ToDictionary(value => (value.EntityId, value.Type.QualifiedTypeId));
        return definition.Dependencies.Select(dependency =>
        {
            if (!byKey.TryGetValue((dependency.EntityId, dependency.ComponentType.QualifiedTypeId), out var value) ||
                value.Type.TypeVersion != dependency.ComponentType.TypeVersion ||
                value.Type.SchemaHash != dependency.ComponentType.SchemaHash)
                return new ConditionalTriggerDependencySnapshot(dependency, null, null);
            return new ConditionalTriggerDependencySnapshot(dependency, value.ValueJson, value.Revision);
        }).ToArray();
    }

    private async Task ValidateScopeAsync(ConditionalTriggerDefinition definition,
        CancellationToken cancellationToken)
    {
        var stateSpace = stateSpaces.Get(definition.StateSpaceId);
        if (stateSpace?.ApplicationRevision.ApplicationId != definition.ApplicationId)
            throw new TriggerSchedulingContractException("CONDITIONAL_STATE_SPACE_SCOPE",
                "The condition state space is not owned by the application.");
        foreach (var dependency in definition.Dependencies)
        {
            var type = componentTypes.Get(dependency.ComponentType.QualifiedTypeId,
                dependency.ComponentType.TypeVersion);
            if (type is null || type.Owner != definition.ApplicationId ||
                type.SchemaHash != dependency.ComponentType.SchemaHash)
                throw new TriggerSchedulingContractException("CONDITIONAL_DEPENDENCY_CONTRACT",
                    "A condition dependency has an unknown, stale, or wrong-scope component contract.");
            if (await components.GetEntityAsync(definition.StateSpaceId, dependency.EntityId,
                    cancellationToken) is null)
                throw new TriggerSchedulingContractException("CONDITIONAL_DEPENDENCY_ENTITY_MISSING",
                    "A condition dependency entity is missing or deleted.");
        }
        if (definition.Notification.StateSpaceId is null)
        {
            if (definition.Notification.EntityIds.Count != 0)
                throw new TriggerSchedulingContractException("CONDITIONAL_NOTIFICATION_SCOPE",
                    "Notification entities require an application state space.");
            return;
        }
        var notificationSpace = stateSpaces.Get(definition.Notification.StateSpaceId);
        if (notificationSpace?.ApplicationRevision.ApplicationId != definition.ApplicationId)
            throw new TriggerSchedulingContractException("CONDITIONAL_NOTIFICATION_SCOPE",
                "The notification state space is outside the application.");
        foreach (var entityId in definition.Notification.EntityIds)
            if (await components.GetEntityAsync(definition.Notification.StateSpaceId, entityId,
                    cancellationToken) is null)
                throw new TriggerSchedulingContractException("CONDITIONAL_NOTIFICATION_ENTITY_MISSING",
                    "A linked notification entity is missing or deleted.");
    }

    private static ConditionalTriggerRecord Row(ConditionalTriggerDefinition definition, DateTimeOffset now)
    {
        var row = new ConditionalTriggerRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            Id = definition.Id,
            Version = definition.Version,
            Lifecycle = Lifecycle(definition.Lifecycle),
            Kind = Kind(definition.Kind),
            Activation = Activation(definition.Activation),
            Rearm = Rearm(definition.Rearm),
            StateSpaceId = definition.StateSpaceId,
            AdapterId = definition.Adapter.Id,
            AdapterVersion = definition.Adapter.Version,
            AdapterConfigurationJson = definition.AdapterConfiguration.Json,
            AdapterConfigurationHash = definition.AdapterConfiguration.Hash,
            Target = "notification-only",
            NotificationTopic = definition.Notification.Topic,
            NotificationSubject = definition.Notification.Subject,
            NotificationBody = definition.Notification.Body,
            NotificationStateSpaceId = definition.Notification.StateSpaceId,
            RecordedAtUtc = now.UtcDateTime
        };
        for (var ordinal = 0; ordinal < definition.Dependencies.Count; ordinal++)
        {
            var value = definition.Dependencies[ordinal];
            row.Dependencies.Add(new ConditionalTriggerDependencyRecord
            {
                ApplicationId = definition.ApplicationId.Value,
                TriggerId = definition.Id,
                TriggerVersion = definition.Version,
                Ordinal = ordinal,
                StateSpaceId = definition.StateSpaceId,
                EntityId = value.EntityId,
                QualifiedTypeId = value.ComponentType.QualifiedTypeId,
                TypeVersion = value.ComponentType.TypeVersion,
                SchemaHash = value.ComponentType.SchemaHash
            });
        }
        for (var ordinal = 0; ordinal < definition.Notification.EntityIds.Count; ordinal++)
            row.NotificationEntities.Add(new ConditionalTriggerNotificationEntityRecord
            {
                ApplicationId = definition.ApplicationId.Value,
                TriggerId = definition.Id,
                TriggerVersion = definition.Version,
                Ordinal = ordinal,
                StateSpaceId = definition.Notification.StateSpaceId!,
                EntityId = definition.Notification.EntityIds[ordinal]
            });
        return row;
    }

    internal static ConditionalTriggerDefinition Definition(ConditionalTriggerRecord row)
    {
        var dependencies = row.Dependencies.OrderBy(value => value.Ordinal)
            .Select(value => ConditionalTriggerDependency.Create(value.EntityId,
                new EcsComponentReference(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash))).ToArray();
        return ConditionalTriggerDefinition.Create(ApplicationIdentifier.Parse(row.ApplicationId), row.Id,
            row.Version, ParseLifecycle(row.Lifecycle), ParseKind(row.Kind), ParseActivation(row.Activation),
            ParseRearm(row.Rearm), row.StateSpaceId, dependencies,
            ConditionalTriggerAdapterReference.Create(row.AdapterId, row.AdapterVersion),
            row.AdapterConfigurationJson, TriggerFireTarget.NotificationOnly,
            TriggerNotificationTarget.Create(row.NotificationTopic, row.NotificationSubject,
                row.NotificationBody, row.NotificationStateSpaceId,
                row.NotificationEntities.OrderBy(value => value.Ordinal).Select(value => value.EntityId).ToArray()));
    }

    private async Task<StoredConditionalTrigger> ProjectAsync(ConditionalTriggerRecord row,
        CancellationToken cancellationToken)
    {
        var state = await db.ConditionalTriggerState.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == row.ApplicationId && value.TriggerId == row.Id &&
            value.CurrentVersion == row.Version, cancellationToken);
        var definition = Definition(row);
        return new StoredConditionalTrigger(definition.ApplicationId, definition.Id, definition.Version,
            definition.Lifecycle, definition.Kind, definition.Activation, definition.Rearm,
            definition.StateSpaceId, definition.Dependencies, definition.Adapter,
            definition.AdapterConfiguration, definition.Notification, state?.CurrentTruth,
            state?.Armed ?? false, Utc(row.RecordedAtUtc));
    }

    private static bool Equivalent(ConditionalTriggerDefinition value, StoredConditionalTrigger stored) =>
        value.ApplicationId == stored.ApplicationId && value.Id == stored.Id && value.Version == stored.Version &&
        value.Lifecycle == stored.Lifecycle && value.Kind == stored.Kind && value.Activation == stored.Activation &&
        value.Rearm == stored.Rearm && value.StateSpaceId == stored.StateSpaceId && value.Adapter == stored.Adapter &&
        value.AdapterConfiguration.Hash == stored.AdapterConfiguration.Hash &&
        value.Notification.Topic == stored.Notification.Topic &&
        value.Notification.Subject == stored.Notification.Subject &&
        value.Notification.Body == stored.Notification.Body &&
        value.Notification.StateSpaceId == stored.Notification.StateSpaceId &&
        value.Notification.EntityIds.SequenceEqual(stored.Notification.EntityIds) &&
        value.Dependencies.SequenceEqual(stored.Dependencies);

    internal static string FireId(string applicationId, string triggerId, int version, string operationId)
    {
        var bytes = Encoding.UTF8.GetBytes($"conditional-trigger-fire\n{applicationId}\n{triggerId}\n{version}\n{operationId}");
        return "trigger-fire." + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
    }

    internal static string Lifecycle(ConditionalTriggerLifecycle value) => value switch
    { ConditionalTriggerLifecycle.Active => "active", ConditionalTriggerLifecycle.Paused => "paused", _ => "cancelled" };
    internal static string Kind(ConditionalTriggerKind value) => value switch
    { ConditionalTriggerKind.WorldClockThreshold => "world-clock-threshold", _ => "state-condition" };
    internal static string Activation(ConditionalTriggerActivation value) => value switch
    { ConditionalTriggerActivation.RisingEdge => "rising-edge", _ => "level" };
    internal static string Rearm(ConditionalTriggerRearm value) => value switch
    { ConditionalTriggerRearm.OnFalse => "on-false", _ => "manual" };
    internal static ConditionalTriggerLifecycle ParseLifecycle(string value) => value switch
    { "active" => ConditionalTriggerLifecycle.Active, "paused" => ConditionalTriggerLifecycle.Paused, _ => ConditionalTriggerLifecycle.Cancelled };
    internal static ConditionalTriggerKind ParseKind(string value) => value == "world-clock-threshold"
        ? ConditionalTriggerKind.WorldClockThreshold : ConditionalTriggerKind.StateCondition;
    internal static ConditionalTriggerActivation ParseActivation(string value) => value == "rising-edge"
        ? ConditionalTriggerActivation.RisingEdge : ConditionalTriggerActivation.Level;
    internal static ConditionalTriggerRearm ParseRearm(string value) => value == "on-false"
        ? ConditionalTriggerRearm.OnFalse : ConditionalTriggerRearm.Manual;
    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero) throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
