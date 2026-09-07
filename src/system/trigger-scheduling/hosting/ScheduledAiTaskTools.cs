using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.DataAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

internal static class ScheduledAiTaskProtocol
{
    public const string Topic = "system.local-ai.task";
}

internal sealed record ScheduledAiTaskEnvelope(
    string ApplicationId,
    string ResolutionFingerprint,
    AiAgentProfile Profile,
    string Provider,
    string Model,
    string Task,
    AiReasoningEffort Reasoning,
    string PrincipalId,
    string AuthenticationMethod,
    string Scope);

/// <summary>Convenience capability for durable one-time or recurring local-AI tasks.</summary>
public sealed class ScheduledAiTaskToolSource(
    ITriggerSchedulingAdministrationService scheduling,
    IPrivateOperatorAuthorizationPolicy authorization,
    IApplicationActivationReader activations) : ISystemAiToolSource
{
    private const string Schema = """
        {"type":"object","additionalProperties":false,
         "required":["requestToken","operation","applicationId","schedule","agent","provider","model","task"],
         "properties":{
           "requestToken":{"type":"string","pattern":"^[0-9a-f]{32}$"},
           "operation":{"enum":["one-time.register","recurring.register"]},
           "applicationId":{"type":"string"},
           "schedule":{"type":"object"},
           "agent":{"type":"object","additionalProperties":false,"required":["id","name","identity"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"identity":{"type":"string"},"instructions":{"type":"string"}}},
           "provider":{"type":"string"},"model":{"type":"string"},
           "task":{"type":"string","minLength":1,"maxLength":8000},
           "reasoning":{"enum":["none","minimal","low","medium","high","xhigh","max","ultra"]},
           "intent":{"type":"string"},"proceduresUsed":{"type":"array","items":{"type":"string"}},
           "preview":{"type":"boolean"}
         }}
        """;

    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
        [new ScheduleTool(scheduling, authorization, activations, context)];

    private sealed class ScheduleTool(
        ITriggerSchedulingAdministrationService scheduling,
        IPrivateOperatorAuthorizationPolicy authorization,
        IApplicationActivationReader activations,
        SystemAiToolSourceContext source) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            "local_ai_task_schedule",
            "Register a durable one-time or recurring local-AI task. At fire time it may read directly and prepare inert plans; writes still require later trusted confirmation.",
            Schema);

        public async Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var preview = invocation.Arguments.TryGetProperty("preview", out var previewValue) && previewValue.GetBoolean();
                if (!preview && (source.ToolApproval is null || !await source.ToolApproval.ConfirmAsync(new(
                        Definition.Name, Definition.Description, invocation.Arguments.Clone()), cancellationToken)))
                    return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                        "Trusted host confirmation is required before registering a scheduled AI task.");

                var decision = authorization.Evaluate(new(source.Invocation.Principal,
                    PrivateOperatorCapability.Modify, source.Invocation.Scope, source.Invocation.CorrelationId));
                if (!decision.Allowed)
                    return AiToolResult.Failure(decision.Code, decision.Recovery);

                var schedule = JsonNode.Parse(invocation.Arguments.GetProperty("schedule").GetRawText())!.AsObject();
                if (schedule.ContainsKey("notification"))
                    return AiToolResult.Failure("SCHEDULED_AI_TASK_NOTIFICATION_FORBIDDEN",
                        "The scheduled AI task owns its notification payload.");
                var agent = invocation.Arguments.GetProperty("agent");
                var profile = new AiAgentProfile(
                    Required(agent, "id"), Required(agent, "name"), Required(agent, "identity"),
                    agent.TryGetProperty("instructions", out var instructions) ? instructions.GetString()! : "");
                var reasoning = ParseReasoning(invocation.Arguments.TryGetProperty("reasoning", out var effort)
                    ? effort.GetString()! : "none");
                var applicationId = ApplicationIdentifier.Parse(
                    Required(invocation.Arguments, "applicationId"));
                var active = activations.Current(applicationId)
                    ?? throw new InvalidOperationException(
                        "Scheduled AI tasks require an active application resolution.");
                var envelope = new ScheduledAiTaskEnvelope(applicationId.Value,
                    active.ResolutionFingerprint, profile,
                    Required(invocation.Arguments, "provider"), Required(invocation.Arguments, "model"),
                    Required(invocation.Arguments, "task"), reasoning,
                    source.Invocation.Principal.PrincipalId, source.Invocation.Principal.AuthenticationMethod,
                    source.Invocation.Scope);
                schedule["notification"] = new JsonObject
                {
                    ["topic"] = ScheduledAiTaskProtocol.Topic,
                    ["subject"] = profile.Name,
                    ["body"] = JsonSerializer.Serialize(envelope),
                    ["stateSpaceId"] = null,
                    ["entityIds"] = new JsonArray()
                };
                var command = TriggerSchedulingAdministrationCommand.Create(
                    Required(invocation.Arguments, "requestToken"),
                    Required(invocation.Arguments, "operation"),
                    applicationId,
                    schedule.ToJsonString());
                var procedures = invocation.Arguments.TryGetProperty("proceduresUsed", out var used)
                    ? used.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : Array.Empty<string>();
                var context = new TriggerSchedulingAdministrationContext(
                    invocation.Arguments.TryGetProperty("intent", out var intent) ? intent.GetString()! : envelope.Task,
                    procedures,
                    decision.Evidence);
                var result = preview
                    ? await scheduling.PreviewAsync(command, context, cancellationToken)
                    : await scheduling.CommitAsync(command, context, cancellationToken);
                return AiToolResult.Success(JsonSerializer.Serialize(result));
            }
            catch (TriggerSchedulingAdministrationException exception)
            {
                return AiToolResult.Failure(exception.Code, exception.Message);
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
            {
                return AiToolResult.Failure("SCHEDULED_AI_TASK_INVALID", exception.Message);
            }
        }

        private static AiReasoningEffort ParseReasoning(string value) => value switch
        {
            "none" => AiReasoningEffort.None, "minimal" => AiReasoningEffort.Minimal,
            "low" => AiReasoningEffort.Low, "medium" => AiReasoningEffort.Medium,
            "high" => AiReasoningEffort.High, "xhigh" => AiReasoningEffort.XHigh,
            "max" => AiReasoningEffort.Max, "ultra" => AiReasoningEffort.Ultra,
            _ => throw new ArgumentException("The scheduled AI reasoning effort is invalid.")
        };

        private static string Required(JsonElement value, string name) =>
            value.GetProperty(name).GetString()!;
    }
}

/// <summary>
/// Consumes durable trigger notifications. Scheduled runs receive no approval gates, so they may
/// read and prepare reviewable plans but cannot autonomously confirm writes.
/// </summary>
internal sealed class ScheduledAiTaskWorker(
    IServiceScopeFactory scopes,
    ILogger<ScheduledAiTaskWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    internal const int MaximumConcurrency = 4;
    private readonly string workerId = "scheduled-ai:" + Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await RunBatchAsync(workerId, stoppingToken);
                if (result.Discovered > 0)
                    logger.LogInformation(
                        "Scheduled AI batch discovered {Discovered}, claimed {Claimed}, recovered {Recovered}, completed {Completed}, retried {Retried}, and failed {Failed} task(s).",
                        result.Discovered, result.Claimed, result.Recovered, result.Completed,
                        result.Retried, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled AI task polling failed; durable leases remain recoverable.");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    internal async Task<ScheduledAiTaskBatchResult> RunBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ScheduledAiTaskClaimBatch batch;
        await using (var claimScope = scopes.CreateAsyncScope())
        {
            var store = claimScope.ServiceProvider.GetRequiredService<SqliteScheduledAiTaskWorkStore>();
            batch = await store.ClaimBatchAsync(workerId, cancellationToken);
        }

        using var concurrency = new SemaphoreSlim(MaximumConcurrency, MaximumConcurrency);
        var pending = new List<Task<ScheduledAiTaskExecutionOutcome>>(
            batch.Leases.Count + batch.ExhaustedNotificationIds.Count);
        foreach (var notificationId in batch.ExhaustedNotificationIds)
            pending.Add(RunBoundedAsync(
                () => FinalizeExhaustedAsync(notificationId), concurrency, cancellationToken));
        foreach (var lease in batch.Leases)
            pending.Add(RunBoundedAsync(
                () => ExecuteAsync(lease, cancellationToken), concurrency, cancellationToken));
        var outcomes = await Task.WhenAll(pending);
        return new(batch.Discovered, batch.Leases.Count,
            outcomes.Count(value => value == ScheduledAiTaskExecutionOutcome.Completed),
            outcomes.Count(value => value == ScheduledAiTaskExecutionOutcome.Retried),
            outcomes.Count(value => value == ScheduledAiTaskExecutionOutcome.Failed),
            batch.Leases.Count(value => value.Recovered));
    }

    private async Task<ScheduledAiTaskExecutionOutcome> ExecuteAsync(
        ScheduledAiTaskLease lease,
        CancellationToken cancellationToken)
    {
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var heartbeat = RenewLeaseAsync(lease, heartbeatCancellation.Token);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ScheduledAiTaskExecutor>()
                .ExecuteAsync(lease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Scheduled AI task {NotificationId} failed outside its durable outcome transaction; its lease remains recoverable.",
                lease.NotificationId);
            return ScheduledAiTaskExecutionOutcome.None;
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            try { await heartbeat; }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested) { }
        }
    }

    private async Task RenewLeaseAsync(
        ScheduledAiTaskLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(SqliteScheduledAiTaskWorkStore.LeaseRenewalInterval,
                    timeProvider, cancellationToken);
                await using var scope = scopes.CreateAsyncScope();
                var renewed = await scope.ServiceProvider
                    .GetRequiredService<SqliteScheduledAiTaskWorkStore>()
                    .RenewAsync(lease, cancellationToken);
                if (!renewed)
                {
                    logger.LogWarning(
                        "Scheduled AI task {NotificationId} lost its lease while the provider was running; a stale result will not commit.",
                        lease.NotificationId);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Scheduled AI task {NotificationId} could not renew its lease; a stale result will not commit after expiry.",
                lease.NotificationId);
        }
    }

    private async Task<ScheduledAiTaskExecutionOutcome> FinalizeExhaustedAsync(string notificationId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ScheduledAiTaskExecutor>()
                .FinalizeExhaustedAsync(notificationId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Scheduled AI task {NotificationId} could not record its exhausted lease; it remains recoverable.",
                notificationId);
            return ScheduledAiTaskExecutionOutcome.None;
        }
    }

    private static async Task<ScheduledAiTaskExecutionOutcome> RunBoundedAsync(
        Func<Task<ScheduledAiTaskExecutionOutcome>> action,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { concurrency.Release(); }
    }
}

internal sealed class ScheduledAiTaskExecutor(
    DantesRoleplayDbContext db,
    SqliteScheduledAiTaskWorkStore work,
    INotificationStore notifications,
    IApplicationActivationReader activations,
    ISystemAiAgentService agents,
    IOperationLog operations,
    TimeProvider timeProvider)
{
    internal async Task<ScheduledAiTaskExecutionOutcome> ExecuteAsync(
        ScheduledAiTaskLease lease,
        CancellationToken cancellationToken)
    {
        var notification = AssertNotification(await notifications.FindAsync(
            id: lease.NotificationId, limit: 1, cancellationToken: cancellationToken), lease.NotificationId);
        ScheduledAiTaskEnvelope scheduled;
        try
        {
            scheduled = JsonSerializer.Deserialize<ScheduledAiTaskEnvelope>(notification.Body)
                ?? throw new JsonException("The scheduled AI task body is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return await PersistAsync(lease, notification, null,
                ScheduledAiTaskExecutionOutcome.Failed, 0, "SCHEDULED_AI_TASK_INVALID",
                exception.Message);
        }

        ApplicationIdentifier applicationId;
        TrustedPrincipalContext principal;
        try
        {
            applicationId = ApplicationIdentifier.Parse(scheduled.ApplicationId);
            principal = TrustedPrincipalContext.VerifiedPrincipal(
                scheduled.PrincipalId, scheduled.AuthenticationMethod);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return await PersistAsync(lease, notification, scheduled,
                ScheduledAiTaskExecutionOutcome.Failed, 0, "SCHEDULED_AI_TASK_INVALID",
                exception.Message);
        }
        var active = activations.Current(applicationId);
        if (active is null || !string.Equals(active.ResolutionFingerprint,
                scheduled.ResolutionFingerprint, StringComparison.Ordinal))
            return await PersistAsync(lease, notification, scheduled,
                ScheduledAiTaskExecutionOutcome.Failed, 0,
                "SCHEDULED_AI_TASK_RESOLUTION_STALE",
                "The scheduled task was not run because its application extensions changed.");

        var started = timeProvider.GetTimestamp();
        AiResponse response;
        try
        {
            response = await agents.SendAsync(
                scheduled.Profile,
                new(scheduled.Provider, scheduled.Model, [new(AiMessageRole.User, scheduled.Task)],
                    AiRequestKind.ScheduledTask, scheduled.Reasoning),
                new(principal, scheduled.Scope, BoundCorrelation(notification.Id)),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var elapsed = Milliseconds(timeProvider.GetElapsedTime(started));
            return await PersistAsync(lease, notification, scheduled,
                lease.Attempt < SqliteScheduledAiTaskWorkStore.MaximumAttempts
                    ? ScheduledAiTaskExecutionOutcome.Retried
                    : ScheduledAiTaskExecutionOutcome.Failed,
                elapsed, "SCHEDULED_AI_TASK_PROVIDER_EXCEPTION", exception.Message);
        }

        var duration = Milliseconds(timeProvider.GetElapsedTime(started));
        return await PersistAsync(lease, notification, scheduled,
            response.Ok
                ? ScheduledAiTaskExecutionOutcome.Completed
                : lease.Attempt < SqliteScheduledAiTaskWorkStore.MaximumAttempts
                    ? ScheduledAiTaskExecutionOutcome.Retried
                    : ScheduledAiTaskExecutionOutcome.Failed,
            duration,
            response.Ok ? string.Empty : NonEmpty(response.ErrorCode, "SCHEDULED_AI_TASK_PROVIDER_FAILED"),
            response.Ok ? response.Text : NonEmpty(response.ErrorMessage,
                "The scheduled AI provider returned a failure."));
    }

    internal async Task<ScheduledAiTaskExecutionOutcome> FinalizeExhaustedAsync(string notificationId)
    {
        var notification = AssertNotification(await notifications.FindAsync(
            id: notificationId, limit: 1), notificationId);
        var task = TryReadTask(notification.Body);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (!await work.FinishExhaustedAsync(notificationId))
            {
                await transaction.RollbackAsync();
                return ScheduledAiTaskExecutionOutcome.None;
            }
            await operations.RecordAsync(
                "local-ai.scheduled-task",
                "Scheduled AI task failed after its final lease expired; provider duration 0 ms.",
                false, task, notificationId, error: "SCHEDULED_AI_TASK_ATTEMPTS_EXHAUSTED");
            await MarkReadAsync(notification);
            await transaction.CommitAsync();
            return ScheduledAiTaskExecutionOutcome.Failed;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ScheduledAiTaskExecutionOutcome> PersistAsync(
        ScheduledAiTaskLease lease,
        NotificationView notification,
        ScheduledAiTaskEnvelope? scheduled,
        ScheduledAiTaskExecutionOutcome outcome,
        long providerDurationMilliseconds,
        string failureKind,
        string detail)
    {
        var success = outcome == ScheduledAiTaskExecutionOutcome.Completed;
        var summary = $"Scheduled AI task {(success ? "completed" : outcome == ScheduledAiTaskExecutionOutcome.Retried ? "will retry" : "failed")}; " +
            $"attempt {lease.Attempt}; queue age {lease.QueueAgeMilliseconds} ms; " +
            $"provider duration {providerDurationMilliseconds} ms. {Bound(detail, 500)}";
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await operations.RecordAsync(
                "local-ai.scheduled-task", summary, success,
                scheduled?.Task ?? string.Empty, notification.Id,
                error: success ? string.Empty : failureKind);
            if (!await work.FinishAsync(lease, outcome, providerDurationMilliseconds,
                    failureKind, detail))
            {
                await transaction.RollbackAsync();
                return ScheduledAiTaskExecutionOutcome.None;
            }
            if (outcome is ScheduledAiTaskExecutionOutcome.Completed or ScheduledAiTaskExecutionOutcome.Failed)
                await MarkReadAsync(notification);
            await transaction.CommitAsync();
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task MarkReadAsync(NotificationView notification)
    {
        if (notification.State == NotificationState.Archived) return;
        var result = await notifications.SetStateAsync(notification.Id, NotificationState.Read);
        if (!result.Ok)
            throw new InvalidOperationException(result.Problem);
    }

    private static NotificationView AssertNotification(
        IReadOnlyList<NotificationView> values,
        string notificationId) => values.SingleOrDefault()
        ?? throw new InvalidOperationException(
            $"The scheduled AI work item refers to missing notification '{notificationId}'.");

    private static string TryReadTask(string body)
    {
        try { return JsonSerializer.Deserialize<ScheduledAiTaskEnvelope>(body)?.Task ?? string.Empty; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string BoundCorrelation(string id)
    {
        var value = "scheduled-ai:" + id;
        return value.Length <= 128 ? value : value[..128];
    }

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string NonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static long Milliseconds(TimeSpan value) =>
        SqliteScheduledAiTaskWorkStore.Milliseconds(value);
}
