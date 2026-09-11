using System.Text.Json;
using DantesRoleplay.AI;

namespace DantesRoleplay.Tests;

public sealed class AiLifecycleContractTests
{
    [Fact]
    public void Descriptor_snapshots_request_and_clears_executor()
    {
        var messages = new List<AiMessage> { new(AiMessageRole.User, "x", Media: [new("image/png", "a")]) };
        var tools = new List<AiToolDefinition> { new("read_value", "read", "{}") };
        var request = new AiProviderRequest("model", messages, AiRequestKind.Task, AiReasoningEffort.None, "", tools,
            (_, _) => Task.FromResult(AiToolResult.Success("{}")), 1);
        var descriptor = new AiProviderCallDescriptor("provider", 0, request);
        messages.Clear(); tools.Clear();
        Assert.Null(descriptor.Request.ToolExecutor);
        Assert.Single(descriptor.Request.Messages); Assert.Single(descriptor.Request.Tools);
    }

    [Fact]
    public void Tool_and_observation_snapshots_are_defensive()
    {
        using var json = JsonDocument.Parse("{\"x\":1}");
        var descriptor = new AiToolDispatchDescriptor(1, new("read_value", "read", "{}"),
            new("call", "read_value", json.RootElement, AiRequestKind.Task));
        json.Dispose();
        var media = new List<AiMediaContent> { new("image/png", "a") };
        var observation = new AiToolDispatchObservation(AiDispatchCompletionKind.Returned, AiToolResult.Success("ok", media));
        media.Clear();
        Assert.Equal(1, descriptor.Invocation.Arguments.GetProperty("x").GetInt32());
        Assert.Single(observation.Result!.Media!);
    }

    [Fact]
    public void Host_only_values_reject_json_and_lifecycle_errors_are_bounded()
    {
        var descriptor = new AiProviderCallDescriptor("provider", 0, new("model", [new(AiMessageRole.User, "x")], AiRequestKind.Task, AiReasoningEffort.None, "", [], null, 1));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(descriptor));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IAiInvocationLifecycle>("{}"));
        var error = new AiLifecycleException("AI_OK", new string('x', 600));
        Assert.Equal(512, error.Message.Length);
    }

    [Fact]
    public void All_observation_and_scope_shapes_reject_json_in_both_directions()
    {
        var provider = new AiProviderCallObservation(AiDispatchCompletionKind.Returned,
            new(true, null, "done", "", [], Usage: new(1, 2, 3, false)));
        var tool = new AiToolDispatchObservation(AiDispatchCompletionKind.Returned, AiToolResult.Success("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(provider));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiProviderCallObservation>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(tool));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiToolDispatchObservation>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiToolDispatchDescriptor>("{}"));
        var scope = new Scope();
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<IAiInvocationLifecycle>(scope));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<IAiProviderCallScope>(scope));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<IAiToolDispatchScope>(scope));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IAiProviderCallScope>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IAiToolDispatchScope>("{}"));
    }

    [Fact]
    public void Provider_observation_freezes_mutable_calls_without_losing_partial_usage()
    {
        var calls = new List<AiToolCall> { new("call.1", "read", "{}") };
        var usage = new AiTokenUsageEvidence(1, 2, 4, false);
        var observation = new AiProviderCallObservation(AiDispatchCompletionKind.Returned,
            new(false, null, "", "", calls, Usage: usage));
        calls.Clear();
        Assert.Single(observation.Response!.ToolCalls);
        Assert.Equal(usage, observation.Response.Usage);
        Assert.Throws<AiLifecycleException>(() => new AiProviderCallObservation(AiDispatchCompletionKind.NotStarted, observation.Response));
        Assert.Throws<AiLifecycleException>(() => new AiProviderCallObservation(AiDispatchCompletionKind.Returned, null));
    }

    [Fact]
    public async Task Legacy_service_cannot_silently_ignore_required_lifecycle()
    {
        IAiService service = new LegacyService();
        var result = await service.SendAgentRequestAsync(new("worker", "Worker", "work"),
            new("fixture", "model", [new(AiMessageRole.User, "work")]), [], new Scope());
        Assert.Equal("AI_LIFECYCLE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public void Null_tool_results_do_not_change_legacy_json_shape()
    {
        var json = JsonSerializer.Serialize(AiResponse.Failure("FAILED", "Failed."), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("toolResults", json);
    }

    private sealed class Scope : IAiInvocationLifecycle, IAiProviderCallScope, IAiToolDispatchScope
    {
        public ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome) => throw new NotSupportedException();
        public ValueTask RecordToolOutcomeAsync(AiToolDispatchObservation outcome) => throw new NotSupportedException();
    }
    private sealed class LegacyService : IAiService
    {
        public IReadOnlyList<AiProviderInfo> ListProviders() => [];
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiResponse> SendMessageAsync(string provider, string model, IReadOnlyList<AiMessage> messages,
            AiReasoningEffort reasoning = AiReasoningEffort.None, IReadOnlyList<string>? allowedTools = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiResponse> SendTaskAsync(string provider, string model, string task,
            AiReasoningEffort reasoning = AiReasoningEffort.None, IReadOnlyList<string>? allowedTools = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiResponse> SendRequestAsync(AiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiResponse> SendAgentRequestAsync(AiAgentProfile profile, AiRequest request, IReadOnlyList<IAiTool> authorizedTools,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Legacy path must not be called.");
    }
}
