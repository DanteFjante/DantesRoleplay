using DantesRoleplay.Applications;

namespace DantesRoleplay.Ecs;

public sealed record EcsReferenceCount(string Kind, int Count);

public sealed record EcsComponentMigrationValue(
    string StateSpaceId,
    string EntityId,
    int ExpectedRevision,
    string ValueJson);

public sealed record ComponentTypeMigrationResult(
    string SourceQualifiedTypeId,
    string TargetQualifiedTypeId,
    int MigratedComponents,
    int RewrittenValues,
    IReadOnlyList<string> StateSpaceIds,
    ComponentTypeLifecycleView Target);

public sealed record RelationshipKindMigrationResult(
    string SourceQualifiedKind,
    string TargetQualifiedKind,
    int MigratedRelationships,
    IReadOnlyList<string> StateSpaceIds);

public sealed record RelationshipKindLifecycleView(
    string QualifiedKind,
    int References,
    IReadOnlyList<string> StateSpaceIds);

public sealed record ComponentTypeLifecycleView(
    ApplicationIdentifier Owner,
    string QualifiedTypeId,
    int LatestVersion,
    DateTime CreatedAtUtc,
    DateTime? DisabledAtUtc,
    IReadOnlyList<EcsReferenceCount> References)
{
    public bool IsEnabled => DisabledAtUtc is null;
}

public sealed record EntityLifecycleView(
    EcsEntityView Entity,
    IReadOnlyList<EcsReferenceCount> References)
{
    public bool IsEnabled => Entity.DeletedAtUtc is null;
}

public sealed class EcsLifecycleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Administrative lifecycle operations for ECS identities. Ordinary registries remain optimized
/// for active runtime records; this surface deliberately exposes disabled records and blockers.
/// </summary>
public interface IEcsLifecycleStore
{
    Task<EcsEntityDiscoveryPage> ListEntitiesIncludingDisabledAsync(
        string stateSpaceId,
        string? afterEntityId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<EcsComponentView?> GetComponentIncludingDisabledAsync(
        string stateSpaceId,
        string entityId,
        string qualifiedTypeId,
        CancellationToken cancellationToken = default);

    Task<ComponentTypeLifecycleView?> GetComponentTypeAsync(
        string qualifiedTypeId,
        CancellationToken cancellationToken = default);

    Task<RelationshipKindLifecycleView> GetRelationshipKindAsync(
        string qualifiedKind,
        CancellationToken cancellationToken = default);

    Task<ComponentTypeLifecycleView> RenameComponentTypeAsync(
        string qualifiedTypeId,
        string correctedQualifiedTypeId,
        CancellationToken cancellationToken = default);

    Task<ComponentTypeMigrationResult> MigrateComponentTypeAsync(
        string sourceQualifiedTypeId,
        string targetQualifiedTypeId,
        IReadOnlyList<EcsComponentMigrationValue>? rewrittenValues = null,
        CancellationToken cancellationToken = default);

    Task<RelationshipKindMigrationResult> MigrateRelationshipKindAsync(
        string sourceQualifiedKind,
        string targetQualifiedKind,
        CancellationToken cancellationToken = default);

    Task<ComponentTypeLifecycleView> SetComponentTypeEnabledAsync(
        string qualifiedTypeId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteComponentTypeAsync(
        string qualifiedTypeId,
        CancellationToken cancellationToken = default);

    Task<EntityLifecycleView?> GetEntityAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default);

    Task<EntityLifecycleView> UpdateEntityAsync(
        string stateSpaceId,
        string entityId,
        string correctedEntityId,
        string name,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<EntityLifecycleView> SetEntityEnabledAsync(
        string stateSpaceId,
        string entityId,
        bool enabled,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteEntityPermanentlyAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteEntityAndComponentsPermanentlyAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default);
}

public interface IEcsWriteTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IEcsWriteTransactionFactory
{
    Task<IEcsWriteTransaction> BeginAsync(CancellationToken cancellationToken = default);
}
