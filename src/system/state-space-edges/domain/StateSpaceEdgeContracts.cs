namespace DantesRoleplay.Ecs;

public sealed record EcsContainmentView(
    string StateSpaceId,
    string ContainedEntityId,
    string ContainerEntityId,
    string Slot,
    int Revision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EcsContainmentDiscoveryPage(
    IReadOnlyList<EcsContainmentView> Containments,
    string? NextContainedEntityId);

public sealed record EcsRelationshipView(
    string StateSpaceId,
    string FromEntityId,
    string ToEntityId,
    string QualifiedKind,
    string DataJson,
    int Revision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IStateSpaceEdgeStore
{
    Task<EcsContainmentView?> GetContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EcsContainmentView>> ListContainmentsAsync(
        string stateSpaceId,
        CancellationToken cancellationToken = default);

    Task<EcsContainmentDiscoveryPage> ListContainmentsAsync(
        string stateSpaceId,
        string containerEntityId,
        string? afterContainedEntityId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<EcsContainmentView> MoveContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        string containerEntityId,
        string slot,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<EcsRelationshipView?> GetRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EcsRelationshipView>> ListRelationshipsAsync(
        string stateSpaceId,
        CancellationToken cancellationToken = default);

    Task<EcsRelationshipView> SetRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        string dataJson,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        int expectedRevision,
        CancellationToken cancellationToken = default);
}
