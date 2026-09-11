using DantesRoleplay.Web.Pages;

namespace DantesRoleplay.Web.Persistence;

public interface IWebPageStore
{
    Task<WebPageDiscoveryPage> ListPageAsync(
        string? afterPageId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<WebPageSummary?> GetSummaryAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<WebPageRevisionDiscoveryPage> ListRevisionsAsync(
        string id,
        int? beforeRevision,
        int limit,
        CancellationToken cancellationToken = default);

    Task<WebPageRevisionDocument?> GetRevisionAsync(
        string id,
        int revision,
        CancellationToken cancellationToken = default);

    Task<WebPageRevisionDocument> AppendDraftAsync(
        string id,
        int baseRevision,
        int expectedLatestRevision,
        string html,
        CancellationToken cancellationToken = default);

    /// <summary>Zero expected latest revision creates an absent page with no active content; existing pages require an exact positive latest revision.</summary>
    Task<WebPageRevisionDocument> AppendBundleDraftAsync(
        string id,
        int expectedLatestRevision,
        WebPageBundle bundle,
        CancellationToken cancellationToken = default);

    Task<WebPageActivationResult> ActivateRevisionAsync(
        string id,
        int revision,
        int expectedActiveRevision,
        CancellationToken cancellationToken = default);

    Task<WebPageDocument> SaveAndActivateAsync(
        string id,
        string html,
        CancellationToken cancellationToken = default);

    Task<WebPageDocument> SaveBundleAndActivateAsync(
        string id,
        WebPageBundle bundle,
        CancellationToken cancellationToken = default);

    Task<WebPageDocument?> GetActiveAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<WebPageAssetDocument?> GetActiveAssetAsync(
        string id,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Exact retained revision read. The caller must authorize draft/historical access; this method does not publish an asset.</summary>
    Task<WebPageAssetDocument?> GetRevisionAssetAsync(
        string id,
        int revision,
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromException<WebPageAssetDocument?>(new WebPageStoreException(
            "REVISION_ASSET_UNAVAILABLE", "Exact revision asset reads are unavailable in this store."));
}

public sealed class WebPageStoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
