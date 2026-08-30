using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.StateSpaceAdministration;

public sealed record StateSpaceCreationRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string ActiveFingerprint,
    string? ExpectedFingerprint);

public sealed record StateSpaceCreationContext(
    string RequestToken,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record StateSpaceBindingSummary(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string ActiveFingerprint,
    int BindingRevision,
    string BindingFingerprint,
    DateTime? CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record StateSpaceCreationPreview(
    StateSpaceBindingSummary Binding,
    string Outcome,
    string OperationId);

public sealed record StateSpaceCreationReceipt(
    StateSpaceBindingSummary Binding,
    string Outcome,
    string OperationId);

public sealed record StateSpaceUpgradeRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string ActiveFingerprint,
    string ExpectedBindingFingerprint);

public sealed record StateSpaceUpgradeContext(
    string RequestToken,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record StateSpaceCompatibilityEvidence(
    string Code,
    int EntityCount,
    int ComponentCount,
    string DependencyCoverageVersion,
    bool DependencyCoverageComplete);

public sealed record StateSpaceUpgradePreview(
    StateSpaceBindingSummary PreviousBinding,
    StateSpaceBindingSummary TargetBinding,
    StateSpaceCompatibilityEvidence Compatibility,
    string Outcome,
    string OperationId);

public sealed record StateSpaceUpgradeReceipt(
    StateSpaceBindingSummary PreviousBinding,
    StateSpaceBindingSummary Binding,
    StateSpaceCompatibilityEvidence Compatibility,
    string Outcome,
    string OperationId);

public sealed class StateSpaceAdministrationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IStateSpaceAdministrationReader
{
    StateSpaceBindingSummary? Get(string stateSpaceId);
    IReadOnlyList<StateSpaceBindingSummary> List(ApplicationIdentifier applicationId, int limit);
}

/// <summary>Creates state spaces and rebinds them only against exact active application evidence.</summary>
public interface IStateSpaceAdministrationService : IStateSpaceAdministrationReader
{
    Task<StateSpaceCreationPreview> PreviewCreateAsync(
        StateSpaceCreationRequest request,
        StateSpaceCreationContext context,
        CancellationToken cancellationToken = default);

    Task<StateSpaceCreationReceipt> CreateAsync(
        StateSpaceCreationRequest request,
        StateSpaceCreationContext context,
        CancellationToken cancellationToken = default);

    Task<StateSpaceUpgradePreview> PreviewUpgradeAsync(
        StateSpaceUpgradeRequest request,
        StateSpaceUpgradeContext context,
        CancellationToken cancellationToken = default);

    Task<StateSpaceUpgradeReceipt> UpgradeAsync(
        StateSpaceUpgradeRequest request,
        StateSpaceUpgradeContext context,
        CancellationToken cancellationToken = default);
}
