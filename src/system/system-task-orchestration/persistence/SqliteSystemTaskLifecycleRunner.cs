namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Owner-local execution loop. The callback is a trusted integration seam, not an authorization
/// adapter. Production workflow registration supplies plan 05's current-authority executor.
/// Every store call closes its transaction before the callback runs or the loop waits.
/// </summary>
internal sealed class SqliteSystemTaskLifecycleRunner
{
    private readonly SqliteSystemTaskLifecycleStore _store;
    private readonly TimeProvider _time;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _pollInterval;

    internal SqliteSystemTaskLifecycleRunner(SqliteSystemTaskLifecycleStore store, TimeProvider time,
        TimeSpan? leaseDuration = null, TimeSpan? pollInterval = null)
    {
        _store = store;
        _time = time;
        _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        if (_leaseDuration <= TimeSpan.Zero || _leaseDuration > TimeSpan.FromMinutes(10)
            || _pollInterval <= TimeSpan.Zero || _pollInterval >= _leaseDuration / 2)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    internal async Task<bool> RunOnceAsync(string workerId,
        Func<SystemTaskLease, CancellationToken, Task<SystemTaskRunOutcome>> execute,
        CancellationToken cancellationToken = default, SystemTaskPurpose? purpose = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var lease = purpose switch
        {
            SystemTaskPurpose.ApplicationValidation => await _store.ClaimNextValidationAsync(workerId, _leaseDuration, cancellationToken),
            SystemTaskPurpose.ProcedureWorkflow => await _store.ClaimNextWorkflowAsync(workerId, _leaseDuration, cancellationToken),
            _ => await _store.ClaimNextAsync(workerId, _leaseDuration, cancellationToken)
        };
        if (lease is null) return false;

        // Shutdown only abandons a lease. It is never converted into a user's durable cancellation.
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var monitoringCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitoring = MonitorAsync(lease, monitoringCancellation.Token);
        if (monitoring.IsCompleted)
        {
            await StopAsync(lease, await monitoring);
            return true;
        }
        var execution = Task.Run(() => execute(lease, executionCancellation.Token), executionCancellation.Token);
        try
        {
            if (await Task.WhenAny(execution, monitoring) == monitoring)
            {
                var reason = await monitoring;
                executionCancellation.Cancel();
                ObserveAbandoned(execution);
                await StopAsync(lease, reason);
                return true;
            }

            SystemTaskRunOutcome outcome;
            try { outcome = await execution; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception)
            {
                // An arbitrary exception may follow a committed host call. Only the executor may
                // classify a known transient failure; an exception is never a blind retry signal.
                outcome = new SystemTaskRunOutcome.Failed(SystemTaskFailureKind.Indeterminate,
                    "SYSTEM_TASK_EXECUTION_UNKNOWN", "Execution stopped without a classified outcome.");
            }

            // Recheck cancellation/deadline immediately before attempting fenced publication.
            var state = await _store.ReadAsync(lease.Request.Handle, CancellationToken.None);
            if (state?.CancellationRequested == true)
                await _store.AcknowledgeCancellationAsync(lease, CancellationToken.None);
            else if (_time.GetUtcNow().UtcDateTime >= lease.Request.Invocation.DeadlineUtc)
                await StopAsync(lease, StopReason.Deadline);
            else if (!cancellationToken.IsCancellationRequested)
            {
                switch (outcome)
                {
                    case SystemTaskRunOutcome.Completed completed:
                        await _store.CompleteAsync(lease, completed.Result, CancellationToken.None);
                        break;
                    case SystemTaskRunOutcome.Waiting waiting:
                        await _store.SaveWaitingAsync(lease, waiting.Checkpoint, CancellationToken.None);
                        break;
                    case SystemTaskRunOutcome.Failed failed:
                        await _store.FailAsync(lease, failed.Kind, failed.Code, failed.SafeMessage, CancellationToken.None);
                        break;
                    default:
                        await _store.FailAsync(lease, SystemTaskFailureKind.Indeterminate,
                            "SYSTEM_TASK_OUTCOME_INVALID", "Execution returned no supported outcome.", CancellationToken.None);
                        break;
                }
            }
            return true;
        }
        finally
        {
            monitoringCancellation.Cancel();
            executionCancellation.Cancel();
            try { await monitoring; }
            catch (OperationCanceledException) { }
            // Callback cancellation is cooperative. Never let its late result publish outside
            // this loop; the store also fences all callback journal writes by lease identity.
            ObserveAbandoned(execution);
        }
    }

    private async Task<StopReason> MonitorAsync(SystemTaskLease lease, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var remaining = lease.Request.Invocation.DeadlineUtc - _time.GetUtcNow().UtcDateTime;
                if (remaining <= TimeSpan.Zero) return StopReason.Deadline;
                var snapshot = await _store.ReadAsync(lease.Request.Handle, cancellationToken);
                if (snapshot?.CancellationRequested == true) return StopReason.CancellationRequested;
                if (!await _store.RenewAsync(lease, _leaseDuration, cancellationToken))
                {
                    // Cancellation can commit after the read above and make renewal fail.
                    // Reconcile that durable request before classifying genuine lease loss;
                    // never acknowledge a replacement worker's attempt with this old lease.
                    var current = await _store.ReadAsync(lease.Request.Handle, CancellationToken.None);
                    if (current?.CancellationRequested == true
                        && current.FencingCounter == lease.Attempt.FencingCounter
                        && current.AttemptCount == lease.AttemptOrdinal)
                        return StopReason.CancellationRequested;
                    return StopReason.LeaseLost;
                }
                await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, _time, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StopReason.Shutdown;
        }
        catch (Exception)
        {
            // A failed renewal is loss of authority to publish, not proof the computation failed.
            return StopReason.LeaseLost;
        }
    }

    private async Task StopAsync(SystemTaskLease lease, StopReason reason)
    {
        if (reason == StopReason.CancellationRequested)
            await _store.AcknowledgeCancellationAsync(lease, CancellationToken.None);
        else if (reason == StopReason.Deadline)
            await _store.FailAsync(lease, SystemTaskFailureKind.Permanent,
                "SYSTEM_TASK_DEADLINE_EXCEEDED", "The task deadline elapsed.", CancellationToken.None);
    }

    private static void ObserveAbandoned(Task execution) => _ = execution.ContinueWith(
        static task => { _ = task.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private enum StopReason { CancellationRequested, Deadline, LeaseLost, Shutdown }
}

internal abstract record SystemTaskRunOutcome
{
    internal sealed record Completed(SystemTaskTerminalOutcome Result) : SystemTaskRunOutcome;
    internal sealed record Waiting(SystemTaskCheckpoint Checkpoint) : SystemTaskRunOutcome;
    internal sealed record Failed(SystemTaskFailureKind Kind, string Code, string SafeMessage) : SystemTaskRunOutcome;
}
