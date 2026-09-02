using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Builds the disposable feature-retrieval vector index once the host is up.
///
/// `IInteractionFeatureRetriever.RebuildAsync` embeds every active procedure and mechanic and
/// replaces the index generation. Nothing called it, so an otherwise correctly configured
/// embedding provider still answered every search from the lexical path — the index simply had no
/// generation to search. This runs in the background, never blocks startup, and fails soft: a
/// missing model or an unreachable Ollama leaves lexical retrieval exactly as it was.
///
/// The index is keyed by a generation derived from the catalog snapshot, so a re-activation makes
/// the previous generation unreachable rather than stale. Rebuilding after an activation currently
/// means restarting the host.
/// </summary>
internal sealed class InteractionRetrievalWarmup(
    IServiceScopeFactory scopes,
    IReadOnlyCollection<string> publishedApplicationIds,
    ILogger<InteractionRetrievalWarmup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var applicationId in publishedApplicationIds)
        {
            if (stoppingToken.IsCancellationRequested) return;
            ApplicationIdentifier application;
            try { application = ApplicationIdentifier.Parse(applicationId); }
            catch (ArgumentException) { continue; }

            try
            {
                using var scope = scopes.CreateScope();
                var retriever = scope.ServiceProvider.GetService<IInteractionFeatureRetriever>();
                if (retriever is null) return;
                var result = await retriever.RebuildAsync(
                    new InteractionFeatureRetrievalScope(application, InteractionRetrievalLane.TrustedFeature),
                    stoppingToken);
                if (result.Rebuilt)
                    log.LogInformation(
                        "Feature retrieval index rebuilt for {Application}: {Count} document(s), generation {Generation}.",
                        application.Value, result.DocumentCount, result.GenerationKey);
                else
                    log.LogInformation(
                        "Feature retrieval index not rebuilt for {Application}: {Code} {Message}",
                        application.Value, result.AvailabilityCode, result.AvailabilityMessage);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                log.LogInformation(exception,
                    "Feature retrieval index rebuild failed for {Application}; lexical retrieval remains available.",
                    application.Value);
            }
        }
    }
}
