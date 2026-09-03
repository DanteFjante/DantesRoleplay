using DantesRoleplay.Applications;
using DantesRoleplay.EcsEffects;

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
/// Executes one exact current application mechanic. Selection and sequencing remain orchestration
/// concerns; effect interpretation and the atomic mutation belong to this application owner.
/// </summary>
public interface IApplicationActionRunner
{
    Task<ApplicationActionExecutionResult> RunAsync(
        ApplicationActionExecutionRequest request,
        CancellationToken cancellationToken = default);
}
