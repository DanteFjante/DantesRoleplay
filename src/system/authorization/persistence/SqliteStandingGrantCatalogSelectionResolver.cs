using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Authorization;

public sealed partial class SqliteStandingGrantTargetResolver
{
    public async Task<StandingGrantTargetResolution> ResolveCatalogSelectionAsync(
        InteractionInvocationHost host, StandingGrantCatalogSelectionOrigin origin,
        string exactDefinitionId, string kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(origin);
        if (scanner is null || overlays is null || roots is null)
            return Unavailable("STANDING_GRANT_CATALOG_SELECTION_OWNER_UNAVAILABLE");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var app = host.ApplicationRevision.ApplicationId;
            CatalogNamespaceIdentity.ValidateRecordId(exactDefinitionId);
            if (app.IsSystem || !exactDefinitionId.StartsWith(app.Value + ".", StringComparison.Ordinal))
                return Denied("STANDING_GRANT_DEFINITION_SCOPE");
            if (kind is not (CatalogNamespaceKinds.Mechanic or CatalogNamespaceKinds.Procedure))
                return Unavailable("STANDING_GRANT_DEFINITION_KIND_UNAVAILABLE");
            if (!GenericSourceDocument.IsNormalizedRelativePath(origin.RelativePath))
                return Denied("STANDING_GRANT_CATALOG_SELECTION_ORIGIN_INVALID");

            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var registered = applications.Get(app);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Denied("STANDING_GRANT_APPLICATION_STALE");

            var source = sources.Get(app, origin.SourceId);
            if (source is null || source.ApplicationId != app || source.Trust != SourceTrust.Trusted
                || source.AllowedRootId != origin.AllowedRootId
                || SourceRegistrationFingerprint.Compute(source) != origin.SourceRegistrationFingerprint)
                return Denied("STANDING_GRANT_SOURCE_DRIFT");
            if (!roots.TryResolve(origin.AllowedRootId, out var configuredRoot)
                || string.IsNullOrWhiteSpace(configuredRoot))
                return Unavailable("STANDING_GRANT_SOURCE_ROOT_UNAVAILABLE");

            var registrations = sources.For(app)
                .Where(value => value.AllowedRootId == origin.AllowedRootId && value.Trust == SourceTrust.Trusted)
                .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
            var scan = await scanner.ScanAsync(app, cancellationToken);
            var selectedScan = scan.Documents.Where(value => registrations.ContainsKey(value.SourceId)).ToArray();
            var selectedProblems = scan.Problems.Where(value => string.IsNullOrEmpty(value.SourceId)
                || registrations.ContainsKey(value.SourceId)).ToArray();
            var overlay = overlays.Resolve(app, selectedScan, selectedProblems);
            if (overlay.Problems.Count != 0)
                return Unavailable("STANDING_GRANT_CATALOG_SELECTION_SCAN_UNAVAILABLE");

            var paths = kind == CatalogNamespaceKinds.Mechanic
                ? new[] { origin.RelativePath, Path.ChangeExtension(origin.RelativePath, ".js").Replace('\\', '/') }
                : new[] { origin.RelativePath };
            var winners = new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal);
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var canonicalRoot = Path.GetFullPath(configuredRoot);
            foreach (var path in paths)
            {
                var values = overlay.Winners.Where(value => value.RelativePath == path).ToArray();
                if (values.Length != 1 || values[0].SourceId != source.SourceId || !values[0].IsText
                    || values[0].Length < 0 || values[0].Length > MaximumLookupBytes)
                    return Denied("STANDING_GRANT_CATALOG_SELECTION_DOCUMENT_STALE");
                var winner = values[0];
                var physical = Path.GetFullPath(Path.Combine(canonicalRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                if (!InsideCatalogRoot(canonicalRoot, physical))
                    return Denied("STANDING_GRANT_SOURCE_PATH_OUTSIDE_ROOT");
                var content = await File.ReadAllBytesAsync(physical, cancellationToken);
                if (content.LongLength != winner.Length
                    || Convert.ToHexString(SHA256.HashData(content)) != winner.ContentFingerprint)
                    return Denied("STANDING_GRANT_CATALOG_SELECTION_DOCUMENT_STALE");
                winners.Add(path, new(winner.LogicalIdentity, winner.SourceId, winner.Trust, winner.Precedence,
                    winner.RelativePath, winner.MediaType, winner.ContentFingerprint, winner.Length, winner.IsText));
                bytes.Add(path, content);
            }
            if (bytes.Values.Sum(value => (long)value.Length) > MaximumLookupBytes)
                return Unavailable("STANDING_GRANT_LOOKUP_LIMIT");

            var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(
                app, winners[origin.RelativePath], winners, bytes);
            if (record is null || record.QualifiedId != exactDefinitionId || record.Kind != kind
                || record.SourceId != source.SourceId || record.SourceLogicalPath != origin.RelativePath)
                return Denied("STANDING_GRANT_CATALOG_SELECTION_DEFINITION_STALE");
            using var json = JsonDocument.Parse(record.ContentJson);
            if (!json.RootElement.TryGetProperty("id", out var id) || id.GetString() != exactDefinitionId)
                return Denied("STANDING_GRANT_ALIAS_UNSUPPORTED");

            var chain = CatalogNamespaceIdentity.NamespaceChain(exactDefinitionId)
                .Select(namespaceId => namespaces.Get(namespaceId)).ToArray();
            if (chain.Length == 0 || chain.Any(value => value is null || !value.IsEnabled
                    || value.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed))
                return Denied("STANDING_GRANT_NAMESPACE_UNREVIEWED");
            var leaf = chain[^1]!;
            if (leaf.Id == CatalogNamespaceIdentity.RootNamespaceId
                || !leaf.AllowedKinds.Contains(kind, StringComparer.Ordinal))
                return Denied("STANDING_GRANT_NAMESPACE_KIND_DENIED");
            if (extensions.For(app).Any(value => value.SourceIds.Contains(source.SourceId, StringComparer.Ordinal)
                    || value.NamespaceIds.Any(ns => leaf.Id == ns || leaf.Id.StartsWith(ns + ".", StringComparison.Ordinal))))
                return Unavailable("STANDING_GRANT_BORROWED_DEFINITION_UNAVAILABLE");

            var proof = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                origin, registered.Revision, registered.Fingerprint, namespaceChain = chain,
                definitionId = record.QualifiedId, record.Kind, definitionRevision = record.Version,
                definitionFingerprint = record.ContentFingerprint,
                documents = winners.Values.OrderBy(value => value.RelativePath, StringComparer.Ordinal)
            }));
            return new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE",
                new(exactDefinitionId, kind, app, leaf.Id,
                    "catalog-selection-owner:" + InteractionCanonicalJson.Fingerprint(
                        "dantes-roleplay/standing-grant-catalog-selection-owner/v1", proof),
                    record.Version, record.ContentFingerprint, CatalogSelection: origin));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or JsonException
            or IOException or UnauthorizedAccessException or NotSupportedException
            or ApplicationCatalogMaterializationException or CatalogNamespaceException
            or InvalidOperationException or KeyNotFoundException)
        {
            return Unavailable("STANDING_GRANT_CATALOG_SELECTION_OWNER_UNAVAILABLE");
        }
    }

    private static bool InsideCatalogRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
