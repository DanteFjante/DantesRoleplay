using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationPreview;

public sealed record ApplicationPreviewSource(
    string SourceId,
    string RegistrationFingerprint,
    int DocumentCount,
    int ProblemCount);

public sealed record ApplicationPreviewResult(
    ApplicationIdentifier ApplicationId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string ScannedDocumentsFingerprint,
    string CandidateManifestFingerprint,
    string PreviewFingerprint,
    bool IsValid,
    IReadOnlyList<ApplicationPreviewSource> Sources,
    IReadOnlyList<EffectiveSourceDocument> Winners,
    IReadOnlyList<ShadowedSourceDocument> Shadows,
    IReadOnlyList<SourceOverlayProblem> Problems);

public sealed class ApplicationPreviewException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Builds a disposable candidate view; it cannot persist or activate that view.</summary>
public interface IApplicationPreviewService
{
    Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default);
}
