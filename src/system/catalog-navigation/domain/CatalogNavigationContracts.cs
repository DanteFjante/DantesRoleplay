using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;

namespace DantesRoleplay.CatalogNavigation;

public enum CatalogDescriptionStatus { Authored, Missing }

public sealed record CatalogCollectionDefinition(string Id, string Title, string Description);

public sealed record CatalogNodeDefinition(
    string Collection,
    string Path,
    string Title,
    string Description,
    CatalogDescriptionStatus DescriptionStatus);

public sealed record CatalogRecordDefinition(
    string Collection,
    string Kind,
    string QualifiedId,
    string Name,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> MatchPhrases,
    string Path,
    string Status,
    int Version,
    string ContentJson,
    string ContentFingerprint,
    string SourceId,
    string SourceLogicalPath);

public sealed record CatalogCollectionSummary(string Id, string Title, string Description, int RecordCount);

public sealed record CatalogNodeView(string Collection, string Path, string Title, string Description, CatalogDescriptionStatus DescriptionStatus);

public sealed record CatalogRecordSummary(
    string Collection,
    string Kind,
    string QualifiedId,
    string Name,
    string Description,
    string Path,
    string Status,
    int Version,
    string ContentFingerprint,
    string SourceId,
    string SourceLogicalPath);

public sealed record CatalogRecordView(CatalogRecordSummary Summary, string ContentJson);

public enum CatalogBrowseEntryKind { Node, Record }

public sealed record CatalogBrowseEntry(CatalogBrowseEntryKind Kind, CatalogNodeView? Node, CatalogRecordSummary? Record)
{
    public string StableKey => Kind == CatalogBrowseEntryKind.Node
        ? $"0/{Node!.Path}"
        : $"1/{Record!.Kind}/{Record.QualifiedId}";
}

public sealed record CatalogBrowseRequest(
    ApplicationIdentifier ApplicationId,
    string Collection,
    string Branch = "",
    int PageSize = CatalogNavigationLimits.DefaultPageSize,
    string? Cursor = null);

public sealed record CatalogSearchRequest(
    ApplicationIdentifier ApplicationId,
    string Query,
    string? Collection = null,
    string Branch = "",
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? Statuses = null,
    int PageSize = CatalogNavigationLimits.DefaultPageSize,
    string? Cursor = null,
    string? NamespaceId = null,
    bool IncludeShadowed = false);

public sealed record CatalogRecordRequest(ApplicationIdentifier ApplicationId, string Collection, string QualifiedId);

public sealed record EffectiveApplicationContentRequest(
    ApplicationIdentifier ApplicationId,
    int PageSize = CatalogNavigationLimits.DefaultPageSize,
    string? Cursor = null);

public sealed record EffectiveApplicationExtensionView(
    string ExtensionId,
    string DisplayName,
    string Description,
    string Classification,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> NamespaceIds);

public sealed record EffectiveApplicationContentRecord(
    CatalogRecordSummary Record,
    string OwnerId,
    string SourceLabel,
    string Classification,
    IReadOnlyList<string> PresentationRoles,
    bool IsAdditive);

public sealed record EffectiveApplicationContentResult(
    string ApplicationId,
    string ResolutionFingerprint,
    IReadOnlyList<EffectiveApplicationExtensionView> ActiveExtensions,
    IReadOnlyList<EffectiveApplicationContentRecord> ResolvedWinners,
    IReadOnlyList<EffectiveApplicationContentRecord> AdditiveExtensionContent,
    string? NextCursor);

public sealed record CatalogBrowseResult(
    CatalogNodeView Node,
    IReadOnlyList<CatalogNodeView> Breadcrumbs,
    IReadOnlyDictionary<string, int> DirectCounts,
    IReadOnlyDictionary<string, int> SubtreeCounts,
    IReadOnlyList<CatalogBrowseEntry> Entries,
    string? NextCursor);

public sealed record CatalogSearchHit(CatalogRecordSummary Record, int Rank);

public sealed record CatalogResolutionDiagnosticView(
    string ResolutionKey,
    string RecordKind,
    string WinnerQualifiedId,
    IReadOnlyList<string> ShadowedQualifiedIds);

public sealed record CatalogSearchResult(
    IReadOnlyList<CatalogSearchHit> Records,
    string? NextCursor,
    IReadOnlyList<CatalogResolutionDiagnosticView>? ResolutionDiagnostics = null);

public interface ICatalogNavigator
{
    IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId);
    CatalogBrowseResult Browse(CatalogBrowseRequest request);
    CatalogSearchResult Search(CatalogSearchRequest request);
    CatalogRecordView Inspect(CatalogRecordRequest request);
    EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request);
    ReadableRulesResult ReadableRules(ReadableRulesRequest request);
}

public static class CatalogNavigationLimits
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumCollections = 64;
    public const int MaximumNodes = 4_096;
    public const int MaximumRecords = 20_000;
    public const int MaximumQueryLength = 256;
    public const int MaximumAliasesPerRecord = 32;
    public const int MaximumTextLength = 5_000;
    public const int MaximumContentLength = 1_000_000;
}

/// <summary>Immutable, validated effective catalog snapshot supplied by an application-manifest owner.</summary>
public sealed class CatalogNavigationManifest
{
    private readonly IReadOnlyList<CatalogCollectionDefinition> _collections;
    private readonly IReadOnlyList<CatalogNodeDefinition> _nodes;
    private readonly IReadOnlyList<CatalogRecordDefinition> _records;

    private CatalogNavigationManifest(
        ApplicationIdentifier applicationId,
        string fingerprint,
        string sortVersion,
        IReadOnlyList<CatalogCollectionDefinition> collections,
        IReadOnlyList<CatalogNodeDefinition> nodes,
        IReadOnlyList<CatalogRecordDefinition> records)
    {
        ApplicationId = applicationId;
        Fingerprint = fingerprint;
        SortVersion = sortVersion;
        _collections = collections;
        _nodes = nodes;
        _records = records;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Fingerprint { get; }
    public string SortVersion { get; }
    public IReadOnlyList<CatalogCollectionDefinition> Collections => _collections;
    public IReadOnlyList<CatalogNodeDefinition> Nodes => _nodes;
    public IReadOnlyList<CatalogRecordDefinition> Records => _records;

    public static CatalogNavigationManifest Create(
        ApplicationIdentifier applicationId,
        string fingerprint,
        string sortVersion,
        IReadOnlyList<CatalogCollectionDefinition> collections,
        IReadOnlyList<CatalogNodeDefinition> nodes,
        IReadOnlyList<CatalogRecordDefinition> records)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(records);
        if (!IsSha256(fingerprint) || !IsIdentifier(sortVersion) || collections.Count is < 1 or > CatalogNavigationLimits.MaximumCollections
            || nodes.Count > CatalogNavigationLimits.MaximumNodes || records.Count > CatalogNavigationLimits.MaximumRecords)
            throw new ArgumentException("The catalog manifest has invalid identity, version, or bounds.");

        var copiedCollections = collections.Select(CopyCollection).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (copiedCollections.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != copiedCollections.Length)
            throw new ArgumentException("Catalog collection IDs must be unique.");
        var collectionIds = copiedCollections.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var copiedNodes = nodes.Select(CopyNode).OrderBy(x => x.Collection, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (copiedNodes.Any(x => !collectionIds.Contains(x.Collection))
            || copiedNodes.GroupBy(x => (x.Collection, x.Path)).Any(x => x.Count() != 1)
            || copiedCollections.Any(collection => !copiedNodes.Any(node => node.Collection == collection.Id && node.Path == "")))
            throw new ArgumentException("Every collection requires one unique described root node.");
        var nodeKeys = copiedNodes.Select(x => (x.Collection, x.Path)).ToHashSet();
        if (copiedNodes.Any(node => Ancestors(node.Path).Any(parent => !nodeKeys.Contains((node.Collection, parent)))))
            throw new ArgumentException("Every logical catalog-node ancestor must be declared.");

        var copiedRecords = records.Select(record => CopyRecord(applicationId, record)).OrderBy(x => x.Collection, StringComparer.Ordinal)
            .ThenBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.QualifiedId, StringComparer.Ordinal).ToArray();
        if (copiedRecords.Any(x => !collectionIds.Contains(x.Collection) || !nodeKeys.Contains((x.Collection, x.Path)))
            || copiedRecords.GroupBy(x => (x.Collection, x.QualifiedId)).Any(x => x.Count() != 1))
            throw new ArgumentException("Every effective catalog record needs one collection/path and unique qualified ID.");

        return new(applicationId, fingerprint, sortVersion, ReadOnly(copiedCollections), ReadOnly(copiedNodes), ReadOnly(copiedRecords));
    }

    private static CatalogCollectionDefinition CopyCollection(CatalogCollectionDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsIdentifier(value.Id) || !IsText(value.Title, 200) || !IsText(value.Description, CatalogNavigationLimits.MaximumTextLength))
            throw new ArgumentException("A catalog collection requires bounded authored metadata.");
        return value with { Title = value.Title.Trim(), Description = value.Description.Trim() };
    }

    private static CatalogNodeDefinition CopyNode(CatalogNodeDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsIdentifier(value.Collection) || !IsPath(value.Path) || !IsText(value.Title, 200) || !Enum.IsDefined(value.DescriptionStatus)
            || (value.DescriptionStatus == CatalogDescriptionStatus.Authored && !IsText(value.Description, CatalogNavigationLimits.MaximumTextLength))
            || (value.DescriptionStatus == CatalogDescriptionStatus.Missing && !string.IsNullOrEmpty(value.Description)))
            throw new ArgumentException("A catalog node requires valid logical metadata and an explicit description status.");
        return value with { Title = value.Title.Trim(), Description = value.Description.Trim() };
    }

    private static CatalogRecordDefinition CopyRecord(ApplicationIdentifier applicationId, CatalogRecordDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsIdentifier(value.Collection) || !IsIdentifier(value.Kind) || !IsQualifiedId(applicationId, value.QualifiedId)
            || !IsText(value.Name, 400) || !IsText(value.Description, CatalogNavigationLimits.MaximumTextLength)
            || !IsPath(value.Path) || !IsIdentifier(value.Status) || value.Version < 1
            || string.IsNullOrWhiteSpace(value.ContentJson) || value.ContentJson.Length > CatalogNavigationLimits.MaximumContentLength
            || !IsSha256(value.ContentFingerprint) || !IsBounded(value.SourceId, 200) || !IsSourcePath(value.SourceLogicalPath)
            || !MatchesFingerprint(value.ContentJson, value.ContentFingerprint)
            || !ValidTerms(value.Aliases) || !ValidTerms(value.MatchPhrases))
            throw new ArgumentException("An effective catalog record has invalid identity, content, or redacted provenance.");
        try { JsonDocument.Parse(value.ContentJson).Dispose(); }
        catch (JsonException) { throw new ArgumentException("An effective catalog record requires valid JSON content."); }
        return value with
        {
            Name = value.Name.Trim(), Description = value.Description.Trim(),
            Aliases = ReadOnly(value.Aliases.Select(x => x.Trim())),
            MatchPhrases = ReadOnly(value.MatchPhrases.Select(x => x.Trim())),
            ContentFingerprint = value.ContentFingerprint.ToUpperInvariant()
        };
    }

    internal static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 63 }
        && char.IsAsciiLetterLower(value[0])
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    internal static bool IsPath(string? value, bool allowEmpty = true) => value is not null
        && (allowEmpty || value.Length > 0)
        && value.Length <= 500
        && (value.Length == 0 || value.Split('/').All(IsIdentifier));

    private static bool IsSourcePath(string? value) => value is { Length: > 0 and <= 500 }
        && value == value.Replace('\\', '/')
        && value.Split('/').All(segment => segment is not "" and not "." and not ".." && !segment.Any(char.IsControl) && !segment.Contains(':'));

    private static bool IsQualifiedId(ApplicationIdentifier applicationId, string? value) => value is { Length: > 2 and <= 400 }
        && value.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
        && value[(applicationId.Value.Length + 1)..].Split('.').All(IsIdentifier);

    private static bool IsText(string? value, int limit) => IsBounded(value, limit) && !string.IsNullOrWhiteSpace(value);
    private static bool IsBounded(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit && !value.Any(char.IsControl);
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static bool ValidTerms(IReadOnlyList<string>? values) => values is not null && values.Count <= CatalogNavigationLimits.MaximumAliasesPerRecord
        && values.All(value => IsText(value, 200));
    private static bool MatchesFingerprint(string content, string fingerprint) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))) == fingerprint.ToUpperInvariant();
    private static IEnumerable<string> Ancestors(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        yield return "";
        for (var index = 1; index < segments.Length; index++) yield return string.Join('/', segments.Take(index));
    }
    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

/// <summary>Pure, vector-free navigation over one validated immutable effective manifest.</summary>
public sealed class InMemoryCatalogNavigator(
    CatalogNavigationManifest manifest,
    CatalogCursorCodec cursors,
    CatalogExtensionResolutionContext? resolution = null) : ICatalogNavigator
{
    private const string BrowseSortVersion = "catalog-browse-v1";

    public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId)
    {
        RequireApplication(applicationId);
        return Array.AsReadOnly(manifest.Collections.Select(collection => new CatalogCollectionSummary(
            collection.Id, collection.Title, collection.Description, manifest.Records.Count(record => record.Collection == collection.Id))).ToArray());
    }

    public CatalogBrowseResult Browse(CatalogBrowseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); RequireApplication(request.ApplicationId);
        ValidatePageSize(request.PageSize); ValidateCollection(request.Collection); ValidatePath(request.Branch);
        var node = FindNode(request.Collection, request.Branch);
        var resolutionFingerprint = resolution?.Fingerprint ?? "none";
        var scope = Scope(request.Collection, request.Branch,
            Fingerprint("browse", request.Collection, request.Branch, resolutionFingerprint),
            BrowseSortVersion, request.PageSize);
        var lastKey = DecodeLastKey(request.Cursor, scope);
        var effectiveRecords = ResolvedRecords();
        var entries = DirectChildren(request.Collection, request.Branch).Concat(DirectRecords(
                request.Collection, request.Branch, effectiveRecords))
            .OrderBy(entry => entry.StableKey, StringComparer.Ordinal).ToArray();
        var page = Page(entries, lastKey, request.PageSize, scope, entry => entry.StableKey);
        return new(ToNodeView(node), Breadcrumbs(request.Collection, request.Branch), Counts(
                request.Collection, request.Branch, direct: true, effectiveRecords),
            Counts(request.Collection, request.Branch, direct: false, effectiveRecords), page.Values, page.NextCursor);
    }

    public CatalogSearchResult Search(CatalogSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); RequireApplication(request.ApplicationId);
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > CatalogNavigationLimits.MaximumQueryLength)
            throw new ArgumentException("A bounded search query is required.", nameof(request));
        if (request.Collection is not null) ValidateCollection(request.Collection);
        if (request.Collection is null && !string.IsNullOrEmpty(request.Branch)) throw new ArgumentException("A branch filter requires a collection.", nameof(request));
        if (request.NamespaceId is not null && !CatalogNamespaceIdentity.IsNamespaceId(request.NamespaceId))
            throw new ArgumentException("A namespace filter must be a registered namespace identifier shape.", nameof(request));
        ValidatePath(request.Branch); ValidatePageSize(request.PageSize);
        var kinds = Set(request.Kinds); var statuses = Set(request.Statuses);
        var normalizedQuery = Normalize(request.Query);
        var collection = request.Collection ?? "*";
        var resolved = CatalogExtensionSearch.Apply(
            resolution,
            values: manifest.Records
                .Where(record => request.Collection is null || record.Collection == request.Collection)
                .Where(record => request.Collection is null || InBranch(record.Path, request.Branch))
                .Where(record => kinds.Count == 0 || kinds.Contains(record.Kind))
                .Where(record => statuses.Count == 0 || statuses.Contains(record.Status))
                .Where(record => request.NamespaceId is null || InNamespace(record.QualifiedId, request.NamespaceId))
                .Select(record => new CatalogSearchHit(ToSummary(record), Rank(record, normalizedQuery) ?? int.MaxValue))
                .OrderBy(value => value.Rank).ThenBy(value => value.Record.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.Record.QualifiedId, StringComparer.Ordinal).ToArray(),
            qualifiedId: hit => hit.Record.QualifiedId,
            recordKind: hit => hit.Record.Kind,
            includeShadowed: request.IncludeShadowed);
        var filter = Fingerprint("search", normalizedQuery, collection, request.Branch, Join(kinds), Join(statuses),
            request.NamespaceId ?? "*", request.IncludeShadowed.ToString(), resolved.ResolutionFingerprint);
        var scope = Scope(collection, request.Branch, filter, manifest.SortVersion, request.PageSize);
        var lastKey = DecodeLastKey(request.Cursor, scope);
        var searchable = resolved.Records.Where(value => value.Rank != int.MaxValue).ToArray();
        var page = Page(searchable, lastKey, request.PageSize, scope,
            hit => $"{hit.Rank:D1}/{hit.Record.Kind}/{hit.Record.QualifiedId}");
        var pageIds = page.Values.Select(value => value.Record.QualifiedId).ToHashSet(StringComparer.Ordinal);
        var resolutions = resolved.Diagnostics.Where(value => pageIds.Contains(value.WinnerQualifiedId)
            || value.ShadowedQualifiedIds.Any(pageIds.Contains)).ToArray();
        return new(page.Values, page.NextCursor, Array.AsReadOnly(resolutions));
    }

    public CatalogRecordView Inspect(CatalogRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); RequireApplication(request.ApplicationId); ValidateCollection(request.Collection);
        if (!IsQualifiedRequestId(request.QualifiedId)) throw new ArgumentException("A qualified record ID in this application is required.", nameof(request));
        var record = manifest.Records.SingleOrDefault(value => value.Collection == request.Collection && value.QualifiedId == request.QualifiedId)
            ?? throw new KeyNotFoundException("CATALOG_RECORD_UNKNOWN");
        return new(ToSummary(record), record.ContentJson);
    }

    public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); RequireApplication(request.ApplicationId);
        ValidatePageSize(request.PageSize);
        var resolved = CatalogExtensionSearch.Apply(resolution, manifest.Records,
            record => record.QualifiedId, record => record.Kind);
        var extensionById = (resolution?.Extensions ?? []).ToDictionary(
            value => value.ExtensionId, StringComparer.Ordinal);
        var baseKeys = manifest.Records.Select(record =>
        {
            var identity = resolution is null
                ? (Owner: "base", Key: record.QualifiedId)
                : CatalogExtensionSearch.OwnerAndKey(resolution, record.QualifiedId);
            return (record.Kind, identity.Owner, identity.Key);
        }).Where(value => value.Owner == "base")
            .Select(value => (value.Kind, value.Key)).ToHashSet();
        var projected = resolved.Records.Select(record =>
        {
            var identity = resolution is null
                ? (Owner: "base", Key: record.QualifiedId)
                : CatalogExtensionSearch.OwnerAndKey(resolution, record.QualifiedId);
            var extension = identity.Owner == "base" ? null : extensionById[identity.Owner];
            var roles = new[] { record.Kind }.Concat(record.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal).Take(8).ToArray();
            return new EffectiveApplicationContentRecord(ToSummary(record), identity.Owner,
                extension?.DisplayName ?? "Core", extension?.Classification ?? "core",
                Array.AsReadOnly(roles), identity.Owner != "base" && !baseKeys.Contains((record.Kind, identity.Key)));
        }).OrderBy(value => value.Record.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Record.Name, StringComparer.Ordinal)
            .ThenBy(value => value.Record.QualifiedId, StringComparer.Ordinal).ToArray();
        var fingerprint = resolution?.Fingerprint ?? "none";
        var scope = Scope("*", "", Fingerprint("effective-content", fingerprint),
            "effective-content-v1", request.PageSize);
        var lastKey = DecodeLastKey(request.Cursor, scope);
        var page = Page(projected, lastKey, request.PageSize, scope,
            value => $"{value.Record.Kind}/{value.Record.Name}/{value.Record.QualifiedId}");
        var activeExtensions = (resolution?.Extensions ?? []).Select(value =>
            new EffectiveApplicationExtensionView(value.ExtensionId, value.DisplayName,
                value.Description, value.Classification, value.SourceIds, value.NamespaceIds)).ToArray();
        return new(manifest.ApplicationId.Value, fingerprint, Array.AsReadOnly(activeExtensions),
            page.Values, Array.AsReadOnly(page.Values.Where(value => value.IsAdditive).ToArray()), page.NextCursor);
    }

    public ReadableRulesResult ReadableRules(ReadableRulesRequest request) =>
        ReadableRuleCatalogProjection.Project(manifest, resolution, request);

    private IReadOnlyList<CatalogBrowseEntry> DirectChildren(string collection, string branch) => manifest.Nodes
        .Where(node => node.Collection == collection && node.Path != branch && Parent(node.Path) == branch)
        .OrderBy(node => node.Path, StringComparer.Ordinal)
        .Select(node => new CatalogBrowseEntry(CatalogBrowseEntryKind.Node, ToNodeView(node), null)).ToArray();

    private IReadOnlyList<CatalogRecordDefinition> ResolvedRecords() => CatalogExtensionSearch.Apply(
        resolution, manifest.Records, record => record.QualifiedId, record => record.Kind).Records;

    private static IReadOnlyList<CatalogBrowseEntry> DirectRecords(
        string collection, string branch, IReadOnlyList<CatalogRecordDefinition> records) => records
        .Where(record => record.Collection == collection && record.Path == branch)
        .OrderBy(record => record.Kind, StringComparer.Ordinal).ThenBy(record => record.QualifiedId, StringComparer.Ordinal)
        .Select(record => new CatalogBrowseEntry(CatalogBrowseEntryKind.Record, null, ToSummary(record))).ToArray();

    private static IReadOnlyDictionary<string, int> Counts(
        string collection, string branch, bool direct, IReadOnlyList<CatalogRecordDefinition> records) => new ReadOnlyDictionary<string, int>(records
        .Where(record => record.Collection == collection && (direct ? record.Path == branch : InBranch(record.Path, branch)))
        .GroupBy(record => record.Kind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

    private IReadOnlyList<CatalogNodeView> Breadcrumbs(string collection, string branch) => AncestorPaths(branch)
        .Select(path => ToNodeView(FindNode(collection, path))).ToArray();

    private CatalogNodeDefinition FindNode(string collection, string branch) => manifest.Nodes.SingleOrDefault(node => node.Collection == collection && node.Path == branch)
        ?? throw new KeyNotFoundException("CATALOG_NODE_UNKNOWN");

    private (IReadOnlyList<T> Values, string? NextCursor) Page<T>(IReadOnlyList<T> entries, string lastKey, int pageSize, CatalogCursorScope scope, Func<T, string> key)
    {
        var values = entries.Where(value => string.IsNullOrEmpty(lastKey) || string.CompareOrdinal(key(value), lastKey) > 0).Take(pageSize + 1).ToArray();
        var hasMore = values.Length > pageSize;
        var page = hasMore ? values[..pageSize] : values;
        return (Array.AsReadOnly(page), hasMore ? cursors.Encode(scope.Bind(key(page[^1]))) : null);
    }

    private string DecodeLastKey(string? cursor, CatalogCursorScope scope) => string.IsNullOrWhiteSpace(cursor) ? "" : cursors.Decode(cursor, scope).LastStableKey;
    private CatalogCursorScope Scope(string collection, string branch, string filter, string sort, int pageSize) => new(manifest.Fingerprint, manifest.ApplicationId.Value, collection, branch, filter, sort, pageSize);
    private void RequireApplication(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (applicationId != manifest.ApplicationId) throw new ArgumentException("CATALOG_APPLICATION_MISMATCH", nameof(applicationId));
    }
    private void ValidateCollection(string collection)
    {
        if (!CatalogNavigationManifest.IsIdentifier(collection) || !manifest.Collections.Any(value => value.Id == collection)) throw new ArgumentException("CATALOG_COLLECTION_UNKNOWN", nameof(collection));
    }
    private void ValidatePath(string path) { if (!CatalogNavigationManifest.IsPath(path)) throw new ArgumentException("CATALOG_PATH_INVALID", nameof(path)); }
    private static void ValidatePageSize(int pageSize) { if (pageSize is < 1 or > CatalogNavigationLimits.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize)); }
    private bool IsQualifiedRequestId(string value) => value is not null && value.StartsWith(manifest.ApplicationId.Value + ".", StringComparison.Ordinal);
    private static CatalogNodeView ToNodeView(CatalogNodeDefinition node) => new(node.Collection, node.Path, node.Title, node.Description, node.DescriptionStatus);
    private static CatalogRecordSummary ToSummary(CatalogRecordDefinition record) => new(record.Collection, record.Kind, record.QualifiedId, record.Name, record.Description, record.Path, record.Status, record.Version, record.ContentFingerprint, record.SourceId, record.SourceLogicalPath);
    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static bool InBranch(string path, string branch) => branch.Length == 0 || path == branch || path.StartsWith(branch + "/", StringComparison.Ordinal);
    private static bool InNamespace(string qualifiedId, string namespaceId)
    {
        var recordNamespace = CatalogNamespaceIdentity.NamespaceOf(qualifiedId);
        return namespaceId == CatalogNamespaceIdentity.RootNamespaceId
            ? recordNamespace == namespaceId
            : recordNamespace == namespaceId || recordNamespace.StartsWith(namespaceId + ".", StringComparison.Ordinal);
    }
    private static IReadOnlyList<string> AncestorPaths(string path) => ["", .. path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select((_, index) => string.Join('/', path.Split('/').Take(index + 1)))];
    private static HashSet<string> Set(IReadOnlyList<string>? values)
    {
        if (values is null) return new(StringComparer.Ordinal);
        if (values.Count > CatalogNavigationLimits.MaximumAliasesPerRecord || values.Any(value => !CatalogNavigationManifest.IsIdentifier(value)))
            throw new ArgumentException("Catalog filters must contain bounded identifiers.");
        return values.ToHashSet(StringComparer.Ordinal);
    }
    private static string Join(IEnumerable<string> values) => string.Join(',', values.Order(StringComparer.Ordinal));
    private static string Fingerprint(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    private static IEnumerable<string> Tokens(string value) => new string(Normalize(value).Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static int? Rank(CatalogRecordDefinition record, string query)
    {
        if (Normalize(record.QualifiedId) == query) return 0;
        if (record.Aliases.Concat(record.MatchPhrases).Any(value => Normalize(value) == query)) return 1;
        if (Normalize(record.Name) == query) return 2;
        var fields = new[] { record.QualifiedId, record.Name, record.Description, record.Path }.Concat(record.Aliases).Concat(record.MatchPhrases).ToArray();
        if (fields.Any(value => Normalize(value).StartsWith(query, StringComparison.Ordinal))) return 3;
        var required = Tokens(query).ToHashSet(StringComparer.Ordinal);
        var actual = fields.SelectMany(Tokens).ToHashSet(StringComparer.Ordinal);
        return required.Count > 0 && required.All(actual.Contains) ? 4 : null;
    }
}
