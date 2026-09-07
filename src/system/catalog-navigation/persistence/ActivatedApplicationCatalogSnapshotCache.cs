using DantesRoleplay.Applications;

namespace DantesRoleplay.CatalogNavigation;

/// <summary>Identifies the database/host boundary within which prepared catalog snapshots may be reused.</summary>
internal sealed class ActivatedApplicationCatalogCacheAuthority;

/// <summary>
/// Bounded process-local reuse for immutable prepared snapshots. Entries are replaced, not accumulated,
/// when one application's verified preparation fingerprint changes.
/// </summary>
internal sealed class ActivatedApplicationCatalogSnapshotCache
{
    private const int MaximumApplicationsPerAuthority = 100;
    private readonly object _gate = new();
    private readonly Dictionary<(ActivatedApplicationCatalogCacheAuthority Authority,
        ApplicationIdentifier ApplicationId), Entry> _entries = [];
    private int _hits;
    private int _misses;

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int Hits => Volatile.Read(ref _hits);
    internal int Misses => Volatile.Read(ref _misses);

    internal ActiveCatalogFeatureSnapshot GetOrCreate(
        ActivatedApplicationCatalogCacheAuthority authority,
        ApplicationIdentifier applicationId,
        string preparationFingerprint,
        Func<ActiveCatalogFeatureSnapshot> factory)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparationFingerprint);
        ArgumentNullException.ThrowIfNull(factory);
        Lazy<ActiveCatalogFeatureSnapshot> value;
        lock (_gate)
        {
            var key = (authority, applicationId);
            if (_entries.TryGetValue(key, out var current)
                && current.PreparationFingerprint == preparationFingerprint)
            {
                Interlocked.Increment(ref _hits);
                value = current.Value;
            }
            else
            {
                Interlocked.Increment(ref _misses);
                value = new(factory, LazyThreadSafetyMode.ExecutionAndPublication);
                if (_entries.ContainsKey(key) || _entries.Keys.Count(existing =>
                        ReferenceEquals(existing.Authority, authority)) < MaximumApplicationsPerAuthority)
                    _entries[key] = new(preparationFingerprint, value);
            }
        }
        try { return value.Value; }
        catch
        {
            lock (_gate)
            {
                var key = (authority, applicationId);
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value, value))
                    _entries.Remove(key);
            }
            throw;
        }
    }

    private sealed record Entry(string PreparationFingerprint, Lazy<ActiveCatalogFeatureSnapshot> Value);
}
