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
    IReadOnlyList<StructuralProjectionMapping> Mappings);

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
    DateTime CreatedAtUtc)
{
    public ProjectionReference Reference => new(QualifiedId, Version, ContentHash);
    public IReadOnlyList<string> EntityRoles => Array.AsReadOnly(ComponentInputs.Select(x => x.EntityRole)
        .Concat(DependencyInputs.SelectMany(x => x.RoleBindings.Values)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}

public sealed record ProjectionMaterializationRequest(string StateSpaceId, ProjectionReference Projection, IReadOnlyDictionary<string, string> RoleEntityIds);
public sealed record ProjectionSourceRevision(string EntityId, EcsComponentReference Type, int Revision);
public sealed record ProjectionMaterializationResult(ProjectionReference Projection, string OutputJson, IReadOnlyList<ProjectionSourceRevision> SourceRevisions);
public sealed record ProjectionImpactGraph(IReadOnlyDictionary<string, IReadOnlyList<string>> Forward, IReadOnlyDictionary<string, IReadOnlyList<string>> Reverse);

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
