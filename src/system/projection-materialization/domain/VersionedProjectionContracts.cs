using System.Collections.ObjectModel;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Projections;

public sealed record ProjectionReference(string QualifiedId, int Version, string ContentHash)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QualifiedId) || QualifiedId.Length > 200 || Version < 1 || !Hash(ContentHash))
            throw new ArgumentException("A projection reference requires an exact ID, positive version, and uppercase SHA-256 content hash.");
    }
    internal static bool Hash(string value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
}

public sealed record ProjectionComponentInput(string InputId, string EntityRole, EcsComponentReference Type);
public sealed record ProjectionDependencyInput(string InputId, ProjectionReference Projection, IReadOnlyDictionary<string, string> RoleBindings);
public sealed record StructuralProjectionMapping(string InputId, string SourcePointer, string TargetPointer);

public sealed record ProjectionDefinitionRequest(
    ApplicationIdentifier Owner,
    string QualifiedId,
    string OutputSchemaJson,
    IReadOnlyList<ProjectionComponentInput> ComponentInputs,
    IReadOnlyList<ProjectionDependencyInput> DependencyInputs,
    IReadOnlyList<StructuralProjectionMapping> Mappings,
    ApplicationObjectContractRequest? ObjectContract = null,
    int? DeclaredVersion = null);

public sealed record RegisteredProjectionDefinition(
    ApplicationIdentifier Owner,
    string QualifiedId,
    int Version,
    string ProfileId,
    string OutputSchemaJson,
    string OutputSchemaHash,
    string ContentHash,
    IReadOnlyList<ProjectionComponentInput> ComponentInputs,
    IReadOnlyList<ProjectionDependencyInput> DependencyInputs,
    IReadOnlyList<StructuralProjectionMapping> Mappings,
    DateTime CreatedAtUtc,
    RegisteredApplicationObjectContract? ObjectContract = null)
{
    public ProjectionReference Reference => new(QualifiedId, Version, ContentHash);
    public IReadOnlyList<string> EntityRoles => ObjectContract is not null
        ? Array.AsReadOnly(ObjectContract.Roles.Select(x => x.RoleId).Order(StringComparer.Ordinal).ToArray())
        : Array.AsReadOnly(ComponentInputs.Select(x => x.EntityRole)
            .Concat(DependencyInputs.SelectMany(x => x.RoleBindings.Values)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
}

public sealed record ProjectionMaterializationRequest(string StateSpaceId, ProjectionReference Projection, IReadOnlyDictionary<string, string> RoleEntityIds);
public sealed record ProjectionSourceRevision(string EntityId, EcsComponentReference Type, int Revision);
public sealed record ProjectionMaterializationResult(ProjectionReference Projection, string OutputJson, IReadOnlyList<ProjectionSourceRevision> SourceRevisions);
public sealed record ProjectionCollectionMaterializationRequest(
    string StateSpaceId,
    ProjectionReference Projection,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string CollectionId,
    string Perspective,
    string? Cursor = null,
    int? PageSize = null);
public sealed record ProjectionCollectionMaterializationResult(
    ProjectionReference Projection,
    string OutputJson,
    IReadOnlyList<ProjectionSourceRevision> SourceRevisions,
    string SourceRevisionFingerprint)
{
    public IReadOnlyList<ProjectionRelationshipRevision> RelationshipRevisions { get; init; } = [];
    public bool Complete { get; init; } = true;
}
public sealed record ProjectionRelationshipRevision(
    string FromEntityId,
    string ToEntityId,
    string QualifiedKind,
    int Revision);
public sealed record ProjectionImpactGraph(IReadOnlyDictionary<string, IReadOnlyList<string>> Forward, IReadOnlyDictionary<string, IReadOnlyList<string>> Reverse);
public sealed record ProjectionSourceSnapshot(
    StateSpaceView StateSpace,
    IReadOnlyList<EcsComponentView> Components);
public sealed record ProjectionPlanCacheSnapshot(
    int RetainedPlans,
    int DeclarationBytes,
    int MappingNodes,
    long Preparations,
    long Hits,
    long Evictions);

public interface IProjectionDefinitionRegistry
{
    RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition);
    RegisteredProjectionDefinition? Get(string qualifiedId, int version);
    ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner);
}

public interface IProjectionMaterializer
{
    Task<ProjectionMaterializationResult> MaterializeAsync(ProjectionMaterializationRequest request, CancellationToken cancellationToken = default);
}

public interface IProjectionCollectionMaterializer
{
    Task<ProjectionCollectionMaterializationResult> MaterializeAsync(
        ProjectionCollectionMaterializationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProjectionReadTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken = default);
}

/// <summary>Reports whether the scoped provider is currently serving one consistent read snapshot.</summary>
public interface IProjectionReadSnapshotStatus
{
    bool IsActive { get; }
    long Revision { get; }
}

/// <summary>One provider-owned read snapshot for a prepared projection's exact component set.</summary>
public interface IProjectionSourceSnapshotReader
{
    Task<ProjectionSourceSnapshot> ReadAsync(
        string stateSpaceId,
        ApplicationIdentifier expectedOwner,
        IReadOnlyList<EcsComponentLocator> locators,
        CancellationToken cancellationToken = default);
}

public interface IProjectionPlanCacheDiagnostics
{
    ProjectionPlanCacheSnapshot Snapshot { get; }
}
