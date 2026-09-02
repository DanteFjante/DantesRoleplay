using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Reports at startup whether each published application catalog actually materializes.
///
/// A single edited file under `catalog/` fails the whole catalog with SOURCE_FILE_DRIFT, and
/// everything downstream goes dark together: catalog search, feature search, and every application
/// mechanic, because they all resolve through the same provider. Nothing said so. The host started
/// cleanly, served reads, and only refused when someone finally tried to run a mechanic — hours
/// later, with the edit long out of mind.
///
/// This does not repair anything and never blocks startup. It converts a silent degradation into
/// one line naming the cause and the file, at the moment it becomes true.
/// </summary>
internal sealed class CatalogHealthCheck(
    IServiceScopeFactory scopes,
    IReadOnlyCollection<string> publishedApplicationIds,
    ILogger<CatalogHealthCheck> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var applicationId in publishedApplicationIds)
        {
            if (stoppingToken.IsCancellationRequested) break;
            ApplicationIdentifier application;
            try { application = ApplicationIdentifier.Parse(applicationId); }
            catch (ArgumentException) { continue; }

            try
            {
                using var scope = scopes.CreateScope();
                var catalogs = scope.ServiceProvider.GetService<IPublicApplicationCatalogProvider>();
                if (catalogs is null) continue;
                if (catalogs.TryGet(application, out _))
                {
                    log.LogInformation("Application catalog '{Application}' materialized.", application.Value);
                    continue;
                }

                var failure = (catalogs as IPublicApplicationCatalogDiagnostics)?.LastFailure(application);
                if (failure is null)
                    log.LogError(
                        "Application catalog '{Application}' is unavailable. Catalog search, feature "
                        + "search and every application mechanic will refuse until it materializes.",
                        application.Value);
                else
                    log.LogError(
                        "Application catalog '{Application}' failed to materialize: {Code} — {Message} "
                        + "Catalog search, feature search and every application mechanic will refuse "
                        + "until this is resolved.",
                        application.Value, failure.Code, failure.Message);
            }
            catch (Exception exception)
            {
                log.LogError(exception,
                    "Application catalog '{Application}' could not be checked at startup.",
                    application.Value);
            }
        }

        return Task.CompletedTask;
    }
}
