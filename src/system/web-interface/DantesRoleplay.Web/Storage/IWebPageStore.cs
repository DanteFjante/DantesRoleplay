using DantesRoleplay.Web.Pages;

namespace DantesRoleplay.Web.Persistence;

public interface IWebPageStore
{
    Task<WebPageDocument> SaveAndActivateAsync(
        string id,
        string html,
        CancellationToken cancellationToken = default);

    Task<WebPageDocument?> GetActiveAsync(
        string id,
        CancellationToken cancellationToken = default);
}
