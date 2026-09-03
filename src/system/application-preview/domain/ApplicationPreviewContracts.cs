using DantesRoleplay.Applications;
using DantesRoleplay.Mechanics;
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
    IReadOnlyList<SourceOverlayProblem> Problems)
{
    public string ResolutionFingerprint { get; init; } = CandidateManifestFingerprint;
    public IReadOnlyList<string> ExtensionIds { get; init; } = [];
    public IReadOnlyList<MechanicAntiSprawlFinding> AntiSprawlFindings { get; init; } = [];
}

public sealed record ApplicationAntiSprawlEvaluation(
    IReadOnlyList<SourceOverlayProblem> Problems,
    IReadOnlyList<MechanicAntiSprawlFinding> Findings)
{
    public static ApplicationAntiSprawlEvaluation Empty { get; } = new([], []);
}

/// <summary>
/// Evaluates the exact winning source documents before application activation. Implementations
/// may read only through trusted registered source roots and must never execute mechanic source.
/// </summary>
public interface IApplicationAntiSprawlGate
{
    Task<ApplicationAntiSprawlEvaluation> EvaluateAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<SourceRegistration> registrations,
        IReadOnlyList<EffectiveSourceDocument> winners,
        CompiledApplicationExtensionSet extensionSet,
        CancellationToken cancellationToken = default);
}

public sealed class EmptyApplicationAntiSprawlGate : IApplicationAntiSprawlGate
{
    public Task<ApplicationAntiSprawlEvaluation> EvaluateAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<SourceRegistration> registrations,
        IReadOnlyList<EffectiveSourceDocument> winners,
        CompiledApplicationExtensionSet extensionSet,
        CancellationToken cancellationToken = default) => Task.FromResult(ApplicationAntiSprawlEvaluation.Empty);
}

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

    Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken = default);

    Task<ApplicationPreviewResult> PreviewExtensionsAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> extensionIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This preview service does not support extension selection.");

    Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> baseSourceIds,
        IReadOnlyList<string> extensionIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This preview service does not support reviewed base-source and extension selection.");
}
