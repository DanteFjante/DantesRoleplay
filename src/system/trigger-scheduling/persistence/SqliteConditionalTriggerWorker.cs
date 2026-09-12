using System.Data.Common;
using System.Security.Cryptography;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteConditionalTriggerWorker(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    ITriggerFireTransactionParticipant participant,
    IApplicationObserverPredicateEvaluator? predicateEvaluator = null) : IConditionalTriggerWorker
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
        var candidates = await db.ConditionalTriggerFireWork.AsNoTracking()
            .Where(value => value.State == "ready" || value.State == "retry" && value.NextAttemptAtUtc <= now.UtcDateTime ||
                value.State == "leased" && value.LeaseExpiresAtUtc <= now.UtcDateTime)
            .OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.FireId)
            .Take(MaximumBatchSize).ToListAsync(cancellationToken);
        var claimed = 0; var completed = 0; var retried = 0; var failed = 0;
        foreach (var candidate in candidates)
        {
            if (!participant.IsAvailable) break;
            var lease = await TryClaimAsync(candidate, workerId, now, cancellationToken);
            if (lease is null) continue;
            claimed++;
            var result = await ExecuteAsync(lease, cancellationToken);
            completed += result == Outcome.Completed ? 1 : 0;
            retried += result == Outcome.Retried ? 1 : 0;
            failed += result == Outcome.Failed ? 1 : 0;
        }
        return new(candidates.Count, claimed, completed, 0, retried, failed);
    }

    private async Task<TriggerFireLease?> TryClaimAsync(ConditionalTriggerFireWorkRecord work,
        string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var targetName = await db.ConditionalTriggers.AsNoTracking()
            .Where(value => value.ApplicationId == work.ApplicationId && value.Id == work.TriggerId &&
                value.Version == work.TriggerVersion)
            .Select(value => value.Target).SingleOrDefaultAsync(cancellationToken);
        if (targetName is null) return null;
        var target = targetName == "procedure-workflow"
            ? TriggerFireTarget.ProcedureWorkflow : TriggerFireTarget.NotificationOnly;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expires = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE trigger_conditional_fire_work
                SET State = 'leased', AttemptCount = AttemptCount + 1, NextAttemptAtUtc = NULL,
                    LeaseOwner = {workerId}, LeaseToken = {token}, LeaseExpiresAtUtc = {expires.UtcDateTime},
                    FailureKind = NULL, Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
                WHERE FireId = {work.FireId} AND AttemptCount < {MaximumAttempts}
                  AND NOT EXISTS (SELECT 1 FROM trigger_conditional_fire_receipt receipt WHERE receipt.Id = {work.FireId})
                  AND (State = 'ready' OR State = 'retry' AND NextAttemptAtUtc <= {now.UtcDateTime}
                       OR State = 'leased' AND LeaseExpiresAtUtc <= {now.UtcDateTime})
                """, cancellationToken);
            if (changed != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
            var attempt = await db.ConditionalTriggerFireWork.AsNoTracking()
                .Where(value => value.FireId == work.FireId && value.LeaseToken == token)
                .Select(value => value.AttemptCount).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TriggerFireLease(work.FireId, ApplicationIdentifier.Parse(work.ApplicationId),
                work.TriggerId, work.TriggerVersion,
                new DateTimeOffset(DateTime.SpecifyKind(work.CreatedAtUtc, DateTimeKind.Utc)),
                TriggerMisfirePolicy.FireOnce, target, attempt,
                workerId, token, expires)
            {
                ScheduleKind = TriggerScheduleKind.Conditional,
                ChangeOperationId = work.ChangeOperationId,
                AdmittedAt = new DateTimeOffset(DateTime.SpecifyKind(work.CreatedAtUtc, DateTimeKind.Utc))
            };
        }
        catch { await RollbackAndClearAsync(transaction); throw; }
    }

    private async Task<Outcome> ExecuteAsync(TriggerFireLease lease, CancellationToken cancellationToken)
    {
        PredicateEvaluation? predicate = null;
        try { predicate = await EvaluatePredicateAsync(lease, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return await FinishFailureAsync(lease, TriggerFireAttemptResult.Permanent(), cancellationToken); }
        if (predicate is { Evaluated: false })
            return await FinishFailureAsync(lease, TriggerFireAttemptResult.Permanent(), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LeaseCurrentAsync(lease, cancellationToken))
            { await transaction.RollbackAsync(cancellationToken); return Outcome.None; }
            if (predicate is not null && !await StagePredicateStateAsync(lease, predicate.Matches,
                    cancellationToken))
            {
                var finished = await CompleteAsync(lease, "not-matched", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return finished ? Outcome.Completed : Outcome.None;
            }
            TriggerFireAttemptResult result;
            try { result = await participant.StageAsync(lease, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { result = Classify(exception); }
            if (result.Disposition != TriggerFireAttemptDisposition.Succeeded)
            {
                await RollbackAndClearAsync(transaction);
                return await FinishFailureAsync(lease, Normalize(result), cancellationToken);
            }
            if (!await CompleteAsync(lease, "due", cancellationToken))
                throw new TriggerSchedulingContractException("TRIGGER_LEASE_STALE",
                    "The conditional trigger lease changed before commit.");
            await transaction.CommitAsync(cancellationToken);
            return Outcome.Completed;
        }
        catch { await RollbackAndClearAsync(transaction); throw; }
    }

    private async Task<PredicateEvaluation?> EvaluatePredicateAsync(TriggerFireLease lease,
        CancellationToken cancellationToken)
    {
        var work = await db.ConditionalTriggerFireWork.AsNoTracking().SingleAsync(value =>
            value.FireId == lease.FireId, cancellationToken);
        if (work.PredicateCaptureJson is null) return null;
        if (predicateEvaluator is null || work.PredicateCaptureFingerprint is null)
            return new(false, false);
        var row = await db.ConditionalTriggers.AsNoTracking().Include(value => value.WorkflowBinding)
            .Include(value => value.PredicateBinding)
            .SingleOrDefaultAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        if (row?.WorkflowBinding is null || row.PredicateBinding is null) return new(false, false);
        var target = TriggerProcedureWorkflowBindingPersistence.Materialize(row.Id, row.Version,
            lease.FireId, lease.AdmittedAt ?? lease.OccurrenceAt, row.WorkflowBinding);
        if (target is null) return new(false, false);
        var workflowHost = target.Submission.InvocationHost;
        var freshHost = new InteractionInvocationHost(workflowHost.Principal,
            workflowHost.ApplicationRevision, workflowHost.StateSpaceId!, workflowHost.GrantReference,
            "predicate." + lease.FireId[13..], workflowHost.StateRevision!,
            InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, workflowHost.Budget.DeadlineUtc));
        var captured = ConditionalTriggerPredicatePersistence.DeserializeCapture(
            work.PredicateCaptureJson, work.PredicateCaptureFingerprint);
        if (!ConditionalTriggerPredicatePersistence.SameSelection(row.PredicateBinding,
                captured.Selection))
            return new(false, false);
        var result = await predicateEvaluator.EvaluateAsync(freshHost, captured, cancellationToken);
        return new(result.Evaluated, result.Matches);
    }

    private async Task<bool> StagePredicateStateAsync(TriggerFireLease lease, bool truth,
        CancellationToken cancellationToken)
    {
        var work = await db.ConditionalTriggerFireWork.AsNoTracking().SingleAsync(value =>
            value.FireId == lease.FireId, cancellationToken);
        var definitionRow = await db.ConditionalTriggers.AsNoTracking()
            .Include(value => value.Dependencies).Include(value => value.RelationshipDependencies)
            .Include(value => value.NotificationEntities).Include(value => value.PredicateBinding)
            .SingleAsync(value => value.ApplicationId == lease.ApplicationId.Value &&
                value.Id == lease.TriggerId && value.Version == lease.TriggerVersion, cancellationToken);
        var definition = SqliteConditionalTriggerStore.Definition(definitionRow);
        var state = await db.ConditionalTriggerState.SingleAsync(value =>
            value.ApplicationId == lease.ApplicationId.Value && value.TriggerId == lease.TriggerId,
            cancellationToken);
        var currentRevision = state.CurrentVersion == lease.TriggerVersion;
        var priorTruth = currentRevision ? state.CurrentTruth : work.PredicatePriorTruth;
        var armed = currentRevision ? state.Armed : work.PredicatePriorArmed == true;
        var fire = armed && truth && (definition.Activation == ConditionalTriggerActivation.Level || priorTruth != true);
        if (currentRevision)
        {
            if (fire) armed = false;
            else if (!truth && definition.Rearm == ConditionalTriggerRearm.OnFalse) armed = true;
            state.CurrentTruth = truth;
            state.Armed = armed;
            state.EvaluationRevision++;
            state.LastOperationId = lease.ChangeOperationId;
            if (fire) state.LastFiredOperationId = lease.ChangeOperationId;
            state.UpdatedAtUtc = UtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }
        return fire;
    }

    private async Task<bool> CompleteAsync(TriggerFireLease lease, string disposition,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (!await LeaseCurrentAsync(lease, cancellationToken)) return false;
        db.ConditionalTriggerFireReceipts.Add(new ConditionalTriggerFireReceiptRecord
        {
            Id = lease.FireId, ApplicationId = lease.ApplicationId.Value,
            TriggerId = lease.TriggerId, TriggerVersion = lease.TriggerVersion,
            ChangeOperationId = lease.ChangeOperationId!, Disposition = disposition,
            RecordedAtUtc = now.UtcDateTime
        });
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_conditional_fire_work SET State = 'completed', NextAttemptAtUtc = NULL,
                LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL, FailureKind = NULL,
                Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId = {lease.FireId} AND State = 'leased' AND LeaseOwner = {lease.WorkerId}
              AND LeaseToken = {lease.LeaseToken} AND LeaseExpiresAtUtc > {now.UtcDateTime}
            """, cancellationToken);
        if (changed != 1) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
                UPDATE trigger_conditional_fire_work SET State = {state}, NextAttemptAtUtc = {next},
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
        db.ConditionalTriggerFireWork.AsNoTracking().AnyAsync(value => value.FireId == lease.FireId &&
            value.State == "leased" && value.LeaseOwner == lease.WorkerId && value.LeaseToken == lease.LeaseToken &&
            value.LeaseExpiresAtUtc > UtcNow().UtcDateTime, cancellationToken);

    private Task CloseStaleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_conditional_fire_work SET State = 'failed', NextAttemptAtUtc = NULL,
                LeaseOwner = NULL, LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
                FailureKind = 'stale-trigger', Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId IN (SELECT work.FireId FROM trigger_conditional_fire_work work
                LEFT JOIN trigger_conditional_definition definition ON definition.ApplicationId = work.ApplicationId
                    AND definition.Id = work.TriggerId AND definition.Version = work.TriggerVersion
                WHERE work.State IN ('ready', 'retry', 'leased') AND definition.ApplicationId IS NULL
                ORDER BY work.UpdatedAtUtc, work.FireId LIMIT {MaximumBatchSize})
            """, cancellationToken);

    private Task CloseExhaustedAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_conditional_fire_work SET State = 'failed', LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = 'attempts-exhausted', Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE FireId IN (SELECT FireId FROM trigger_conditional_fire_work
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
        if (now.Offset != TimeSpan.Zero) throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
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
    private sealed record PredicateEvaluation(bool Evaluated, bool Matches);
}
