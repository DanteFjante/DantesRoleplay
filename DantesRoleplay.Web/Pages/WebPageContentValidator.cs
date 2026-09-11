using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Web.Pages;

/// <summary>Normalizes a page payload before it reaches draft or revision storage.</summary>
public static class WebPageContentValidator
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static WebPageBundle Normalize(WebPageBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var assets = NormalizeAssets(bundle.Assets);
        return bundle.ContentFormat switch
        {
            WebPageContentFormat.Html => NormalizeHtml(bundle, assets),
            WebPageContentFormat.Composition => NormalizeComposition(bundle, assets),
            _ => throw Invalid("UNKNOWN_CONTENT_FORMAT", "The page content format is not supported.")
        };
    }

    private static WebPageBundle NormalizeHtml(WebPageBundle bundle, IReadOnlyList<WebPageAssetUpload> assets)
    {
        if (bundle.CompositionJson is not null || bundle.CompositionHash is not null)
            throw Invalid("HTML_COMPOSITION_FIELDS", "HTML content cannot include composition fields.");
        if (string.IsNullOrWhiteSpace(bundle.Html))
            throw Invalid("EMPTY_HTML", "index.html cannot be empty.");
        var htmlBytes = CountUtf8(bundle.Html, "HTML_TOO_LARGE", "index.html exceeds its size limit.",
            "INVALID_HTML_ENCODING", "index.html must be valid UTF-8.");
        EnsureTotal(htmlBytes, assets, 0);
        return new WebPageBundle(bundle.Html, assets) { ContentFormat = WebPageContentFormat.Html };
    }

    private static WebPageBundle NormalizeComposition(WebPageBundle bundle, IReadOnlyList<WebPageAssetUpload> assets)
    {
        if (bundle.Html != string.Empty)
            throw Invalid("COMPOSITION_HTML_REQUIRED_EMPTY", "Composition content requires an exactly empty HTML payload.");
        if (string.IsNullOrWhiteSpace(bundle.CompositionJson))
            throw Invalid("MISSING_COMPOSITION", "Composition content requires composition JSON.");
        _ = CountUtf8(bundle.CompositionJson, "COMPOSITION_TOO_LARGE", "Composition JSON exceeds its size limit.",
            "INVALID_COMPOSITION_ENCODING", "composition.json must be valid UTF-8.");

        var parsed = new WebCompositionParser().Parse(bundle.CompositionJson, assets.Select(asset => asset.Path));
        if (!parsed.IsValid)
        {
            var error = parsed.Errors[0];
            throw Invalid(error.Code, error.Message);
        }

        var canonical = Canonicalize(bundle.CompositionJson);
        var canonicalBytes = CountUtf8(canonical, "COMPOSITION_TOO_LARGE", "Composition JSON exceeds its size limit.",
            "INVALID_COMPOSITION_ENCODING", "composition.json must be valid UTF-8.");
        EnsureTotal(0, assets, canonicalBytes);
        var hash = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(canonical)));
        if (bundle.CompositionHash is not null && !string.Equals(bundle.CompositionHash, hash, StringComparison.Ordinal))
            throw Invalid("COMPOSITION_HASH_MISMATCH", "The supplied composition hash does not match canonical composition JSON.");
        return new WebPageBundle(string.Empty, assets)
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = canonical,
            CompositionHash = hash
        };
    }

    private static IReadOnlyList<WebPageAssetUpload> NormalizeAssets(IReadOnlyList<WebPageAssetUpload>? assets)
    {
        if (assets is null) throw Invalid("MISSING_ASSETS", "The asset list is required.");
        if (assets.Count > WebPageBundleLimits.MaximumEntries - 1)
            throw TooLarge("The bundle contains more than 256 regular files.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<WebPageAssetUpload>(assets.Count);
        long assetBytes = 0;
        foreach (var asset in assets)
        {
            if (asset is null || asset.Content is null)
                throw Invalid("INVALID_ASSET", "Assets require a path and content.");
            if (!WebPageAssetPath.TryValidate(asset.Path, out var path))
                throw Invalid("UNSAFE_ASSET_PATH", $"The asset path '{asset.Path}' is not safe.");
            if (!path.StartsWith("assets/", StringComparison.Ordinal))
                throw Invalid("ASSET_ROOT_REQUIRED", $"The asset '{path}' must be below the root assets/ directory.");
            if (!paths.Add(path)) throw Invalid("DUPLICATE_ASSET_PATH", $"The asset path '{path}' appears more than once.");
            if (asset.Content.Length > WebPageBundleLimits.MaximumEntryBytes)
                throw TooLarge($"The asset '{path}' exceeds its size limit.");
            assetBytes = checked(assetBytes + asset.Content.Length);
            if (assetBytes > WebPageBundleLimits.MaximumUncompressedBytes)
                throw TooLarge("The bundle exceeds the 25 MiB uncompressed limit.");
            normalized.Add(new WebPageAssetUpload(path, asset.Content.ToArray()));
        }
        return normalized.AsReadOnly();
    }

    private static string Canonicalize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = WebComposition.MaximumDepth
            });
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output)) WriteCanonical(writer, document.RootElement);
            return Utf8.GetString(output.ToArray());
        }
        catch (JsonException exception)
        {
            throw Invalid("INVALID_JSON", exception.Message);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static int CountUtf8(string value, string code, string message, string encodingCode, string encodingMessage)
    {
        int count;
        try { count = Utf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw Invalid(encodingCode, encodingMessage); }
        if (count > WebPageBundleLimits.MaximumHtmlBytes) throw TooLarge(code, message);
        return count;
    }

    private static void EnsureTotal(int contentBytes, IReadOnlyList<WebPageAssetUpload> assets, int compositionBytes)
    {
        long total = contentBytes + compositionBytes;
        foreach (var asset in assets) total = checked(total + asset.Content.Length);
        if (total > WebPageBundleLimits.MaximumUncompressedBytes)
            throw TooLarge("The bundle exceeds the 25 MiB uncompressed limit.");
    }

    private static WebPageBundleException Invalid(string code, string message) => new(code, message);
    private static WebPageBundleException TooLarge(string message) => new("BUNDLE_TOO_LARGE", message, 413);
    private static WebPageBundleException TooLarge(string code, string message) => new(code, message, 413);
}
