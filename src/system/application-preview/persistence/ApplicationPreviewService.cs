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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var revision = applications.Get(applicationId)
            ?? throw new ApplicationPreviewException(
                "APPLICATION_UNKNOWN", "The requested application is not registered.");
        var registrations = sources.For(applicationId);
        var scan = await scanner.ScanAsync(applicationId, cancellationToken);
        var candidate = overlays.Resolve(applicationId, scan.Documents, scan.Problems);
        var sourceSummaries = registrations.Select(registration => new ApplicationPreviewSource(
                registration.SourceId,
                SourceRegistrationFingerprint.Compute(registration),
                scan.Documents.Count(document => document.SourceId == registration.SourceId),
                scan.Problems.Count(problem => problem.SourceId == registration.SourceId)))
            .ToArray();
        var scannedDocumentsFingerprint = ScannedDocumentsFingerprint(scan.Documents);
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
