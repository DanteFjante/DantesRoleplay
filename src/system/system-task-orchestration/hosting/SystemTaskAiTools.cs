using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.SystemTasks;

/// <summary>Direct local-AI access to the existing durable system-task owner.</summary>
public sealed class SystemTaskAiToolSource(ISystemTaskService tasks) : ISystemAiToolSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
    [
        new DelegateTool("system_task_prepare",
            "Prepare a bounded durable system task and its reviewable plan. Writes remain inert until separately confirmed.",
            """{"type":"object","additionalProperties":false,"required":["conversationId","operation","intent","idempotencyKey"],"properties":{"conversationId":{"type":"string"},"operation":{"enum":["resolve","submit"]},"intent":{"type":"string"},"agenda":{"type":["array","null"],"items":{"type":"object","additionalProperties":false,"required":["capabilityId","input"],"properties":{"capabilityId":{"type":"string"},"input":{"type":"object"}}}},"idempotencyKey":{"type":"string"}}}""",
            (call, token) => PrepareAsync(context, call, token)),
        new DelegateTool("system_task_get",
            "Read one durable system task, its plan, confirmations, and execution receipts.",
            """{"type":"object","additionalProperties":false,"required":["taskId"],"properties":{"taskId":{"type":"string"}}}""",
            (call, token) => GetAsync(context, call, token)),
        new DelegateTool("system_tasks_list",
            "List recent durable system tasks for one conversation.",
            """{"type":"object","additionalProperties":false,"required":["conversationId"],"properties":{"conversationId":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":100}}}""",
            (call, token) => ListAsync(context, call, token)),
        new DelegateTool("system_task_execute",
            "Execute an already externally confirmed system-task plan and return its durable per-step receipt.",
            """{"type":"object","additionalProperties":false,"required":["taskId","confirmationId","planFingerprint","idempotencyKey"],"properties":{"taskId":{"type":"string"},"confirmationId":{"type":"string"},"planFingerprint":{"type":"string"},"idempotencyKey":{"type":"string"}}}""",
            (call, token) => ExecuteAsync(context, call, token))
    ];

    private async Task<AiToolResult> PrepareAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        try
        {
            var agenda = call.Arguments.TryGetProperty("agenda", out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(item => new SystemTaskAgendaItem(
                    item.GetProperty("capabilityId").GetString()!, item.GetProperty("input").Clone())).ToArray()
                : null;
            var result = await tasks.PrepareAsync(Context(source),
                Required(call, "conversationId"),
                new(Required(call, "operation"), Required(call, "intent"), agenda,
                    Required(call, "idempotencyKey")), cancellationToken);
            return Success(result);
        }
        catch (SystemTaskException exception) { return AiToolResult.Failure(exception.Code, exception.Message); }
    }

    private async Task<AiToolResult> GetAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await tasks.GetAsync(Context(source), Required(call, "taskId"), cancellationToken);
            return result is null
                ? AiToolResult.Failure("SYSTEM_TASK_NOT_FOUND", "The system task was not found in this trusted scope.")
                : Success(result);
        }
        catch (SystemTaskException exception) { return AiToolResult.Failure(exception.Code, exception.Message); }
    }

    private async Task<AiToolResult> ListAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        try
        {
            var limit = call.Arguments.TryGetProperty("limit", out var value) ? value.GetInt32() : 20;
            var result = await tasks.ListAsync(Context(source), Required(call, "conversationId"),
                null, null, limit, cancellationToken);
            return Success(result);
        }
        catch (SystemTaskException exception) { return AiToolResult.Failure(exception.Code, exception.Message); }
    }

    private async Task<AiToolResult> ExecuteAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        if (source.ToolApproval is null || !await source.ToolApproval.ConfirmAsync(new(
                "system_task_execute",
                "Execute an externally confirmed durable system-task plan.",
                call.Arguments.Clone()), cancellationToken))
            return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                "Trusted host confirmation is required before executing this system task.");
        try
        {
            var result = await tasks.ExecuteAsync(Context(source), Required(call, "taskId"), new(
                Required(call, "confirmationId"), Required(call, "planFingerprint"),
                Required(call, "idempotencyKey")), cancellationToken);
            return Success(result);
        }
        catch (SystemTaskException exception) { return AiToolResult.Failure(exception.Code, exception.Message); }
    }

    private static SystemTaskRequestContext Context(SystemAiToolSourceContext source) => new(
        source.Invocation.Principal, source.Invocation.Scope, source.Invocation.CorrelationId);

    private static string Required(AiToolInvocation call, string name) =>
        call.Arguments.GetProperty(name).GetString()!;

    private static AiToolResult Success<T>(T value) =>
        AiToolResult.Success(JsonSerializer.Serialize(value, Json));

    private sealed class DelegateTool(
        string name,
        string description,
        string schema,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> invoke) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(name, description, schema);
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default) => invoke(invocation, cancellationToken);
    }
}
