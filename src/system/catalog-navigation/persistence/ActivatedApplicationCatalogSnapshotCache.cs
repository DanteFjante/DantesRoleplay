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
        Func<ActiveCatalogFeatureSnapshot> factory,
        Action? register = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparationFingerprint);
        ArgumentNullException.ThrowIfNull(factory);
        Entry entry;
        lock (_gate)
        {
            var key = (authority, applicationId);
            if (_entries.TryGetValue(key, out var current)
                && current.PreparationFingerprint == preparationFingerprint)
            {
                Interlocked.Increment(ref _hits);
                entry = current;
            }
            else
            {
                Interlocked.Increment(ref _misses);
                entry = new(preparationFingerprint, new(factory, LazyThreadSafetyMode.ExecutionAndPublication));
                if (_entries.ContainsKey(key) || _entries.Keys.Count(existing =>
                        ReferenceEquals(existing.Authority, authority)) < MaximumApplicationsPerAuthority)
                    _entries[key] = entry;
            }
        }
        ActiveCatalogFeatureSnapshot snapshot;
        try { snapshot = entry.Value.Value; }
        catch
        {
            lock (_gate)
            {
                var key = (authority, applicationId);
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
            }
            throw;
        }
        if (register is not null)
        {
            lock (entry.RegistrationGate)
            {
                if (!entry.Registered)
                {
                    // Registration failures leave the pure snapshot usable and retryable.
                    register();
                    entry.Registered = true;
                }
            }
        }
        return snapshot;
    }

    internal bool TryGetPrepared(ActivatedApplicationCatalogCacheAuthority authority, ApplicationIdentifier applicationId,
        string activationFingerprint, out ActiveCatalogFeatureSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue((authority, applicationId), out var entry) && entry.Value.IsValueCreated)
            {
                var prepared = entry.Value.Value; // IsValueCreated prevents invoking a cold Lazy factory.
                if (prepared.EffectiveSetFingerprint == activationFingerprint)
                {
                    snapshot = prepared;
                    return true;
                }
            }
        }
        snapshot = null!;
        return false;
    }

    private sealed record Entry(string PreparationFingerprint, Lazy<ActiveCatalogFeatureSnapshot> Value)
    {
        internal object RegistrationGate { get; } = new();
        internal bool Registered { get; set; }
    }
}
