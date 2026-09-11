using DantesRoleplay.DataAccess;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.SystemTasks;

/// <summary>Claims only durable procedure workflows and delegates each fenced lease to plan 05.</summary>
internal sealed class SystemTaskWorkflowBackgroundWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<SystemTaskWorkflowBackgroundWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly string workerId = $"procedure-workflow.{Environment.ProcessId}.{Guid.NewGuid():n}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Procedure workflow polling failed; durable work remains available for recovery.");
            }

            try { await Task.Delay(PollInterval, time, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    internal async Task<bool> RunOnceAsync(string claimantId,
        CancellationToken cancellationToken = default)
    {
        string? connectionString;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            connectionString = db.Database.GetConnectionString();
        }
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The durable procedure workflow runner requires SQLite storage.");

        var runner = new SqliteSystemTaskLifecycleRunner(
            new SqliteSystemTaskLifecycleStore(connectionString, time), time);
        return await runner.RunOnceAsync(claimantId, ExecuteLeaseAsync,
            cancellationToken, SystemTaskPurpose.ProcedureWorkflow);
    }

    private async Task<SystemTaskRunOutcome> ExecuteLeaseAsync(SystemTaskLease lease,
        CancellationToken cancellationToken)
    {
        // The lifecycle runner may fence and abandon a callback that does not stop promptly.
        // Keep its scoped dependencies alive until that callback actually exits.
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemInnerWorkerProcedureExecutor>()
            .ExecuteLeaseAsync(lease, cancellationToken);
    }
}
