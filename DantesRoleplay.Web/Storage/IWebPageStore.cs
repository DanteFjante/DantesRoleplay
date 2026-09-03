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
}

public sealed class WebPageStoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
