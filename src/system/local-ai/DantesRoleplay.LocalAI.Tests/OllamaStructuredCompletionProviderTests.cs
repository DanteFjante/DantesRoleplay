using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Tests;

public sealed class OllamaStructuredCompletionProviderTests
{
    private const string Schema = """
        {"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}
        """;

    [Fact]
    public async Task Disabled_provider_never_calls_ollama()
    {
        var handler = new AsyncHandler((_, _) => throw new InvalidOperationException("HTTP must not run"));
        var provider = new OllamaStructuredCompletionProvider(new HttpClient(handler), new()
        {
            AllowedTaskClasses = Tasks()
        });

        var result = await provider.CompleteAsync(Request("question"));

        Assert.False(result.Ok);
        Assert.Equal("LOCAL_MODEL_DISABLED", result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Valid_request_is_schema_bound_nonthinking_and_has_no_tools()
    {
        string? chatBody = null;
        var handler = ValidHandler(async request =>
        {
            chatBody = await request.Content!.ReadAsStringAsync();
            return Chat("{\"value\":\"answer\"}");
        });
        var provider = Provider(handler);

        var result = await provider.CompleteAsync(Request("question"));

        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.Equal("digest-8b", result.Identity!.Revision);
        Assert.Equal("standard", result.Identity.Profile);
        Assert.Equal(3, handler.Calls);
        using var body = JsonDocument.Parse(chatBody!);
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("format").ValueKind);
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Unsupported_task_and_schema_mismatch_fail_closed()
    {
        var unsupportedHandler = ValidHandler(_ => Task.FromResult(Chat("{\"value\":\"answer\"}")));
        var unsupported = await Provider(unsupportedHandler).CompleteAsync(
            Request("question") with { TaskClass = "write.anything" });
        Assert.Equal("LOCAL_MODEL_REQUEST_INVALID", unsupported.ErrorCode);
        Assert.Equal(0, unsupportedHandler.Calls);

        var mismatchHandler = ValidHandler(_ => Task.FromResult(Chat("{\"value\":12}")));
        var mismatch = await Provider(mismatchHandler).CompleteAsync(Request("question"));
        Assert.False(mismatch.Ok);
        Assert.Equal("LOCAL_MODEL_SCHEMA_MISMATCH", mismatch.ErrorCode);
    }

    [Fact]
    public async Task Waiting_interactive_call_overtakes_waiting_background_call()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        var handler = ValidHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var value = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            order.Enqueue(value);
            if (value == "first-background")
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
            return Chat("{\"value\":\"answer\"}");
        });
        var provider = Provider(handler);
        Assert.True((await provider.CheckAsync()).Ready);

        var first = provider.CompleteAsync(Request("first-background") with { Priority = LocalModelPriority.Background });
        await firstStarted.Task;
        var second = provider.CompleteAsync(Request("second-background") with { Priority = LocalModelPriority.Background });
        var interactive = provider.CompleteAsync(Request("interactive"));
        releaseFirst.SetResult();
        await Task.WhenAll(first, second, interactive);

        Assert.Equal(["first-background", "interactive", "second-background"], order);
    }

    [Fact]
    public async Task Full_request_queue_fails_closed_as_saturated()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = ValidHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var value = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            if (value == "first")
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
            return Chat("{\"value\":\"answer\"}");
        });
        var provider = Provider(handler);
        Assert.True((await provider.CheckAsync()).Ready);
        using var cancellation = new CancellationTokenSource();

        var first = provider.CompleteAsync(Request("first"));
        await firstStarted.Task;
        var queued = Enumerable.Range(0, 32)
            .Select(index => provider.CompleteAsync(Request($"queued-{index}"), cancellation.Token))
            .ToArray();

        var saturated = await provider.CompleteAsync(Request("overflow"));

        Assert.False(saturated.Ok);
        Assert.Equal("LOCAL_MODEL_SATURATED", saturated.ErrorCode);
        cancellation.Cancel();
        releaseFirst.SetResult();
        Assert.True((await first).Ok);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Task.WhenAll(queued));
    }

    [Fact]
    public async Task Live_qwen3_8b_returns_schema_valid_no_tools_output_when_enabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION"),
                "1",
                StringComparison.Ordinal)) return;
        var provider = new OllamaStructuredCompletionProvider(new HttpClient(), new()
        {
            Enabled = true,
            Model = "qwen3:8b",
            Timeout = TimeSpan.FromMinutes(2),
            AllowedTaskClasses = Tasks()
        });

        var result = await provider.CompleteAsync(Request("Return the word ready."));

        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        using var output = JsonDocument.Parse(result.Json);
        Assert.False(string.IsNullOrWhiteSpace(output.RootElement.GetProperty("value").GetString()));
    }

    private static StructuredCompletionRequest Request(string prompt) =>
        new("test.schema", "Return schema-valid JSON.", prompt, Schema);

    private static OllamaStructuredCompletionProvider Provider(AsyncHandler handler) =>
        new(new HttpClient(handler), new()
        {
            Enabled = true,
            Model = "qwen3:8b",
            MaxConcurrentRequests = 1,
            ReadinessCache = TimeSpan.FromMinutes(1),
            AllowedTaskClasses = Tasks()
        });

    private static IReadOnlySet<string> Tasks() =>
        new HashSet<string>(StringComparer.Ordinal) { "test.schema" };

    private static AsyncHandler ValidHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> chat) =>
        new(async (request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json(new { models = new[] { new { name = "qwen3:8b", model = "qwen3:8b", digest = "digest-8b" } } }),
            "/api/show" => Json(new { capabilities = new[] { "completion", "tools", "thinking" } }),
            "/api/chat" => await chat(request),
            _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri.AbsolutePath}")
        });

    private static HttpResponseMessage Chat(string content) => Json(new
    {
        model = "qwen3:8b",
        message = new { role = "assistant", content },
        done = true,
        prompt_eval_count = 10,
        eval_count = 5
    });

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return response(request, cancellationToken);
        }
    }
}
