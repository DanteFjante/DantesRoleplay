using System.Security.Cryptography;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

internal sealed class SqliteScheduledAiTaskWorkStore(
    DantesRoleplayDbContext db,
    TimeProvider timeProvider)
{
    internal const int MaximumBatchSize = 8;
    internal const int MaximumAttempts = 3;
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(30);

    internal async Task<ScheduledAiTaskClaimBatch> ClaimBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkerId(workerId);
        var now = timeProvider.GetUtcNow();
        await DiscoverAsync(now, cancellationToken);
        var exhausted = await FindExhaustedAsync(now, cancellationToken);
        db.ChangeTracker.Clear();
        var utc = now.UtcDateTime;
        var candidates = await db.ScheduledAiTaskWork.AsNoTracking()
            .Where(value => value.AttemptCount < MaximumAttempts &&
                (
                value.State == "ready" ||
                value.State == "retry" && value.NextAttemptAtUtc <= utc ||
                value.State == "leased" && value.LeaseExpiresAtUtc <= utc))
            .OrderBy(value => value.EnqueuedAtUtc)
            .ThenBy(value => value.NotificationId)
            .Take(MaximumBatchSize - exhausted.Count)
            .Select(value => new Candidate(
                value.NotificationId, value.State, value.EnqueuedAtUtc))
            .ToArrayAsync(cancellationToken);
        var leases = new List<ScheduledAiTaskLease>(candidates.Length);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await TryClaimAsync(candidate, workerId, now, cancellationToken);
            if (lease is not null) leases.Add(lease);
        }
        return new(candidates.Length + exhausted.Count, leases.AsReadOnly(), exhausted);
    }

    internal async Task<bool> FinishAsync(
        ScheduledAiTaskLease lease,
        ScheduledAiTaskExecutionOutcome outcome,
        long providerDurationMilliseconds,
        string failureKind,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        if (outcome is not (ScheduledAiTaskExecutionOutcome.Completed or
            ScheduledAiTaskExecutionOutcome.Retried or ScheduledAiTaskExecutionOutcome.Failed))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        var now = timeProvider.GetUtcNow();
        var retry = outcome == ScheduledAiTaskExecutionOutcome.Retried && lease.Attempt < MaximumAttempts;
        var state = outcome == ScheduledAiTaskExecutionOutcome.Completed
            ? "completed"
            : retry ? "retry" : "failed";
        var next = retry
            ? now.Add(lease.Attempt == 1 ? FirstRetryDelay : SecondRetryDelay).UtcDateTime
            : (DateTime?)null;
        var terminalAt = state == "retry" ? (DateTime?)null : now.UtcDateTime;
        var storedFailureKind = state == "completed" ? null : Bound(failureKind, 100);
        var storedFailureMessage = state == "completed" ? null : Bound(failureMessage, 500);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE scheduled_ai_task_work
            SET State = {state}, NextAttemptAtUtc = {next}, LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = {storedFailureKind},
                FailureMessage = {storedFailureMessage},
                ProviderDurationMilliseconds = {Math.Max(0, providerDurationMilliseconds)},
                Revision = Revision + 1, UpdatedAtUtc = {now.UtcDateTime},
                CompletedAtUtc = {terminalAt}
            WHERE NotificationId = {lease.NotificationId} AND State = 'leased'
              AND LeaseOwner = {lease.WorkerId} AND LeaseToken = {lease.LeaseToken}
              AND LeaseExpiresAtUtc > {now.UtcDateTime}
            """, cancellationToken);
        return changed == 1;
    }

    internal async Task<bool> RenewAsync(
        ScheduledAiTaskLease lease,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.Add(LeaseDuration);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE scheduled_ai_task_work
            SET LeaseExpiresAtUtc = {expires.UtcDateTime}, Revision = Revision + 1,
                UpdatedAtUtc = {now.UtcDateTime}
            WHERE NotificationId = {lease.NotificationId} AND State = 'leased'
              AND AttemptCount = {lease.Attempt} AND LeaseOwner = {lease.WorkerId}
              AND LeaseToken = {lease.LeaseToken} AND LeaseExpiresAtUtc > {now.UtcDateTime}
            """, cancellationToken);
        return changed == 1;
    }

    internal async Task<bool> FinishExhaustedAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE scheduled_ai_task_work
            SET State = 'failed', NextAttemptAtUtc = NULL, LeaseOwner = NULL, LeaseToken = NULL,
                LeaseExpiresAtUtc = NULL, FailureKind = 'attempts-exhausted',
                FailureMessage = 'The final worker lease expired before a result committed.',
                ProviderDurationMilliseconds = 0, Revision = Revision + 1,
                UpdatedAtUtc = {now}, CompletedAtUtc = {now}
            WHERE NotificationId = {notificationId} AND State = 'leased'
              AND AttemptCount >= {MaximumAttempts} AND LeaseExpiresAtUtc <= {now}
            """, cancellationToken);
        return changed == 1;
    }

    private async Task DiscoverAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unread = NotificationState.Unread.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT OR IGNORE INTO scheduled_ai_task_work
                (NotificationId, State, AttemptCount, NextAttemptAtUtc, LeaseOwner, LeaseToken,
                 LeaseExpiresAtUtc, FailureKind, FailureMessage, QueueAgeMilliseconds,
                 ProviderDurationMilliseconds, Revision, EnqueuedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            SELECT Id, 'ready', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0,
                   CreatedAt, {now.UtcDateTime}, NULL
            FROM notification
            WHERE Topic = {ScheduledAiTaskProtocol.Topic} AND State = {unread}
            """, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FindExhaustedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var utc = now.UtcDateTime;
        return await db.ScheduledAiTaskWork.AsNoTracking()
            .Where(value => value.State == "leased" && value.AttemptCount >= MaximumAttempts &&
                value.LeaseExpiresAtUtc <= utc)
            .OrderBy(value => value.LeaseExpiresAtUtc)
            .ThenBy(value => value.NotificationId)
            .Take(MaximumBatchSize)
            .Select(value => value.NotificationId)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<ScheduledAiTaskLease?> TryClaimAsync(
        Candidate candidate,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expires = now.Add(LeaseDuration);
        var queueAge = Milliseconds(now - new DateTimeOffset(
            DateTime.SpecifyKind(candidate.EnqueuedAtUtc, DateTimeKind.Utc)));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE scheduled_ai_task_work
                SET State = 'leased', AttemptCount = AttemptCount + 1, NextAttemptAtUtc = NULL,
                    LeaseOwner = {workerId}, LeaseToken = {token},
                    LeaseExpiresAtUtc = {expires.UtcDateTime}, FailureKind = NULL,
                    FailureMessage = NULL, QueueAgeMilliseconds = {queueAge},
                    ProviderDurationMilliseconds = NULL, Revision = Revision + 1,
                    UpdatedAtUtc = {now.UtcDateTime}, CompletedAtUtc = NULL
                WHERE NotificationId = {candidate.NotificationId} AND AttemptCount < {MaximumAttempts}
                  AND (State = 'ready' OR
                       State = 'retry' AND NextAttemptAtUtc <= {now.UtcDateTime} OR
                       State = 'leased' AND LeaseExpiresAtUtc <= {now.UtcDateTime})
                """, cancellationToken);
            if (changed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            var attempt = await db.ScheduledAiTaskWork.AsNoTracking()
                .Where(value => value.NotificationId == candidate.NotificationId &&
                    value.LeaseToken == token)
                .Select(value => value.AttemptCount)
                .SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(candidate.NotificationId, attempt, workerId, token, expires, queueAge,
                candidate.State == "leased");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    internal static long Milliseconds(TimeSpan value) =>
        Math.Max(0, checked((long)Math.Round(value.TotalMilliseconds,
            MidpointRounding.AwayFromZero)));

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static void ValidateWorkerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not ('.' or '_' or ':' or '-')))
            throw new TriggerSchedulingContractException(
                "TRIGGER_WORKER_ID", "The scheduled AI worker ID is invalid.");
    }

    private sealed record Candidate(string NotificationId, string State, DateTime EnqueuedAtUtc);
}
