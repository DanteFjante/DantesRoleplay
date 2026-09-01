using DantesRoleplay.Applications;

namespace DantesRoleplay.CatalogNavigation;

/// <summary>
/// Supplies manifests already filtered to the anonymous/public authorization scope. Counts,
/// search, and cursors are built only after this boundary. A private or unclassified manifest
/// must never be returned here.
/// </summary>
public interface IPublicApplicationCatalogProvider
{
    bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator);
}

/// <summary>One recorded reason a published catalog could not be materialized.</summary>
public sealed record PublicApplicationCatalogFailure(string Code, string Message);

/// <summary>
/// Reports why the most recent materialization attempt for a published application failed. A
/// provider that cannot fail, or that has not yet failed, reports nothing. This exists so an
/// unavailable catalog names its own cause instead of being an undiagnosable dead end.
/// </summary>
public interface IPublicApplicationCatalogDiagnostics
{
    PublicApplicationCatalogFailure? LastFailure(ApplicationIdentifier applicationId);
}

/// <summary>
/// Host-owned publication policy. Application registration, activation, source trust, and loopback
/// access do not imply that an application catalog is public.
/// </summary>
public interface IPublicApplicationCatalogPolicy
{
    bool IsPublished(ApplicationIdentifier applicationId);
}

public sealed class EmptyPublicApplicationCatalogPolicy : IPublicApplicationCatalogPolicy
{
    public bool IsPublished(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return false;
    }
}

public sealed class ConfiguredPublicApplicationCatalogPolicy : IPublicApplicationCatalogPolicy
{
    private readonly HashSet<ApplicationIdentifier> _published;

    public ConfiguredPublicApplicationCatalogPolicy(IEnumerable<string>? applicationIds)
    {
        var values = (applicationIds ?? []).ToArray();
        if (values.Length > 100)
            throw new ArgumentException("At most 100 application catalogs may be published.", nameof(applicationIds));
        _published = values.Select(ApplicationIdentifier.Parse).ToHashSet();
        if (_published.Count != values.Length)
            throw new ArgumentException("Published application catalog IDs must be unique.", nameof(applicationIds));
    }

    public bool IsPublished(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return _published.Contains(applicationId);
    }
}

/// <summary>Safe host default until activation and authorization explicitly publish a manifest.</summary>
public sealed class EmptyPublicApplicationCatalogProvider : IPublicApplicationCatalogProvider
{
    public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        navigator = null!;
        return false;
    }
}

/// <summary>Bounded fixture/host adapter for explicitly public immutable navigators.</summary>
public sealed class InMemoryPublicApplicationCatalogProvider : IPublicApplicationCatalogProvider
{
    private readonly IReadOnlyDictionary<ApplicationIdentifier, ICatalogNavigator> _navigators;

    public InMemoryPublicApplicationCatalogProvider(IReadOnlyDictionary<ApplicationIdentifier, ICatalogNavigator> navigators)
    {
        ArgumentNullException.ThrowIfNull(navigators);
        if (navigators.Count > CatalogNavigationLimits.MaximumCollections || navigators.Any(pair => pair.Key is null || pair.Value is null))
            throw new ArgumentException("The public application-catalog set is invalid or unbounded.", nameof(navigators));
        _navigators = new Dictionary<ApplicationIdentifier, ICatalogNavigator>(navigators);
    }

    public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return _navigators.TryGetValue(applicationId, out navigator!);
    }
}
