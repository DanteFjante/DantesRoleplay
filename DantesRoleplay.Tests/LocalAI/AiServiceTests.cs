using System.Text.Json;
using DantesRoleplay.AI;

namespace DantesRoleplay.Tests;

public sealed class AiServiceTests
{
    private const string ObjectSchema = """
        {"type":"object","additionalProperties":false,"required":["answer"],"properties":{"answer":{"type":"string"}}}
        """;

    [Fact]
    public async Task Message_task_and_structured_request_use_one_provider_neutral_surface()
    {
        var provider = new QueueProvider([
            Success("plain"),
            Success("task complete"),
            Success("{\"answer\":\"structured\"}", structured: true)
        ]);
        var service = new AiService([provider]);

        var message = await service.SendMessageAsync(
            "test", "model", [new(AiMessageRole.User, "hello")], AiReasoningEffort.Low);
        var task = await service.SendTaskAsync("test", "model", "do the work");
        var structured = await service.SendRequestAsync(new(
            "test", "model", [new(AiMessageRole.User, "answer")],
            AiRequestKind.StructuredRequest, ResponseSchemaJson: ObjectSchema));

        Assert.Equal(["test"], service.ListProviders().Select(value => value.Id));
        Assert.Equal("plain", message.Text);
        Assert.Equal("task complete", task.Text);
        Assert.Equal("structured", structured.StructuredData!.Value.GetProperty("answer").GetString());
        Assert.Equal(
            [AiRequestKind.Message, AiRequestKind.Task, AiRequestKind.StructuredRequest],
            provider.Requests.Select(value => value.Kind));
        Assert.Equal(AiReasoningEffort.Low, provider.Requests[0].Reasoning);
    }

    [Fact]
    public async Task Tool_calls_execute_in_process_and_return_to_the_provider_without_mcp()
    {
        var provider = new QueueProvider([
            new(true, Model(), "", "", [new("call-1", "system_read", "{\"key\":\"value\"}")]),
            Success("finished")
        ]);
        var tool = new CapturingTool();
        var service = new AiService([provider], [tool]);

        var result = await service.SendTaskAsync(
            "test", "model", "inspect the system", allowedTools: ["system_read"]);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal("finished", result.Text);
        Assert.Equal("value", tool.Arguments.GetProperty("key").GetString());
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains(provider.Requests[1].Messages, value =>
            value.Role == AiMessageRole.Tool && value.ToolCallId == "call-1" && value.Content == "{\"found\":true}");
        var attached = Assert.Single(result.Media!);
        Assert.Equal("actor.fixture", attached.EntityId);
        Assert.Equal("visual-0", attached.MediaId);
        Assert.Single(Assert.Single(provider.Requests[1].Messages,
            value => value.Role == AiMessageRole.Tool).Media!);
    }

    [Fact]
    public async Task Invalid_structured_output_and_unknown_tools_fail_closed()
    {
        var provider = new QueueProvider([Success("{\"answer\":12}", structured: true)]);
        var service = new AiService([provider]);

        var unknown = await service.SendTaskAsync(
            "test", "model", "task", allowedTools: ["missing"]);
        var mismatch = await service.SendRequestAsync(new(
            "test", "model", [new(AiMessageRole.User, "answer")],
            AiRequestKind.StructuredRequest, ResponseSchemaJson: ObjectSchema));

        Assert.Equal("AI_TOOL_UNKNOWN", unknown.ErrorCode);
        Assert.Equal("AI_RESPONSE_SCHEMA_MISMATCH", mismatch.ErrorCode);
        var validation = Assert.Single(mismatch.Activities!, value => value.Kind == "validation");
        Assert.Equal("failed", validation.Status);
        Assert.False(validation.InputValidated);
        Assert.Equal("AI_RESPONSE_SCHEMA_MISMATCH", validation.ErrorCode);
    }

    [Fact]
    public async Task Agent_request_receives_identity_rules_and_only_authorized_capabilities()
    {
        var provider = new QueueProvider([Success("ready")]);
        var tool = new CapturingTool();
        var service = new AiService([provider]);

        var result = await service.SendAgentRequestAsync(
            new("world.steward", "World Steward", "You maintain the current world state.",
                "Preserve established facts."),
            new("test", "model", [new(AiMessageRole.User, "Inspect the system.")]),
            [tool]);

        Assert.True(result.Ok, result.ErrorMessage);
        var system = Assert.Single(provider.Requests[0].Messages, value => value.Role == AiMessageRole.System);
        Assert.Contains("World Steward", system.Content, StringComparison.Ordinal);
        Assert.Contains("world.steward", system.Content, StringComparison.Ordinal);
        Assert.Contains("system_read: Read a system value.", system.Content, StringComparison.Ordinal);
        Assert.Contains("cannot confirm your own write", system.Content, StringComparison.Ordinal);
        Assert.Equal(["system_read"], provider.Requests[0].Tools.Select(value => value.Name));
    }

    private static AiProviderResponse Success(string text, bool structured = false) =>
        new(true, Model(), text, structured ? text : "", []);

    private static AiModel Model() => new(
        "test", "model", "Test model",
        AiModelCapabilities.Messages | AiModelCapabilities.Tasks |
        AiModelCapabilities.Reasoning | AiModelCapabilities.StructuredOutput | AiModelCapabilities.Tools,
        [AiReasoningEffort.None, AiReasoningEffort.Low], IsDefault: true);

    private sealed class QueueProvider(IEnumerable<AiProviderResponse> responses) : IAiProvider
    {
        private readonly Queue<AiProviderResponse> _responses = new(responses);
        public List<AiProviderRequest> Requests { get; } = [];
        public AiProviderInfo Info { get; } = new("test", "Test");

        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([Model()]);

        public Task<AiProviderResponse> SendAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CapturingTool : IAiTool
    {
        public JsonElement Arguments { get; private set; }
        public AiToolDefinition Definition { get; } = new(
            "system_read",
            "Read a system value.",
            """{"type":"object","additionalProperties":false,"required":["key"],"properties":{"key":{"type":"string"}}}""");

        public Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Arguments = invocation.Arguments.Clone();
            return Task.FromResult(AiToolResult.Success("{\"found\":true}", [new(
                "image/png",
                "iVBORw0KGgo=",
                new string('a', 64),
                "Fixture portrait",
                "actor.fixture",
                "visual-0",
                "portrait",
                1,
                1,
                "") ]));
        }
    }
}
