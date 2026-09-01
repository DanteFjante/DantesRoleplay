using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.CatalogNavigation;

/// <summary>
/// Internal active-catalog view for server-side retrieval. It preserves source-winner trust without
/// exposing a source root and does not replace the public navigator as catalog authority.
/// </summary>
public sealed record ActiveCatalogFeatureDocument(CatalogRecordDefinition Record, SourceTrust Trust);

public sealed class ActiveCatalogFeatureSnapshot
{
    public ActiveCatalogFeatureSnapshot(
        CatalogNavigationManifest manifest,
        IReadOnlyList<ActiveCatalogFeatureDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(documents);
        var copied = documents.OrderBy(value => value.Record.Collection, StringComparer.Ordinal)
            .ThenBy(value => value.Record.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Record.QualifiedId, StringComparer.Ordinal).ToArray();
        var current = manifest.Records.ToDictionary(value => (value.Collection, value.QualifiedId));
        if (copied.Any(value => !Enum.IsDefined(value.Trust))
            || copied.Select(value => (value.Record.Collection, value.Record.QualifiedId)).Distinct().Count() != copied.Length
            || copied.Any(value => !current.TryGetValue((value.Record.Collection, value.Record.QualifiedId), out var record)
                || record.ContentFingerprint != value.Record.ContentFingerprint || record.Version != value.Record.Version
                || record.ContentJson != value.Record.ContentJson))
            throw new ArgumentException("The active feature snapshot has invalid provenance.", nameof(documents));
        Manifest = manifest;
        Documents = Array.AsReadOnly(copied);
    }

    public CatalogNavigationManifest Manifest { get; }
    public IReadOnlyList<ActiveCatalogFeatureDocument> Documents { get; }
    public CatalogExtensionResolutionContext? Resolution { get; init; }
}

public interface IActiveCatalogFeatureSnapshotProvider
{
    bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot);
}
