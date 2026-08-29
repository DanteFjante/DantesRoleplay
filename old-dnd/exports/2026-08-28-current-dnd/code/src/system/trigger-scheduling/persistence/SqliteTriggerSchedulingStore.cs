using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.TriggerScheduling;

/// <summary>
/// SQLite owner for immutable trigger registrations and append-only evidence. Admission, current
/// revision resolution, fingerprints, and fire evaluation are recomputed here with a trusted clock.
/// </summary>
public sealed class SqliteTriggerSchedulingStore(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    IEnumerable<IObservationAppendTransactionParticipant>? observationParticipants = null) : ITriggerSchedulingStore
{
    public async Task<TriggerSchedulingWriteResult<StoredObservationStructure>> AppendStructureAsync(
        ObservationStructureDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = UtcNow();
        await RequireApplicationAsync(definition.ApplicationId, cancellationToken);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var existing = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
        if (existing is not null)
            return Same(existing, definition) ? TriggerSchedulingWriteResult<StoredObservationStructure>.Replay(Structure(existing)) : TriggerSchedulingWriteResult<StoredObservationStructure>.Conflict();

        var current = await db.Set<TriggerObservationStructureCurrentRecord>().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id, cancellationToken);
        RequireNewer(definition.Version, current?.CurrentVersion, "STRUCTURE");
        var row = new TriggerObservationStructureRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id, Version = definition.Version,
            SchemaProfileId = definition.SchemaProfileId, NormalizedSchema = definition.NormalizedSchema,
            SchemaHash = definition.SchemaHash, Description = definition.Description,
            Status = StructureStatus(definition.Status),
            DataClassification = DataClassification(definition.DataClassification),
            RecordedAtUtc = now.UtcDateTime
        };
        var pointer = current ?? new TriggerObservationStructureCurrentRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id
        };
        pointer.CurrentVersion = definition.Version;
        db.Add(row);
        if (current is null) db.Add(pointer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredObservationStructure>.Appended(Structure(definition, now));
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await RollbackAndDetachAsync(transaction, [row, pointer], cancellationToken);
            var winner = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
            if (winner is not null)
                return Same(winner, definition) ? TriggerSchedulingWriteResult<StoredObservationStructure>.Replay(Structure(winner)) : TriggerSchedulingWriteResult<StoredObservationStructure>.Conflict();
            throw;
        }
    }

    public async Task<TriggerSchedulingWriteResult<StoredObservationSource>> AppendSourceAsync(
        ObservationSourceDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = UtcNow();
        await RequireApplicationAsync(definition.ApplicationId, cancellationToken);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var existing = await db.TriggerObservationSources.AsNoTracking()
            .Include(value => value.AllowedStructures)
            .Include(value => value.AllowedPrincipals)
            .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
        if (existing is not null)
            return Same(existing, definition) ? TriggerSchedulingWriteResult<StoredObservationSource>.Replay(Source(existing)) : TriggerSchedulingWriteResult<StoredObservationSource>.Conflict();

        var current = await db.Set<TriggerObservationSourceCurrentRecord>().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id, cancellationToken);
        RequireNewer(definition.Version, current?.CurrentVersion, "SOURCE");
        foreach (var structure in definition.AllowedStructures)
            await RequireCurrentActiveStructureAsync(definition.ApplicationId, structure, cancellationToken);

        var row = new TriggerObservationSourceRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id, Version = definition.Version,
            Status = SourceStatus(definition.Status), ReplayWindowSeconds = checked((int)definition.ReplayWindow.TotalSeconds),
            RequestsPerMinute = definition.RequestsPerMinute, RecordedAtUtc = now.UtcDateTime
        };
        foreach (var structure in definition.AllowedStructures)
        {
            row.AllowedStructures.Add(new TriggerObservationSourceStructureRecord
            {
                ApplicationId = definition.ApplicationId.Value, SourceId = definition.Id, SourceVersion = definition.Version,
                StructureId = structure.Id, StructureVersion = structure.Version
            });
        }
        foreach (var principalId in definition.AllowedPrincipalIds)
        {
            row.AllowedPrincipals.Add(new TriggerObservationSourcePrincipalRecord
            {
                ApplicationId = definition.ApplicationId.Value,
                SourceId = definition.Id,
                SourceVersion = definition.Version,
                PrincipalId = principalId
            });
        }
        var pointer = current ?? new TriggerObservationSourceCurrentRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id
        };
        pointer.CurrentVersion = definition.Version;
        db.Add(row);
        if (current is null) db.Add(pointer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredObservationSource>.Appended(Source(definition, now));
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await RollbackAndDetachAsync(transaction,
                row.AllowedStructures.Cast<object>().Concat(row.AllowedPrincipals).Prepend(row).Append(pointer), cancellationToken);
            var winner = await db.TriggerObservationSources.AsNoTracking()
                .Include(value => value.AllowedStructures)
                .Include(value => value.AllowedPrincipals)
                .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
            if (winner is not null)
                return Same(winner, definition) ? TriggerSchedulingWriteResult<StoredObservationSource>.Replay(Source(winner)) : TriggerSchedulingWriteResult<StoredObservationSource>.Conflict();
            throw;
        }
    }

    public async Task<TriggerSchedulingWriteResult<StoredOneTimeTrigger>> AppendOneTimeTriggerAsync(
        OneTimeTriggerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = UtcNow();
        await RequireApplicationAsync(definition.ApplicationId, cancellationToken);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var existing = await db.OneTimeTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
        if (existing is not null)
            return Same(existing, definition) ? TriggerSchedulingWriteResult<StoredOneTimeTrigger>.Replay(Trigger(existing)) : TriggerSchedulingWriteResult<StoredOneTimeTrigger>.Conflict();

        var current = await db.Set<OneTimeTriggerCurrentRecord>().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id, cancellationToken);
        RequireNewer(definition.Version, current?.CurrentVersion, "TRIGGER");
        var row = new OneTimeTriggerRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id, Version = definition.Version,
            DueAtUtc = definition.DueAt.UtcDateTime, MisfirePolicy = MisfirePolicy(definition.MisfirePolicy),
            Target = Target(definition.Target), Lifecycle = Lifecycle(definition.Lifecycle),
            NotificationTopic = definition.Notification.Topic,
            NotificationSubject = definition.Notification.Subject,
            NotificationBody = definition.Notification.Body,
            NotificationStateSpaceId = definition.Notification.StateSpaceId,
            RecordedAtUtc = now.UtcDateTime
        };
        for (var ordinal = 0; ordinal < definition.Notification.EntityIds.Count; ordinal++)
        {
            row.NotificationEntities.Add(new OneTimeTriggerNotificationEntityRecord
            {
                ApplicationId = definition.ApplicationId.Value,
                TriggerId = definition.Id,
                TriggerVersion = definition.Version,
                Ordinal = ordinal,
                StateSpaceId = definition.Notification.StateSpaceId!,
                EntityId = definition.Notification.EntityIds[ordinal]
            });
        }
        var pointer = current ?? new OneTimeTriggerCurrentRecord
        {
            ApplicationId = definition.ApplicationId.Value, Id = definition.Id
        };
        pointer.CurrentVersion = definition.Version;
        db.Add(row);
        if (current is null) db.Add(pointer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredOneTimeTrigger>.Appended(Trigger(definition, now));
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await RollbackAndDetachAsync(transaction,
                row.NotificationEntities.Cast<object>().Prepend(row).Append(pointer), cancellationToken);
            var winner = await db.OneTimeTriggers.AsNoTracking()
                .Include(value => value.NotificationEntities)
                .SingleOrDefaultAsync(value =>
                value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
            if (winner is not null)
                return Same(winner, definition) ? TriggerSchedulingWriteResult<StoredOneTimeTrigger>.Replay(Trigger(winner)) : TriggerSchedulingWriteResult<StoredOneTimeTrigger>.Conflict();
            throw;
        }
    }

    public async Task<TriggerSchedulingWriteResult<StoredRecurringTrigger>> AppendRecurringTriggerAsync(
        RecurringTriggerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = UtcNow();
        await RequireApplicationAsync(definition.ApplicationId, cancellationToken);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var existing = await db.Set<RecurringTriggerRecord>().AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value &&
                value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
        if (existing is not null)
            return Same(existing, definition)
                ? TriggerSchedulingWriteResult<StoredRecurringTrigger>.Replay(Recurring(existing))
                : TriggerSchedulingWriteResult<StoredRecurringTrigger>.Conflict();

        var current = await db.Set<RecurringTriggerCurrentRecord>().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.Id == definition.Id,
            cancellationToken);
        RequireNewer(definition.Version, current?.CurrentVersion, "RECURRING_TRIGGER");
        var row = RecurringRecord(definition, now);
        var pointer = current ?? new RecurringTriggerCurrentRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            Id = definition.Id
        };
        pointer.CurrentVersion = definition.Version;
        var state = await db.Set<RecurringTriggerStateRecord>().SingleOrDefaultAsync(value =>
            value.ApplicationId == definition.ApplicationId.Value && value.TriggerId == definition.Id,
            cancellationToken);
        var initial = definition.Lifecycle == RecurringTriggerLifecycle.Active
            ? RecurringScheduleEvaluator.NextOnOrAfter(definition, now)?.OccurrenceAtUtc.UtcDateTime
            : null;
        state ??= new RecurringTriggerStateRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            TriggerId = definition.Id,
            Revision = -1
        };
        state.CurrentVersion = definition.Version;
        state.NextOccurrenceAtUtc = initial;
        state.LastOccurrenceAtUtc = null;
        state.LastDisposition = null;
        state.LastFailureKind = null;
        state.Revision++;
        state.UpdatedAtUtc = now.UtcDateTime;
        db.Add(row);
        if (current is null) db.Add(pointer);
        if (db.Entry(state).State == EntityState.Detached) db.Add(state);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredRecurringTrigger>.Appended(Recurring(definition, now));
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await RollbackAndDetachAsync(transaction,
                row.NotificationEntities.Cast<object>().Prepend(row).Append(pointer).Append(state),
                cancellationToken);
            var winner = await db.Set<RecurringTriggerRecord>().AsNoTracking()
                .Include(value => value.NotificationEntities)
                .SingleOrDefaultAsync(value => value.ApplicationId == definition.ApplicationId.Value &&
                    value.Id == definition.Id && value.Version == definition.Version, cancellationToken);
            if (winner is not null)
                return Same(winner, definition)
                    ? TriggerSchedulingWriteResult<StoredRecurringTrigger>.Replay(Recurring(winner))
                    : TriggerSchedulingWriteResult<StoredRecurringTrigger>.Conflict();
            throw;
        }
    }

    public async Task<TriggerSchedulingWriteResult<StoredObservation>> AppendObservationAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(submission);
        if (!principal.Verified)
            throw new TriggerSchedulingContractException("OBSERVATION_PRINCIPAL_REQUIRED", "A verified observation principal is required.");
        var now = UtcNow();
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var source = await CurrentSourceAsync(applicationId, submission.Source.Id, cancellationToken);
        if (!source.AllowedPrincipals.Any(value => value.PrincipalId == principal.PrincipalId))
            throw new TriggerSchedulingContractException("OBSERVATION_PRINCIPAL_FORBIDDEN", "The current source does not permit this principal.");
        var structure = await CurrentStructureAsync(applicationId, submission.Structure, cancellationToken);
        var admitted = ObservationAdmissionEvaluator.Evaluate(applicationId, submission, SourceDefinition(source), StructureDefinition(structure), clock);
        var existing = await FindObservationIdentityAsync(admitted, cancellationToken);
        if (existing is not null) return ObservationReplay(existing, admitted);

        var row = ObservationRecord(admitted, principal.PrincipalId, now);
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var stored = Observation(row);
            foreach (var participant in observationParticipants ?? [])
                await participant.StageAsync(stored, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredObservation>.Appended(stored);
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var winner = await FindObservationIdentityAsync(admitted, cancellationToken);
            if (winner is not null) return ObservationReplay(winner, admitted);
            throw;
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

    public async Task<TriggerSchedulingWriteResult<StoredTriggerFireReceipt>> AppendFireReceiptAsync(
        OneTimeTriggerDefinition trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var now = UtcNow();
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var current = await db.Set<OneTimeTriggerCurrentRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == trigger.ApplicationId.Value && value.Id == trigger.Id, cancellationToken)
            ?? throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_TRIGGER_NOT_FOUND", "The one-time trigger is not registered.");
        if (current.CurrentVersion != trigger.Version)
            throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_TRIGGER_STALE", "The one-time trigger revision is no longer current.");
        var definition = await db.OneTimeTriggers.AsNoTracking()
            .Include(value => value.NotificationEntities)
            .SingleAsync(value =>
            value.ApplicationId == trigger.ApplicationId.Value && value.Id == trigger.Id && value.Version == trigger.Version, cancellationToken);
        if (!Same(definition, trigger))
            throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_TRIGGER_STALE", "The stored trigger revision does not match the supplied definition.");
        var evaluation = OneTimeTriggerEvaluator.Evaluate(trigger, clock);
        if (evaluation.Disposition == OneTimeTriggerDisposition.Pending)
            throw new TriggerSchedulingContractException("TRIGGER_FIRE_NOT_ELIGIBLE", "A pending one-time trigger has no fire receipt.");
        var existing = await db.TriggerFireReceipts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == evaluation.FireId, cancellationToken);
        if (existing is not null) return FireReplay(existing, trigger, evaluation);

        var row = new TriggerFireReceiptRecord
        {
            Id = evaluation.FireId, ApplicationId = trigger.ApplicationId.Value, TriggerId = trigger.Id,
            TriggerVersion = trigger.Version, OccurrenceAtUtc = evaluation.OccurrenceAt.UtcDateTime,
            Disposition = Disposition(evaluation.Disposition), RecordedAtUtc = now.UtcDateTime
        };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return TriggerSchedulingWriteResult<StoredTriggerFireReceipt>.Appended(Fire(row));
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await RollbackAndDetachAsync(transaction, [row], cancellationToken);
            var winner = await db.TriggerFireReceipts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == evaluation.FireId, cancellationToken);
            if (winner is not null) return FireReplay(winner, trigger, evaluation);
            throw;
        }
    }

    private async Task<TriggerObservationSourceRecord> CurrentSourceAsync(ApplicationIdentifier applicationId, string sourceId, CancellationToken cancellationToken)
    {
        var current = await db.Set<TriggerObservationSourceCurrentRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == sourceId, cancellationToken)
            ?? throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_SOURCE_NOT_FOUND", "The observation source is not registered.");
        return await db.TriggerObservationSources.AsNoTracking()
            .Include(value => value.AllowedStructures)
            .Include(value => value.AllowedPrincipals)
            .SingleAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == sourceId && value.Version == current.CurrentVersion, cancellationToken);
    }

    private async Task<TriggerObservationStructureRecord> CurrentStructureAsync(ApplicationIdentifier applicationId, ObservationStructureReference requested, CancellationToken cancellationToken)
    {
        var current = await db.Set<TriggerObservationStructureCurrentRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == requested.Id, cancellationToken)
            ?? throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_STRUCTURE_NOT_FOUND", "The observation structure is not registered.");
        if (current.CurrentVersion != requested.Version)
            throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_OBSERVATION_STALE", "The requested observation structure revision is no longer current.");
        return await db.TriggerObservationStructures.AsNoTracking().SingleAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == requested.Id && value.Version == requested.Version, cancellationToken);
    }

    private async Task RequireCurrentActiveStructureAsync(ApplicationIdentifier applicationId, ObservationStructureReference requested, CancellationToken cancellationToken)
    {
        var structure = await CurrentStructureAsync(applicationId, requested, cancellationToken);
        if (structure.Status != "active")
            throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_STRUCTURE_STALE", "A source may allow only a current active observation structure.");
    }

    private async Task<TriggerObservationRecord?> FindObservationIdentityAsync(AdmittedObservation admitted, CancellationToken cancellationToken)
    {
        var submission = admitted.Submission;
        var byRequest = await db.TriggerObservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == admitted.ApplicationId.Value && value.RequestId == submission.RequestId, cancellationToken);
        var byOccurrence = await db.TriggerObservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == admitted.ApplicationId.Value && value.SourceId == submission.Source.Id && value.SourceVersion == admitted.SourceVersion &&
            value.SourceInstanceId == submission.Source.InstanceId && value.OccurrenceId == submission.Source.OccurrenceId, cancellationToken);
        if (byRequest is not null && byOccurrence is not null && byRequest.Id != byOccurrence.Id)
            return ConflictSentinel;
        return byRequest ?? byOccurrence;
    }

    private static readonly TriggerObservationRecord ConflictSentinel = new()
    {
        Id = string.Empty, ApplicationId = string.Empty, RequestId = string.Empty, SourceId = string.Empty,
        SourceInstanceId = string.Empty, OccurrenceId = string.Empty, StructureId = string.Empty,
        StructureHash = string.Empty, DataJson = string.Empty, DataHash = string.Empty, RequestFingerprint = string.Empty
    };

    private static TriggerSchedulingWriteResult<StoredObservation> ObservationReplay(TriggerObservationRecord existing, AdmittedObservation admitted) =>
        existing != ConflictSentinel && existing.RequestFingerprint == admitted.RequestFingerprint
            ? TriggerSchedulingWriteResult<StoredObservation>.Replay(Observation(existing))
            : TriggerSchedulingWriteResult<StoredObservation>.Conflict();

    private static TriggerSchedulingWriteResult<StoredTriggerFireReceipt> FireReplay(TriggerFireReceiptRecord existing, OneTimeTriggerDefinition trigger, OneTimeTriggerEvaluation evaluation) =>
        Same(existing, trigger, evaluation)
            ? TriggerSchedulingWriteResult<StoredTriggerFireReceipt>.Replay(Fire(existing))
            : TriggerSchedulingWriteResult<StoredTriggerFireReceipt>.Conflict();

    private async Task RequireApplicationAsync(ApplicationIdentifier applicationId, CancellationToken cancellationToken)
    {
        if (!await db.Set<ApplicationRegistryRecord>().AsNoTracking().AnyAsync(value => value.Id == applicationId.Value, cancellationToken))
            throw new TriggerSchedulingContractException("TRIGGER_SCHEDULING_APPLICATION_NOT_FOUND", "The trigger-scheduling application is not registered.");
    }

    private DateTimeOffset UtcNow()
    {
        if (clock.UtcNow.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return clock.UtcNow;
    }

    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private async Task RollbackAndDetachAsync(IDbContextTransaction transaction, IEnumerable<object> rows, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        foreach (var row in rows.Distinct(ReferenceEqualityComparer.Instance))
        {
            var entry = db.Entry(row);
            if (entry.State != EntityState.Detached) entry.State = EntityState.Detached;
        }
    }

    private static void RequireNewer(int version, int? currentVersion, string kind)
    {
        if (currentVersion is not null && version <= currentVersion)
            throw new TriggerSchedulingContractException($"TRIGGER_SCHEDULING_{kind}_REVISION_STALE", "A new revision must be greater than the current revision.");
    }

    private static string SourceStatus(ObservationSourceStatus value) => value == ObservationSourceStatus.Enabled ? "enabled" : "disabled";
    private static string StructureStatus(ObservationStructureStatus value) => value == ObservationStructureStatus.Active ? "active" : "retired";
    internal static string DataClassification(ObservationDataClassification value) => value switch
    {
        ObservationDataClassification.General => "general",
        ObservationDataClassification.PrivacyMinimizedSignal => "privacy-minimized-signal",
        ObservationDataClassification.RawLocation => "raw-location",
        ObservationDataClassification.ThirdPartyNotificationContent => "third-party-notification-content",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    internal static ObservationDataClassification ParseDataClassification(string value) => value switch
    {
        "general" => ObservationDataClassification.General,
        "privacy-minimized-signal" => ObservationDataClassification.PrivacyMinimizedSignal,
        "raw-location" => ObservationDataClassification.RawLocation,
        "third-party-notification-content" => ObservationDataClassification.ThirdPartyNotificationContent,
        _ => throw new TriggerSchedulingContractException("OBSERVATION_STRUCTURE_CLASSIFICATION",
            "The stored observation data classification is invalid.")
    };
    private static string MisfirePolicy(TriggerMisfirePolicy value) => value == TriggerMisfirePolicy.Skip ? "skip" : "fire-once";
    private static string Target(TriggerFireTarget value) => value == TriggerFireTarget.NotificationOnly ? "notification-only" : throw new ArgumentOutOfRangeException(nameof(value));
    private static string Lifecycle(TriggerLifecycle value) => value == TriggerLifecycle.Active ? "active" : "cancelled";
    private static string RecurringLifecycle(RecurringTriggerLifecycle value) => value switch
    {
        RecurringTriggerLifecycle.Active => "active",
        RecurringTriggerLifecycle.Paused => "paused",
        RecurringTriggerLifecycle.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private static string RecurrenceKindValue(RecurrenceKind value) => value switch
    {
        RecurrenceKind.Daily => "daily",
        RecurrenceKind.Weekly => "weekly",
        RecurrenceKind.Monthly => "monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private static string GapPolicy(RecurrenceGapPolicy value) =>
        value == RecurrenceGapPolicy.Skip ? "skip" : "next-valid";
    private static string OverlapPolicy(RecurrenceOverlapPolicy value) =>
        value == RecurrenceOverlapPolicy.Earlier ? "earlier" : "later";
    private static int WeekdaysMask(IReadOnlyList<DayOfWeek> values) =>
        values.Aggregate(0, (mask, value) => mask | 1 << (int)value);
    private static string Disposition(OneTimeTriggerDisposition value) => value switch { OneTimeTriggerDisposition.Due => "due", OneTimeTriggerDisposition.Missed => "missed", _ => throw new ArgumentOutOfRangeException(nameof(value)) };

    private static bool Same(TriggerObservationStructureRecord row, ObservationStructureDefinition value) =>
        row.SchemaProfileId == value.SchemaProfileId && row.NormalizedSchema == value.NormalizedSchema && row.SchemaHash == value.SchemaHash && row.Description == value.Description && row.Status == StructureStatus(value.Status) && row.DataClassification == DataClassification(value.DataClassification);
    private static bool Same(TriggerObservationSourceRecord row, ObservationSourceDefinition value) =>
        row.Status == SourceStatus(value.Status) && row.ReplayWindowSeconds == value.ReplayWindow.TotalSeconds && row.RequestsPerMinute == value.RequestsPerMinute &&
        row.AllowedStructures.OrderBy(item => item.StructureId).ThenBy(item => item.StructureVersion).Select(item => (item.StructureId, item.StructureVersion))
            .SequenceEqual(value.AllowedStructures.OrderBy(item => item.Id).ThenBy(item => item.Version).Select(item => (item.Id, item.Version))) &&
        row.AllowedPrincipals.OrderBy(item => item.PrincipalId).Select(item => item.PrincipalId)
            .SequenceEqual(value.AllowedPrincipalIds.Order(StringComparer.Ordinal));
    private static bool Same(OneTimeTriggerRecord row, OneTimeTriggerDefinition value) =>
        row.DueAtUtc == value.DueAt.UtcDateTime && row.MisfirePolicy == MisfirePolicy(value.MisfirePolicy) &&
        row.Target == Target(value.Target) && row.Lifecycle == Lifecycle(value.Lifecycle) &&
        row.NotificationTopic == value.Notification.Topic && row.NotificationSubject == value.Notification.Subject &&
        row.NotificationBody == value.Notification.Body && row.NotificationStateSpaceId == value.Notification.StateSpaceId &&
        row.NotificationEntities.OrderBy(item => item.Ordinal).Select(item => (item.StateSpaceId, item.EntityId))
            .SequenceEqual(value.Notification.EntityIds.Select(item => (value.Notification.StateSpaceId!, item)));
    private static bool Same(RecurringTriggerRecord row, RecurringTriggerDefinition value) =>
        row.Lifecycle == RecurringLifecycle(value.Lifecycle) &&
        row.Kind == RecurrenceKindValue(value.Pattern.Kind) && row.Interval == value.Pattern.Interval &&
        row.LocalTimeSeconds == checked((int)value.Pattern.LocalTime.ToTimeSpan().TotalSeconds) &&
        row.TimeZoneId == value.Pattern.TimeZoneId && row.StartDate == value.Pattern.StartDate &&
        row.EndDate == value.Pattern.EndDate && row.WeekdaysMask == WeekdaysMask(value.Pattern.Weekdays) &&
        row.DayOfMonth == value.Pattern.DayOfMonth && row.GapPolicy == GapPolicy(value.Pattern.GapPolicy) &&
        row.OverlapPolicy == OverlapPolicy(value.Pattern.OverlapPolicy) &&
        row.MisfirePolicy == MisfirePolicy(value.MisfirePolicy) && row.Target == Target(value.Target) &&
        row.NotificationTopic == value.Notification.Topic && row.NotificationSubject == value.Notification.Subject &&
        row.NotificationBody == value.Notification.Body && row.NotificationStateSpaceId == value.Notification.StateSpaceId &&
        row.NotificationEntities.OrderBy(item => item.Ordinal).Select(item => (item.StateSpaceId, item.EntityId))
            .SequenceEqual(value.Notification.EntityIds.Select(item => (value.Notification.StateSpaceId!, item)));
    private static bool Same(TriggerFireReceiptRecord row, OneTimeTriggerDefinition trigger, OneTimeTriggerEvaluation evaluation) =>
        row.ApplicationId == trigger.ApplicationId.Value && row.TriggerId == trigger.Id && row.TriggerVersion == trigger.Version && row.OccurrenceAtUtc == evaluation.OccurrenceAt.UtcDateTime && row.Disposition == Disposition(evaluation.Disposition);

    private static ObservationSourceDefinition SourceDefinition(TriggerObservationSourceRecord value) =>
        ObservationSourceDefinition.Create(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version,
            value.Status == "enabled" ? ObservationSourceStatus.Enabled : ObservationSourceStatus.Disabled,
            value.AllowedStructures.OrderBy(item => item.StructureId).ThenBy(item => item.StructureVersion)
                .Select(item => ObservationStructureReference.Create(item.StructureId, item.StructureVersion)).ToArray(),
            value.AllowedPrincipals.OrderBy(item => item.PrincipalId).Select(item => item.PrincipalId).ToArray(),
            TimeSpan.FromSeconds(value.ReplayWindowSeconds), value.RequestsPerMinute);

    private static ObservationStructureDefinition StructureDefinition(TriggerObservationStructureRecord value) =>
        ObservationStructureDefinition.Create(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version,
            value.SchemaProfileId, value.NormalizedSchema, value.SchemaHash, value.Description,
            value.Status == "active" ? ObservationStructureStatus.Active : ObservationStructureStatus.Retired,
            ParseDataClassification(value.DataClassification));

    private static TriggerObservationRecord ObservationRecord(
        AdmittedObservation admitted,
        string principalId,
        DateTimeOffset receivedAt)
    {
        var submission = admitted.Submission;
        return new TriggerObservationRecord
        {
            Id = "observation." + admitted.RequestFingerprint[..32].ToLowerInvariant(),
            ApplicationId = admitted.ApplicationId.Value, RequestId = submission.RequestId,
            SourceId = submission.Source.Id, SourceVersion = admitted.SourceVersion,
            SourceInstanceId = submission.Source.InstanceId, OccurrenceId = submission.Source.OccurrenceId,
            StructureId = submission.Structure.Id, StructureVersion = submission.Structure.Version,
            StructureHash = admitted.StructureHash, ObservedAtUtc = submission.ObservedAt.UtcDateTime,
            ReceivedAtUtc = receivedAt.UtcDateTime, DataJson = submission.Data.Json, DataHash = submission.Data.Hash,
            RequestFingerprint = admitted.RequestFingerprint, PrincipalId = principalId
        };
    }

    private static StoredObservationStructure Structure(ObservationStructureDefinition value, DateTimeOffset recordedAt) => new(value.ApplicationId, value.Id, value.Version, value.SchemaProfileId, value.NormalizedSchema, value.SchemaHash, value.Description, value.Status, value.DataClassification, recordedAt);
    private static StoredObservationStructure Structure(TriggerObservationStructureRecord value) => new(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version, value.SchemaProfileId, value.NormalizedSchema, value.SchemaHash, value.Description, value.Status == "active" ? ObservationStructureStatus.Active : ObservationStructureStatus.Retired, ParseDataClassification(value.DataClassification), new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
    private static StoredObservationSource Source(ObservationSourceDefinition value, DateTimeOffset recordedAt) => new(value.ApplicationId, value.Id, value.Version, value.Status, value.AllowedStructures, value.AllowedPrincipalIds, value.ReplayWindow, value.RequestsPerMinute, recordedAt);
    private static StoredObservationSource Source(TriggerObservationSourceRecord value) => new(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version, value.Status == "enabled" ? ObservationSourceStatus.Enabled : ObservationSourceStatus.Disabled, value.AllowedStructures.OrderBy(item => item.StructureId).ThenBy(item => item.StructureVersion).Select(item => ObservationStructureReference.Create(item.StructureId, item.StructureVersion)).ToArray(), value.AllowedPrincipals.OrderBy(item => item.PrincipalId).Select(item => item.PrincipalId).ToArray(), TimeSpan.FromSeconds(value.ReplayWindowSeconds), value.RequestsPerMinute, new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
    private static StoredOneTimeTrigger Trigger(OneTimeTriggerDefinition value, DateTimeOffset recordedAt) =>
        new(value.ApplicationId, value.Id, value.Version, value.DueAt, value.MisfirePolicy, value.Target,
            value.Lifecycle, value.Notification, recordedAt);
    private static StoredOneTimeTrigger Trigger(OneTimeTriggerRecord value) =>
        new(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version,
            new DateTimeOffset(value.DueAtUtc, TimeSpan.Zero),
            value.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
            TriggerFireTarget.NotificationOnly,
            value.Lifecycle == "active" ? TriggerLifecycle.Active : TriggerLifecycle.Cancelled,
            TriggerNotificationTarget.Create(value.NotificationTopic, value.NotificationSubject,
                value.NotificationBody, value.NotificationStateSpaceId,
                value.NotificationEntities.OrderBy(item => item.Ordinal).Select(item => item.EntityId).ToArray()),
            new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
    private static RecurringTriggerRecord RecurringRecord(
        RecurringTriggerDefinition value,
        DateTimeOffset recordedAt)
    {
        var pattern = value.Pattern;
        var row = new RecurringTriggerRecord
        {
            ApplicationId = value.ApplicationId.Value,
            Id = value.Id,
            Version = value.Version,
            Lifecycle = RecurringLifecycle(value.Lifecycle),
            Kind = RecurrenceKindValue(pattern.Kind),
            Interval = pattern.Interval,
            LocalTimeSeconds = checked((int)pattern.LocalTime.ToTimeSpan().TotalSeconds),
            TimeZoneId = pattern.TimeZoneId,
            StartDate = pattern.StartDate,
            EndDate = pattern.EndDate,
            WeekdaysMask = WeekdaysMask(pattern.Weekdays),
            DayOfMonth = pattern.DayOfMonth,
            GapPolicy = GapPolicy(pattern.GapPolicy),
            OverlapPolicy = OverlapPolicy(pattern.OverlapPolicy),
            MisfirePolicy = MisfirePolicy(value.MisfirePolicy),
            Target = Target(value.Target),
            NotificationTopic = value.Notification.Topic,
            NotificationSubject = value.Notification.Subject,
            NotificationBody = value.Notification.Body,
            NotificationStateSpaceId = value.Notification.StateSpaceId,
            RecordedAtUtc = recordedAt.UtcDateTime
        };
        for (var ordinal = 0; ordinal < value.Notification.EntityIds.Count; ordinal++)
        {
            row.NotificationEntities.Add(new RecurringTriggerNotificationEntityRecord
            {
                ApplicationId = value.ApplicationId.Value,
                TriggerId = value.Id,
                TriggerVersion = value.Version,
                Ordinal = ordinal,
                StateSpaceId = value.Notification.StateSpaceId!,
                EntityId = value.Notification.EntityIds[ordinal]
            });
        }
        return row;
    }
    private static StoredRecurringTrigger Recurring(RecurringTriggerDefinition value, DateTimeOffset recordedAt) =>
        new(value.ApplicationId, value.Id, value.Version, value.Lifecycle, value.Pattern,
            value.MisfirePolicy, value.Target, value.Notification, recordedAt);
    private static StoredRecurringTrigger Recurring(RecurringTriggerRecord value)
    {
        var pattern = Pattern(value);
        return new(ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version,
            value.Lifecycle switch
            {
                "active" => RecurringTriggerLifecycle.Active,
                "paused" => RecurringTriggerLifecycle.Paused,
                _ => RecurringTriggerLifecycle.Cancelled
            }, pattern, value.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
            TriggerFireTarget.NotificationOnly,
            TriggerNotificationTarget.Create(value.NotificationTopic, value.NotificationSubject,
                value.NotificationBody, value.NotificationStateSpaceId,
                value.NotificationEntities.OrderBy(item => item.Ordinal).Select(item => item.EntityId).ToArray()),
            new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
    }
    internal static RecurrencePattern Pattern(RecurringTriggerRecord value)
    {
        var localTime = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(value.LocalTimeSeconds));
        var gap = value.GapPolicy == "skip" ? RecurrenceGapPolicy.Skip : RecurrenceGapPolicy.NextValid;
        var overlap = value.OverlapPolicy == "earlier" ? RecurrenceOverlapPolicy.Earlier : RecurrenceOverlapPolicy.Later;
        return value.Kind switch
        {
            "daily" => RecurrencePattern.Daily(value.Interval, localTime, value.TimeZoneId,
                value.StartDate, value.EndDate, gap, overlap),
            "weekly" => RecurrencePattern.Weekly(value.Interval, localTime, value.TimeZoneId,
                Enum.GetValues<DayOfWeek>().Where(day => (value.WeekdaysMask & 1 << (int)day) != 0).ToArray(),
                value.StartDate, value.EndDate, gap, overlap),
            "monthly" => RecurrencePattern.Monthly(value.Interval, localTime, value.TimeZoneId,
                value.DayOfMonth!.Value, value.StartDate, value.EndDate, gap, overlap),
            _ => throw new TriggerSchedulingContractException("RECURRENCE_KIND", "The stored recurrence kind is invalid.")
        };
    }
    private static StoredObservation Observation(TriggerObservationRecord value) => new(value.Id, ApplicationIdentifier.Parse(value.ApplicationId), value.RequestId, value.SourceId, value.SourceVersion, value.SourceInstanceId, value.OccurrenceId, value.StructureId, value.StructureVersion, value.StructureHash, new DateTimeOffset(value.ObservedAtUtc, TimeSpan.Zero), new DateTimeOffset(value.ReceivedAtUtc, TimeSpan.Zero), value.DataJson, value.DataHash, value.RequestFingerprint, value.PrincipalId);
    private static StoredTriggerFireReceipt Fire(TriggerFireReceiptRecord value) => new(value.Id, ApplicationIdentifier.Parse(value.ApplicationId), value.TriggerId, value.TriggerVersion, new DateTimeOffset(value.OccurrenceAtUtc, TimeSpan.Zero), value.Disposition == "due" ? OneTimeTriggerDisposition.Due : OneTimeTriggerDisposition.Missed, new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
}
