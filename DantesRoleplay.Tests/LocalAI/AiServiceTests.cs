using System.Text.Json;
using DantesRoleplay.AI;
using Json.Schema;

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

    [Theory]
    [InlineData("{\"answer\":\"yes\"}", true)]
    [InlineData("{\"answer\":12}", false)]
    public async Task Response_schemas_resolve_local_references_without_global_retention(string output, bool valid)
    {
        var schemaId = new Uri($"urn:schema-test:{Guid.NewGuid():N}");
        var schema = """
            {"$id":"SCHEMA_ID","$schema":"https://json-schema.org/draft/2020-12/schema",
             "$defs":{"answer":{"type":"string"}},"type":"object","additionalProperties":false,
             "required":["answer"],"properties":{"answer":{"$ref":"#/$defs/answer"}}}
            """.Replace("SCHEMA_ID", schemaId.ToString(), StringComparison.Ordinal);
        var service = new AiService([new QueueProvider([Success(output, structured: true)])]);
        var result = await service.SendRequestAsync(new(
            "test", "model", [new(AiMessageRole.User, "answer")],
            AiRequestKind.StructuredRequest, ResponseSchemaJson: schema));

        Assert.Equal(valid, result.Ok);
        if (!valid) Assert.Equal("AI_RESPONSE_SCHEMA_MISMATCH", result.ErrorCode);
        Assert.Null(SchemaRegistry.Global.Get(schemaId));
        Assert.Null(BuildOptions.Default.SchemaRegistry.Get(schemaId));
    }

    [Theory]
    [InlineData("{\"key\":\"value\"}", true)]
    [InlineData("{\"key\":12}", false)]
    public async Task Tool_schemas_remain_local_during_registration_and_execution(string arguments, bool valid)
    {
        var schemaId = new Uri($"urn:schema-test:{Guid.NewGuid():N}");
        var schema = """
            {"$id":"SCHEMA_ID","$defs":{"key":{"type":"string"}},
             "type":"object","additionalProperties":false,"required":["key"],
             "properties":{"key":{"$ref":"#/$defs/key"}}}
            """.Replace("SCHEMA_ID", schemaId.ToString(), StringComparison.Ordinal);
        var provider = new QueueProvider([
            new(true, Model(), "", "", [new("call-1", "system_read", arguments)]),
            Success("finished")
        ]);
        var tool = new CapturingTool(schema);
        var service = new AiService([provider], [tool]);
        Assert.Null(SchemaRegistry.Global.Get(schemaId));
        Assert.Null(BuildOptions.Default.SchemaRegistry.Get(schemaId));

        var result = await service.SendTaskAsync("test", "model", "inspect", allowedTools: ["system_read"]);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(valid ? JsonValueKind.Object : JsonValueKind.Undefined, tool.Arguments.ValueKind);
        if (!valid) Assert.Contains(provider.Requests[1].Messages,
            message => message.Role == AiMessageRole.Tool && message.Content.Contains("AI_TOOL_ARGUMENTS_INVALID"));
        Assert.Null(SchemaRegistry.Global.Get(schemaId));
        Assert.Null(BuildOptions.Default.SchemaRegistry.Get(schemaId));
    }

    [Fact]
    public async Task Repeated_tool_calls_reuse_one_compilation_and_add_no_compilation_allocations()
    {
        var provider = new QueueProvider([
            new(true, Model(), "", "", [new("call-1", "system_read", "{\"key\":\"one\"}")]),
            new(true, Model(), "", "", [new("call-2", "system_read", "{\"key\":\"two\"}")]),
            Success("finished")
        ]);
        var tool = new CapturingTool();
        var service = new AiService([provider], [tool]);
        var prepared = service.ToolSchemaPreparation;

        var result = await service.SendTaskAsync(
            "test", "model", "inspect twice", allowedTools: ["system_read"]);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(2, tool.InvocationCount);
        Assert.Equal(1, prepared.Compilations);
        Assert.True(prepared.AllocatedBytes > 0);
        Assert.Equal(prepared, service.ToolSchemaPreparation);
    }

    [Fact]
    public async Task A_changed_tool_definition_gets_a_new_scope_local_compilation()
    {
        const string stringSchema = """
            {"type":"object","required":["key"],"properties":{"key":{"type":"string"}}}
            """;
        const string integerSchema = """
            {"type":"object","required":["key"],"properties":{"key":{"type":"integer"}}}
            """;
        var originalProvider = new QueueProvider([
            new(true, Model(), "", "", [new("call-1", "system_read", "{\"key\":12}")]),
            Success("original finished")
        ]);
        var tool = new MutableTool(stringSchema);
        var originalService = new AiService([originalProvider], [tool]);
        tool.ChangeSchema(integerSchema);

        var original = await originalService.SendTaskAsync(
            "test", "model", "use original", allowedTools: ["system_read"]);

        Assert.True(original.Ok, original.ErrorMessage);
        Assert.Equal(0, tool.InvocationCount);
        Assert.Equal(stringSchema, Assert.Single(originalProvider.Requests[0].Tools).InputSchemaJson);
        Assert.Contains(originalProvider.Requests[1].Messages, message =>
            message.Role == AiMessageRole.Tool && message.Content.Contains("AI_TOOL_ARGUMENTS_INVALID"));

        var changedProvider = new QueueProvider([
            new(true, Model(), "", "", [new("call-2", "system_read", "{\"key\":12}")]),
            Success("changed finished")
        ]);
        var changedService = new AiService([changedProvider], [tool]);
        var changed = await changedService.SendTaskAsync(
            "test", "model", "use changed", allowedTools: ["system_read"]);

        Assert.True(changed.Ok, changed.ErrorMessage);
        Assert.Equal(1, tool.InvocationCount);
        Assert.Equal(integerSchema, Assert.Single(changedProvider.Requests[0].Tools).InputSchemaJson);
        Assert.Equal(1, originalService.ToolSchemaPreparation.Compilations);
        Assert.Equal(1, changedService.ToolSchemaPreparation.Compilations);
    }

    [Fact]
    public async Task Reused_schema_never_authorizes_a_tool_omitted_from_the_request()
    {
        var provider = new QueueProvider([
            new(true, Model(), "", "", [new("call-1", "system_write", "{\"key\":\"value\"}")]),
            Success("finished")
        ]);
        var read = new CapturingTool(name: "system_read");
        var write = new CapturingTool(name: "system_write");
        var service = new AiService([provider], [read, write]);

        var result = await service.SendTaskAsync(
            "test", "model", "read only", allowedTools: ["system_read"]);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(["system_read"], provider.Requests[0].Tools.Select(value => value.Name));
        Assert.Equal(0, read.InvocationCount);
        Assert.Equal(0, write.InvocationCount);
        Assert.Contains(provider.Requests[1].Messages, message =>
            message.Role == AiMessageRole.Tool && message.Content.Contains("AI_TOOL_UNKNOWN"));
    }

    [Fact]
    public async Task Malformed_tool_schemas_fail_registration_and_authorized_agent_preparation()
    {
        Assert.Throws<ArgumentException>(() =>
            new AiService([new QueueProvider([])], [new CapturingTool("{")]));

        var provider = new QueueProvider([Success("must not run")]);
        var service = new AiService([provider]);
        var result = await service.SendAgentRequestAsync(
            new("world.steward", "World Steward", "You maintain the current world state.", ""),
            new("test", "model", [new(AiMessageRole.User, "Inspect.")]),
            [new CapturingTool("{")]);

        Assert.False(result.Ok);
        Assert.Equal("AI_TOOL_INVALID", result.ErrorCode);
        Assert.Empty(provider.Requests);
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

    private sealed class CapturingTool(string? schema = null, string name = "system_read") : IAiTool
    {
        public JsonElement Arguments { get; private set; }
        public int InvocationCount { get; private set; }
        public AiToolDefinition Definition { get; } = new(
            name,
            "Read a system value.",
            schema ?? """{"type":"object","additionalProperties":false,"required":["key"],"properties":{"key":{"type":"string"}}}""");

        public Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
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

    private sealed class MutableTool(string schema) : IAiTool
    {
        public int InvocationCount { get; private set; }
        public AiToolDefinition Definition { get; private set; } = new(
            "system_read", "Read a system value.", schema);

        public void ChangeSchema(string schemaJson) =>
            Definition = Definition with { InputSchemaJson = schemaJson };

        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(AiToolResult.Success("{\"found\":true}"));
        }
    }
}
