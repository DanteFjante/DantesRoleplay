using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Web.Pages;

/// <summary>Exact retained-content evidence. A reference is not publication or execution authority.</summary>
public sealed record WebPageContentReference
{
    private const string InventoryDomain = "dantes-roleplay/web-page-asset-inventory/v1";

    [JsonConstructor]
    public WebPageContentReference(string pageId, int? revision = null, string? contentFormat = null,
        string? contentHash = null, string? assetInventoryFingerprint = null)
    {
        PageId = pageId;
        Revision = revision;
        ContentFormat = contentFormat;
        ContentHash = contentHash;
        AssetInventoryFingerprint = assetInventoryFingerprint;
        ValidateCore();
    }

    public string PageId { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Revision { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentFormat { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentHash { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AssetInventoryFingerprint { get; }
    [JsonIgnore] public bool IsPinned { get { ValidateCore(); return Revision is not null; } }

    public WebPageContentReference Validate()
    {
        ValidateCore();
        return this;
    }

    public static WebPageContentReference FromRevision(WebPageRevisionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var summary = document.Summary ?? throw Invalid("CONTENT_REFERENCE_SUMMARY_REQUIRED", "A revision summary is required.");
        if (!WebPageId.IsValid(summary.PageId) || summary.Revision < 1)
            throw Invalid("CONTENT_REFERENCE_INVALID_IDENTITY", "The retained page identity is invalid.");
        if (document.ContentFormat != summary.ContentFormat || document.CompositionHash != summary.CompositionHash)
            throw Invalid("CONTENT_REFERENCE_SUMMARY_MISMATCH", "The document and summary content facts differ.");
        if (document.Assets is null || document.Assets.Count > WebPageBundleLimits.MaximumEntries - 1)
            throw Invalid("CONTENT_REFERENCE_ASSET_LIMIT", "The retained asset collection is invalid.");

        var assets = new List<WebPageAssetUpload>(document.Assets.Count);
        var inventory = new List<WebPageAssetDocument>(document.Assets.Count);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long assetBytes = 0;
        foreach (var asset in document.Assets)
        {
            if (asset?.Content?.Length > WebPageBundleLimits.MaximumEntryBytes)
                throw Invalid("CONTENT_REFERENCE_ASSET_LIMIT", "A retained asset exceeds its size limit.");
            if (asset is null || asset.Content is null || asset.PageId != summary.PageId || asset.Revision != summary.Revision ||
                !WebPageAssetPath.TryValidate(asset.Path, out var path) || !path.StartsWith("assets/", StringComparison.Ordinal) ||
                !paths.Add(path) || !SafeContentType(asset.ContentType) || !UpperHash(asset.ContentHash) ||
                !string.Equals(asset.ContentHash, Hash(asset.Content), StringComparison.Ordinal))
                throw Invalid("CONTENT_REFERENCE_INVALID_ASSET", "A retained asset does not match its exact metadata or bytes.");
            assetBytes = checked(assetBytes + asset.Content.Length);
            if (assetBytes > WebPageBundleLimits.MaximumUncompressedBytes)
                throw Invalid("CONTENT_REFERENCE_ASSET_LIMIT", "Retained assets exceed their size limit.");
            assets.Add(new(path, asset.Content));
            inventory.Add(asset with { Path = path });
        }
        if (summary.AssetCount != inventory.Count || summary.AssetBytes != assetBytes)
            throw Invalid("CONTENT_REFERENCE_SUMMARY_MISMATCH", "The retained asset summary does not match its assets.");

        WebPageBundle normalized;
        try
        {
            normalized = WebPageContentValidator.Normalize(new WebPageBundle(document.Html, assets)
            {
                ContentFormat = document.ContentFormat,
                CompositionJson = document.CompositionJson,
                CompositionHash = document.CompositionHash
            });
        }
        catch (WebPageBundleException exception)
        {
            throw Invalid("CONTENT_REFERENCE_INVALID_CONTENT", exception.Message);
        }
        if (normalized.ContentFormat != document.ContentFormat || normalized.Html != document.Html ||
            normalized.CompositionJson != document.CompositionJson || normalized.CompositionHash != document.CompositionHash)
            throw Invalid("CONTENT_REFERENCE_NONCANONICAL_CONTENT", "Retained content is not an exact canonical payload.");

        var contentHash = normalized.ContentFormat == WebPageContentFormat.Composition
            ? normalized.CompositionHash!
            : Hash(Encoding.UTF8.GetBytes(normalized.Html));
        if (!UpperHash(summary.ContentHash) || summary.ContentHash != contentHash)
            throw Invalid("CONTENT_REFERENCE_CONTENT_HASH_MISMATCH", "The retained content hash does not match its content.");
        return new(summary.PageId, summary.Revision, normalized.ContentFormat, contentHash, InventoryFingerprint(inventory));
    }

    public bool MatchesRevision(WebPageRevisionDocument document)
    {
        ValidateCore();
        return IsPinned && Equals(FromRevision(document));
    }

    public void RequireMatches(WebPageRevisionDocument document)
    {
        if (!MatchesRevision(document))
            throw Invalid("CONTENT_REFERENCE_MISMATCH", "The retained revision does not match this exact content reference.");
    }

    private void ValidateCore()
    {
        if (!WebPageId.IsValid(PageId)) throw Invalid("CONTENT_REFERENCE_INVALID_PAGE", "The content reference page ID is invalid.");
        var values = new object?[] { Revision, ContentFormat, ContentHash, AssetInventoryFingerprint };
        if (values.All(value => value is null)) return;
        if (values.Any(value => value is null) || Revision < 1 || ContentFormat is not (WebPageContentFormat.Html or WebPageContentFormat.Composition) ||
            !UpperHash(ContentHash!) || !UpperHash(AssetInventoryFingerprint!))
            throw Invalid("CONTENT_REFERENCE_PARTIAL_PIN", "A content reference must be legacy or carry one complete exact pin.");
    }

    private static string InventoryFingerprint(IEnumerable<WebPageAssetDocument> assets)
    {
        var items = assets.OrderBy(asset => asset.Path, StringComparer.Ordinal)
            .Select(asset => new object[] { asset.Path, asset.ContentType, asset.ContentHash, asset.Content.Length }).ToArray();
        var json = JsonSerializer.Serialize(items);
        return Hash(Encoding.UTF8.GetBytes(InventoryDomain + '\0' + json));
    }

    private static bool SafeContentType(string? value) => value is { Length: > 0 and <= 200 } && !value.Any(char.IsControl);
    private static bool UpperHash(string? value) => value is { Length: 64 } && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static WebPageStoreException Invalid(string code, string message) => new(code, message);
}
