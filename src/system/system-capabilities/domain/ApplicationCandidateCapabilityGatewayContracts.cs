using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.SystemCapabilities;

public sealed record ApplicationCandidateCapabilityInvocationResult(
    bool Ok,
    string CapabilityId,
    string Mode,
    JsonElement? Data,
    string OperationId,
    string ReadBackFingerprint,
    SystemCapabilityError? Error);

public sealed record ApplicationCandidateCapabilityDescriptor(
    string Id,
    int Version,
    string SourceFingerprint,
    string Owner,
    string Description,
    string Mode,
    string InputSchemaJson,
    string OutputSchemaJson,
    IReadOnlyList<string> ProcedureIds,
    IReadOnlyList<StandingGrantCapability> RequiredStandingGrantCapabilities,
    bool RequiresConfirmation,
    bool RequiresIdempotencyKey);

public sealed record ApplicationCandidateCapabilityDiscoveryResult(
    bool Ok,
    IReadOnlyList<ApplicationCandidateCapabilityDescriptor> Capabilities,
    SystemCapabilityError? Error,
    AuthorizationAuditEvidence AuthorizationEvidence);

public interface IApplicationCandidateCapabilityGateway
{
    ApplicationCandidateCapabilityDiscoveryResult Discover(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string correlationId);

    Task<ApplicationCandidateCapabilityInvocationResult> InvokeAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string capabilityId,
        string inputJson,
        string? idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);
}
