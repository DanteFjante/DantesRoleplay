using System.Text.Json;
using DantesRoleplay.AI;

namespace DantesRoleplay.Tests;

public sealed class AiAgentBoundaryTests
{
    private const string Schema = """{"type":"object","required":["answer"],"properties":{"answer":{"type":"string"}}}""";

    [Fact]
    public async Task Agent_cannot_select_a_global_tool_outside_its_materialized_authority()
    {
        var provider = new QueueProvider(new AiProviderResponse(true, null, "done", "", []));
        var global = new Tool("global_tool");
        var local = new Tool("local_tool");
        var service = new AiService([provider], [global]);

        var denied = await service.SendAgentRequestAsync(Profile(), Request(["global_tool"]), [local]);

        Assert.Equal("AI_TOOL_UNKNOWN", denied.ErrorCode);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, global.Calls);
        var allowed = await service.SendAgentRequestAsync(Profile(), Request(["local_tool"]), [local]);
        Assert.True(allowed.Ok);
        Assert.Equal("local_tool", Assert.Single(Assert.Single(provider.Requests).Tools).Name);
    }

    [Fact]
    public async Task Agent_uses_context_bound_instance_even_when_global_tool_has_same_name()
    {
        var provider = new QueueProvider(
            new(true, null, "", "", [new("call.1", "read_value", "{}")]),
            new(true, null, "done", "", []));
        var global = new Tool("read_value");
        var scoped = new Tool("read_value");
        var service = new AiService([provider], [global]);

        var result = await service.SendAgentRequestAsync(Profile(), Request(["read_value"]), [scoped]);

        Assert.True(result.Ok);
        Assert.Equal(0, global.Calls);
        Assert.Equal(1, scoped.Calls);
        Assert.Single(result.Activities!, value => value.Kind == "tool-call" && value.Status == "requested");
    }

    [Fact]
    public async Task Direct_requests_keep_explicit_access_to_registered_tools()
    {
        var provider = new QueueProvider(
            new(true, null, "", "", [new("call.1", "global_tool", "{}")]),
            new(true, null, "done", "", []));
        var global = new Tool("global_tool");
        var result = await new AiService([provider], [global]).SendRequestAsync(Request(["global_tool"]));
        Assert.True(result.Ok);
        Assert.Equal(1, global.Calls);
    }

    [Fact]
    public async Task Provider_failure_retains_usage_and_prior_tool_activity_without_claiming_success()
    {
        var provider = new QueueProvider(
            new(true, null, "", "", [new("call.1", "read_value", "{}")], PromptTokens: 11, OutputTokens: 3),
            new(false, null, "", "", [new("call.2", "read_value", "{}")], PromptTokens: 7, OutputTokens: 2, ErrorCode: "PROVIDER_LOST", ErrorMessage: "Lost connection."));
        var tool = new Tool("read_value");
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request(["read_value"]));

        Assert.False(result.Ok);
        Assert.Equal("PROVIDER_LOST", result.ErrorCode);
        Assert.Equal(18, result.PromptTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Equal(["call.1", "call.2"], result.ToolCalls.Select(value => value.Id));
        Assert.Equal(1, tool.Calls);
        Assert.Contains(result.Activities!, value => value.Kind == "tool-call" && value.Status == "completed");
        Assert.Contains(result.Activities!, value => value.ToolCallId == "call.2" && value.Status == "requested");
        Assert.DoesNotContain(result.Activities!, value => value.ToolCallId == "call.2" && value.Status == "completed");
        Assert.DoesNotContain(result.Activities!, value => value.Kind == "result" && value.Status == "completed");
    }

    [Theory]
    [InlineData("{\"answer\":42}")]
    [InlineData("not-json")]
    public async Task Schema_failure_retains_provider_usage(string output)
    {
        var provider = new QueueProvider(new AiProviderResponse(true, null, output, output, [], PromptTokens: 13, OutputTokens: 5));
        var result = await new AiService([provider]).SendRequestAsync(Request([]) with { ResponseSchemaJson = Schema });
        Assert.Equal("AI_RESPONSE_SCHEMA_MISMATCH", result.ErrorCode);
        Assert.Equal(13, result.PromptTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Null(result.StructuredData);
    }

    [Fact]
    public async Task Tool_round_limit_retains_usage_and_does_not_execute_excess_call()
    {
        var provider = new QueueProvider(
            new AiProviderResponse(true, null, "", "", [new("call.1", "read_value", "{}")], PromptTokens: 9, OutputTokens: 4));
        var tool = new Tool("read_value");
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request(["read_value"]) with { MaximumToolRounds = 0 });
        Assert.Equal("AI_TOOL_ROUND_LIMIT", result.ErrorCode);
        Assert.Equal(9, result.PromptTokens);
        Assert.Equal(4, result.OutputTokens);
        Assert.Equal("call.1", Assert.Single(result.ToolCalls).Id);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Final_round_call_after_a_successful_round_is_observed_but_not_executed()
    {
        var provider = new QueueProvider(
            new(true, null, "", "", [new("call.1", "read_value", "{}")], PromptTokens: 9, OutputTokens: 4),
            new(true, null, "", "", [new("call.2", "read_value", "{}")], PromptTokens: 7, OutputTokens: 3));
        var tool = new Tool("read_value");
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request(["read_value"]) with { MaximumToolRounds = 1 });

        Assert.Equal("AI_TOOL_ROUND_LIMIT", result.ErrorCode);
        Assert.Equal(["call.1", "call.2"], result.ToolCalls.Select(value => value.Id));
        Assert.Equal(1, tool.Calls);
        Assert.Equal(16, result.PromptTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.Contains(result.Activities!, value => value.ToolCallId == "call.2" && value.Status == "requested");
        Assert.DoesNotContain(result.Activities!, value => value.ToolCallId == "call.2" && value.Status == "completed");
    }

    [Fact]
    public async Task Unexpected_call_without_authorized_tools_is_observed_but_not_executed()
    {
        var provider = new QueueProvider(new AiProviderResponse(true, null, "", "", [new("call.1", "read_value", "{}")], PromptTokens: 5, OutputTokens: 2));
        var tool = new Tool("read_value");
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request([]));

        Assert.Equal("AI_TOOL_CALL_UNEXPECTED", result.ErrorCode);
        Assert.Equal("call.1", Assert.Single(result.ToolCalls).Id);
        Assert.Equal(0, tool.Calls);
        Assert.Equal(5, result.PromptTokens);
        Assert.Equal(2, result.OutputTokens);
        Assert.Contains(result.Activities!, value => value.ToolCallId == "call.1" && value.Status == "requested");
        Assert.DoesNotContain(result.Activities!, value => value.ToolCallId == "call.1" && value.Status == "completed");
    }

    private static AiAgentProfile Profile() => new("fixture.worker", "Worker", "Perform the selected task.");
    private static AiRequest Request(IReadOnlyList<string> allowed) => new("fixture", "model", [new(AiMessageRole.User, "work")], AllowedTools: allowed);

    private sealed class Tool(string name) : IAiTool
    {
        public int Calls { get; private set; }
        public AiToolDefinition Definition => new(name, "Read a fixture value.", "{\"type\":\"object\"}");
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(AiToolResult.Success("{}"));
        }
    }

    private sealed class QueueProvider(params AiProviderResponse[] responses) : IAiProvider
    {
        private readonly Queue<AiProviderResponse> _responses = new(responses);
        public List<AiProviderRequest> Requests { get; } = [];
        public AiProviderInfo Info => new("fixture", "Fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
