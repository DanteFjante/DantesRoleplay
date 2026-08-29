using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.LegacyStateAdoption;

public sealed record LegacyComponentTypeMapping(
    string LegacyDefinitionId,
    EcsComponentReference ComponentType);

public sealed record LegacyRelationshipKindMapping(
    string LegacyKind,
    string QualifiedKind);

public sealed record LegacyStateAdoptionRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string ActiveFingerprint,
    IReadOnlyList<LegacyComponentTypeMapping> ComponentMappings,
    IReadOnlyList<LegacyRelationshipKindMapping> RelationshipMappings);

public sealed record LegacyStateAdoptionContext(
    string RequestToken,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record LegacyStateInventory(
    int EntityCount,
    int ComponentCount,
    int ContainmentCount,
    int RelationshipCount,
    string SourceFingerprint,
    string EvidenceFingerprint);

public sealed record LegacyStateAdoptionPreview(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    LegacyStateInventory Inventory,
    string Outcome,
    string OperationId);

public sealed record LegacyStateAdoptionReceipt(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    LegacyStateInventory Inventory,
    string Outcome,
    string OperationId);

public sealed class LegacyStateAdoptionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ILegacyStateAdoptionService
{
    Task<LegacyStateAdoptionPreview> PreviewAsync(
        LegacyStateAdoptionRequest request,
        LegacyStateAdoptionContext context,
        CancellationToken cancellationToken = default);

    Task<LegacyStateAdoptionReceipt> AdoptAsync(
        LegacyStateAdoptionRequest request,
        LegacyStateAdoptionContext context,
        CancellationToken cancellationToken = default);
}
