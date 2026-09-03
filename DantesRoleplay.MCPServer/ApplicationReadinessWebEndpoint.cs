using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Reports the independently versioned pieces required to serve one application. A listener is not
/// considered ready merely because the process accepts HTTP connections.
/// </summary>
public static class ApplicationReadinessWebEndpoint
{
    public static async Task<IResult> ReadAsync(
        string applicationId,
        DantesRoleplayDbContext db,
        IApplicationRegistry applications,
        IApplicationActivationReader activations,
        IPublicApplicationCatalogProvider catalogs,
        IWebPagePublicationDirectory publications,
        IWebPageStore pages,
        CancellationToken cancellationToken)
    {
        ApplicationIdentifier application;
        try
        {
            application = ApplicationIdentifier.Parse(applicationId);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new
            {
                status = "failed",
                code = "APPLICATION_ID_INVALID",
                message = "The application ID is invalid."
            });
        }

        var checks = new List<ApplicationReadinessCheck>();
        try
        {
            var available = await db.Database.CanConnectAsync(cancellationToken);
            var databaseIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                db.Database.GetConnectionString() ?? "unconfigured")));
            checks.Add(new("database", available ? "ready" : "failed",
                available ? "DATABASE_AVAILABLE" : "DATABASE_UNAVAILABLE",
                available
                    ? $"The authoritative database is available; configured identity {databaseIdentity}."
                    : $"The authoritative database is unavailable; configured identity {databaseIdentity}."));
        }
        catch (Exception exception)
        {
            checks.Add(new("database", "failed", "DATABASE_UNAVAILABLE", exception.Message));
        }

        ApplicationRevision? registration = null;
        try { registration = applications.Get(application); }
        catch (Exception exception)
        {
            checks.Add(new("application-registration", "failed", "APPLICATION_REGISTRY_UNAVAILABLE", exception.Message));
        }
        if (!checks.Any(value => value.Name == "application-registration"))
            checks.Add(registration is null
                ? new("application-registration", "failed", "APPLICATION_NOT_REGISTERED",
                    "The application is not registered.")
                : new("application-registration", "ready", "APPLICATION_REGISTERED",
                    $"Revision {registration.Revision}; fingerprint {registration.Fingerprint}."));

        ActiveApplicationManifest? activation = null;
        try { activation = activations.Current(application); }
        catch (Exception exception)
        {
            checks.Add(new("active-catalog-snapshot", "failed", "APPLICATION_ACTIVATION_UNAVAILABLE", exception.Message));
        }
        if (!checks.Any(value => value.Name == "active-catalog-snapshot"))
            checks.Add(activation is null
                ? new("active-catalog-snapshot", "failed", "APPLICATION_NOT_ACTIVATED",
                    "The application has no active catalog snapshot.")
                : new("active-catalog-snapshot", "ready", "APPLICATION_ACTIVATED",
                    $"Activation revision {activation.ActivationRevision}; fingerprint {activation.ActivationFingerprint}."));

        try
        {
            if (catalogs.TryGet(application, out _))
                checks.Add(new("catalog-materialization", "ready", "APPLICATION_CATALOG_AVAILABLE",
                    "The active catalog snapshot materialized and its retained files passed fingerprint checks."));
            else
            {
                var failure = (catalogs as IPublicApplicationCatalogDiagnostics)?.LastFailure(application);
                checks.Add(new("catalog-materialization", "failed",
                    failure?.Code ?? "APPLICATION_CATALOG_UNAVAILABLE",
                    failure?.Message ?? "The active catalog snapshot could not be materialized."));
            }
        }
        catch (Exception exception)
        {
            checks.Add(new("catalog-materialization", "failed", "APPLICATION_CATALOG_UNAVAILABLE", exception.Message));
        }

        checks.Add(activation is null
            ? new("extension-resolution", "failed", "EXTENSION_RESOLUTION_UNAVAILABLE",
                "Extension resolution is unavailable without an active catalog snapshot.")
            : new("extension-resolution", "ready", "EXTENSION_RESOLUTION_AVAILABLE",
                $"Fingerprint {activation.ResolutionFingerprint}; extensions "
                + (activation.Extensions.Count == 0
                    ? "none."
                    : string.Join(", ", activation.Extensions.Select(value => value.ExtensionId)) + ".")));

        try
        {
            var publication = await publications.FindIndexAsync(application, cancellationToken);
            var summary = publication is null
                ? null
                : await pages.GetSummaryAsync(publication.ContentPageId, cancellationToken);
            checks.Add(publication is null || summary is null || summary.ActiveRevision < 1
                ? new("web-page-revision", "failed", "WEB_INDEX_PAGE_UNAVAILABLE",
                    "No active published index page revision is available for the application.")
                : new("web-page-revision", "ready", "WEB_INDEX_PAGE_AVAILABLE",
                    $"Page {publication.ContentPageId}; active revision {summary.ActiveRevision}."));
        }
        catch (Exception exception)
        {
            checks.Add(new("web-page-revision", "failed", "WEB_INDEX_PAGE_UNAVAILABLE", exception.Message));
        }

        var ready = checks.All(value => value.Status == "ready");
        return Results.Json(new
        {
            status = ready ? "ready" : "failed",
            applicationId = application.Value,
            checkedAtUtc = DateTime.UtcNow,
            checks
        }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record ApplicationReadinessCheck(string Name, string Status, string Code, string Message);
}
