using System.Runtime.CompilerServices;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tests;

public sealed class CodexAiClientTests
{
    [Fact]
    public async Task Cumulative_usage_is_not_summed_and_foreign_usage_is_ignored()
    {
        var session = new FakeSession([Usage(1, 2, 4), Usage(2, 3, 7), Usage(2, 3, 7),
            new("usage", Usage: new("foreign", "turn", 99, 99, 198)), Reply(), Terminal()]);
        var result = await Client(session).SendAsync(Request());
        Assert.True(result.Ok);
        Assert.Equal(new AiTokenUsageEvidence(2, 3, 7, true), result.Usage);
        Assert.Null(session.ResumedThread);
    }

    [Theory]
    [InlineData("decrease")]
    [InlineData("equal-total")]
    [InlineData("malformed")]
    [InlineData("invalid-sum")]
    public async Task Invalid_snapshots_preserve_prior_lower_bound(string kind)
    {
        var invalid = kind switch
        {
            "decrease" => Usage(1, 2, 3),
            "equal-total" => Usage(3, 3, 7),
            "invalid-sum" => Usage(long.MaxValue, 1, long.MaxValue),
            _ => new CodexProtocolEvent("usage", ErrorCode: "CODEX_USAGE_INVALID", ThreadId: "thread", TurnId: "turn")
        };
        var result = await Client(new([Usage(2, 3, 7), invalid, Reply(), Terminal()])).SendAsync(Request());
        Assert.Equal(new AiTokenUsageEvidence(2, 3, 7, false), result.Usage);
    }

    [Fact]
    public async Task Missing_usage_is_unknown_even_on_success()
    {
        var result = await Client(new([Reply(), Terminal()])).SendAsync(Request());
        Assert.True(result.Ok);
        Assert.Null(result.Usage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foreign")]
    public async Task Unverified_terminal_cannot_finalize_usage(string thread)
    {
        var session = new FakeSession([Usage(1, 1, 2), Reply(), Terminal() with { ThreadId = thread }]);
        var result = await Client(session).SendAsync(Request());
        Assert.Equal("CODEX_TERMINAL_IDENTITY_MISMATCH", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(1, 1, 2, false), result.Usage);
        Assert.Equal(["interrupt", "dispose"], session.Lifecycle);
    }

    [Fact]
    public async Task Cancellation_retains_usage_and_interrupts_before_disposal()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeSession([Usage(1, 1, 2), new("cancel")]) { Cancel = cancellation.Cancel };
        var result = await Client(session).SendAsync(Request(), cancellation.Token);
        Assert.Equal("CODEX_TURN_CANCELLED", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(1, 1, 2, false), result.Usage);
        Assert.Equal(["interrupt", "dispose"], session.Lifecycle);
    }

    [Fact]
    public async Task Protocol_failure_retains_partial_usage()
    {
        var session = new FakeSession([Usage(1, 1, 2), new("fail")]);
        var result = await Client(session).SendAsync(Request());
        Assert.Equal("CODEX_PROCESS_EXITED", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(1, 1, 2, false), result.Usage);
        Assert.Equal(["interrupt", "dispose"], session.Lifecycle);
    }

    [Fact]
    public async Task Deadline_covers_model_discovery()
    {
        var factory = new FakeFactory(new([])) { BlockDiscovery = true };
        var result = await new CodexAiClient(factory).SendAsync(Request() with { MaximumDuration = TimeSpan.FromMilliseconds(20) });
        Assert.Equal("CODEX_TURN_TIMEOUT", result.ErrorCode);
        Assert.Null(result.Usage);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("delta")]
    public async Task Utf8_cap_preserves_partial_usage(string type)
    {
        var text = type == "reply" ? new CodexProtocolEvent("reply", Reply: "åå") : new("delta", Delta: "åå");
        var session = new FakeSession([Usage(1, 1, 2), text, Terminal()]);
        var result = await Client(session).SendAsync(Request() with { MaximumResponseBytes = 3 });
        Assert.Equal("CODEX_RESPONSE_OVERSIZE", result.ErrorCode);
        Assert.Equal(new AiTokenUsageEvidence(1, 1, 2, false), result.Usage);
        Assert.Equal(["interrupt", "dispose"], session.Lifecycle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Concurrent_callbacks_cannot_exceed_allowance(int maximum)
    {
        var invoked = 0;
        var session = new FakeSession([new("callbacks"), Reply(), Terminal()]);
        var result = await Client(session).SendAsync(Request() with
        {
            MaximumToolCalls = maximum, Tools = [new("read", "Read.", "{}")],
            ToolExecutor = (_, _) => { Interlocked.Increment(ref invoked); return Task.FromResult(AiToolResult.Success("{}")); }
        });
        Assert.Equal("CODEX_TOOL_CALL_LIMIT", result.ErrorCode);
        Assert.Equal(maximum, invoked);
        Assert.Equal(["interrupt", "dispose"], session.Lifecycle);
    }

    [Fact]
    public void Normalizer_preserves_text_usage_and_terminal_identity()
    {
        var text = new string('å', 9_000);
        var delta = JsonSerializer.SerializeToElement(new { delta = text });
        Assert.Equal(text, CodexAppServerProcessSession.NormalizeNotification("item/agentMessage/delta", delta)!.Delta);
        var reply = JsonSerializer.SerializeToElement(new { item = new { type = "agentMessage", text } });
        Assert.Equal(text, CodexAppServerProcessSession.NormalizeNotification("item/completed", reply)!.Reply);
        var usage = JsonSerializer.SerializeToElement(new { threadId = "thread", turnId = "turn", tokenUsage = new
        {
            total = new { inputTokens = 4, outputTokens = 2, totalTokens = 7 }, last = new { inputTokens = 1 }
        } });
        Assert.Equal(new CodexProtocolTokenUsage("thread", "turn", 4, 2, 7),
            CodexAppServerProcessSession.NormalizeNotification("thread/tokenUsage/updated", usage)!.Usage);
        var terminal = JsonSerializer.SerializeToElement(new { threadId = "thread", turn = new { id = "turn", status = "completed" } });
        var normalized = CodexAppServerProcessSession.NormalizeNotification("turn/completed", terminal)!;
        Assert.Equal("thread", normalized.ThreadId);
        Assert.Equal("turn", normalized.TurnId);
    }

    [Theory]
    [InlineData("\"bad\"")]
    [InlineData("null")]
    [InlineData("9223372036854775808")]
    public void Malformed_usage_becomes_unknown_without_throwing(string input)
    {
        using var document = JsonDocument.Parse("{\"threadId\":\"thread\",\"turnId\":\"turn\",\"tokenUsage\":{\"total\":{\"inputTokens\":" + input + ",\"outputTokens\":1,\"totalTokens\":2}}}");
        var result = CodexAppServerProcessSession.NormalizeNotification("thread/tokenUsage/updated", document.RootElement)!;
        Assert.Equal("CODEX_USAGE_INVALID", result.ErrorCode);
        Assert.Null(result.Usage);
    }

    private static CodexAiClient Client(FakeSession session) => new(new FakeFactory(session));
    private static AiProviderRequest Request() => new("model", [new(AiMessageRole.User, "x")], AiRequestKind.Task,
        AiReasoningEffort.None, "", [], null, MaximumOutputTokens: 2048, MaximumResponseBytes: 1024);
    private static CodexProtocolEvent Usage(long input, long output, long total) => new("usage", Usage: new("thread", "turn", input, output, total));
    private static CodexProtocolEvent Reply() => new("reply", Reply: "done");
    private static CodexProtocolEvent Terminal() => new("terminal", Status: "completed", ThreadId: "thread", TurnId: "turn");

    private sealed class FakeFactory(FakeSession session) : ICodexAppServerFactory
    {
        public bool BlockDiscovery { get; init; }
        public int CreateCalls { get; private set; }
        public Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<IReadOnlyList<CodexModelDescriptor>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            if (BlockDiscovery) await Task.Delay(Timeout.Infinite, cancellationToken);
            return [new("model", "model", "", [], "", true)];
        }
        public Task<ICodexAppServerSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult<ICodexAppServerSession>(session);
        }
    }
    private sealed class FakeSession(IReadOnlyList<CodexProtocolEvent> events) : ICodexAppServerSession
    {
        private AiToolExecutor? executor;
        public Action? Cancel { get; init; }
        public string? ResumedThread { get; private set; }
        public List<string> Lifecycle { get; } = [];
        public Task<CodexTurnStartResult> StartTurnAsync(string? thread, string message, CancellationToken cancellationToken = default)
        {
            ResumedThread = thread;
            return Task.FromResult(new CodexTurnStartResult("thread", "turn", "model", "codex", "running"));
        }
        public Task<CodexTurnStartResult> StartTurnAsync(string? thread, string message, CodexTurnSettings settings, CancellationToken cancellationToken = default)
        {
            executor = settings.ToolExecutor;
            return StartTurnAsync(thread, message, cancellationToken);
        }
        public async IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var value in events)
            {
                if (value.Type == "cancel") Cancel!();
                cancellationToken.ThrowIfCancellationRequested();
                if (value.Type == "fail") throw new CodexBridgeException("CODEX_PROCESS_EXITED", "Process failed.");
                if (value.Type == "callbacks")
                    await Task.WhenAll(Enumerable.Range(0, 8).Select(index => executor!(new($"call.{index}", "read", "{}"), cancellationToken)));
                yield return value;
                await Task.Yield();
            }
        }
        public Task RespondApprovalAsync(string request, string decision, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken = default) { Lifecycle.Add("interrupt"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Lifecycle.Add("dispose"); return ValueTask.CompletedTask; }
    }
}
