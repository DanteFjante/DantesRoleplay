namespace DantesRoleplay.Interactions;

/// <summary>
/// Register one singleton per derived-index authority and share it between scoped retrievers.
/// Only bounded generation keys and completion signals are retained, never scoped services or work.
/// The owning caller executes and awaits its refresh within its own lifetime.
/// </summary>
public sealed class InteractionRetrievalRefreshCoordinator
{
    internal const int MaximumGenerations = 16;
    internal const int MaximumWaitersPerGeneration = 64;
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _active = new(StringComparer.Ordinal);

    internal async Task<InteractionFeatureRebuildResult> RunAsync(string generationKey,
        Func<Task<InteractionFeatureRebuildResult>> refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        bool owner;
        lock (_sync)
        {
            owner = !_active.TryGetValue(generationKey, out entry!);
            if (owner)
            {
                if (_active.Count >= MaximumGenerations) return Busy();
                entry = new Entry();
                _active.Add(generationKey, entry);
            }
            else
            {
                if (entry.Waiters >= MaximumWaitersPerGeneration) return Busy();
                entry.Waiters++;
            }
        }
        if (!owner)
        {
            try { return await entry.Completion.Task.WaitAsync(MaximumWait, cancellationToken); }
            catch (TimeoutException) { return Busy(); }
            finally { lock (_sync) entry.Waiters--; }
        }

        var result = new InteractionFeatureRebuildResult(false, 0, AvailabilityCode: "VECTOR_REFRESH_INTERRUPTED",
            AvailabilityMessage: "The owning refresh did not complete; lexical retrieval remains available.");
        try
        {
            result = await refresh();
            return result;
        }
        finally
        {
            lock (_sync)
            {
                entry.Completion.TrySetResult(result);
                _active.Remove(generationKey);
            }
        }
    }

    private static InteractionFeatureRebuildResult Busy() => new(false, 0,
        AvailabilityCode: "VECTOR_REFRESH_BUSY", AvailabilityMessage: "Refresh capacity or wait budget is exhausted; lexical retrieval remains available.");

    private sealed class Entry
    {
        internal readonly TaskCompletionSource<InteractionFeatureRebuildResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Waiters;
    }
}
