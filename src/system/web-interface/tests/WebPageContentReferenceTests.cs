using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Tests;

public sealed class WebPageContentReferenceTests
{
    [Fact]
    public void Legacy_reference_serializes_as_the_existing_page_id_only_shape()
    {
        var legacy = new WebPageContentReference("home");
        Assert.False(legacy.IsPinned);
        Assert.Equal("{\"PageId\":\"home\"}", JsonSerializer.Serialize(legacy));
        Assert.Equal(legacy, JsonSerializer.Deserialize<WebPageContentReference>("{\"PageId\":\"home\"}"));
        Assert.Equal("CONTENT_REFERENCE_PARTIAL_PIN", Code(() => new WebPageContentReference("home", 1)));
    }

    [Fact]
    public void Exact_pin_is_stable_across_asset_order_and_has_no_asset_bodies()
    {
        var first = HtmlDocument("page", 2, [Asset("assets/a.css", "text/css", [1]), Asset("assets/b.svg", "image/svg+xml", [2])]);
        var second = first with { Assets = first.Assets.Reverse().ToArray() };
        var left = WebPageContentReference.FromRevision(first);
        var right = WebPageContentReference.FromRevision(second);

        Assert.True(left.IsPinned);
        Assert.Equal(left.AssetInventoryFingerprint, right.AssetInventoryFingerprint);
        Assert.True(left.MatchesRevision(second));
        var json = JsonSerializer.Serialize(left);
        Assert.DoesNotContain("\"Content\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AQI=", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_and_revision_drift_cannot_match_an_exact_pin()
    {
        var source = HtmlDocument("page", 2, [Asset("assets/a.css", "text/css", [1])]);
        var pin = WebPageContentReference.FromRevision(source);
        var changedBytes = HtmlDocument("page", 2, [Asset("assets/a.css", "text/css", [2])]);
        var changedType = HtmlDocument("page", 2, [Asset("assets/a.css", "text/plain", [1])]);
        var changedPath = HtmlDocument("page", 2, [Asset("assets/b.css", "text/css", [1])]);
        var changedRevision = HtmlDocument("page", 3, [Asset("assets/a.css", "text/css", [1])]);

        Assert.False(pin.MatchesRevision(changedBytes));
        Assert.False(pin.MatchesRevision(changedType));
        Assert.False(pin.MatchesRevision(changedPath));
        Assert.False(pin.MatchesRevision(changedRevision));
        Assert.Equal("CONTENT_REFERENCE_MISMATCH", Code(() => pin.RequireMatches(changedRevision)));
    }

    [Fact]
    public void Corrupt_retained_composition_or_asset_evidence_is_refused()
    {
        var composition = CompositionDocument("page", 1);
        var noncanonical = composition with { CompositionJson = "{\"root\":{\"kind\":\"text\",\"text\":\"x\"},\"components\":[],\"generation\":\"g\",\"formatVersion\":1}" };
        Assert.Equal("CONTENT_REFERENCE_NONCANONICAL_CONTENT", Code(() => WebPageContentReference.FromRevision(noncanonical)));

        var html = HtmlDocument("page", 1, [Asset("assets/a.css", "text/css", [1])]);
        var invalidAsset = html with { Assets = [html.Assets[0] with { ContentHash = new string('A', 64) }] };
        Assert.Equal("CONTENT_REFERENCE_INVALID_ASSET", Code(() => WebPageContentReference.FromRevision(invalidAsset)));
    }

    private static WebPageRevisionDocument HtmlDocument(string pageId, int revision, IReadOnlyList<WebPageAssetDocument> assets) =>
        Document(pageId, revision, new WebPageBundle("<p>page</p>", assets.Select(asset => new WebPageAssetUpload(asset.Path, asset.Content)).ToArray()), assets);

    private static WebPageRevisionDocument CompositionDocument(string pageId, int revision) =>
        Document(pageId, revision, new WebPageBundle(string.Empty, [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = "{\"formatVersion\":1,\"generation\":\"g\",\"components\":[],\"root\":{\"kind\":\"text\",\"text\":\"x\"}}"
        }, []);

    private static WebPageRevisionDocument Document(string pageId, int revision, WebPageBundle bundle, IReadOnlyList<WebPageAssetDocument> sourceAssets)
    {
        var normalized = WebPageContentValidator.Normalize(bundle);
        var contentTypes = sourceAssets.ToDictionary(asset => asset.Path, asset => asset.ContentType, StringComparer.Ordinal);
        var assets = normalized.Assets.Select(asset => Asset(asset.Path,
            contentTypes.TryGetValue(asset.Path, out var contentType) ? contentType : ContentType(asset.Path), asset.Content, pageId, revision)).ToArray();
        var contentHash = normalized.CompositionHash ?? Hash(Encoding.UTF8.GetBytes(normalized.Html));
        var summary = new WebPageRevisionSummary(pageId, revision, false, DateTime.UtcNow, contentHash, assets.Length,
            assets.Sum(asset => (long)asset.Content.Length)) { ContentFormat = normalized.ContentFormat, CompositionHash = normalized.CompositionHash };
        return new(summary, normalized.Html, assets)
        { ContentFormat = normalized.ContentFormat, CompositionJson = normalized.CompositionJson, CompositionHash = normalized.CompositionHash };
    }

    private static WebPageAssetDocument Asset(string path, string contentType, byte[] content, string pageId = "page", int revision = 1) =>
        new(pageId, revision, path, contentType, Hash(content), content);
    private static string ContentType(string path) => path.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "text/css";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Code(Action action) => Assert.Throws<WebPageStoreException>(action).Code;
}
