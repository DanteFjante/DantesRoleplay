using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer;

/// <summary>Runs the two bounded derived-work queues; it exposes no MCP tool.</summary>
public sealed class KnowledgeBackgroundWorker(
    KnowledgeBackgroundQueue queue,
    KnowledgeBackgroundOptions options,
    IServiceScopeFactory scopes) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        RunAsync(KnowledgeBackgroundJobKind.EmbeddingSync, stoppingToken),
        RunAsync(KnowledgeBackgroundJobKind.KnowledgeProposals, stoppingToken));

    private async Task RunAsync(KnowledgeBackgroundJobKind kind, CancellationToken stoppingToken)
    {
        await foreach (var work in queue.ReadAllAsync(kind, stoppingToken))
        {
            var snapshot = queue.MarkRunning(work);
            if (snapshot is null) continue;
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, queue.Cancellation(work.JobId));
            KnowledgeBackgroundOutcome outcome;
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<KnowledgeBackgroundJobProcessor>();
                outcome = await processor.ProcessAsync(work, linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OperationCanceledException) when (queue.Get(work.JobId)?.Status == "cancelled") { continue; }
            catch (Exception exception)
            {
                outcome = new(
                    "failed",
                    true,
                    ErrorCode: "BACKGROUND_WORKER_FAILED",
                    ErrorMessage: exception.Message.Length <= 500 ? exception.Message : exception.Message[..500]);
            }

            if (outcome.Retryable && snapshot.Attempt < options.MaxAttempts)
            {
                if (options.RetryDelay > TimeSpan.Zero)
                {
                    try { await Task.Delay(options.RetryDelay, linkedCancellation.Token); }
                    catch (OperationCanceledException) when (queue.Get(work.JobId)?.Status == "cancelled") { continue; }
                }
                queue.Requeue(work);
                continue;
            }
            queue.Complete(work, outcome);
        }
    }
}
