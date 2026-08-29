using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationPreview;

public sealed class ApplicationPreviewService(
    IApplicationRegistry applications,
    ISourceRegistry sources,
    IRegisteredSourceScanner scanner,
    ISourceOverlayResolver overlays) : IApplicationPreviewService
{
    public async Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, null, cancellationToken);

    public async Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, sourceIds, cancellationToken);

    private async Task<ApplicationPreviewResult> PreviewCoreAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string>? sourceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var revision = applications.Get(applicationId)
            ?? throw new ApplicationPreviewException(
                "APPLICATION_UNKNOWN", "The requested application is not registered.");
        var registrations = SelectSources(sources.For(applicationId), sourceIds);
        var selectedIds = registrations.Select(value => value.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        var scan = await scanner.ScanAsync(applicationId, cancellationToken);
        var documents = scan.Documents.Where(value => selectedIds.Contains(value.SourceId)).ToArray();
        var problems = scan.Problems.Where(value => selectedIds.Contains(value.SourceId)).ToArray();
        var candidate = overlays.Resolve(applicationId, documents, problems);
        var sourceSummaries = registrations.Select(registration => new ApplicationPreviewSource(
                registration.SourceId,
                SourceRegistrationFingerprint.Compute(registration),
                documents.Count(document => document.SourceId == registration.SourceId),
                problems.Count(problem => problem.SourceId == registration.SourceId)))
            .ToArray();
        var scannedDocumentsFingerprint = ScannedDocumentsFingerprint(documents);
        var previewFingerprint = Fingerprint(
            revision.Fingerprint, sourceSummaries, scannedDocumentsFingerprint, candidate.Fingerprint);

        return new(
            applicationId,
            revision.Revision,
            revision.Fingerprint,
            scannedDocumentsFingerprint,
            candidate.Fingerprint,
            previewFingerprint,
            candidate.IsValid,
            Array.AsReadOnly(sourceSummaries),
            candidate.Winners,
            candidate.Shadows,
            candidate.Problems);
    }

    private static IReadOnlyList<SourceRegistration> SelectSources(
        IReadOnlyList<SourceRegistration> registrations,
        IReadOnlyList<string>? requested)
    {
        if (requested is null)
            return Array.AsReadOnly(registrations.OrderBy(value => value.SourceId, StringComparer.Ordinal).ToArray());
        if (requested.Count is < 1 or > 100
            || requested.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200)
            || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
            throw new ApplicationPreviewException("SOURCE_SELECTION_INVALID",
                "sourceIds must contain 1 through 100 unique registered source IDs.");
        var wanted = requested.ToHashSet(StringComparer.Ordinal);
        var selected = registrations.Where(value => wanted.Contains(value.SourceId))
            .OrderBy(value => value.SourceId, StringComparer.Ordinal).ToArray();
        if (selected.Length != wanted.Count)
            throw new ApplicationPreviewException("SOURCE_SELECTION_UNKNOWN",
                "Every selected source ID must be registered for the application.");
        return Array.AsReadOnly(selected);
    }

    private static string Fingerprint(
        string applicationFingerprint,
        IReadOnlyList<ApplicationPreviewSource> sources,
        string scannedDocumentsFingerprint,
        string candidateFingerprint)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationFingerprint,
            sources = sources.Select(source => new
            {
                source.SourceId,
                source.RegistrationFingerprint
            }).ToArray(),
            scannedDocumentsFingerprint,
            candidateFingerprint
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static string ScannedDocumentsFingerprint(IReadOnlyList<GenericSourceDocument> documents)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(documents
            .OrderBy(document => document.SourceId, StringComparer.Ordinal)
            .ThenBy(document => document.RelativePath, StringComparer.Ordinal)
            .Select(document => new
            {
                applicationId = document.ApplicationId.Value,
                document.SourceId,
                trust = document.Trust.ToString().ToLowerInvariant(),
                document.Precedence,
                document.RelativePath,
                document.MediaType,
                document.ContentFingerprint,
                document.Length,
                document.IsText
            }).ToArray());
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}
