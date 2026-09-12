using DantesRoleplay.AI;

namespace DantesRoleplay.Tests;

public sealed class AiLifecycleExecutionTests
{
    [Fact]
    public async Task Every_round_and_returned_tool_are_admitted_and_observed_in_order()
    {
        var lifecycle = new Lifecycle();
        var tool = new Tool();
        var provider = new Provider((call, _, _) => Task.FromResult(call == 1 ? WithCalls("call.1") : Complete()));
        var result = await Run(provider, tool, lifecycle);
        Assert.True(result.Ok);
        Assert.Equal(["admit-provider.0", "observe-provider.0", "admit-tool.1", "observe-tool.1", "admit-provider.1", "observe-provider.1"], lifecycle.Events);
        Assert.Equal(2, lifecycle.Providers.Count);
        Assert.Single(lifecycle.Tools);
        Assert.Equal(1, tool.Calls);
        Assert.Equal("{\"receipt\":\"host.receipt.1\"}", Assert.Single(result.ToolResults!).Result.Content);
        Assert.All(lifecycle.Providers, value => Assert.Equal(AiDispatchCompletionKind.Returned, value.Kind));
    }

    [Fact]
    public async Task Dynamic_callbacks_use_the_same_tool_admission_without_a_second_charge()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider(async (_, request, token) =>
        {
            await request.ToolExecutor!(new("dynamic.1", "read", "{}"), token);
            return Complete();
        });
        var tool = new Tool();
        var result = await Run(provider, tool, lifecycle);
        Assert.True(result.Ok);
        Assert.Equal(["admit-provider.0", "admit-tool.1", "observe-tool.1", "observe-provider.0"], lifecycle.Events);
        Assert.Single(result.ToolResults!);
        Assert.Single(lifecycle.Tools);
    }

    [Fact]
    public async Task Denied_provider_admission_does_not_call_provider_or_invent_outcome()
    {
        var lifecycle = new Lifecycle { BeforeProvider = _ => throw new AiLifecycleException("GRANT_REVOKED", "Grant revoked.") };
        var provider = new Provider((_, _, _) => Task.FromResult(Complete()));
        var result = await Run(provider, new(), lifecycle);
        Assert.Equal("GRANT_REVOKED", result.ErrorCode);
        Assert.Equal(0, provider.Calls);
        Assert.Empty(lifecycle.Providers);
    }

    [Fact]
    public async Task Cancellation_after_admission_records_not_started_without_zero_usage()
    {
        using var cancellation = new CancellationTokenSource();
        var lifecycle = new Lifecycle { BeforeProvider = _ => cancellation.Cancel() };
        var provider = new Provider((_, _, _) => Task.FromResult(Complete()));
        var result = await Run(provider, new(), lifecycle, cancellation: cancellation.Token);
        Assert.Equal("AI_REQUEST_CANCELLED", result.ErrorCode);
        Assert.Equal(0, provider.Calls);
        var observation = Assert.Single(lifecycle.Providers);
        Assert.Equal(AiDispatchCompletionKind.NotStarted, observation.Kind);
        Assert.Null(observation.Response);
    }

    [Fact]
    public async Task Provider_throw_is_recorded_with_unknown_usage()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider((_, _, _) => throw new InvalidOperationException("private provider details"));
        var result = await Run(provider, new(), lifecycle);
        Assert.Equal("AI_PROVIDER_FAILED", result.ErrorCode);
        Assert.DoesNotContain("private", result.ErrorMessage);
        Assert.Equal(AiDispatchCompletionKind.Threw, Assert.Single(lifecycle.Providers).Kind);
        Assert.Null(lifecycle.Providers[0].Response);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task Provider_returned_failure_retains_actual_partial_usage_for_settlement()
    {
        var lifecycle = new Lifecycle();
        var partial = new AiTokenUsageEvidence(7, 2, 10, false);
        var provider = new Provider((_, _, _) => Task.FromResult(AiProviderResponse.Failure("PROVIDER_LOST", "Lost.") with { Usage = partial }));
        var result = await Run(provider, new(), lifecycle);
        Assert.Equal("PROVIDER_LOST", result.ErrorCode);
        Assert.Equal(partial, result.Usage);
        Assert.Equal(partial, Assert.Single(lifecycle.Providers).Response!.Usage);
    }

    [Fact]
    public async Task Cancellation_after_actual_provider_return_does_not_erase_complete_evidence()
    {
        using var cancellation = new CancellationTokenSource();
        var lifecycle = new Lifecycle();
        var provider = new Provider((_, _, _) => { cancellation.Cancel(); return Task.FromResult(Complete()); });
        var result = await Run(provider, new(), lifecycle, cancellation: cancellation.Token);
        Assert.Equal("AI_REQUEST_CANCELLED", result.ErrorCode);
        Assert.True(result.Usage!.IsComplete);
        Assert.Equal(AiDispatchCompletionKind.Returned, Assert.Single(lifecycle.Providers).Kind);
        Assert.True(lifecycle.Providers[0].Response!.Usage!.IsComplete);
    }

    [Fact]
    public async Task Provider_observer_failure_keeps_already_returned_usage_and_stops_tools()
    {
        var lifecycle = new Lifecycle { AfterProvider = _ => throw new IOException("evidence store failed") };
        var tool = new Tool();
        var provider = new Provider((_, _, _) => Task.FromResult(WithCalls("call.1")));
        var result = await Run(provider, tool, lifecycle);
        Assert.Equal("AI_LIFECYCLE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(3, 2, 5, true), result.Usage);
        Assert.Equal("call.1", Assert.Single(result.ToolCalls).Id);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Tool_observer_failure_keeps_actual_success_receipt_payload_and_activity()
    {
        var lifecycle = new Lifecycle { AfterTool = _ => throw new IOException("evidence store failed") };
        var provider = new Provider((_, _, _) => Task.FromResult(WithCalls("call.1", "call.2")));
        var tool = new Tool();
        var result = await Run(provider, tool, lifecycle);
        Assert.Equal("AI_LIFECYCLE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, tool.Calls);
        var retained = Assert.Single(result.ToolResults!);
        Assert.True(retained.Result.Ok);
        Assert.Equal("{\"receipt\":\"host.receipt.1\"}", retained.Result.Content);
        Assert.Contains(result.Activities!, value => value.ToolCallId == "call.1" && value.Status == "completed");
        Assert.DoesNotContain(result.Activities!, value => value.Kind == "result" && value.Status == "completed");
    }

    [Fact]
    public async Task Oversized_actual_tool_result_stops_with_explicit_unavailable_marker()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider((_, _, _) => Task.FromResult(WithCalls("call.1", "call.2")));
        var tool = new Tool { Content = new string('x', 2_000) };
        var result = await Run(provider, tool, lifecycle, Request() with { MaximumResponseBytes = 512 });
        Assert.Equal("AI_LIFECYCLE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(1, tool.Calls);
        var marker = Assert.Single(result.ToolResults!);
        Assert.Equal("AI_TOOL_RESULT_UNAVAILABLE", marker.Result.ErrorCode);
        Assert.Empty(marker.Result.Content);
        Assert.Equal("call.1", marker.Call.Id);
        Assert.Contains(result.Activities!, value => value.ErrorCode == "AI_TOOL_RESULT_BYTE_LIMIT");
        Assert.Equal(AiDispatchCompletionKind.Returned, Assert.Single(lifecycle.Tools).Kind);
    }

    [Fact]
    public async Task Cumulative_tool_results_share_one_retention_allowance()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider((_, _, _) => Task.FromResult(WithCalls("call.1", "call.2", "call.3")));
        var tool = new Tool { Content = new string('x', 250) };
        var result = await Run(provider, tool, lifecycle, Request() with { MaximumResponseBytes = 700 });
        Assert.Equal("AI_LIFECYCLE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(2, tool.Calls);
        Assert.Equal(2, result.ToolResults!.Count);
        Assert.True(result.ToolResults[0].Result.Ok);
        Assert.Equal("AI_TOOL_RESULT_UNAVAILABLE", result.ToolResults[1].Result.ErrorCode);
    }

    [Fact]
    public async Task Tool_cancelled_after_admission_is_not_started_and_keeps_completed_provider_usage()
    {
        using var cancellation = new CancellationTokenSource();
        var lifecycle = new Lifecycle { BeforeTool = _ => cancellation.Cancel() };
        var provider = new Provider((_, _, _) => Task.FromResult(WithCalls("call.1")));
        var tool = new Tool();
        var result = await Run(provider, tool, lifecycle, cancellation: cancellation.Token);
        Assert.Equal("AI_REQUEST_CANCELLED", result.ErrorCode);
        Assert.Equal(0, tool.Calls);
        Assert.Equal(AiDispatchCompletionKind.NotStarted, Assert.Single(lifecycle.Tools).Kind);
        Assert.Null(lifecycle.Tools[0].Result);
        Assert.True(result.Usage!.IsComplete);
    }

    [Fact]
    public async Task Invalid_arguments_do_not_reserve_an_actual_tool_execution()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider((call, _, _) => Task.FromResult(call == 1
            ? Complete() with { ToolCalls = [new("call.1", "read", "[]")] } : Complete()));
        var tool = new Tool();
        var result = await Run(provider, tool, lifecycle);
        Assert.True(result.Ok);
        Assert.Equal(0, tool.Calls);
        Assert.Empty(lifecycle.Tools);
        Assert.Empty(result.ToolResults!);
    }

    [Fact]
    public async Task Tool_returned_error_code_cannot_change_dispatch_accounting_or_duplicate_activity()
    {
        var lifecycle = new Lifecycle();
        var provider = new Provider((call, _, _) => Task.FromResult(call == 1 ? WithCalls("call.1") : Complete()));
        var tool = new Tool { ReturnedResult = AiToolResult.Failure("AI_TOOL_ARGUMENTS_INVALID", "Tool rejected its input.") };
        var result = await Run(provider, tool, lifecycle);
        Assert.Single(lifecycle.Tools);
        Assert.Single(result.ToolResults!);
        Assert.Single(result.Activities!, value => value.ToolCallId == "call.1" && value.Status == "failed");
    }

    private static AiRequest Request() => new("fixture", "model", [new(AiMessageRole.User, "work")], AllowedTools: ["read"]);
    private static AiProviderResponse Complete() => new(true, null, "done", "", [], Usage: new(3, 2, 5, true));
    private static AiProviderResponse WithCalls(params string[] ids) => Complete() with
    {
        Text = "", ToolCalls = ids.Select(id => new AiToolCall(id, "read", "{}")).ToArray()
    };
    private static Task<AiResponse> Run(Provider provider, Tool tool, Lifecycle lifecycle, AiRequest? request = null, CancellationToken cancellation = default) =>
        new AiService([provider]).SendAgentRequestAsync(new("worker", "Worker", "Run selected tools."), request ?? Request(), [tool], lifecycle, cancellation);

    private sealed class Provider(Func<int, AiProviderRequest, CancellationToken, Task<AiProviderResponse>> send) : IAiProvider
    {
        public int Calls { get; private set; }
        public AiProviderInfo Info => new("fixture", "Fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, CancellationToken cancellationToken = default) => send(++Calls, request, cancellationToken);
    }
    private sealed class Tool : IAiTool
    {
        public int Calls { get; private set; }
        public string Content { get; init; } = "{\"receipt\":\"host.receipt.1\"}";
        public AiToolResult? ReturnedResult { get; init; }
        public AiToolDefinition Definition => new("read", "Read.", "{\"type\":\"object\"}");
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ReturnedResult ?? AiToolResult.Success(Content));
        }
    }
    private sealed class Lifecycle : IAiInvocationLifecycle
    {
        public List<string> Events { get; } = [];
        public List<AiProviderCallObservation> Providers { get; } = [];
        public List<AiToolDispatchObservation> Tools { get; } = [];
        public Action<AiProviderCallDescriptor>? BeforeProvider { get; init; }
        public Action<AiProviderCallObservation>? AfterProvider { get; init; }
        public Action<AiToolDispatchDescriptor>? BeforeTool { get; init; }
        public Action<AiToolDispatchObservation>? AfterTool { get; init; }
        public ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call, CancellationToken cancellationToken)
        {
            Events.Add($"admit-provider.{call.Round}");
            BeforeProvider?.Invoke(call);
            return ValueTask.FromResult<IAiProviderCallScope>(new ProviderScope(this, call.Round));
        }
        private sealed class ProviderScope(Lifecycle owner, int round) : IAiProviderCallScope
        {
            public ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome)
            {
                owner.Events.Add($"observe-provider.{round}");
                owner.Providers.Add(outcome);
                owner.AfterProvider?.Invoke(outcome);
                return ValueTask.CompletedTask;
            }
            public ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch, CancellationToken cancellationToken)
            {
                owner.Events.Add($"admit-tool.{dispatch.DispatchOrdinal}");
                owner.BeforeTool?.Invoke(dispatch);
                return ValueTask.FromResult<IAiToolDispatchScope>(new ToolScope(owner, dispatch.DispatchOrdinal));
            }
        }
        private sealed class ToolScope(Lifecycle owner, int ordinal) : IAiToolDispatchScope
        {
            public ValueTask RecordToolOutcomeAsync(AiToolDispatchObservation outcome)
            {
                owner.Events.Add($"observe-tool.{ordinal}");
                owner.Tools.Add(outcome);
                owner.AfterTool?.Invoke(outcome);
                return ValueTask.CompletedTask;
            }
        }
    }
}
