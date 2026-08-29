namespace DantesRoleplay.Web.Pages;

public sealed class WebPage
{
    public required string Id { get; set; }

    public int ActiveRevision { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<WebPageRevision> Revisions { get; set; } = [];
}

public sealed class WebPageRevision
{
    public long Id { get; set; }

    public required string PageId { get; set; }

    public WebPage? Page { get; set; }

    public int Revision { get; set; }

    public required string Html { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<WebPageAsset> Assets { get; set; } = [];
}

public sealed class WebPageAsset
{
    public long Id { get; set; }

    public long PageRevisionId { get; set; }

    public WebPageRevision? PageRevision { get; set; }

    public required string Path { get; set; }

    public required string ContentType { get; set; }

    public required string ContentHash { get; set; }

    public required byte[] Content { get; set; }
}

public sealed record WebPageDocument(
    string Id,
    int Revision,
    string Html,
    DateTime CreatedAt);

public sealed record WebPageAssetDocument(
    string PageId,
    int Revision,
    string Path,
    string ContentType,
    string ContentHash,
    byte[] Content);

public sealed record WebPageSummary(
    string Id,
    int ActiveRevision,
    int LatestRevision,
    DateTime UpdatedAtUtc);

public sealed record WebPageDiscoveryPage(
    IReadOnlyList<WebPageSummary> Pages,
    string? NextPageId);

public sealed record WebPageAssetSummary(
    string Path,
    string ContentType,
    string ContentHash,
    int Length);

public sealed record WebPageRevisionSummary(
    string PageId,
    int Revision,
    bool IsActive,
    DateTime CreatedAtUtc,
    string ContentHash,
    int AssetCount,
    long AssetBytes);

public sealed record WebPageRevisionDiscoveryPage(
    IReadOnlyList<WebPageRevisionSummary> Revisions,
    int? NextRevision);

public sealed record WebPageRevisionDocument(
    WebPageRevisionSummary Summary,
    string Html,
    IReadOnlyList<WebPageAssetDocument> Assets);

public sealed record WebPageActivationResult(
    string PageId,
    int ActiveRevision,
    int LatestRevision,
    DateTime UpdatedAtUtc);
