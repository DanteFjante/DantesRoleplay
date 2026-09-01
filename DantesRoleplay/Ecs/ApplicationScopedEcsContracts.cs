using DantesRoleplay.Applications;

namespace DantesRoleplay.Ecs;

public sealed record StateSpaceView(
    string StateSpaceId,
    ApplicationRevision ApplicationRevision,
    string ManifestFingerprint,
    int BindingRevision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public string ResolutionFingerprint { get; init; } = ManifestFingerprint;
    public EcsStateSpaceScope Scope { get; init; } = EcsStateSpaceScope.Runtime;
}

public sealed record StateSpaceDiscoveryPage(
    IReadOnlyList<StateSpaceView> StateSpaces,
    string? NextStateSpaceId);

public sealed record EcsEntityView(
    string StateSpaceId,
    string EntityId,
    string Name,
    int Revision,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc);

public sealed record EcsEntityDiscoveryPage(
    IReadOnlyList<EcsEntityView> Entities,
    string? NextEntityId);

public sealed record EcsComponentReference(string QualifiedTypeId, int TypeVersion, string SchemaHash)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QualifiedTypeId) || TypeVersion < 1
            || SchemaHash is not { Length: 64 }
            || !SchemaHash.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F'))
            throw new ArgumentException("A component reference requires an exact type ID, positive version, and uppercase SHA-256 hash.");
    }
}

public sealed record EcsComponentWrite(
    string StateSpaceId,
    string EntityId,
    EcsComponentReference Type,
    string ValueJson,
    int ExpectedRevision)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Type);
        Type.Validate();
        if (string.IsNullOrWhiteSpace(StateSpaceId) || StateSpaceId.Length > 200
            || string.IsNullOrWhiteSpace(EntityId) || EntityId.Length > 200
            || ExpectedRevision < 0)
            throw new ArgumentException("A component write requires state-space/entity IDs and a nonnegative expected revision.");
    }
}

public sealed record EcsComponentView(
    string StateSpaceId,
    string EntityId,
    EcsComponentReference Type,
    string ValueJson,
    int Revision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EcsComponentDiscoveryPage(
    IReadOnlyList<EcsComponentView> Components,
    string? NextQualifiedTypeId);

/// <summary>A bounded, exact component location used by structural read-only consumers.</summary>
public sealed record EcsComponentLocator(string EntityId, string QualifiedTypeId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EntityId) || EntityId.Length > 200
            || string.IsNullOrWhiteSpace(QualifiedTypeId) || QualifiedTypeId.Length > 200)
            throw new ArgumentException("A component locator requires bounded entity and type IDs.");
    }
}

public interface IStateSpaceRegistry
{
    StateSpaceView Create(StateSpaceBinding binding);
    StateSpaceView? Get(string stateSpaceId);
    StateSpaceDiscoveryPage ListPage(
        ApplicationIdentifier applicationId,
        string? afterStateSpaceId,
        int limit);
}

/// <summary>
/// A bounded discovery request over one state space. At least one of <see cref="NameQuery"/> or
/// <see cref="QualifiedTypeId"/> must be supplied; the search never returns component values.
/// </summary>
public sealed record EcsEntitySearch(
    string? NameQuery,
    string? QualifiedTypeId,
    string? AfterEntityId,
    int Limit)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NameQuery) && string.IsNullOrWhiteSpace(QualifiedTypeId))
            throw new ArgumentException("An entity search requires a name query or a component type filter.");
        if (NameQuery is { Length: > 200 } || QualifiedTypeId is { Length: > 200 }
            || AfterEntityId is { Length: > 200 })
            throw new ArgumentException("Entity search terms may not exceed 200 characters.");
        if (Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(Limit));
    }
}

/// <summary>
/// Optional bounded name/component discovery over one application state space. It is separate from
/// <see cref="IEntityComponentStore"/> so that a reader which cannot search stays honest about it:
/// a caller that does not find this interface must keep requiring exact IDs.
/// </summary>
public interface IEntityComponentSearchStore
{
    Task<EcsEntityDiscoveryPage> SearchEntitiesAsync(
        string stateSpaceId,
        EcsEntitySearch search,
        CancellationToken cancellationToken = default);
}

public interface IEntityComponentStore
{
    Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name, CancellationToken cancellationToken = default);
    Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken = default);
    Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId, int limit, CancellationToken cancellationToken = default);
    Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId, string qualifiedTypeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId, IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default);
    Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId, string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default);
    Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default);
    Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default);
    Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default);
    Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default);
}
