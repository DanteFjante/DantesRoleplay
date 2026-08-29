using System.Data.Common;
using System.Security.Cryptography;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteOneTimeTriggerWorker(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    ITriggerSchedulingStore receipts,
    ITriggerFireTransactionParticipant participant) : IOneTimeTriggerWorker
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
        await CloseStaleOrExhaustedAsync(now, cancellationToken);
        db.ChangeTracker.Clear();
        var candidates = await CandidatesAsync(now, participant.IsAvailable, cancellationToken);
        var claimed = 0;
        var completed = 0;
        var missed = 0;
        var retried = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trigger = await DefinitionAsync(candidate, cancellationToken);
            var evaluation = OneTimeTriggerEvaluator.Evaluate(trigger, clock);
            await EnsureWorkAsync(trigger, evaluation.FireId, now, cancellationToken);
            if (evaluation.Disposition == OneTimeTriggerDisposition.Missed)
            {
                if (await CompleteMissedAsync(trigger, evaluation.FireId, now, cancellationToken)) missed++;
                continue;
            }
            if (!participant.IsAvailable) continue;

            var lease = await TryClaimAsync(trigger, evaluation.FireId, workerId, now, cancellationToken);
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
        bool participantAvailable,
        CancellationToken cancellationToken)
    {
        var utc = now.UtcDateTime;
        var oldestFireOnce = now.AddHours(-TriggerSchedulingLimits.MaximumFireOnceLatenessHours).UtcDateTime;
        return await (
            from current in db.OneTimeTriggerCurrent.AsNoTracking()
            join definition in db.OneTimeTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.Lifecycle == "active" && definition.DueAtUtc <= utc
            where participantAvailable ||
                (definition.MisfirePolicy == "skip" && definition.DueAtUtc < utc) ||
                (definition.MisfirePolicy == "fire-once" && definition.DueAtUtc < oldestFireOnce)
            where !db.TriggerFireReceipts.Any(receipt =>
                receipt.ApplicationId == definition.ApplicationId &&
                receipt.TriggerId == definition.Id &&
                receipt.TriggerVersion == definition.Version &&
                receipt.OccurrenceAtUtc == definition.DueAtUtc)
            where !db.TriggerFireWork.Any(work =>
                work.ApplicationId == definition.ApplicationId &&
                work.TriggerId == definition.Id &&
                work.TriggerVersion == definition.Version &&
                work.OccurrenceAtUtc == definition.DueAtUtc &&
                (work.State == "completed" || work.State == "missed" || work.State == "failed" ||
                 work.State == "retry" && work.NextAttemptAtUtc > utc ||
                 work.State == "leased" && work.LeaseExpiresAtUtc > utc))
            orderby definition.DueAtUtc, definition.ApplicationId, definition.Id, definition.Version
            select new Candidate(definition.ApplicationId, definition.Id, definition.Version,
                definition.DueAtUtc, definition.MisfirePolicy, definition.Target,
                definition.NotificationTopic, definition.NotificationSubject,
                definition.NotificationBody, definition.NotificationStateSpaceId))
            .Take(MaximumBatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task CloseStaleOrExhaustedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var utc = now.UtcDateTime;
        var hasStale = await db.TriggerFireWork.AsNoTracking().AnyAsync(work =>
            (work.State == "ready" || work.State == "retry" || work.State == "leased") &&
            db.OneTimeTriggerCurrent.Any(current =>
                current.ApplicationId == work.ApplicationId && current.Id == work.TriggerId &&
                current.CurrentVersion != work.TriggerVersion), cancellationToken);
        if (hasStale)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_fire_work
                SET State = 'failed', NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                    LeaseExpiresAtUtc = NULL, FailureKind = 'stale-trigger', Revision = Revision + 1,
                    UpdatedAtUtc = {utc}
                WHERE FireId IN (
                    SELECT work.FireId FROM trigger_fire_work work
                    WHERE work.State IN ('ready', 'retry', 'leased')
                      AND EXISTS (
                          SELECT 1 FROM trigger_one_time_current current
                          WHERE current.ApplicationId = work.ApplicationId
                            AND current.Id = work.TriggerId
                            AND current.CurrentVersion <> work.TriggerVersion)
                    ORDER BY work.UpdatedAtUtc, work.FireId
                    LIMIT {MaximumBatchSize})
                """, cancellationToken);
        var hasExhausted = await db.TriggerFireWork.AsNoTracking().AnyAsync(work =>
            work.State == "leased" && work.AttemptCount >= MaximumAttempts &&
            work.LeaseExpiresAtUtc <= utc, cancellationToken);
        if (hasExhausted)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_fire_work
                SET State = 'failed', NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                    LeaseExpiresAtUtc = NULL, FailureKind = 'attempts-exhausted', Revision = Revision + 1,
                    UpdatedAtUtc = {utc}
                WHERE FireId IN (
                    SELECT FireId FROM trigger_fire_work
                    WHERE State = 'leased' AND AttemptCount >= {MaximumAttempts}
                      AND LeaseExpiresAtUtc <= {utc}
                    ORDER BY LeaseExpiresAtUtc, FireId
                    LIMIT {MaximumBatchSize})
                """, cancellationToken);
    }

    private async Task EnsureWorkAsync(
        OneTimeTriggerDefinition trigger,
        string fireId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var utc = now.UtcDateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT OR IGNORE INTO trigger_fire_work
                (FireId, ApplicationId, TriggerId, TriggerVersion, OccurrenceAtUtc, State,
                 AttemptCount, NextAttemptAtUtc, LeaseOwner, LeaseToken, LeaseExpiresAtUtc,
                 FailureKind, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({fireId}, {trigger.ApplicationId.Value}, {trigger.Id}, {trigger.Version},
                {trigger.DueAt.UtcDateTime}, 'ready', 0, NULL, NULL, NULL, NULL, NULL, 0, {utc}, {utc})
            """, cancellationToken);
        var storedId = await db.TriggerFireWork.AsNoTracking()
            .Where(value => value.ApplicationId == trigger.ApplicationId.Value &&
                value.TriggerId == trigger.Id && value.TriggerVersion == trigger.Version &&
                value.OccurrenceAtUtc == trigger.DueAt.UtcDateTime)
            .Select(value => value.FireId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.Equals(storedId, fireId, StringComparison.Ordinal))
            throw new TriggerSchedulingContractException("TRIGGER_FIRE_WORK_IDENTITY_CONFLICT",
                "The stored trigger occurrence work identity conflicts with its deterministic fire ID.");
    }

    private async Task<bool> CompleteMissedAsync(
        OneTimeTriggerDefinition trigger,
        string fireId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var utc = now.UtcDateTime;
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_fire_work
                SET State = 'missed', NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                    LeaseExpiresAtUtc = NULL, FailureKind = NULL, Revision = Revision + 1,
                    UpdatedAtUtc = {utc}
                WHERE FireId = {fireId}
                  AND (State = 'ready' OR
                       State = 'retry' AND NextAttemptAtUtc <= {utc} OR
                       State = 'leased' AND LeaseExpiresAtUtc <= {utc})
                """, cancellationToken);
            if (changed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            var receipt = await receipts.AppendFireReceiptAsync(trigger, cancellationToken);
            if (receipt.Disposition == TriggerSchedulingWriteDisposition.Conflict)
                throw new TriggerSchedulingContractException("TRIGGER_FIRE_CONFLICT", "The deterministic fire receipt conflicts with stored evidence.");
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task<TriggerFireLease?> TryClaimAsync(
        OneTimeTriggerDefinition trigger,
        string fireId,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var utc = now.UtcDateTime;
        var expires = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_fire_work
                SET State = 'leased', AttemptCount = AttemptCount + 1, NextAttemptAtUtc = NULL,
                    LeaseOwner = {workerId}, LeaseToken = {token}, LeaseExpiresAtUtc = {expires.UtcDateTime},
                    FailureKind = NULL, Revision = Revision + 1, UpdatedAtUtc = {utc}
                WHERE FireId = {fireId} AND AttemptCount < {MaximumAttempts}
                  AND NOT EXISTS (SELECT 1 FROM trigger_fire_receipt receipt WHERE receipt.Id = {fireId})
                  AND (State = 'ready' OR
                       State = 'retry' AND NextAttemptAtUtc <= {utc} OR
                       State = 'leased' AND LeaseExpiresAtUtc <= {utc})
                """, cancellationToken);
            if (changed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            var attempt = await db.TriggerFireWork.AsNoTracking()
                .Where(value => value.FireId == fireId && value.LeaseToken == token)
                .Select(value => value.AttemptCount)
                .SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(fireId, trigger.ApplicationId, trigger.Id, trigger.Version, trigger.DueAt,
                trigger.MisfirePolicy, trigger.Target, attempt, workerId, token, expires);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task<ExecutionOutcome> ExecuteAsync(
        OneTimeTriggerDefinition trigger,
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
                return await FinishFailureAsync(lease, TriggerFireAttemptResult.Permanent(
                    TriggerFireFailureKind.StaleTrigger), cancellationToken);
            }

            var evaluation = OneTimeTriggerEvaluator.Evaluate(trigger, clock);
            if (evaluation.Disposition == OneTimeTriggerDisposition.Missed)
            {
                var receipt = await receipts.AppendFireReceiptAsync(trigger, cancellationToken);
                if (receipt.Disposition == TriggerSchedulingWriteDisposition.Conflict)
                    throw new TriggerSchedulingContractException("TRIGGER_FIRE_CONFLICT", "The deterministic fire receipt conflicts with stored evidence.");
                if (await FinishTerminalInTransactionAsync(lease, "missed", cancellationToken) != 1)
                    throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE", "The trigger lease expired before missed evidence could commit.");
                await transaction.CommitAsync(cancellationToken);
                return ExecutionOutcome.Missed;
            }

            TriggerFireAttemptResult result;
            try
            {
                result = await participant.StageAsync(lease, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = Classify(exception);
            }

            if (result.Disposition != TriggerFireAttemptDisposition.Succeeded)
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(lease, Normalize(result), cancellationToken);
            }
            if (!await LeaseIsCurrentAsync(lease, cancellationToken) ||
                !await TriggerIsCurrentAsync(lease, cancellationToken) ||
                OneTimeTriggerEvaluator.Evaluate(trigger, clock).Disposition != OneTimeTriggerDisposition.Due)
            {
                await RollbackAndClearAsync(transaction);
                return ExecutionOutcome.None;
            }

            var success = await receipts.AppendFireReceiptAsync(trigger, cancellationToken);
            if (success.Disposition == TriggerSchedulingWriteDisposition.Conflict)
                throw new TriggerSchedulingContractException("TRIGGER_FIRE_CONFLICT", "The deterministic fire receipt conflicts with stored evidence.");
            if (await FinishTerminalInTransactionAsync(lease, "completed", cancellationToken) != 1)
                throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE", "The trigger lease expired before completion could commit.");
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
            return await FinishFailureAsync(lease, Classify(exception), CancellationToken.None);
        }
    }

    private async Task<ExecutionOutcome> FinishFailureAsync(
        TriggerFireLease lease,
        TriggerFireAttemptResult result,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var transient = result.Disposition == TriggerFireAttemptDisposition.TransientFailure;
        var retry = transient && lease.Attempt < MaximumAttempts;
        var state = retry ? "retry" : "failed";
        var next = retry ? now.Add(lease.Attempt == 1 ? FirstRetryDelay : SecondRetryDelay).UtcDateTime : (DateTime?)null;
        var failure = retry
            ? Failure(result.FailureKind ?? TriggerFireFailureKind.HandlerUnavailable)
            : transient ? "attempts-exhausted" : Failure(result.FailureKind ?? TriggerFireFailureKind.PermanentHandler);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_fire_work
                SET State = {state}, NextAttemptAtUtc = {next}, LeaseOwner = NULL, LeaseToken = NULL,
                    LeaseExpiresAtUtc = NULL, FailureKind = {failure}, Revision = Revision + 1,
                    UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {lease.FireId} AND State = 'leased'
                  AND LeaseOwner = {lease.WorkerId} AND LeaseToken = {lease.LeaseToken}
                  AND LeaseExpiresAtUtc > {now.UtcDateTime}
                """, cancellationToken);
            if (changed != 1)
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

    private async Task<int> FinishTerminalInTransactionAsync(
        TriggerFireLease lease,
        string state,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_fire_work
            SET State = {state}, NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = NULL, Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId = {lease.FireId} AND State = 'leased'
              AND LeaseOwner = {lease.WorkerId} AND LeaseToken = {lease.LeaseToken}
              AND LeaseExpiresAtUtc > {now.UtcDateTime}
            """, cancellationToken);
    }

    private Task<bool> LeaseIsCurrentAsync(TriggerFireLease lease, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        return db.TriggerFireWork.AsNoTracking().AnyAsync(value =>
            value.FireId == lease.FireId && value.State == "leased" &&
            value.LeaseOwner == lease.WorkerId && value.LeaseToken == lease.LeaseToken &&
            value.LeaseExpiresAtUtc > now.UtcDateTime, cancellationToken);
    }

    private Task<bool> TriggerIsCurrentAsync(TriggerFireLease lease, CancellationToken cancellationToken) =>
        db.OneTimeTriggerCurrent.AsNoTracking().AnyAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId &&
            value.CurrentVersion == lease.TriggerVersion, cancellationToken);

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }

    private async Task RollbackAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
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

    private async Task<OneTimeTriggerDefinition> DefinitionAsync(
        Candidate value,
        CancellationToken cancellationToken)
    {
        var entityIds = await db.Set<OneTimeTriggerNotificationEntityRecord>().AsNoTracking()
            .Where(link => link.ApplicationId == value.ApplicationId && link.TriggerId == value.Id &&
                link.TriggerVersion == value.Version)
            .OrderBy(link => link.Ordinal)
            .Select(link => link.EntityId)
            .ToArrayAsync(cancellationToken);
        return OneTimeTriggerDefinition.Create(
            ApplicationIdentifier.Parse(value.ApplicationId), value.Id, value.Version,
            new DateTimeOffset(value.DueAtUtc, TimeSpan.Zero),
            value.MisfirePolicy == "skip" ? TriggerMisfirePolicy.Skip : TriggerMisfirePolicy.FireOnce,
            TriggerFireTarget.NotificationOnly, TriggerLifecycle.Active,
            TriggerNotificationTarget.Create(value.NotificationTopic, value.NotificationSubject,
                value.NotificationBody, value.NotificationStateSpaceId, entityIds));
    }

    private static TriggerFireAttemptResult Normalize(TriggerFireAttemptResult result) => result switch
    {
        { Disposition: TriggerFireAttemptDisposition.Succeeded, FailureKind: null } => result,
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

    private sealed record Candidate(
        string ApplicationId,
        string Id,
        int Version,
        DateTime DueAtUtc,
        string MisfirePolicy,
        string Target,
        string NotificationTopic,
        string NotificationSubject,
        string NotificationBody,
        string? NotificationStateSpaceId);

    private enum ExecutionOutcome { None, Completed, Missed, Retried, Failed }
}

public sealed class TriggerSchedulingTransientException : Exception
{
    public TriggerSchedulingTransientException() : base("The trigger target is temporarily unavailable.") { }
}

internal sealed class UnavailableTriggerFireTransactionParticipant : ITriggerFireTransactionParticipant
{
    public bool IsAvailable => false;

    public Task<TriggerFireAttemptResult> StageAsync(
        TriggerFireLease lease,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No trigger fire transaction participant is registered.");
}
