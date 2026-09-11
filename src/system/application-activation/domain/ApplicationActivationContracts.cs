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
    /// <summary>
    /// Identifies the preparation and retained-content rules applied before this revision became
    /// active. Null identifies a legacy metadata-only revision, whose bytes may still be resolved
    /// from its registered source under the historical fail-closed fallback.
    /// </summary>
    public string? PreparationVersion { get; init; }
}

public sealed record ActivatedApplicationDocumentEvidence(
    ApplicationIdentifier ApplicationId,
    int ActivationRevision,
    string LogicalIdentity,
    string ContentFingerprint,
    long Length,
    byte[]? RetainedBytes,
    bool IsLegacyMetadataOnly);

/// <summary>
/// Reads immutable document evidence for an activation revision. Prepared revisions always return
/// retained bytes; only legacy metadata-only revisions may return null.
/// </summary>
public interface IActivatedApplicationEvidenceReader
{
    ActivatedApplicationDocumentEvidence? ReadDocumentEvidence(
        ApplicationIdentifier applicationId,
        int activationRevision,
        string logicalIdentity);
}

public sealed record ApplicationDefinitionDiscoveryEvidence(
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> RelativePaths);

public sealed record ApplicationDefinitionDependencyEvidence(
    string GraphFingerprint,
    string CoverageVersion,
    bool CoverageComplete);

public sealed record ApplicationDefinitionDerivedIndexStatus(
    string Status,
    bool IsAvailable,
    bool IsActivationGate);

/// <summary>
/// Canonical definition-change evidence derived from one immutable activation revision. It is a
/// definition signal, not an ordinary object/state-change event.
/// </summary>
public sealed record ApplicationDefinitionChange(
    ApplicationIdentifier Target,
    int Revision,
    string Fingerprint,
    string SourceOperationId,
    DateTime ChangedAtUtc,
    ApplicationDefinitionDiscoveryEvidence Discovery,
    ApplicationDefinitionDependencyEvidence Dependencies,
    ApplicationDefinitionDerivedIndexStatus DerivedIndex);

public interface IApplicationDefinitionChangeReader
{
    ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier applicationId);

    ApplicationDefinitionChange? RevisionChange(
        ApplicationIdentifier applicationId,
        int activationRevision);

    IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(
        ApplicationIdentifier applicationId,
        int afterActivationRevision,
        int limit);
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

public sealed class ApplicationActivationException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

public interface IApplicationActivationReader
{
    ActiveApplicationManifest? Current(ApplicationIdentifier applicationId);
    /// <summary>Returns an exact retained generation, without substituting today's active generation.</summary>
    ActiveApplicationManifest? ReadRevision(ApplicationIdentifier applicationId, int activationRevision) => null;
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
