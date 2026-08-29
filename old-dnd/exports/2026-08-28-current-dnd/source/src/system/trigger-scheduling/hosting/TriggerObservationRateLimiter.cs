using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public sealed class InMemoryTriggerObservationRateLimiter(ITriggerClock clock)
    : ITriggerObservationRateLimiter
{
    public const int PrincipalRequestsPerMinute = 10;
    public const int PrincipalConcurrency = 2;
    private const int MaximumTrackedWindows = 4096;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);

    private readonly object sync = new();
    private readonly Dictionary<string, WindowCounter> principalWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowCounter> sourceWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> active = new(StringComparer.Ordinal);

    public ValueTask<ITriggerObservationRateLease?> TryAcquireAsync(
        string principalId,
        ApplicationIdentifier applicationId,
        string sourceId,
        int sourceRequestsPerMinute,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(principalId)) throw new ArgumentException("A principal is required.", nameof(principalId));
        ArgumentNullException.ThrowIfNull(applicationId);
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("A source is required.", nameof(sourceId));
        if (sourceRequestsPerMinute is < 1 or > PrincipalRequestsPerMinute)
            throw new ArgumentOutOfRangeException(nameof(sourceRequestsPerMinute));

        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        var sourceKey = string.Join('\0', principalId, applicationId.Value, sourceId);
        lock (sync)
        {
            if (active.GetValueOrDefault(principalId) >= PrincipalConcurrency)
                return ValueTask.FromResult<ITriggerObservationRateLease?>(null);
            var requiredSlots = (principalWindows.ContainsKey(principalId) ? 0 : 1) +
                (sourceWindows.ContainsKey(sourceKey) ? 0 : 1);
            if (principalWindows.Count + sourceWindows.Count + requiredSlots > MaximumTrackedWindows)
            {
                PruneExpired(now);
                requiredSlots = (principalWindows.ContainsKey(principalId) ? 0 : 1) +
                    (sourceWindows.ContainsKey(sourceKey) ? 0 : 1);
                if (principalWindows.Count + sourceWindows.Count + requiredSlots > MaximumTrackedWindows)
                    return ValueTask.FromResult<ITriggerObservationRateLease?>(null);
            }

            var principal = Current(principalWindows, principalId, now);
            var source = Current(sourceWindows, sourceKey, now);
            if (principal.Count >= PrincipalRequestsPerMinute || source.Count >= sourceRequestsPerMinute)
                return ValueTask.FromResult<ITriggerObservationRateLease?>(null);

            principal.Count++;
            source.Count++;
            active[principalId] = active.GetValueOrDefault(principalId) + 1;
            return ValueTask.FromResult<ITriggerObservationRateLease?>(new Lease(this, principalId));
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var key in principalWindows
                     .Where(value => now >= value.Value.StartedAt + WindowDuration)
                     .Select(value => value.Key).ToArray())
            principalWindows.Remove(key);
        foreach (var key in sourceWindows
                     .Where(value => now >= value.Value.StartedAt + WindowDuration)
                     .Select(value => value.Key).ToArray())
            sourceWindows.Remove(key);
    }

    private static WindowCounter Current(
        Dictionary<string, WindowCounter> windows,
        string key,
        DateTimeOffset now)
    {
        if (!windows.TryGetValue(key, out var value))
        {
            value = new WindowCounter(now);
            windows.Add(key, value);
        }
        else if (now >= value.StartedAt + WindowDuration)
        {
            value.StartedAt = now;
            value.Count = 0;
        }
        return value;
    }

    private void Release(string principalId)
    {
        lock (sync)
        {
            var count = active.GetValueOrDefault(principalId);
            if (count <= 1) active.Remove(principalId);
            else active[principalId] = count - 1;
        }
    }

    private sealed class WindowCounter(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }

    private sealed class Lease(InMemoryTriggerObservationRateLimiter owner, string principalId)
        : ITriggerObservationRateLease
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(principalId);
            return ValueTask.CompletedTask;
        }
    }
}
