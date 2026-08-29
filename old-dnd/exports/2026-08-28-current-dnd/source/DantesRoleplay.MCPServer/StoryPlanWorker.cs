using DantesRoleplay.DataAccess;

namespace DantesRoleplay.MCPServer;

/// <summary>One serial durable story-plan worker. SQLite remains authoritative when the wake queue drops work.</summary>
public sealed class StoryPlanWorker(StoryPlanWakeQueue wake, IServiceScopeFactory scopes) : BackgroundService
{
    private readonly StoryPlanWakeQueue _wake = wake;
    private readonly IServiceScopeFactory _scopes = scopes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task<string>? wakeTask = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
                wakeTask ??= _wake.ReadAsync(stoppingToken).AsTask();
                var poll = Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                if (await Task.WhenAny(wakeTask, poll) == wakeTask)
                {
                    await wakeTask;
                    wakeTask = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch
            {
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStoryPlanStore>();
        var lease = await store.ClaimNextAsync($"story-worker.{Environment.ProcessId}.{Guid.NewGuid():n}", DateTime.UtcNow, cancellationToken);
        if (lease is null) return;
        var processor = scope.ServiceProvider.GetRequiredService<IStoryPlanStepProcessor>();
        await processor.ProcessAsync(lease, cancellationToken);
    }
}
