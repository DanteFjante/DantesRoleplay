using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

public sealed record ApplicationMechanicProjectionMapping(
    IReadOnlyDictionary<string, EcsComponentReference> Components,
    IReadOnlyDictionary<string, string> Relationships);

public sealed record ApplicationMechanicEvaluationRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedMechanicId,
    string ContentFingerprint,
    ApplicationMechanicProjectionMapping Mapping,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string InputJson,
    long Seed,
    MechanicExecutionContext? Execution = null);

public sealed record ApplicationMechanicEvaluationResult(
    string QualifiedMechanicId,
    string ContentFingerprint,
    MechanicProjection? Projection,
    MechanicRunResult? Run,
    IReadOnlyList<string> Problems)
{
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

/// <summary>Evaluates one exact active catalog mechanic; it never applies the proposed output.</summary>
public interface IApplicationMechanicEvaluator
{
    Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
        ApplicationMechanicEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
