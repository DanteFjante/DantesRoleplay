using DantesRoleplay.Applications;

namespace DantesRoleplay.Projections;

public sealed record ProjectionImpactNode(
    string Id,
    string Kind,
    string QualifiedId,
    int Version,
    string ContractHash,
    string? Pointer);

public sealed record ProjectionImpactEdge(string DependencyId, string ConsumerId, string Reason);

public sealed record ProjectionImpactSnapshot(
    ApplicationIdentifier ApplicationId,
    IReadOnlyList<ProjectionImpactNode> Nodes,
    IReadOnlyList<ProjectionImpactEdge> Edges);

public sealed record ProjectionImpactRoot(
    string Id,
    string Kind,
    string QualifiedId,
    int Version,
    string ContractHash,
    string? Pointer);

public sealed record ProjectionImpactDependent(
    ProjectionImpactNode Node,
    int Depth,
    IReadOnlyList<string> Reasons);

public sealed record ProjectionImpactReport(
    ApplicationIdentifier ApplicationId,
    string GraphFingerprint,
    ProjectionImpactRoot? Root,
    bool Transitive,
    IReadOnlyList<ProjectionImpactNode> Nodes,
    IReadOnlyList<ProjectionImpactEdge> Edges,
    IReadOnlyList<ProjectionImpactDependent> Dependents);

public sealed class ProjectionImpactException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IProjectionImpactSnapshotReader
{
    ProjectionImpactSnapshot Read(ApplicationIdentifier applicationId);
}

public interface IProjectionImpactService
{
    ProjectionImpactReport Analyze(
        ApplicationIdentifier applicationId,
        string? rootId = null,
        bool transitive = true);
}
