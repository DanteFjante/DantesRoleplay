using System.Data.Common;
using System.Security.Cryptography;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteRecurringTriggerWorker(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    ITriggerFireTransactionParticipant participant) : IRecurringTriggerWorker
{
    public const int MaximumBatchSize = 8;
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(30);

    public async Task<TriggerWorkerBatchResult> RunBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkerId(workerId);
        var now = UtcNow();
        await CloseStaleAsync(now, cancellationToken);
        await CloseExhaustedAsync(now, cancellationToken);
        db.ChangeTracker.Clear();
        var candidates = await CandidatesAsync(now, cancellationToken);
        var claimed = 0;
        var completed = 0;
        var missed = 0;
        var retried = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trigger = await DefinitionAsync(candidate, cancellationToken);
            var occurrence = await CurrentOccurrenceAsync(trigger, candidate.NextOccurrenceAtUtc,
                now, cancellationToken);
            if (occurrence is null) continue;
            var fireId = TriggerSchedulingFingerprint.RecurringFire(trigger, occurrence.Value);
            await EnsureWorkAsync(trigger, occurrence.Value, fireId, now, cancellationToken);
            var disposition = Disposition(trigger, occurrence.Value, now);
            if (disposition == OneTimeTriggerDisposition.Missed)
            {
                if (await CompleteAsync(trigger, occurrence.Value, fireId, "missed", now,
                    cancellationToken)) missed++;
                continue;
            }
            if (!participant.IsAvailable) continue;

            var lease = await TryClaimAsync(trigger, occurrence.Value, fireId, workerId, now,
                cancellationToken);
            if (lease is null) continue;
            claimed++;
            var outcome = await ExecuteAsync(trigger, lease, cancellationToken);
            completed += outcome == ExecutionOutcome.Completed ? 1 : 0;
            missed += outcome == ExecutionOutcome.Missed ? 1 : 0;
            retried += outcome == ExecutionOutcome.Retried ? 1 : 0;
            failed += outcome == ExecutionOutcome.Failed ? 1 : 0;
        }

        return new(candidates.Count, claimed, completed, missed, retried, failed);
    }

    private async Task<IReadOnlyList<Candidate>> CandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await (from current in db.RecurringTriggerCurrent.AsNoTracking()
               join definition in db.RecurringTriggers.AsNoTracking()
                   on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                   equals new { definition.ApplicationId, definition.Id, definition.Version }
               join state in db.RecurringTriggerState.AsNoTracking()
                   on new { current.ApplicationId, TriggerId = current.Id, CurrentVersion = current.CurrentVersion }
                   equals new { state.ApplicationId, state.TriggerId, state.CurrentVersion }
               where definition.Lifecycle == "active" && state.NextOccurrenceAtUtc != null &&
                   state.NextOccurrenceAtUtc <= now.UtcDateTime
               orderby state.NextOccurrenceAtUtc, definition.ApplicationId, definition.Id
               select new Candidate(definition.ApplicationId, definition.Id, definition.Version,
                   state.NextOccurrenceAtUtc!.Value, definition.MisfirePolicy,
                   definition.NotificationTopic, definition.NotificationSubject,
                   definition.NotificationBody, definition.NotificationStateSpaceId))
            .Take(MaximumBatchSize).ToListAsync(cancellationToken);

    private async Task<DateTimeOffset?> CurrentOccurrenceAsync(
        RecurringTriggerDefinition trigger,
        DateTime next,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await db.RecurringTriggerFireWork.AsNoTracking()
            .Where(value => value.ApplicationId == trigger.ApplicationId.Value &&
                value.TriggerId == trigger.Id && value.TriggerVersion == trigger.Version &&
                (value.State == "ready" || value.State == "retry" || value.State == "leased"))
            .OrderBy(value => value.OccurrenceAtUtc)
            .Select(value => (DateTime?)value.OccurrenceAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
            return new DateTimeOffset(DateTime.SpecifyKind(active.Value, DateTimeKind.Utc));
        var latest = RecurringScheduleEvaluator.LatestOnOrBefore(trigger, now)?.OccurrenceAtUtc;
        var minimum = new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc));
        return latest is not null && latest >= minimum ? latest : minimum <= now ? minimum : null;
    }

    private async Task EnsureWorkAsync(
        RecurringTriggerDefinition trigger,
        DateTimeOffset occurrence,
        string fireId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT OR IGNORE INTO trigger_recurring_fire_work
                (FireId, ApplicationId, TriggerId, TriggerVersion, OccurrenceAtUtc, State,
                 AttemptCount, NextAttemptAtUtc, LeaseOwner, LeaseToken, LeaseExpiresAtUtc,
                 FailureKind, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({fireId}, {trigger.ApplicationId.Value}, {trigger.Id}, {trigger.Version},
                {occurrence.UtcDateTime}, 'ready', 0, NULL, NULL, NULL, NULL, NULL, 0,
                {now.UtcDateTime}, {now.UtcDateTime})
            """, cancellationToken);
        var storedId = await db.RecurringTriggerFireWork.AsNoTracking()
            .Where(value => value.ApplicationId == trigger.ApplicationId.Value &&
                value.TriggerId == trigger.Id && value.TriggerVersion == trigger.Version &&
                value.OccurrenceAtUtc == occurrence.UtcDateTime)
            .Select(value => value.FireId).SingleOrDefaultAsync(cancellationToken);
        if (!string.Equals(storedId, fireId, StringComparison.Ordinal))
            throw new TriggerSchedulingContractException("RECURRING_FIRE_WORK_IDENTITY_CONFLICT",
                "The stored recurring occurrence conflicts with its deterministic fire ID.");
    }

    private async Task<TriggerFireLease?> TryClaimAsync(
        RecurringTriggerDefinition trigger,
        DateTimeOffset occurrence,
        string fireId,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expires = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_recurring_fire_work
                SET State = 'leased', AttemptCount = AttemptCount + 1, NextAttemptAtUtc = NULL,
                    LeaseOwner = {workerId}, LeaseToken = {token}, LeaseExpiresAtUtc = {expires.UtcDateTime},
                    FailureKind = NULL, Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {fireId} AND AttemptCount < {MaximumAttempts}
                  AND NOT EXISTS (SELECT 1 FROM trigger_recurring_fire_receipt receipt WHERE receipt.Id = {fireId})
                  AND EXISTS (SELECT 1 FROM trigger_recurring_state state
                      WHERE state.ApplicationId = {trigger.ApplicationId.Value}
                        AND state.TriggerId = {trigger.Id} AND state.CurrentVersion = {trigger.Version}
                        AND state.NextOccurrenceAtUtc <= {occurrence.UtcDateTime})
                  AND (State = 'ready' OR
                       State = 'retry' AND NextAttemptAtUtc <= {now.UtcDateTime} OR
                       State = 'leased' AND LeaseExpiresAtUtc <= {now.UtcDateTime})
                """, cancellationToken);
            if (changed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            var attempt = await db.RecurringTriggerFireWork.AsNoTracking()
                .Where(value => value.FireId == fireId && value.LeaseToken == token)
                .Select(value => value.AttemptCount).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TriggerFireLease(fireId, trigger.ApplicationId, trigger.Id, trigger.Version,
                occurrence, trigger.MisfirePolicy, trigger.Target, attempt, workerId, token, expires)
                { ScheduleKind = TriggerScheduleKind.Recurring };
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task<ExecutionOutcome> ExecuteAsync(
        RecurringTriggerDefinition trigger,
        TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LeaseIsCurrentAsync(lease, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ExecutionOutcome.None;
            }
            if (!await TriggerIsCurrentAsync(lease, cancellationToken))
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(trigger, lease,
                    TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger),
                    cancellationToken);
            }
            var now = UtcNow();
            if (Disposition(trigger, lease.OccurrenceAt, now) == OneTimeTriggerDisposition.Missed)
            {
                if (!await FinishTerminalAsync(trigger, lease, "missed", null, now, cancellationToken))
                    throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE",
                        "The recurring trigger lease expired before missed evidence could commit.");
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ExecutionOutcome.Missed;
            }

            TriggerFireAttemptResult result;
            try { result = await participant.StageAsync(lease, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { result = Classify(exception); }
            if (result.Disposition != TriggerFireAttemptDisposition.Succeeded)
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(trigger, lease, Normalize(result), cancellationToken);
            }
            now = UtcNow();
            if (!await LeaseIsCurrentAsync(lease, cancellationToken) ||
                !await TriggerIsCurrentAsync(lease, cancellationToken) ||
                Disposition(trigger, lease.OccurrenceAt, now) != OneTimeTriggerDisposition.Due)
            {
                await RollbackAndClearAsync(transaction);
                return ExecutionOutcome.None;
            }
            if (!await FinishTerminalAsync(trigger, lease, "due", null, now, cancellationToken))
                throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE",
                    "The recurring trigger lease expired before completion could commit.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ExecutionOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackAndClearAsync(transaction);
            return await FinishFailureAsync(trigger, lease, Classify(exception), CancellationToken.None);
        }
    }

    private async Task<bool> CompleteAsync(
        RecurringTriggerDefinition trigger,
        DateTimeOffset occurrence,
        string fireId,
        string disposition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_recurring_fire_work
                SET State = {disposition}, NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                    LeaseExpiresAtUtc = NULL, FailureKind = NULL, Revision = Revision + 1,
                    UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {fireId} AND (State = 'ready' OR
                    State = 'retry' AND NextAttemptAtUtc <= {now.UtcDateTime} OR
                    State = 'leased' AND LeaseExpiresAtUtc <= {now.UtcDateTime})
                """, cancellationToken);
            if (changed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            await AppendReceiptAndAdvanceAsync(trigger, occurrence, fireId, disposition, null,
                now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task<bool> FinishTerminalAsync(
        RecurringTriggerDefinition trigger,
        TriggerFireLease lease,
        string? disposition,
        string? failure,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = failure is not null ? "failed"
            : disposition == "due" ? "completed" : disposition!;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_recurring_fire_work
            SET State = {state}, NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = {failure}, Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId = {lease.FireId} AND State = 'leased'
              AND LeaseOwner = {lease.WorkerId} AND LeaseToken = {lease.LeaseToken}
              AND LeaseExpiresAtUtc > {now.UtcDateTime}
            """, cancellationToken);
        if (changed != 1) return false;
        if (failure == "stale-trigger") return true;
        await AppendReceiptAndAdvanceAsync(trigger, lease.OccurrenceAt, lease.FireId,
            disposition, failure, now, cancellationToken);
        return true;
    }

    private async Task AppendReceiptAndAdvanceAsync(
        RecurringTriggerDefinition trigger,
        DateTimeOffset occurrence,
        string fireId,
        string? disposition,
        string? failure,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (disposition is not null)
        {
            db.RecurringTriggerFireReceipts.Add(new RecurringTriggerFireReceiptRecord
            {
                Id = fireId,
                ApplicationId = trigger.ApplicationId.Value,
                TriggerId = trigger.Id,
                TriggerVersion = trigger.Version,
                OccurrenceAtUtc = occurrence.UtcDateTime,
                Disposition = disposition,
                RecordedAtUtc = now.UtcDateTime
            });
        }
        var next = RecurringScheduleEvaluator.NextAfter(trigger, occurrence)?.OccurrenceAtUtc.UtcDateTime;
        var advanced = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_recurring_state
            SET NextOccurrenceAtUtc = {next}, LastOccurrenceAtUtc = {occurrence.UtcDateTime},
                LastDisposition = {disposition}, LastFailureKind = {failure},
                Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
            WHERE ApplicationId = {trigger.ApplicationId.Value} AND TriggerId = {trigger.Id}
              AND CurrentVersion = {trigger.Version} AND NextOccurrenceAtUtc <= {occurrence.UtcDateTime}
            """, cancellationToken);
        if (advanced != 1)
            throw new TriggerSchedulingContractException("RECURRING_STATE_STALE",
                "The recurring schedule changed before its occurrence could advance.");
    }

    private async Task<ExecutionOutcome> FinishFailureAsync(
        RecurringTriggerDefinition trigger,
        TriggerFireLease lease,
        TriggerFireAttemptResult result,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var transient = result.Disposition == TriggerFireAttemptDisposition.TransientFailure;
        var retry = transient && lease.Attempt < MaximumAttempts;
        var failure = retry
            ? Failure(result.FailureKind ?? TriggerFireFailureKind.HandlerUnavailable)
            : transient ? "attempts-exhausted" : Failure(result.FailureKind ?? TriggerFireFailureKind.PermanentHandler);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (retry)
            {
                var next = now.Add(lease.Attempt == 1 ? FirstRetryDelay : SecondRetryDelay).UtcDateTime;
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE trigger_recurring_fire_work
                    SET State = 'retry', NextAttemptAtUtc = {next}, LeaseOwner = NULL, LeaseToken = NULL,
                        LeaseExpiresAtUtc = NULL, FailureKind = {failure}, Revision = Revision + 1,
                        UpdatedAtUtc = {now.UtcDateTime}
                    WHERE FireId = {lease.FireId} AND State = 'leased'
                      AND LeaseOwner = {lease.WorkerId} AND LeaseToken = {lease.LeaseToken}
                      AND LeaseExpiresAtUtc > {now.UtcDateTime}
                    """, cancellationToken);
                if (changed != 1) { await transaction.RollbackAsync(cancellationToken); return ExecutionOutcome.None; }
            }
            else if (!await FinishTerminalAsync(trigger, lease, null, failure, now, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ExecutionOutcome.None;
            }
            await transaction.CommitAsync(cancellationToken);
            return retry ? ExecutionOutcome.Retried : ExecutionOutcome.Failed;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task CloseStaleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_recurring_fire_work
            SET State = 'failed', NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = 'stale-trigger', Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId IN (SELECT work.FireId FROM trigger_recurring_fire_work work
                WHERE work.State IN ('ready', 'retry', 'leased') AND EXISTS (
                    SELECT 1 FROM trigger_recurring_current current
                    WHERE current.ApplicationId = work.ApplicationId AND current.Id = work.TriggerId
                      AND current.CurrentVersion <> work.TriggerVersion)
                ORDER BY work.UpdatedAtUtc, work.FireId LIMIT {MaximumBatchSize})
            """, cancellationToken);
    }

    private async Task CloseExhaustedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await db.RecurringTriggerFireWork.AsNoTracking()
            .Where(value => value.State == "leased" && value.AttemptCount >= MaximumAttempts &&
                value.LeaseExpiresAtUtc <= now.UtcDateTime)
            .OrderBy(value => value.LeaseExpiresAtUtc).ThenBy(value => value.FireId)
            .Take(MaximumBatchSize).Select(value => new { value.FireId, value.ApplicationId,
                value.TriggerId, value.TriggerVersion, value.OccurrenceAtUtc })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var candidate = await CandidateAsync(row.ApplicationId, row.TriggerId, row.TriggerVersion,
                cancellationToken);
            if (candidate is null) continue;
            var trigger = await DefinitionAsync(candidate, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE trigger_recurring_fire_work
                    SET State = 'failed', LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
                        FailureKind = 'attempts-exhausted', Revision = Revision + 1,
                        UpdatedAtUtc = {now.UtcDateTime}
                    WHERE FireId = {row.FireId} AND State = 'leased' AND AttemptCount >= {MaximumAttempts}
                      AND LeaseExpiresAtUtc <= {now.UtcDateTime}
                    """, cancellationToken);
                if (changed == 1)
                    await AppendReceiptAndAdvanceAsync(trigger,
                        new DateTimeOffset(DateTime.SpecifyKind(row.OccurrenceAtUtc, DateTimeKind.Utc)),
                        row.FireId, null, "attempts-exhausted", now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch { await RollbackAndClearAsync(transaction); throw; }
        }
    }

    private Task<bool> LeaseIsCurrentAsync(TriggerFireLease lease, CancellationToken cancellationToken) =>
        db.RecurringTriggerFireWork.AsNoTracking().AnyAsync(value => value.FireId == lease.FireId &&
            value.State == "leased" && value.LeaseOwner == lease.WorkerId &&
            value.LeaseToken == lease.LeaseToken && value.LeaseExpiresAtUtc > UtcNow().UtcDateTime,
            cancellationToken);

    private Task<bool> TriggerIsCurrentAsync(TriggerFireLease lease, CancellationToken cancellationToken) =>
        db.RecurringTriggerCurrent.AsNoTracking().AnyAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId &&
            value.CurrentVersion == lease.TriggerVersion, cancellationToken);

    private async Task<Candidate?> CandidateAsync(string app, string id, int version,
        CancellationToken cancellationToken) => await db.RecurringTriggers.AsNoTracking()
        .Where(value => value.ApplicationId == app && value.Id == id && value.Version == version)
        .Select(value => new Candidate(value.ApplicationId, value.Id, value.Version, DateTime.MinValue,
            value.MisfirePolicy, value.NotificationTopic, value.NotificationSubject,
            value.NotificationBody, value.NotificationStateSpaceId))
        .SingleOrDefaultAsync(cancellationToken);

    private async Task<RecurringTriggerDefinition> DefinitionAsync(Candidate value,
        CancellationToken cancellationToken)
    {
        var row = await db.RecurringTriggers.AsNoTracking().SingleAsync(record =>
            record.ApplicationId == value.ApplicationId && record.Id == value.Id &&
            record.Version == value.Version, cancellationToken);
        var entities = await db.RecurringTriggerNotificationEntities.AsNoTracking()
            .Where(link => link.ApplicationId == value.ApplicationId && link.TriggerId == value.Id &&
                link.TriggerVersion == value.Version).OrderBy(link => link.Ordinal)
            .Select(link => link.EntityId).ToArrayAsync(cancellationToken);
        return RecurringTriggerDefinition.Create(ApplicationIdentifier.Parse(value.ApplicationId),
            value.Id, value.Version, SqliteTriggerSchedulingStore.Pattern(row),
            row.Lifecycle switch { "active" => RecurringTriggerLifecycle.Active,
                "paused" => RecurringTriggerLifecycle.Paused, _ => RecurringTriggerLifecycle.Cancelled },
            row.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
            TriggerFireTarget.NotificationOnly, TriggerNotificationTarget.Create(row.NotificationTopic,
                row.NotificationSubject, row.NotificationBody, row.NotificationStateSpaceId, entities));
    }

    private static OneTimeTriggerDisposition Disposition(RecurringTriggerDefinition trigger,
        DateTimeOffset occurrence, DateTimeOffset now) => occurrence == now ||
        trigger.MisfirePolicy == TriggerMisfirePolicy.FireOnce &&
        now - occurrence <= TimeSpan.FromHours(TriggerSchedulingLimits.MaximumFireOnceLatenessHours)
            ? OneTimeTriggerDisposition.Due : OneTimeTriggerDisposition.Missed;

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        finally
        {
            try { await transaction.DisposeAsync(); }
            catch (ObjectDisposedException) { }
            db.ChangeTracker.Clear();
        }
    }

    private static TriggerFireAttemptResult Normalize(TriggerFireAttemptResult result) => result switch
    {
        { Disposition: TriggerFireAttemptDisposition.TransientFailure,
            FailureKind: TriggerFireFailureKind.HandlerUnavailable or TriggerFireFailureKind.TransientDatabase } => result,
        { Disposition: TriggerFireAttemptDisposition.PermanentFailure,
            FailureKind: TriggerFireFailureKind.PermanentHandler or TriggerFireFailureKind.StaleTrigger } => result,
        _ => TriggerFireAttemptResult.Permanent()
    };

    private static TriggerFireAttemptResult Classify(Exception exception) => exception switch
    {
        DbException or DbUpdateException => TriggerFireAttemptResult.Transient(TriggerFireFailureKind.TransientDatabase),
        TriggerSchedulingTransientException => TriggerFireAttemptResult.Transient(),
        _ => TriggerFireAttemptResult.Permanent()
    };

    private static string Failure(TriggerFireFailureKind kind) => kind switch
    {
        TriggerFireFailureKind.HandlerUnavailable => "handler-unavailable",
        TriggerFireFailureKind.TransientDatabase => "transient-database",
        TriggerFireFailureKind.PermanentHandler => "permanent-handler",
        TriggerFireFailureKind.StaleTrigger => "stale-trigger",
        TriggerFireFailureKind.AttemptsExhausted => "attempts-exhausted",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void ValidateWorkerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or ':' or '-')))
            throw new TriggerSchedulingContractException("TRIGGER_WORKER_ID", "The worker ID is invalid.");
    }

    private sealed record Candidate(string ApplicationId, string Id, int Version,
        DateTime NextOccurrenceAtUtc, string MisfirePolicy, string NotificationTopic,
        string NotificationSubject, string NotificationBody, string? NotificationStateSpaceId);
    private enum ExecutionOutcome { None, Completed, Missed, Retried, Failed }
}
