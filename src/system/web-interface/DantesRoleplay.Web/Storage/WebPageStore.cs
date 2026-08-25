using System.Security.Cryptography;
using DantesRoleplay.Web.Pages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Persistence;

public sealed class WebPageStore(WebContentDbContext db) : IWebPageStore
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public async Task<WebPageDiscoveryPage> ListPageAsync(
        string? afterPageId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        if (afterPageId is not null)
        {
            if (!WebPageId.IsValid(afterPageId))
                throw new ArgumentException("The page cursor key is invalid.", nameof(afterPageId));
            if (!await db.Pages.AsNoTracking().AnyAsync(page => page.Id == afterPageId, cancellationToken))
                throw new WebPageStoreException("CURSOR_STALE", "The page cursor no longer names an existing page.");
        }

        var rows = await db.Pages
            .AsNoTracking()
            .Where(page => afterPageId == null || string.Compare(page.Id, afterPageId) > 0)
            .OrderBy(page => page.Id)
            .Take(limit + 1)
            .Select(page => new WebPageSummary(
                page.Id,
                page.ActiveRevision,
                page.Revisions.Max(revision => revision.Revision),
                page.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var values = hasMore ? rows[..limit] : rows;
        return new(values, hasMore ? values[^1].Id : null);
    }

    public Task<WebPageSummary?> GetSummaryAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id)) return Task.FromResult<WebPageSummary?>(null);
        return db.Pages
            .AsNoTracking()
            .Where(page => page.Id == id)
            .Select(page => new WebPageSummary(
                page.Id,
                page.ActiveRevision,
                page.Revisions.Max(revision => revision.Revision),
                page.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WebPageRevisionDiscoveryPage> ListRevisionsAsync(
        string id,
        int? beforeRevision,
        int limit,
        CancellationToken cancellationToken = default)
    {
        RequirePageId(id);
        ValidateLimit(limit);
        if (beforeRevision is <= 0)
            throw new ArgumentOutOfRangeException(nameof(beforeRevision));
        var page = await db.Pages.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new WebPageStoreException("PAGE_UNKNOWN", "The page is unknown.");
        if (beforeRevision is not null && !await db.PageRevisions.AsNoTracking().AnyAsync(
                revision => revision.PageId == id && revision.Revision == beforeRevision,
                cancellationToken))
            throw new WebPageStoreException("CURSOR_STALE", "The revision cursor no longer names an existing revision.");

        var rows = await db.PageRevisions
            .AsNoTracking()
            .Where(revision => revision.PageId == id &&
                (beforeRevision == null || revision.Revision < beforeRevision))
            .OrderByDescending(revision => revision.Revision)
            .Take(limit + 1)
            .Select(revision => new
            {
                revision.Revision,
                revision.Html,
                revision.CreatedAt,
                AssetCount = revision.Assets.Count,
                AssetBytes = revision.Assets.Select(asset => (long?)asset.Content.Length).Sum() ?? 0
            })
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var values = (hasMore ? rows[..limit] : rows)
            .Select(revision => new WebPageRevisionSummary(
                id,
                revision.Revision,
                revision.Revision == page.ActiveRevision,
                revision.CreatedAt,
                HtmlHash(revision.Html),
                revision.AssetCount,
                revision.AssetBytes))
            .ToArray();
        return new(values, hasMore ? values[^1].Revision : null);
    }

    public async Task<WebPageRevisionDocument?> GetRevisionAsync(
        string id,
        int revision,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id) || revision < 1) return null;
        var activeRevision = await db.Pages.AsNoTracking()
            .Where(page => page.Id == id)
            .Select(page => (int?)page.ActiveRevision)
            .SingleOrDefaultAsync(cancellationToken);
        if (activeRevision is null) return null;
        var row = await db.PageRevisions
            .AsNoTracking()
            .Include(value => value.Assets)
            .SingleOrDefaultAsync(
                value => value.PageId == id && value.Revision == revision,
                cancellationToken);
        return row is null ? null : Document(row, activeRevision.Value);
    }

    public async Task<WebPageRevisionDocument> AppendDraftAsync(
        string id,
        int baseRevision,
        int expectedLatestRevision,
        string html,
        CancellationToken cancellationToken = default)
    {
        RequirePageId(id);
        RequireRevision(baseRevision, nameof(baseRevision));
        RequireRevision(expectedLatestRevision, nameof(expectedLatestRevision));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var page = await db.Pages.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new WebPageStoreException("PAGE_UNKNOWN", "The page is unknown.");
        var latestRevision = await db.PageRevisions
            .Where(value => value.PageId == id)
            .MaxAsync(value => value.Revision, cancellationToken);
        if (latestRevision != expectedLatestRevision)
            throw new WebPageStoreException("PAGE_LATEST_STALE", "The page has a newer revision. Reload before saving another draft.");
        var baseRow = await db.PageRevisions
            .AsNoTracking()
            .Include(value => value.Assets)
            .SingleOrDefaultAsync(
                value => value.PageId == id && value.Revision == baseRevision,
                cancellationToken)
            ?? throw new WebPageStoreException("REVISION_UNKNOWN", "The base revision is unknown.");
        var assets = baseRow.Assets
            .OrderBy(asset => asset.Path, StringComparer.Ordinal)
            .Select(asset => new WebPageAssetUpload(asset.Path, asset.Content))
            .ToArray();
        _ = ValidateContent(html, assets);
        var now = DateTime.UtcNow;
        var row = new WebPageRevision
        {
            PageId = id,
            Revision = latestRevision + 1,
            Html = html,
            CreatedAt = now,
            Assets = baseRow.Assets
                .OrderBy(asset => asset.Path, StringComparer.Ordinal)
                .Select(asset => new WebPageAsset
                {
                    Path = asset.Path,
                    ContentType = asset.ContentType,
                    ContentHash = asset.ContentHash,
                    Content = asset.Content.ToArray()
                })
                .ToList()
        };
        db.PageRevisions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Document(row, page.ActiveRevision);
    }

    public async Task<WebPageActivationResult> ActivateRevisionAsync(
        string id,
        int revision,
        int expectedActiveRevision,
        CancellationToken cancellationToken = default)
    {
        RequirePageId(id);
        RequireRevision(revision, nameof(revision));
        RequireRevision(expectedActiveRevision, nameof(expectedActiveRevision));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var page = await db.Pages.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new WebPageStoreException("PAGE_UNKNOWN", "The page is unknown.");
        if (page.ActiveRevision != expectedActiveRevision)
            throw new WebPageStoreException("PAGE_ACTIVE_STALE", "The active page changed. Reload before publishing or rolling back.");
        if (revision == page.ActiveRevision)
            throw new WebPageStoreException("PAGE_ALREADY_ACTIVE", "The target revision is already active.");
        if (!await db.PageRevisions.AnyAsync(
                value => value.PageId == id && value.Revision == revision,
                cancellationToken))
            throw new WebPageStoreException("REVISION_UNKNOWN", "The target revision is unknown.");

        page.ActiveRevision = revision;
        page.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var latestRevision = await db.PageRevisions
            .Where(value => value.PageId == id)
            .MaxAsync(value => value.Revision, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(id, revision, latestRevision, page.UpdatedAt);
    }

    public async Task<WebPageDocument> SaveAndActivateAsync(
        string id,
        string html,
        CancellationToken cancellationToken = default) =>
        await SaveAndActivateAsync(id, html, [], cancellationToken);

    public async Task<WebPageDocument> SaveBundleAndActivateAsync(
        string id,
        WebPageBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(bundle.Assets);
        if (EncodingByteCount(bundle.Html) > WebPageBundleLimits.MaximumHtmlBytes)
        {
            throw new ArgumentException("The bundle HTML exceeds its size limit.", nameof(bundle));
        }

        return await SaveAndActivateAsync(id, bundle.Html, bundle.Assets, cancellationToken);
    }

    private async Task<WebPageDocument> SaveAndActivateAsync(
        string id,
        string html,
        IReadOnlyList<WebPageAssetUpload> assets,
        CancellationToken cancellationToken)
    {
        RequirePageId(id);
        var validatedAssets = ValidateContent(html, assets);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var page = await db.Pages
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        var nextRevision = page is null
            ? 1
            : await db.PageRevisions
                .Where(revision => revision.PageId == id)
                .MaxAsync(revision => revision.Revision, cancellationToken) + 1;
        var now = DateTime.UtcNow;

        if (page is null)
        {
            page = new WebPage
            {
                Id = id,
                ActiveRevision = nextRevision,
                UpdatedAt = now
            };
            db.Pages.Add(page);
        }
        else
        {
            page.ActiveRevision = nextRevision;
            page.UpdatedAt = now;
        }

        var revision = CreateRevision(id, nextRevision, html, validatedAssets, now);
        db.PageRevisions.Add(revision);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WebPageDocument(id, nextRevision, html, now);
    }

    public Task<WebPageDocument?> GetActiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id))
        {
            return Task.FromResult<WebPageDocument?>(null);
        }

        return db.Pages
            .AsNoTracking()
            .Where(page => page.Id == id)
            .Join(
                db.PageRevisions.AsNoTracking(),
                page => new { PageId = page.Id, Revision = page.ActiveRevision },
                revision => new { revision.PageId, revision.Revision },
                (_, revision) => new WebPageDocument(
                    revision.PageId,
                    revision.Revision,
                    revision.Html,
                    revision.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WebPageAssetDocument?> GetActiveAssetAsync(
        string id,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id) ||
            !WebPageAssetPath.TryValidate(path, out var validatedPath) ||
            validatedPath == "index.html" ||
            !validatedPath.StartsWith("assets/", StringComparison.Ordinal))
        {
            return Task.FromResult<WebPageAssetDocument?>(null);
        }

        return db.Pages
            .AsNoTracking()
            .Where(page => page.Id == id)
            .Join(
                db.PageRevisions.AsNoTracking(),
                page => new { PageId = page.Id, Revision = page.ActiveRevision },
                revision => new { revision.PageId, revision.Revision },
                (_, revision) => revision)
            .Join(
                db.PageAssets.AsNoTracking().Where(asset => asset.Path == validatedPath),
                revision => revision.Id,
                asset => asset.PageRevisionId,
                (revision, asset) => new WebPageAssetDocument(
                    revision.PageId,
                    revision.Revision,
                    asset.Path,
                    asset.ContentType,
                    asset.ContentHash,
                    asset.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static int EncodingByteCount(string html) =>
        System.Text.Encoding.UTF8.GetByteCount(html);

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static void RequirePageId(string id)
    {
        if (!WebPageId.IsValid(id)) throw new ArgumentException("Page ID is invalid.", nameof(id));
    }

    private static void RequireRevision(int revision, string name)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(name);
    }

    private static IReadOnlyList<(string Path, byte[] Content)> ValidateContent(
        string html,
        IReadOnlyList<WebPageAssetUpload> assets)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(assets);
        if (string.IsNullOrWhiteSpace(html)) throw new ArgumentException("HTML cannot be empty.", nameof(html));
        if (EncodingByteCount(html) > WebPageBundleLimits.MaximumHtmlBytes)
            throw new ArgumentException("HTML exceeds its size limit.", nameof(html));
        if (assets.Count > WebPageBundleLimits.MaximumEntries - 1)
            throw new ArgumentException("The bundle has too many assets.", nameof(assets));

        var validated = new List<(string Path, byte[] Content)>(assets.Count);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = EncodingByteCount(html);
        foreach (var asset in assets)
        {
            if (asset.Content is null) throw new ArgumentException("Asset content cannot be null.", nameof(assets));
            if (!WebPageAssetPath.TryValidate(asset.Path, out var path) ||
                path == "index.html" || !path.StartsWith("assets/", StringComparison.Ordinal))
                throw new ArgumentException("An asset path is invalid.", nameof(assets));
            if (!paths.Add(path)) throw new ArgumentException("Asset paths must be unique.", nameof(assets));
            if (asset.Content.Length > WebPageBundleLimits.MaximumEntryBytes)
                throw new ArgumentException("An asset exceeds the per-entry limit.", nameof(assets));
            totalBytes += asset.Content.Length;
            if (totalBytes > WebPageBundleLimits.MaximumUncompressedBytes)
                throw new ArgumentException("The bundle exceeds the total size limit.", nameof(assets));
            validated.Add((path, asset.Content.ToArray()));
        }
        return validated;
    }

    private static WebPageRevision CreateRevision(
        string pageId,
        int revision,
        string html,
        IReadOnlyList<(string Path, byte[] Content)> assets,
        DateTime createdAt)
    {
        var row = new WebPageRevision
        {
            PageId = pageId,
            Revision = revision,
            Html = html,
            CreatedAt = createdAt
        };
        foreach (var asset in assets)
        {
            row.Assets.Add(new WebPageAsset
            {
                Path = asset.Path,
                ContentType = ResolveContentType(asset.Path),
                ContentHash = Convert.ToHexString(SHA256.HashData(asset.Content)),
                Content = asset.Content
            });
        }
        return row;
    }

    private static WebPageRevisionDocument Document(WebPageRevision row, int activeRevision)
    {
        var assets = row.Assets
            .OrderBy(asset => asset.Path, StringComparer.Ordinal)
            .Select(asset => new WebPageAssetDocument(
                row.PageId,
                row.Revision,
                asset.Path,
                asset.ContentType,
                asset.ContentHash,
                asset.Content.ToArray()))
            .ToArray();
        return new(
            new(
                row.PageId,
                row.Revision,
                row.Revision == activeRevision,
                row.CreatedAt,
                HtmlHash(row.Html),
                assets.Length,
                assets.Sum(asset => (long)asset.Content.Length)),
            row.Html,
            assets);
    }

    private static string HtmlHash(string html) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html)));

    private static string ResolveContentType(string path) =>
        ContentTypes.TryGetContentType(path, out var contentType)
            ? contentType
            : "application/octet-stream";
}
