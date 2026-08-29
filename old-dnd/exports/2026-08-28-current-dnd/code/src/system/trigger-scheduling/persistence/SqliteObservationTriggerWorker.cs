using System.Data.Common;
using System.Security.Cryptography;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteObservationTriggerWorker(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    SqliteObservationTriggerStore store,
    ITriggerFireTransactionParticipant participant) : IObservationTriggerWorker
{
    public const int MaximumBatchSize = 8;
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(30);

    public async Task<TriggerWorkerBatchResult> RunBatchAsync(string workerId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkerId(workerId);
        var now = UtcNow();
        await CloseStaleAsync(now, cancellationToken);
        await CloseExhaustedAsync(now, cancellationToken);
        var candidates = await db.ObservationTriggerMatchWork.AsNoTracking()
            .Where(value => value.State == "ready" || value.State == "retry" && value.NextAttemptAtUtc <= now.UtcDateTime ||
                value.State == "leased" && value.LeaseExpiresAtUtc <= now.UtcDateTime)
            .OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.FireId)
            .Take(MaximumBatchSize).ToListAsync(cancellationToken);
        var claimed = 0; var completed = 0; var retried = 0; var failed = 0;
        foreach (var candidate in candidates)
        {
            var lease = await TryClaimAsync(candidate, workerId, now, cancellationToken);
            if (lease is null) continue;
            claimed++;
            var outcome = await ExecuteAsync(lease, cancellationToken);
            completed += outcome == Outcome.Completed ? 1 : 0;
            retried += outcome == Outcome.Retried ? 1 : 0;
            failed += outcome == Outcome.Failed ? 1 : 0;
        }
        return new(candidates.Count, claimed, completed, 0, retried, failed);
    }

    private async Task<TriggerFireLease?> TryClaimAsync(ObservationTriggerMatchWorkRecord work,
        string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expires = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_observation_match_work SET State = 'leased',
                    AttemptCount = AttemptCount + 1, NextAttemptAtUtc = NULL,
                    LeaseOwner = {workerId}, LeaseToken = {token}, LeaseExpiresAtUtc = {expires.UtcDateTime},
                    FailureKind = NULL, Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {work.FireId} AND AttemptCount < {MaximumAttempts}
                  AND NOT EXISTS (SELECT 1 FROM trigger_observation_match_receipt receipt WHERE receipt.Id = {work.FireId})
                  AND (State = 'ready' OR State = 'retry' AND NextAttemptAtUtc <= {now.UtcDateTime}
                       OR State = 'leased' AND LeaseExpiresAtUtc <= {now.UtcDateTime})
                """, cancellationToken);
            if (changed != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
            var attempt = await db.ObservationTriggerMatchWork.AsNoTracking()
                .Where(value => value.FireId == work.FireId && value.LeaseToken == token)
                .Select(value => value.AttemptCount).SingleAsync(cancellationToken);
            var observedAt = await db.TriggerObservations.AsNoTracking().Where(value => value.Id == work.ObservationId)
                .Select(value => value.ObservedAtUtc).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TriggerFireLease(work.FireId, ApplicationIdentifier.Parse(work.ApplicationId),
                work.TriggerId, work.TriggerVersion,
                new DateTimeOffset(DateTime.SpecifyKind(observedAt, DateTimeKind.Utc)),
                TriggerMisfirePolicy.FireOnce, TriggerFireTarget.NotificationOnly, attempt,
                workerId, token, expires)
            { ScheduleKind = TriggerScheduleKind.Observation, ObservationId = work.ObservationId };
        }
        catch { await RollbackAndClearAsync(transaction); throw; }
    }

    private async Task<Outcome> ExecuteAsync(TriggerFireLease lease, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LeaseCurrentAsync(lease, cancellationToken))
            { await transaction.RollbackAsync(cancellationToken); return Outcome.None; }
            ObservationTriggerDefinition definition;
            ObservationMatchInput observation;
            IObservationMatchAdapter adapter;
            try
            {
                (definition, observation) = await CurrentInputAsync(lease, cancellationToken);
                adapter = store.ResolveAdapter(definition.Adapter);
            }
            catch (TriggerSchedulingContractException)
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(lease,
                    TriggerFireAttemptResult.Permanent(TriggerFireFailureKind.StaleTrigger), cancellationToken);
            }
            bool matched;
            try { matched = adapter.Evaluate(definition, observation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(lease, Classify(exception), cancellationToken);
            }
            if (matched)
            {
                TriggerFireAttemptResult result;
                try { result = await participant.StageAsync(lease, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { result = Classify(exception); }
                if (result.Disposition != TriggerFireAttemptDisposition.Succeeded)
                {
                    await RollbackAndClearAsync(transaction);
                    return await FinishFailureAsync(lease, Normalize(result), cancellationToken);
                }
            }
            var now = UtcNow();
            if (!await LeaseCurrentAsync(lease, cancellationToken))
                throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE",
                    "The observation match lease expired before commit.");
            db.ObservationTriggerMatchReceipts.Add(new ObservationTriggerMatchReceiptRecord
            {
                Id = lease.FireId, ApplicationId = lease.ApplicationId.Value, TriggerId = lease.TriggerId,
                TriggerVersion = lease.TriggerVersion, ObservationId = lease.ObservationId!,
                Disposition = matched ? "matched" : "not-matched", RecordedAtUtc = now.UtcDateTime
            });
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_observation_match_work SET State = 'completed', NextAttemptAtUtc = NULL,
                    LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL, FailureKind = NULL,
                    Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {lease.FireId} AND State = 'leased' AND LeaseOwner = {lease.WorkerId}
                  AND LeaseToken = {lease.LeaseToken} AND LeaseExpiresAtUtc > {now.UtcDateTime}
                """, cancellationToken);
            if (changed != 1) throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE",
                "The observation match lease changed before commit.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Outcome.Completed;
        }
        catch (OperationCanceledException)
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackAndClearAsync(transaction);
            return await FinishFailureAsync(lease, Classify(exception), cancellationToken);
        }
    }

    private async Task<(ObservationTriggerDefinition Definition, ObservationMatchInput Input)> CurrentInputAsync(
        TriggerFireLease lease, CancellationToken cancellationToken)
    {
        var current = await db.ObservationTriggerCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.Id == lease.TriggerId, cancellationToken);
        if (current?.CurrentVersion != lease.TriggerVersion) throw Stale();
        var row = await db.ObservationTriggers.AsNoTracking().Include(value => value.NotificationEntities)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        if (row is null || row.Lifecycle != "active") throw Stale();
        var sourceCurrent = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == row.ApplicationId && value.Id == row.SourceId, cancellationToken);
        var source = await db.TriggerObservationSources.AsNoTracking().Include(value => value.AllowedStructures)
            .SingleOrDefaultAsync(value => value.ApplicationId == row.ApplicationId && value.Id == row.SourceId &&
                value.Version == row.SourceVersion, cancellationToken);
        var structureCurrent = await db.TriggerObservationStructureCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == row.ApplicationId && value.Id == row.StructureId, cancellationToken);
        var structure = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == row.ApplicationId && value.Id == row.StructureId &&
                value.Version == row.StructureVersion, cancellationToken);
        if (sourceCurrent?.CurrentVersion != row.SourceVersion || source?.Status != "enabled" ||
            !source.AllowedStructures.Any(value => value.StructureId == row.StructureId &&
                value.StructureVersion == row.StructureVersion) ||
            structureCurrent?.CurrentVersion != row.StructureVersion || structure?.Status != "active" ||
            structure.SchemaHash != row.StructureHash) throw Stale();
        var observation = await db.TriggerObservations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == lease.ObservationId && value.ApplicationId == row.ApplicationId &&
            value.SourceId == row.SourceId && value.SourceVersion == row.SourceVersion &&
            value.StructureId == row.StructureId && value.StructureVersion == row.StructureVersion &&
            value.StructureHash == row.StructureHash, cancellationToken);
        if (observation is null) throw Stale();
        var canonical = ObservationDataCanonicalizer.ParseObject(observation.DataJson);
        if (canonical.Hash != observation.DataHash) throw Stale();
        return (SqliteObservationTriggerStore.Definition(row), new ObservationMatchInput(
            lease.ApplicationId, observation.Id, observation.SourceId, observation.SourceVersion,
            observation.StructureId, observation.StructureVersion, observation.StructureHash, canonical));
    }

    private async Task<Outcome> FinishFailureAsync(TriggerFireLease lease,
        TriggerFireAttemptResult result, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var transient = result.Disposition == TriggerFireAttemptDisposition.TransientFailure;
        var retry = transient && lease.Attempt < MaximumAttempts;
        var failure = retry ? Failure(result.FailureKind ?? TriggerFireFailureKind.HandlerUnavailable)
            : transient ? "attempts-exhausted" : Failure(result.FailureKind ?? TriggerFireFailureKind.PermanentHandler);
        var state = retry ? "retry" : "failed";
        var next = retry ? now.Add(lease.Attempt == 1 ? FirstRetryDelay : SecondRetryDelay).UtcDateTime : (DateTime?)null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_observation_match_work SET State = {state}, NextAttemptAtUtc = {next},
                    LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL, FailureKind = {failure},
                    Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {lease.FireId} AND State = 'leased' AND LeaseOwner = {lease.WorkerId}
                  AND LeaseToken = {lease.LeaseToken} AND LeaseExpiresAtUtc > {now.UtcDateTime}
                """, cancellationToken);
            if (changed != 1) { await transaction.RollbackAsync(cancellationToken); return Outcome.None; }
            await transaction.CommitAsync(cancellationToken);
            return retry ? Outcome.Retried : Outcome.Failed;
        }
        catch { await RollbackAndClearAsync(transaction); throw; }
    }

    private Task<bool> LeaseCurrentAsync(TriggerFireLease lease, CancellationToken cancellationToken) =>
        db.ObservationTriggerMatchWork.AsNoTracking().AnyAsync(value => value.FireId == lease.FireId &&
            value.State == "leased" && value.LeaseOwner == lease.WorkerId && value.LeaseToken == lease.LeaseToken &&
            value.LeaseExpiresAtUtc > UtcNow().UtcDateTime, cancellationToken);

    private Task CloseStaleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_observation_match_work SET State = 'failed', NextAttemptAtUtc = NULL,
                LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
                FailureKind = 'stale-trigger', Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId IN (SELECT work.FireId FROM trigger_observation_match_work work
                LEFT JOIN trigger_observation_match_current current ON current.ApplicationId = work.ApplicationId
                    AND current.Id = work.TriggerId
                LEFT JOIN trigger_observation_match_definition definition ON definition.ApplicationId = work.ApplicationId
                    AND definition.Id = work.TriggerId AND definition.Version = work.TriggerVersion
                WHERE work.State IN ('ready', 'retry', 'leased') AND
                    (current.CurrentVersion IS NULL OR current.CurrentVersion <> work.TriggerVersion OR
                     definition.Lifecycle <> 'active')
                ORDER BY work.UpdatedAtUtc, work.FireId LIMIT {MaximumBatchSize})
            """, cancellationToken);

    private Task CloseExhaustedAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_observation_match_work SET State = 'failed', LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = 'attempts-exhausted', Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId IN (SELECT FireId FROM trigger_observation_match_work
                WHERE State = 'leased' AND AttemptCount >= {MaximumAttempts} AND LeaseExpiresAtUtc <= {now.UtcDateTime}
                ORDER BY LeaseExpiresAtUtc, FireId LIMIT {MaximumBatchSize})
            """, cancellationToken);

    private async Task RollbackAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
        finally { db.ChangeTracker.Clear(); }
    }
    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero) throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC",
            "The trigger clock must use UTC.");
        return now;
    }
    private static TriggerSchedulingContractException Stale() => new("OBSERVATION_TRIGGER_STALE",
        "The observation trigger, source, structure, or observation is stale.");
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
        TriggerFireFailureKind.StaleTrigger => "stale-trigger",
        TriggerFireFailureKind.AttemptsExhausted => "attempts-exhausted",
        _ => "permanent-handler"
    };
    private static void ValidateWorkerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or ':' or '-')))
            throw new TriggerSchedulingContractException("TRIGGER_WORKER_ID", "The worker ID is invalid.");
    }
    private enum Outcome { None, Completed, Retried, Failed }
}
