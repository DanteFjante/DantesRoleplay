using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

public sealed record ApplicationMechanicProjectionMapping(
    IReadOnlyDictionary<string, EcsComponentReference> Components,
    IReadOnlyDictionary<string, string> Relationships);

public sealed record ApplicationMechanicProjectionMappingProblem(string Code, string SafeMessage);

public sealed record ApplicationMechanicProjectionMappingResult(
    ApplicationMechanicProjectionMapping? Mapping,
    IReadOnlyList<ApplicationMechanicProjectionMappingProblem> Problems)
{
    public bool Resolved => Mapping is not null && Problems.Count == 0;
}

/// <summary>
/// Resolves catalog-local component and relationship identifiers to the exact component versions
/// installed in one application state space. Both write actions and read models use this owner so
/// extension resolution cannot drift between execution surfaces.
/// </summary>
public interface IApplicationMechanicProjectionMappingResolver
{
    Task<ApplicationMechanicProjectionMappingResult> ResolveAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        string qualifiedMechanicId,
        MechanicRequirements requirements,
        CancellationToken cancellationToken = default);
}

public sealed record ApplicationMechanicEvaluationRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedMechanicId,
    string ContentFingerprint,
    ApplicationMechanicProjectionMapping Mapping,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string InputJson,
    long Seed,
    MechanicExecutionContext? Execution = null,
    MechanicAudienceContext? Audience = null,
    string? ReadModelQueryId = null);

public sealed record ApplicationMechanicEvaluationResult(
    string QualifiedMechanicId,
    string ContentFingerprint,
    MechanicProjection? Projection,
    MechanicRunResult? Run,
    IReadOnlyList<string> Problems)
{
    /// <summary>Ordered proposals from the evaluated child tree; the root execution owner applies them atomically.</summary>
    public CompositionProposal Proposal { get; init; } = CompositionProposal.Empty;

    public bool Evaluated => Problems.Count == 0 && Projection is not null && Run is not null;
    public bool Ok => Evaluated && Run!.Ok;
}

public interface IApplicationMechanicProjectionResolver
{
    Task<ProjectionResult> ResolveAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        MechanicRequirements requirements,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyDictionary<string, string> roleAssignments,
        string inputJson,
        long seed,
        CancellationToken cancellationToken = default);
}

/// <summary>Materializes exact registered application objects for an object-based reducer.</summary>
public interface IApplicationMechanicObjectProjectionResolver
{
    Task<ProjectionResult> ResolveAsync(
        string stateSpaceId,
        ApplicationIdentifier applicationId,
        MechanicRequirements requirements,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyDictionary<string, string> roleAssignments,
        string inputJson,
        long seed,
        CancellationToken cancellationToken = default);
}

public interface IApplicationAuthorizedProjectionResolver
{
    Task<ProjectionResult> ResolveAsync(ApplicationMechanicEvaluationRequest request,
        MechanicRequirements requirements, CancellationToken cancellationToken = default);
}

/// <summary>Evaluates one exact active catalog mechanic; it never applies the proposed output.</summary>
public interface IApplicationMechanicEvaluator
{
    Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
        ApplicationMechanicEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
