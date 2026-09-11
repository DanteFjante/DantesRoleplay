using DantesRoleplay.AI;

namespace DantesRoleplay.Tests;

public sealed class AiHostLimitTests
{
    [Fact]
    public async Task Parallel_provider_callbacks_cannot_expand_the_total_tool_allowance()
    {
        var tool = new CountingTool();
        var provider = new CallbackProvider(async (request, token) =>
        {
            await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
                request.ToolExecutor!(new($"call.{index}", "read", "{}"), token)));
            return Success(new(7, 3, 10, true));
        });
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request() with { MaximumToolCalls = 2 });
        Assert.Equal("AI_TOOL_CALL_LIMIT", result.ErrorCode);
        Assert.Equal(2, tool.Calls);
        Assert.DoesNotContain(result.Activities!, value => value.Kind == "result" && value.Status == "completed");
    }

    [Fact]
    public async Task Callback_allowance_is_shared_across_provider_rounds()
    {
        var tool = new CountingTool();
        var rounds = 0;
        var provider = new CallbackProvider(async (request, token) =>
        {
            rounds++;
            await request.ToolExecutor!(new($"callback.{rounds}", "read", "{}"), token);
            return rounds == 1
                ? Success(new(4, 2, 6, true)) with { ToolCalls = [new("returned.1", "read", "{}")] }
                : Success(new(5, 2, 7, true));
        });
        var result = await new AiService([provider], [tool]).SendRequestAsync(Request() with { MaximumToolCalls = 2 });
        Assert.Equal("AI_TOOL_CALL_LIMIT", result.ErrorCode);
        Assert.Equal(2, tool.Calls);
        Assert.Equal(2, rounds);
        Assert.Contains(result.ToolCalls, value => value.Id == "callback.1");
        Assert.Contains(result.ToolCalls, value => value.Id == "returned.1");
    }

    [Fact]
    public async Task Complete_usage_adds_distinct_provider_requests_and_preserves_total_overhead()
    {
        var rounds = 0;
        var provider = new CallbackProvider((request, token) => Task.FromResult(++rounds == 1
            ? Success(new(4, 2, 9, true)) with { ToolCalls = [new("call.1", "read", "{}")] }
            : Success(new(5, 3, 10, true))));
        var result = await new AiService([provider], [new CountingTool()]).SendRequestAsync(Request());
        Assert.True(result.Ok);
        Assert.Equal(new AiTokenUsageEvidence(9, 5, 19, true), result.Usage);
    }

    [Fact]
    public async Task Missing_round_usage_keeps_other_counts_as_incomplete_even_when_legacy_counts_exist()
    {
        var rounds = 0;
        var provider = new CallbackProvider((request, token) => Task.FromResult(++rounds == 1
            ? Success(new(4, 2, 6, true)) with { ToolCalls = [new("call.1", "read", "{}")] }
            : Success(null) with { PromptTokens = 10, OutputTokens = 2 }));
        var result = await new AiService([provider], [new CountingTool()]).SendRequestAsync(Request());
        Assert.Equal(new AiTokenUsageEvidence(4, 2, 6, false), result.Usage);
    }

    [Fact]
    public async Task Cancellation_keeps_prior_round_usage_and_executed_tool_activity()
    {
        using var cancellation = new CancellationTokenSource();
        var rounds = 0;
        var provider = new CallbackProvider(async (request, token) =>
        {
            if (++rounds == 1)
                return Success(new(4, 2, 6, true)) with { ToolCalls = [new("call.1", "read", "{}")] };
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Success(null);
        });
        var result = await new AiService([provider], [new CountingTool()]).SendRequestAsync(Request(), cancellation.Token);
        Assert.Equal("AI_REQUEST_CANCELLED", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(4, 2, 6, false), result.Usage);
        Assert.Contains(result.Activities!, value => value.ToolCallId == "call.1" && value.Status == "completed");
    }

    [Fact]
    public async Task Timeout_stops_a_cooperative_provider_and_does_not_invent_usage()
    {
        var provider = new CallbackProvider(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Success(null);
        });
        var result = await new AiService([provider]).SendRequestAsync(Request() with
        {
            AllowedTools = [], MaximumDuration = TimeSpan.FromMilliseconds(20)
        });
        Assert.Equal("AI_REQUEST_TIMEOUT", result.ErrorCode);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task Byte_limit_counts_utf8_and_keeps_complete_provider_usage_on_local_rejection()
    {
        var provider = new CallbackProvider((request, token) => Task.FromResult(Success(new(2, 1, 3, true)) with { Text = "éé" }));
        var result = await new AiService([provider]).SendRequestAsync(Request() with { AllowedTools = [], MaximumResponseBytes = 3 });
        Assert.Equal("AI_RESPONSE_BYTE_LIMIT", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(2, 1, 3, true), result.Usage);
    }

    [Fact]
    public async Task Invalid_usage_does_not_become_complete_or_overflow_the_accounting_projection()
    {
        var provider = new CallbackProvider((request, token) => Task.FromResult(Success(new(long.MaxValue, 1, long.MaxValue, true))));
        var result = await new AiService([provider]).SendRequestAsync(Request() with { AllowedTools = [] });
        Assert.Null(result.Usage);
    }

    private static AiRequest Request() => new("fixture", "model", [new(AiMessageRole.User, "work")], AllowedTools: ["read"]);
    private static AiProviderResponse Success(AiTokenUsageEvidence? usage) => new(true, null, "done", "", [], Usage: usage);
    private sealed class CountingTool : IAiTool
    {
        private int calls;
        public int Calls => calls;
        public AiToolDefinition Definition => new("read", "Read a fixture.", "{\"type\":\"object\"}");
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(AiToolResult.Success("{}"));
        }
    }
    private sealed class CallbackProvider(Func<AiProviderRequest, CancellationToken, Task<AiProviderResponse>> send) : IAiProvider
    {
        public AiProviderInfo Info => new("fixture", "Fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, CancellationToken cancellationToken = default) => send(request, cancellationToken);
    }
}
