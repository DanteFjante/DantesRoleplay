using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
internal sealed class ScheduledAiTaskWorker(IServiceScopeFactory scopes) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<IAiService>() is not null)
                    await RunBatchAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private static async Task RunBatchAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<INotificationStore>();
        var pending = await notifications.FindAsync(state: NotificationState.Unread,
            topic: ScheduledAiTaskProtocol.Topic, limit: 8, cancellationToken: cancellationToken);
        foreach (var notification in pending.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id, StringComparer.Ordinal))
        {
            var claimed = await notifications.SetStateAsync(notification.Id, NotificationState.Read, cancellationToken);
            if (!claimed.Ok) continue;
            try
            {
                var scheduled = JsonSerializer.Deserialize<ScheduledAiTaskEnvelope>(notification.Body)
                    ?? throw new JsonException("The scheduled AI task body is empty.");
                var applicationId = ApplicationIdentifier.Parse(scheduled.ApplicationId);
                var active = services.GetRequiredService<IApplicationActivationReader>()
                    .Current(applicationId);
                if (active is null
                    || !string.Equals(active.ResolutionFingerprint, scheduled.ResolutionFingerprint,
                        StringComparison.Ordinal))
                {
                    await services.GetRequiredService<IOperationLog>().RecordAsync(
                        "local-ai.scheduled-task",
                        "The scheduled task was not run because its application extensions changed.",
                        false, scheduled.Task, notification.Id,
                        error: "SCHEDULED_AI_TASK_RESOLUTION_STALE",
                        cancellationToken: cancellationToken);
                    continue;
                }
                var principal = TrustedPrincipalContext.VerifiedPrincipal(
                    scheduled.PrincipalId, scheduled.AuthenticationMethod);
                var response = await services.GetRequiredService<ISystemAiAgentService>().SendAsync(
                    scheduled.Profile,
                    new(scheduled.Provider, scheduled.Model, [new(AiMessageRole.User, scheduled.Task)],
                        AiRequestKind.Task, scheduled.Reasoning),
                    new(principal, scheduled.Scope, BoundCorrelation(notification.Id)),
                    cancellationToken: cancellationToken);
                await services.GetRequiredService<IOperationLog>().RecordAsync(
                    "local-ai.scheduled-task",
                    response.Ok ? Bound(response.Text, 500) : Bound(response.ErrorMessage, 500),
                    response.Ok,
                    scheduled.Task,
                    notification.Id,
                    error: response.ErrorCode,
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await services.GetRequiredService<IOperationLog>().RecordAsync(
                    "local-ai.scheduled-task", Bound(exception.Message, 500), false,
                    subject: notification.Id, error: "SCHEDULED_AI_TASK_FAILED",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private static string BoundCorrelation(string id)
    {
        var value = "scheduled-ai:" + id;
        return value.Length <= 128 ? value : value[..128];
    }

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
