using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationPreview;

public sealed class ApplicationPreviewService(
    IApplicationRegistry applications,
    ISourceRegistry sources,
    IApplicationExtensionRegistry extensions,
    IRegisteredSourceScanner scanner,
    ISourceOverlayResolver overlays) : IApplicationPreviewService
{
    public ApplicationPreviewService(
        IApplicationRegistry applications,
        ISourceRegistry sources,
        IRegisteredSourceScanner scanner,
        ISourceOverlayResolver overlays)
        : this(applications, sources, new InMemoryApplicationExtensionRegistry(sources), scanner, overlays)
    {
    }

    public async Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, null, cancellationToken);

    public async Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, sourceIds, cancellationToken);

    public async Task<ApplicationPreviewResult> PreviewExtensionsAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> extensionIds,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, null, cancellationToken, extensionIds);

    public async Task<ApplicationPreviewResult> PreviewAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string> baseSourceIds,
        IReadOnlyList<string> extensionIds,
        CancellationToken cancellationToken = default) =>
        await PreviewCoreAsync(applicationId, baseSourceIds, cancellationToken, extensionIds);

    private async Task<ApplicationPreviewResult> PreviewCoreAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<string>? sourceIds,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? extensionIds = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var revision = applications.Get(applicationId)
            ?? throw new ApplicationPreviewException(
                "APPLICATION_UNKNOWN", "The requested application is not registered.");
        var availableExtensions = extensions.For(applicationId);
        CompiledApplicationExtensionSet extensionSet;
        try
        {
            extensionSet = sourceIds is null || extensionIds is not null
                ? ApplicationExtensionSetCompiler.Compile(applicationId, availableExtensions, extensionIds)
                : InferLegacyExtensionSet(applicationId, availableExtensions, sourceIds);
        }
        catch (ApplicationExtensionSetException exception)
        {
            throw new ApplicationPreviewException(exception.Code, exception.Message);
        }
        var registrations = extensionIds is not null
            ? SelectExtensionSources(sources.For(applicationId), availableExtensions, extensionSet, sourceIds)
            : sourceIds is null
                ? SelectExtensionSources(sources.For(applicationId), availableExtensions, extensionSet, null)
                : SelectSources(sources.For(applicationId), sourceIds);
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
            revision.Fingerprint, sourceSummaries, scannedDocumentsFingerprint, candidate.Fingerprint,
            extensionSet.Fingerprint);

        return new ApplicationPreviewResult(
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
            candidate.Problems)
        {
            ResolutionFingerprint = extensionSet.Fingerprint,
            ExtensionIds = extensionSet.Extensions.Select(value => value.ExtensionId).ToArray()
        };
    }

    private static IReadOnlyList<SourceRegistration> SelectExtensionSources(
        IReadOnlyList<SourceRegistration> registrations,
        IReadOnlyList<ApplicationExtensionRegistration> availableExtensions,
        CompiledApplicationExtensionSet extensionSet,
        IReadOnlyList<string>? baseSourceIds)
    {
        var selectedExtensionSources = extensionSet.Extensions.SelectMany(value => value.SourceIds)
            .ToHashSet(StringComparer.Ordinal);
        var everyKnownExtensionSource = availableExtensions.SelectMany(value => value.SourceIds)
            .ToHashSet(StringComparer.Ordinal);
        var selectedBaseSources = baseSourceIds is null
            ? registrations.Where(value => !everyKnownExtensionSource.Contains(value.SourceId)).ToArray()
            : SelectSources(registrations, baseSourceIds).ToArray();
        if (selectedBaseSources.Any(value => everyKnownExtensionSource.Contains(value.SourceId)))
            throw new ApplicationPreviewException("BASE_SOURCE_SELECTION_INCLUDES_EXTENSION",
                "sourceIds used with extensionIds may contain only reviewed base sources; extension membership is selected by extensionIds.");
        var selectedBaseIds = selectedBaseSources.Select(value => value.SourceId).ToHashSet(StringComparer.Ordinal);
        var selected = registrations.Where(value => selectedBaseIds.Contains(value.SourceId)
                || selectedExtensionSources.Contains(value.SourceId))
            .OrderBy(value => value.SourceId, StringComparer.Ordinal).ToArray();
        return Array.AsReadOnly(selected);
    }

    private static CompiledApplicationExtensionSet InferLegacyExtensionSet(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationExtensionRegistration> available,
        IReadOnlyList<string> sourceIds)
    {
        var selected = sourceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var extension in available)
        {
            var count = extension.SourceIds.Count(selected.Contains);
            if (count != 0 && count != extension.SourceIds.Count)
                throw new ApplicationExtensionSetException("EXTENSION_SOURCE_SELECTION_PARTIAL",
                    $"Legacy source selection includes only part of extension '{extension.ExtensionId}'.");
        }
        var extensionIds = available.Where(value => value.SourceIds.All(selected.Contains))
            .Select(value => value.ExtensionId).ToArray();
        return ApplicationExtensionSetCompiler.Compile(applicationId, available, extensionIds);
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
        string candidateFingerprint,
        string resolutionFingerprint)
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
            candidateFingerprint,
            resolutionFingerprint
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
