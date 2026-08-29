using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.RegistryAdministration;

public sealed record RegistryAdministrationContext(
    string RequestToken,
    string? ExpectedFingerprint,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record RegistryRegistrationPreview<T>(
    T Registration,
    string Fingerprint,
    string Outcome,
    string OperationId);

public sealed record RegistryRegistrationReceipt<T>(
    T Registration,
    string Fingerprint,
    string Outcome,
    string OperationId);

public sealed class RegistryAdministrationException(
    string code,
    string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Owns the one transaction joining immutable registry state to its durable replay/audit receipt.
/// It cannot resolve paths, scan files, or activate an application.
/// </summary>
public interface IRegistryAdministrationService
{
    Task<RegistryRegistrationPreview<ApplicationRevision>> PreviewApplicationAsync(
        ApplicationRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default);

    Task<RegistryRegistrationReceipt<ApplicationRevision>> RegisterApplicationAsync(
        ApplicationRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default);

    Task<RegistryRegistrationPreview<SourceRegistration>> PreviewSourceAsync(
        SourceRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default);

    Task<RegistryRegistrationReceipt<SourceRegistration>> RegisterSourceAsync(
        SourceRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default);
}
