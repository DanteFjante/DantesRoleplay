using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationActivation;

public sealed record ActivatedApplicationSource(
    string SourceId,
    string RegistrationFingerprint,
    int DocumentCount,
    int ProblemCount);

public sealed record ActivatedApplicationDocument(
    string LogicalIdentity,
    string SourceId,
    SourceTrust Trust,
    int Precedence,
    string RelativePath,
    string MediaType,
    string ContentFingerprint,
    long Length,
    bool IsText);

public sealed record ActivatedApplicationExtension(
    string ExtensionId,
    string RegistrationFingerprint,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> NamespaceIds,
    IReadOnlyList<string> HigherPriorityThan,
    bool OverridesBase);

public sealed record ActiveApplicationManifest(
    ApplicationIdentifier ApplicationId,
    int ActivationRevision,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string PreviewFingerprint,
    string ScannedDocumentsFingerprint,
    string CandidateManifestFingerprint,
    string DependencyGraphFingerprint,
    string ActivationFingerprint,
    string DependencyCoverageVersion,
    bool DependencyCoverageComplete,
    IReadOnlyList<ActivatedApplicationSource> Sources,
    IReadOnlyList<ActivatedApplicationDocument> Winners,
    string ActivatedByOperationId,
    DateTime ActivatedAtUtc)
{
    public string ResolutionFingerprint { get; init; } = ActivationFingerprint;
    public IReadOnlyList<ActivatedApplicationExtension> Extensions { get; init; } = [];
}

public sealed record ApplicationActivationRequest(
    ApplicationIdentifier ApplicationId,
    string PreviewFingerprint,
    string? ExpectedActiveFingerprint,
    IReadOnlyList<string>? SourceIds = null)
{
    public IReadOnlyList<string>? ExtensionIds { get; init; }
}

public sealed record ApplicationActivationContext(
    string RequestToken,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record ApplicationActivationPreview(
    ActiveApplicationManifest Activation,
    string Outcome,
    string OperationId);

public sealed record ApplicationActivationReceipt(
    ActiveApplicationManifest Activation,
    string Outcome,
    string OperationId);

public sealed class ApplicationActivationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IApplicationActivationReader
{
    ActiveApplicationManifest? Current(ApplicationIdentifier applicationId);
}

public sealed record ActivatedApplicationTextDocument(
    ApplicationIdentifier ApplicationId,
    int ActivationRevision,
    string ActivationFingerprint,
    string SourceId,
    string RelativePath,
    string ContentFingerprint,
    string Text,
    IReadOnlyList<string>? SourceIds = null);

/// <summary>
/// Reads one exact text winner from the current active application manifest. Implementations must
/// revalidate retained source registration, root containment, byte length, and content fingerprint.
/// Protocol and browser callers never provide source roots or filesystem paths.
/// </summary>
public interface IActivatedApplicationDocumentReader
{
    ActivatedApplicationTextDocument? ReadText(
        ApplicationIdentifier applicationId,
        string relativePath);
}

public sealed class ActivatedApplicationDocumentReadException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>Owns exact-preview activation and audit; it grants no executable or state-space authority.</summary>
public interface IApplicationActivationService : IApplicationActivationReader
{
    Task<ApplicationActivationPreview> PreviewAsync(
        ApplicationActivationRequest request,
        ApplicationActivationContext context,
        CancellationToken cancellationToken = default);

    Task<ApplicationActivationReceipt> ActivateAsync(
        ApplicationActivationRequest request,
        ApplicationActivationContext context,
        CancellationToken cancellationToken = default);
}
