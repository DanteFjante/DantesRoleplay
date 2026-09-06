using DantesRoleplay.Applications;
using DantesRoleplay.EcsEffects;

namespace DantesRoleplay.Projections;

public sealed record ApplicationObjectRelationshipEdit(
    string Path,
    string Operation,
    string TargetEntityId,
    int ExpectedRevision);

public sealed record ApplicationObjectWriteRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    ProjectionReference Object,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string CollectionId,
    string Perspective,
    string IdempotencyKey,
    string ExpectedSourceRevisionFingerprint,
    string ChangesJson,
    IReadOnlyList<ApplicationObjectRelationshipEdit> RelationshipEdits);

public sealed record ApplicationObjectWriteResult(
    bool Applied,
    bool Replayed,
    bool NoOp,
    string OperationId,
    string OutputJson,
    string SourceRevisionFingerprint,
    IReadOnlyList<ApplicationEcsEffectReceipt> Receipts);

public sealed class ApplicationObjectWriteException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Applies one exact catalog-declared object edit through the typed ECS effect boundary and
/// returns a freshly materialized object with new source evidence.
/// </summary>
public interface IApplicationObjectWriteService
{
    Task<ApplicationObjectWriteResult> WriteAsync(
        ApplicationObjectWriteRequest request,
        CancellationToken cancellationToken = default);
}
