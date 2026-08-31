using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

/// <summary>Bounded control projection over the authoritative immutable web-page store.</summary>
public sealed class ControlPageEditor(IWebPageStore pages)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 1024;
    public const int MaximumJsonBodyBytes = WebPageBundleLimits.MaximumHtmlBytes + 4096;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);
    private readonly IWebPageStore _pages = pages;

    public async Task<ControlPageResult<WebPageSummary>> ListPagesAsync(
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        var pageSize = PageSize(limit);
        var page = await _pages.ListPageAsync(
            Decode(cursor, "pages", "all", pageSize), pageSize, cancellationToken);
        return new(
            page.Pages,
            page.NextPageId is null ? null : Encode(new("pages", "all", pageSize, page.NextPageId)));
    }

    public Task<WebPageSummary?> GetPageAsync(
        string pageId,
        CancellationToken cancellationToken = default) =>
        _pages.GetSummaryAsync(PageId(pageId), cancellationToken);

    public async Task<ControlPageResult<WebPageRevisionSummary>> ListRevisionsAsync(
        string pageId,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        pageId = PageId(pageId);
        var pageSize = PageSize(limit);
        var lastKey = Decode(cursor, "revisions", pageId, pageSize);
        int? before = null;
        if (lastKey is not null)
        {
            if (!int.TryParse(
                    lastKey, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                throw Stale();
            before = parsed;
        }
        var page = await _pages.ListRevisionsAsync(pageId, before, pageSize, cancellationToken);
        return new(
            page.Revisions,
            page.NextRevision is null
                ? null
                : Encode(new("revisions", pageId, pageSize,
                    page.NextRevision.Value.ToString(CultureInfo.InvariantCulture))));
    }

    public async Task<ControlPageRevisionDetail?> GetRevisionAsync(
        string pageId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var document = await _pages.GetRevisionAsync(
            PageId(pageId), Revision(revision), cancellationToken);
        return document is null ? null : Detail(document);
    }

    public async Task<ControlPageRevisionDetail> AppendDraftAsync(
        string pageId,
        ControlPageDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Html is null || string.IsNullOrWhiteSpace(request.Html))
            throw Invalid("INVALID_HTML", "Draft HTML cannot be empty.");
        if (Encoding.UTF8.GetByteCount(request.Html) > WebPageBundleLimits.MaximumHtmlBytes)
            throw new ControlPageEditorException(
                "HTML_TOO_LARGE", "Draft HTML exceeds the 1 MiB limit.", StatusCodes.Status413PayloadTooLarge);
        var document = await _pages.AppendDraftAsync(
            PageId(pageId),
            Revision(request.BaseRevision),
            Revision(request.ExpectedLatestRevision),
            request.Html,
            cancellationToken);
        return Detail(document);
    }

    public Task<WebPageActivationResult> ActivateAsync(
        string pageId,
        ControlPageActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pages.ActivateRevisionAsync(
            PageId(pageId),
            Revision(request.Revision),
            Revision(request.ExpectedActiveRevision),
            cancellationToken);
    }

    public async Task<ControlPageBundle?> ExportAsync(
        string pageId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        pageId = PageId(pageId);
        revision = Revision(revision);
        var document = await _pages.GetRevisionAsync(pageId, revision, cancellationToken);
        if (document is null) return null;
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(
                archive, "index.html", Encoding.UTF8.GetBytes(document.Html), cancellationToken);
            foreach (var asset in document.Assets.OrderBy(value => value.Path, StringComparer.Ordinal))
                await WriteEntryAsync(archive, asset.Path, asset.Content, cancellationToken);
        }
        return new($"{pageId}-revision-{revision}.zip", output.ToArray());
    }

    public async Task<string?> PreviewHtmlAsync(
        string pageId,
        int revision,
        CancellationToken cancellationToken = default) =>
        (await _pages.GetRevisionAsync(PageId(pageId), Revision(revision), cancellationToken))?.Html;

    public async Task<WebPageAssetDocument?> PreviewAssetAsync(
        string pageId,
        int revision,
        string? assetPath,
        CancellationToken cancellationToken = default)
    {
        pageId = PageId(pageId);
        revision = Revision(revision);
        if (!WebPageAssetPath.TryValidate("assets/" + assetPath, out var path) ||
            !path.StartsWith("assets/", StringComparison.Ordinal))
            throw Invalid("INVALID_ASSET_PATH", "The preview asset path is invalid.");
        var document = await _pages.GetRevisionAsync(pageId, revision, cancellationToken);
        return document?.Assets.SingleOrDefault(asset => asset.Path == path);
    }

    public static async Task<T> ReadBodyAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentLength > MaximumJsonBodyBytes)
            throw new ControlPageEditorException(
                "BODY_TOO_LARGE", "The page-editor request exceeds its size limit.",
                StatusCodes.Status413PayloadTooLarge);
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumJsonBodyBytes)
                throw new ControlPageEditorException(
                    "BODY_TOO_LARGE", "The page-editor request exceeds its size limit.",
                    StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0) throw Invalid("INVALID_BODY", "A JSON request body is required.");
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), RequestJson)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("INVALID_BODY", "The page-editor request body is invalid.");
        }
    }

    public static void ApplyPreviewHeaders(HttpResponse response, bool asset)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentSecurityPolicy =
            "default-src 'none'; base-uri 'none'; connect-src 'none'; font-src 'self' data:; " +
            "form-action 'none'; frame-ancestors 'self'; img-src 'self' data: blob:; " +
            "media-src 'self' data: blob:; object-src 'none'; script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; worker-src 'none'";
        response.Headers.XFrameOptions = "SAMEORIGIN";
        response.Headers["Cross-Origin-Resource-Policy"] = asset ? "cross-origin" : "same-origin";
    }

    private static ControlPageRevisionDetail Detail(WebPageRevisionDocument document) =>
        new(
            document.Summary,
            document.Html,
            document.Assets.Select(asset => new WebPageAssetSummary(
                asset.Path, asset.ContentType, asset.ContentHash, asset.Content.Length)).ToArray());

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var target = entry.Open();
        await target.WriteAsync(content, cancellationToken);
    }

    private static string PageId(string value)
    {
        if (!WebPageId.IsValid(value)) throw Invalid("INVALID_PAGE_ID", "The page ID is invalid.");
        return value;
    }

    private static int Revision(int value)
    {
        if (value < 1) throw Invalid("INVALID_REVISION", "Page revisions start at 1.");
        return value;
    }

    private static int PageSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultPageSize;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize) ||
            pageSize is < 1 or > MaximumPageSize)
            throw Invalid("INVALID_LIMIT", "Page size must be an integer from 1 through 100.");
        return pageSize;
    }

    private static string? Decode(string? cursor, string kind, string scope, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        if (cursor.Length > MaximumCursorLength)
            throw Invalid("CURSOR_INVALID", "The page-editor cursor is invalid.");
        ControlPageCursor token;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            token = JsonSerializer.Deserialize<ControlPageCursor>(Convert.FromBase64String(encoded))
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw Invalid("CURSOR_INVALID", "The page-editor cursor is invalid.");
        }
        if (token.Kind != kind || token.Scope != scope || token.PageSize != pageSize ||
            string.IsNullOrWhiteSpace(token.LastKey) || token.LastKey.Length > WebPageId.MaximumLength)
            throw Stale();
        return token.LastKey;
    }

    private static string Encode(ControlPageCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ControlPageEditorException Invalid(string code, string message) =>
        new(code, message, StatusCodes.Status400BadRequest);

    private static ControlPageEditorException Stale() =>
        new("CURSOR_STALE", "The page-editor cursor no longer matches this list. Restart it.",
            StatusCodes.Status409Conflict);

    private sealed record ControlPageCursor(string Kind, string Scope, int PageSize, string LastKey);
}

public sealed class ControlPageEditorException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record ControlPageResult<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record ControlPageRevisionDetail(
    WebPageRevisionSummary Summary,
    string Html,
    IReadOnlyList<WebPageAssetSummary> Assets);
public sealed record ControlPageBundle(string FileName, byte[] Content);
public sealed record ControlPageDraftRequest(int ExpectedLatestRevision, int BaseRevision, string? Html);
public sealed record ControlPageActivationRequest(int ExpectedActiveRevision, int Revision);
