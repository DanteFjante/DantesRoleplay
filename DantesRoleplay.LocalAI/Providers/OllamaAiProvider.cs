using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.AI;
using DantesRoleplay.Retrieval;
using Json.Schema;

namespace DantesRoleplay.AI.Ollama;

/// <summary>
/// Ollama adapter for model discovery, ordinary or reasoning-enabled messages, structured output,
/// and native function-tool calls. It also implements the former closed structured-completion
/// seam while existing orchestration components migrate to <see cref="IAiService"/>.
/// </summary>
public sealed class OllamaAiProvider(HttpClient http, OllamaCompletionOptions options)
    : ILocalStructuredCompletionProvider, IAiProvider
{
    private readonly SemaphoreSlim _readinessGate = new(1, 1);
    private readonly PriorityLimiter _limiter = new(options.MaxConcurrentRequests);
    private LocalModelStatus? _cachedStatus;
    private DateTimeOffset _cachedUntil;

    public AiProviderInfo Info { get; } = new("ollama", "Ollama");

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || options.ValidateProviderSettings() is not null) return [];
        try
        {
            using var timeout = Timeout(cancellationToken);
            var tags = await GetAsync<TagsResponse>("api/tags", timeout.Token);
            var result = new List<AiModel>();
            foreach (var installed in (tags.Models ?? []).Take(100))
            {
                var id = string.IsNullOrWhiteSpace(installed.Model) ? installed.Name : installed.Model;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var show = await PostAsync<ShowRequest, ShowResponse>("api/show", new(id), timeout.Token);
                var capabilities = show.Capabilities ?? [];
                if (!capabilities.Contains("completion", StringComparer.Ordinal)) continue;
                var flags = AiModelCapabilities.Messages | AiModelCapabilities.Tasks |
                            AiModelCapabilities.StructuredOutput;
                var efforts = new List<AiReasoningEffort> { AiReasoningEffort.None };
                if (capabilities.Contains("thinking", StringComparer.Ordinal))
                {
                    flags |= AiModelCapabilities.Reasoning;
                    efforts.Add(AiReasoningEffort.Medium);
                }
                if (capabilities.Contains("tools", StringComparer.Ordinal))
                    flags |= AiModelCapabilities.Tools;
                if (capabilities.Contains("vision", StringComparer.Ordinal))
                    flags |= AiModelCapabilities.Images;
                result.Add(new(
                    "ollama",
                    id,
                    id,
                    flags,
                    efforts,
                    installed.Digest ?? "",
                    string.Equals(id, options.Model, StringComparison.Ordinal)));
            }
            return result.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException)
        {
            return [];
        }
    }

    public async Task<AiProviderResponse> SendAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Model) || request.Messages is null or { Count: 0 } ||
            request.MaximumOutputTokens is < 1 or > 131_072 ||
            request.Messages.Any(message => message is null || message.Content.Length > options.MaxPromptCharacters) ||
            request.Messages.Sum(message => message.Content.Length) > options.MaxPromptCharacters)
            return AiProviderResponse.Failure("OLLAMA_REQUEST_INVALID", "The Ollama request is invalid or exceeds its configured bounds.");
        var invalid = options.ValidateProviderSettings();
        if (!options.Enabled)
            return AiProviderResponse.Failure("OLLAMA_DISABLED", "Ollama is disabled.");
        if (invalid is not null)
            return AiProviderResponse.Failure("OLLAMA_CONFIG_INVALID", invalid);

        JsonElement? format = null;
        if (!string.IsNullOrWhiteSpace(request.ResponseSchemaJson))
        {
            try
            {
                using var schema = JsonDocument.Parse(request.ResponseSchemaJson);
                format = schema.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                return AiProviderResponse.Failure("OLLAMA_SCHEMA_INVALID", Safe(exception.Message));
            }
        }

        try
        {
            using var timeout = Timeout(cancellationToken);
            await using var lease = await _limiter.AcquireAsync(
                AiRequestKinds.IsBackground(request.Kind) ? LocalModelPriority.Background : LocalModelPriority.Interactive,
                timeout.Token);
            var models = await ListModelsAsync(timeout.Token);
            var model = models.SingleOrDefault(value => string.Equals(value.Id, request.Model, StringComparison.Ordinal));
            if (model is null)
                return AiProviderResponse.Failure("OLLAMA_MODEL_UNAVAILABLE", $"Ollama model '{request.Model}' is not available.");
            if (request.Reasoning != AiReasoningEffort.None &&
                !model.Capabilities.HasFlag(AiModelCapabilities.Reasoning))
                return AiProviderResponse.Failure("OLLAMA_REASONING_UNSUPPORTED", "The selected Ollama model does not support reasoning.");
            if (request.Tools.Count > 0 && !model.Capabilities.HasFlag(AiModelCapabilities.Tools))
                return AiProviderResponse.Failure("OLLAMA_TOOLS_UNSUPPORTED", "The selected Ollama model does not support tools.");
            if (request.Messages.Any(message => message.Media is { Count: > 0 }) &&
                !model.Capabilities.HasFlag(AiModelCapabilities.Images))
                return AiProviderResponse.Failure("OLLAMA_IMAGES_UNSUPPORTED", "The selected Ollama model does not support image input.");

            var messages = ConvertMessages(request.Messages);
            var tools = ConvertTools(request.Tools);
            var response = await PostAsync<AiChatRequest, AiChatResponse>(
                "api/chat",
                new(
                    request.Model,
                    messages,
                    false,
                    request.Reasoning != AiReasoningEffort.None,
                    format,
                    tools,
                    new(0, Math.Min(request.MaximumOutputTokens, options.MaxOutputTokens)),
                    options.KeepAlive),
                timeout.Token);
            if (!response.Done || response.Message is null ||
                !string.Equals(response.Model, request.Model, StringComparison.Ordinal))
                return AiProviderResponse.Failure("OLLAMA_RESPONSE_INVALID", "Ollama returned an incomplete or mismatched response.");
            var calls = (response.Message.ToolCalls ?? []).Select((call, index) => new AiToolCall(
                $"ollama-{Guid.NewGuid():n}-{index}",
                call.Function.Name,
                call.Function.Arguments.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : call.Function.Arguments.GetRawText())).ToArray();
            var content = response.Message.Content ?? "";
            if (calls.Length == 0 && string.IsNullOrWhiteSpace(content))
                return AiProviderResponse.Failure("OLLAMA_RESPONSE_INVALID", "Ollama returned no message or tool call.");
            if (content.Length > options.MaxResponseCharacters || response.EvalCount > options.MaxOutputTokens)
                return AiProviderResponse.Failure("OLLAMA_OUTPUT_BUDGET_EXCEEDED", "Ollama returned an oversized response.");
            return new(
                true,
                model,
                content,
                format is null ? "" : content,
                calls,
                response.PromptEvalCount,
                response.EvalCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiProviderResponse.Failure("OLLAMA_TIMEOUT", "Ollama did not answer before the configured timeout.");
        }
        catch (OperationCanceledException) { throw; }
        catch (LocalModelSaturatedException)
        {
            return AiProviderResponse.Failure("OLLAMA_SATURATED", "The Ollama request queue is full.");
        }
        catch (HttpRequestException exception)
        {
            return AiProviderResponse.Failure("OLLAMA_UNAVAILABLE", Safe(exception.Message));
        }
        catch (JsonException exception)
        {
            return AiProviderResponse.Failure("OLLAMA_RESPONSE_INVALID", Safe(exception.Message));
        }
    }

    public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        StatusAsync(forceRefresh: true, cancellationToken);

    public async Task<StructuredCompletionResult> CompleteAsync(
        StructuredCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (request is null ||
            !options.AllowedTaskClasses.Contains(request.TaskClass) ||
            string.IsNullOrWhiteSpace(request.SystemPrompt) ||
            string.IsNullOrWhiteSpace(request.UserPrompt) ||
            request.SystemPrompt.Length + request.UserPrompt.Length > options.MaxPromptCharacters ||
            string.IsNullOrWhiteSpace(request.ResponseSchema) ||
            request.ResponseSchema.Length > 20_000)
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_REQUEST_INVALID",
                "The structured completion request exceeds its closed task, prompt, or schema bounds.");

        JsonSchema schema;
        JsonElement schemaElement;
        try
        {
            schema = JsonSchema.FromText(request.ResponseSchema);
            using var schemaDocument = JsonDocument.Parse(request.ResponseSchema);
            schemaElement = schemaDocument.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException)
        {
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_SCHEMA_INVALID", Safe(exception.Message));
        }

        try
        {
            using var timeout = Timeout(cancellationToken);
            await using var lease = await _limiter.AcquireAsync(request.Priority, timeout.Token);
            var status = await StatusAsync(forceRefresh: false, timeout.Token);
            if (!status.Ready || status.Identity is null)
                return StructuredCompletionResult.Failure(
                    status.ErrorCode, status.ErrorMessage, stopwatch.ElapsedMilliseconds);

            var response = await PostAsync<ChatRequest, ChatResponse>(
                "api/chat",
                new(
                    options.Model,
                    [new("system", request.SystemPrompt), new("user", request.UserPrompt)],
                    false,
                    false,
                    schemaElement,
                    new(0, options.MaxOutputTokens),
                    options.KeepAlive),
                timeout.Token);
            if (!response.Done || response.Message is null ||
                !string.Equals(response.Model, options.Model, StringComparison.Ordinal))
                return StructuredCompletionResult.Failure(
                    "LOCAL_MODEL_RESPONSE_INVALID",
                    "Ollama returned an incomplete response or a different model.",
                    stopwatch.ElapsedMilliseconds);
            if (string.IsNullOrWhiteSpace(response.Message.Content) ||
                response.Message.Content.Length > options.MaxResponseCharacters ||
                response.EvalCount > options.MaxOutputTokens)
                return StructuredCompletionResult.Failure(
                    "LOCAL_MODEL_OUTPUT_BUDGET_EXCEEDED",
                    "Ollama returned an empty or oversized response.",
                    stopwatch.ElapsedMilliseconds);

            using var output = JsonDocument.Parse(response.Message.Content);
            if (output.RootElement.ValueKind != JsonValueKind.Object ||
                !schema.Evaluate(output.RootElement).IsValid)
                return StructuredCompletionResult.Failure(
                    "LOCAL_MODEL_SCHEMA_MISMATCH",
                    "Ollama output does not match the requested response schema.",
                    stopwatch.ElapsedMilliseconds);
            return new(
                status.Identity,
                output.RootElement.GetRawText(),
                stopwatch.ElapsedMilliseconds,
                response.PromptEvalCount,
                response.EvalCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_TIMEOUT", "Ollama did not answer before the configured timeout.", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (LocalModelSaturatedException)
        {
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_SATURATED", "The local model request queue is full.", stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_UNAVAILABLE", Safe(exception.Message), stopwatch.ElapsedMilliseconds);
        }
        catch (JsonException exception)
        {
            return StructuredCompletionResult.Failure(
                "LOCAL_MODEL_RESPONSE_INVALID", Safe(exception.Message), stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<LocalModelStatus> StatusAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _readinessGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cachedUntil)
                return _cachedStatus;
            var status = await CheckCoreAsync(cancellationToken);
            _cachedStatus = status;
            _cachedUntil = DateTimeOffset.UtcNow.Add(options.ReadinessCache);
            return status;
        }
        finally { _readinessGate.Release(); }
    }

    private async Task<LocalModelStatus> CheckCoreAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return LocalModelStatus.Unavailable("LOCAL_MODEL_DISABLED", "The local completion model is disabled.");
        var invalid = options.Validate();
        if (invalid is not null)
            return LocalModelStatus.Unavailable("LOCAL_MODEL_CONFIG_INVALID", invalid);
        try
        {
            using var timeout = Timeout(cancellationToken);
            var tags = await GetAsync<TagsResponse>("api/tags", timeout.Token);
            var installed = (tags.Models ?? []).FirstOrDefault(model =>
                string.Equals(model.Name, options.Model, StringComparison.Ordinal) ||
                string.Equals(model.Model, options.Model, StringComparison.Ordinal));
            if (installed is null)
                return LocalModelStatus.Unavailable(
                    "LOCAL_MODEL_MISSING", $"Configured Ollama model '{options.Model}' is not installed.");
            if (string.IsNullOrWhiteSpace(installed.Digest))
                return LocalModelStatus.Unavailable(
                    "LOCAL_MODEL_RESPONSE_INVALID", "Ollama did not report an installed-model digest.");
            var show = await PostAsync<ShowRequest, ShowResponse>(
                "api/show", new(options.Model), timeout.Token);
            if (!(show.Capabilities ?? []).Contains("completion", StringComparer.Ordinal))
                return LocalModelStatus.Unavailable(
                    "LOCAL_MODEL_CAPABILITY_MISSING",
                    $"Configured Ollama model '{options.Model}' does not report completion capability.");
            return new(true, new("ollama", options.Model, installed.Digest, options.Profile));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LocalModelStatus.Unavailable("LOCAL_MODEL_TIMEOUT", "Ollama readiness timed out.");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException exception)
        {
            return LocalModelStatus.Unavailable("LOCAL_MODEL_UNAVAILABLE", Safe(exception.Message));
        }
        catch (JsonException exception)
        {
            return LocalModelStatus.Unavailable("LOCAL_MODEL_RESPONSE_INVALID", Safe(exception.Message));
        }
    }

    private CancellationTokenSource Timeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(options.Timeout);
        return source;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(options.Endpoint, path), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new JsonException("Ollama returned an empty JSON response.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri(options.Endpoint, path), request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new JsonException("Ollama returned an empty JSON response.");
    }

    private static string Safe(string value) => value.Length <= 500 ? value : value[..500];

    private static IReadOnlyList<AiChatMessage> ConvertMessages(IReadOnlyList<AiMessage> messages)
    {
        var toolNames = messages
            .SelectMany(message => message.ToolCalls ?? [])
            .ToDictionary(call => call.Id, call => call.Name, StringComparer.Ordinal);
        return messages.Select(message => new AiChatMessage(
            message.Role switch
            {
                AiMessageRole.System => "system",
                AiMessageRole.User => "user",
                AiMessageRole.Assistant => "assistant",
                AiMessageRole.Tool => "tool",
                _ => throw new ArgumentOutOfRangeException(nameof(message.Role))
            },
            message.Content,
            message.ToolCalls?.Select(call => new AiChatToolCall(
                new(call.Name, ParseArguments(call.ArgumentsJson)))).ToArray(),
            message.Role == AiMessageRole.Tool && toolNames.TryGetValue(message.ToolCallId, out var toolName)
                ? toolName : null,
            message.Media?.Select(media => media.Base64Data).ToArray())).ToArray();
    }

    private static IReadOnlyList<AiChatTool>? ConvertTools(IReadOnlyList<AiToolDefinition> tools)
    {
        if (tools.Count == 0) return null;
        return tools.Select(tool => new AiChatTool("function", new(
            tool.Name, tool.Description, ParseArguments(tool.InputSchemaJson)))).ToArray();
    }

    private static JsonElement ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] IReadOnlyList<TagModel>? Models);
    private sealed record TagModel(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("digest")] string Digest);
    private sealed record ShowRequest([property: JsonPropertyName("model")] string Model);
    private sealed record ShowResponse(
        [property: JsonPropertyName("capabilities")] IReadOnlyList<string>? Capabilities);
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("format")] JsonElement Format,
        [property: JsonPropertyName("options")] ChatOptions Options,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);
    private sealed record ChatResponse(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount);

    private sealed record AiChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<AiChatToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ToolName = null,
        [property: JsonPropertyName("images"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Images = null);
    private sealed record AiChatFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] JsonElement Arguments);
    private sealed record AiChatToolCall(
        [property: JsonPropertyName("function")] AiChatFunction Function);
    private sealed record AiChatTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] AiChatToolDefinition Function);
    private sealed record AiChatToolDefinition(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);
    private sealed record AiChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<AiChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonElement? Format,
        [property: JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<AiChatTool>? Tools,
        [property: JsonPropertyName("options")] ChatOptions Options,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);
    private sealed record AiChatResponse(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("message")] AiChatMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount);

    private sealed class PriorityLimiter(int maximum)
    {
        private const int MaximumQueuedRequests = 32;
        private readonly object _sync = new();
        private readonly Queue<TaskCompletionSource<Lease>> _interactive = new();
        private readonly Queue<TaskCompletionSource<Lease>> _background = new();
        private int _active;

        public ValueTask<Lease> AcquireAsync(LocalModelPriority priority, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_active < maximum)
                {
                    _active++;
                    return ValueTask.FromResult(new Lease(this));
                }
                if (_interactive.Count + _background.Count >= MaximumQueuedRequests)
                    throw new LocalModelSaturatedException();
                var completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
                (priority == LocalModelPriority.Interactive ? _interactive : _background).Enqueue(completion);
                if (cancellationToken.CanBeCanceled)
                    cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                return new(completion.Task);
            }
        }

        private void Release()
        {
            lock (_sync)
            {
                while (TryTake(_interactive) || TryTake(_background)) return;
                _active--;
            }
        }

        private bool TryTake(Queue<TaskCompletionSource<Lease>> queue)
        {
            while (queue.TryDequeue(out var completion))
                if (completion.TrySetResult(new Lease(this))) return true;
            return false;
        }

        public sealed class Lease(PriorityLimiter owner) : IAsyncDisposable
        {
            private PriorityLimiter? _owner = owner;
            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class LocalModelSaturatedException : Exception { }
}
