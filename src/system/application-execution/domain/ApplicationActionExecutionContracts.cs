using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationExecution;

public enum ApplicationActionExecutionDisposition
{
    Succeeded,
    Replayed,
    Failed,
    Stale,
    Unsupported
}

public sealed record ApplicationActionExecutionRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedMechanicId,
    int MechanicVersion,
    string ContentFingerprint,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string InputJson,
    long Seed,
    ApplicationEcsExecutionIdentity ExecutionIdentity);

public sealed record ApplicationActionExecutionProblem(string Code, string SafeMessage);

public sealed record ApplicationActionExecutionResult(
    ApplicationActionExecutionDisposition Disposition,
    string OperationId,
    string QualifiedMechanicId,
    string ContentFingerprint,
    long Seed,
    string Narration,
    int AppliedEffectCount,
    IReadOnlyList<ApplicationActionExecutionProblem> Problems)
{
    public bool Successful => Disposition is ApplicationActionExecutionDisposition.Succeeded
        or ApplicationActionExecutionDisposition.Replayed;
    public int MechanicVersion { get; init; }
    public IReadOnlyList<string> AffectedEntityIds { get; init; } = [];
    public IReadOnlyList<ApplicationEcsEffectReceipt> EffectReceipts { get; init; } = [];
}

/// <summary>
/// The common, host-verified translation from a mechanic proposal to typed ECS effects. Action
/// execution and event reactions share this result so reactions cannot invent a second effect
/// mapping or weaken the normal component/entity snapshot checks.
/// </summary>
public sealed record ApplicationEcsEffectBatchBuildResult(
    ApplicationEcsEffectBatch? Batch,
    IReadOnlyList<ApplicationActionExecutionProblem> Problems,
    bool Stale)
{
    public bool Ok => Batch is not null && Problems.Count == 0;
}

public interface IApplicationEcsEffectBatchBuilder
{
    Task<ApplicationEcsEffectBatchBuildResult> BuildAsync(
        StateSpaceView stateSpace,
        ApplicationMechanicProjectionMapping mapping,
        MechanicProjection projection,
        MechanicRequirements requirements,
        CompositionProposal proposal,
        string mechanicId,
        int mechanicVersion,
        long seed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes one exact current application mechanic. Selection and sequencing remain orchestration
/// concerns; effect interpretation and the atomic mutation belong to this application owner.
/// </summary>
public interface IApplicationActionRunner
{
    Task<ApplicationActionExecutionResult> RunAsync(
        ApplicationActionExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ApplicationActionInvocationRequest(
    InteractionInvocationHost Host, string QualifiedMechanicId, int MechanicVersion, string ContentFingerprint,
    IReadOnlyDictionary<string, string> RoleEntityIds, string InputJson = "{}");

public interface IApplicationActionInvocationAdapter
{
    Task<InteractionInvocationResult> ExecuteAsync(ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default);
}
