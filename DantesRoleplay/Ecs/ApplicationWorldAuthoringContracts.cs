namespace DantesRoleplay.EcsEffects;

public sealed record ApplicationWorldAuthoringComponent(
    string QualifiedTypeId,
    int ExpectedRevision,
    string ValueJson);

public sealed record ApplicationWorldAuthoringContainment(
    string ContainerEntityId,
    string Slot,
    int ExpectedRevision);

public sealed record ApplicationWorldAuthoringEntity(
    string EntityId,
    string Name,
    int ExpectedRevision,
    IReadOnlyList<ApplicationWorldAuthoringComponent> Components,
    ApplicationWorldAuthoringContainment? Containment);

public sealed record ApplicationWorldAuthoringRelationship(
    string FromEntityId,
    string ToEntityId,
    string QualifiedKind,
    int ExpectedRevision,
    string ValueJson);

public sealed record ApplicationWorldAuthoringRequest(
    string RequestToken,
    string ApplicationId,
    string StateSpaceId,
    string RootEntityId,
    IReadOnlyList<ApplicationWorldAuthoringEntity> Entities,
    IReadOnlyList<ApplicationWorldAuthoringRelationship> Relationships);

public sealed record ApplicationWorldAuthoringContext(
    string Intent,
    IReadOnlyList<string> ProceduresUsed);

public sealed record ApplicationWorldAuthoringResult(
    bool Accepted,
    bool DryRun,
    bool Replayed,
    int ReviewedEntityCount,
    int AppliedEffectCount,
    string OperationId,
    string ErrorCode = "",
    IReadOnlyList<ApplicationEcsEffectProblem>? Problems = null,
    IReadOnlyList<ApplicationEcsEffectReceipt>? Receipts = null);

/// <summary>
/// Private manifest-shaped authoring boundary for one existing application world root. It derives
/// exact application ECS effects and delegates one atomic transaction; it never deletes state.
/// </summary>
public interface IApplicationWorldAuthoringSynchronizer
{
    Task<ApplicationWorldAuthoringResult> SynchronizeAsync(
        ApplicationWorldAuthoringRequest request,
        ApplicationWorldAuthoringContext context,
        bool dryRun,
        CancellationToken cancellationToken = default);
}
