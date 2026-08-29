using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.TriggerScheduling;

internal sealed class TriggerSchedulingBackgroundWorker(IServiceScopeFactory scopes) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly string workerId = $"trigger-worker.{Environment.ProcessId}.{Guid.NewGuid():n}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<IOneTimeTriggerWorker>();
                    await worker.RunBatchAsync(workerId + ".one-time", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { }
                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<IObservationTriggerWorker>();
                    await worker.RunBatchAsync(workerId + ".observation", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { }
                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<IRecurringTriggerWorker>();
                    await worker.RunBatchAsync(workerId + ".recurring", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { }
                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<IConditionalTriggerWorker>();
                    await worker.RunBatchAsync(workerId + ".conditional", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Durable work remains authoritative; the next bounded poll retries discovery.
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
