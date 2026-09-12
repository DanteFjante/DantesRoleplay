using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.SystemTasks;

/// <summary>
/// Host-only policy for draining explicitly submitted application validation work. Candidate input
/// cannot select the provider, model, reasoning, tools, duration, or output limits.
/// </summary>
internal sealed class SystemTaskApplicationValidationWorkerOptions
{
    internal const string SectionName = "ApplicationValidationWorker";
    internal const string DefaultModel = "gpt-5.6-sol";

    internal bool Enabled { get; private init; }
    internal string Model { get; private init; } = DefaultModel;

    internal static SystemTaskApplicationValidationWorkerOptions FromConfiguration(IConfiguration? configuration)
    {
        var enabledText = configuration?[$"{SectionName}:Enabled"];
        var enabled = false;
        if (enabledText is not null && !bool.TryParse(enabledText, out enabled))
            throw new InvalidOperationException($"{SectionName}:Enabled must be true or false.");
        var model = configuration?[$"{SectionName}:Model"] ?? DefaultModel;
        if (string.IsNullOrWhiteSpace(model) || model.Length > 120 || model != model.Trim())
            throw new InvalidOperationException($"{SectionName}:Model must be a nonblank model id of at most 120 characters.");
        return new()
        {
            Enabled = enabled,
            Model = model
        };
    }
}

/// <summary>
/// Claims only application-validation leases. One lease is awaited at a time and the durable
/// lifecycle classifies a failed provider attempt as terminal rather than blindly retrying it.
/// </summary>
internal sealed class SystemTaskApplicationValidationBackgroundWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    SystemTaskApplicationValidationWorkerOptions options,
    ILogger<SystemTaskApplicationValidationBackgroundWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(10);
    private const int MaximumOutputTokens = 2_048;
    private readonly string workerId = $"application-validation.{Environment.ProcessId}.{Guid.NewGuid():n}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
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
                    "Application validation polling failed; durable work remains available for operator review.");
            }

            try { await Task.Delay(PollInterval, time, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    internal async Task<bool> RunOnceAsync(string claimantId,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return false;
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemTaskApplicationValidationService>()
            .RunNextAsync(claimantId,
                scope.ServiceProvider.GetRequiredService<SystemInnerWorkerValidationInvoker>(),
                ProviderConfiguration(),
                scope.ServiceProvider.GetRequiredService<SystemTaskAiInvocationLifecycleFactory>(),
                cancellationToken);
    }

    private AiRequest ProviderConfiguration() => new(
        "codex", options.Model, [], AiRequestKind.Task, AiReasoningEffort.Low,
        AllowedTools: [], MaximumToolRounds: 0, MaximumOutputTokens: MaximumOutputTokens,
        MaximumToolCalls: 0, MaximumResponseBytes: ApplicationCandidateReuseJudgmentLimits.OutputUtf8Bytes,
        MaximumDuration: MaximumDuration);
}
