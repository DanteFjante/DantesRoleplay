using DantesRoleplay.Effects;

namespace DantesRoleplay.World;

/// <summary>
/// A root-owned, immutable reservation for one entity that does not yet exist in persistent
/// world state. Every child sees the same ordered overlay, but the overlay exposes no writes.
/// </summary>
public sealed record StagedWorldTarget(string EntityId, string Name);

/// <summary>
/// Bounds every entity a staged child may mention. The root declares the complete set before a
/// child runs, preventing a child from inventing an unrelated target or revising an earlier
/// fragment.
/// </summary>
public sealed record StagedWorldBoundary(
    StagedWorldTarget Target,
    IReadOnlySet<string> AllowedEntityIds);

public sealed record StagedWorldProblem(string Code, string Path, string Reason);

/// <summary>
/// One immutable ordered bundle and its read-only virtual world. A valid plan has never written
/// persistent state; its effects still have to be applied by the owning root transaction.
/// </summary>
public sealed record StagedWorldPlan(
    string Status,
    StagedWorldBoundary Boundary,
    IReadOnlyList<Effect> Effects,
    IWorldStore? World,
    IReadOnlyList<StagedWorldProblem> Problems)
{
    public bool Valid => Status == "valid" && World is not null;
}

/// <summary>
/// Builds and extends a dry-run-validated, read-only world overlay. It is generic kernel
/// infrastructure: it has no character, campaign, item, or MCP vocabulary.
/// </summary>
public interface IStagedWorldComposer
{
    Task<StagedWorldPlan> StartAsync(
        StagedWorldBoundary boundary,
        CancellationToken cancellationToken = default);

    Task<StagedWorldPlan> AppendAsync(
        StagedWorldPlan prior,
        IReadOnlyList<Effect> fragment,
        CancellationToken cancellationToken = default);
}
