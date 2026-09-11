using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Json.Schema;

namespace DantesRoleplay.AI;

public sealed partial class AiService : IAiService
{
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;
    private readonly IReadOnlyDictionary<string, PreparedAiTool> _tools;
    private long _toolSchemaCompilations;
    private long _toolSchemaCompilationAllocatedBytes;

    public AiService(IEnumerable<IAiProvider> providers, IEnumerable<IAiTool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = UniqueProviders(providers);
        _tools = UniqueTools(tools ?? []);
    }

    public IReadOnlyList<AiProviderInfo> ListProviders() =>
        _providers.Values.Select(value => value.Info)
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(
        string provider,
        CancellationToken cancellationToken = default) =>
        ResolveProvider(provider, out var resolved, out var failure)
            ? resolved!.ListModelsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<AiModel>>([]);

    public Task<AiResponse> SendMessageAsync(
        string provider,
        string model,
        IReadOnlyList<AiMessage> messages,
        AiReasoningEffort reasoning = AiReasoningEffort.None,
        IReadOnlyList<string>? allowedTools = null,
        CancellationToken cancellationToken = default) =>
        SendRequestAsync(new(provider, model, messages, Reasoning: reasoning, AllowedTools: allowedTools), cancellationToken);

    public Task<AiResponse> SendTaskAsync(
        string provider,
        string model,
        string task,
        AiReasoningEffort reasoning = AiReasoningEffort.None,
        IReadOnlyList<string>? allowedTools = null,
        CancellationToken cancellationToken = default) =>
        SendRequestAsync(new(
            provider,
            model,
            [new(AiMessageRole.User, task)],
            AiRequestKind.Task,
            reasoning,
            AllowedTools: allowedTools), cancellationToken);

    public async Task<AiResponse> SendRequestAsync(
        AiRequest request,
        CancellationToken cancellationToken = default) =>
        await SendRequestCoreAsync(request, _tools, null, cancellationToken);

    public async Task<AiResponse> SendAgentRequestAsync(
        AiAgentProfile profile,
        AiRequest request,
        IReadOnlyList<IAiTool> authorizedTools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizedTools);
        if (!ValidProfile(profile, out var invalid))
            return AiResponse.Failure("AI_AGENT_PROFILE_INVALID", invalid);

        IReadOnlyDictionary<string, PreparedAiTool> authorized;
        try
        {
            authorized = UniqueTools(authorizedTools);
        }
        catch (ArgumentException exception)
        {
            return AiResponse.Failure("AI_TOOL_INVALID", Bound(exception.Message));
        }

        var allowed = request.AllowedTools ?? authorized.Keys.ToArray();
        // Agent requests are scoped to the host-materialized tools. Static tools remain available
        // to direct requests, but naming one here cannot enlarge this invocation's authority.
        return await SendRequestCoreAsync(request with { AllowedTools = allowed }, authorized, profile, cancellationToken);
    }

    private async Task<AiResponse> SendRequestCoreAsync(
        AiRequest request,
        IReadOnlyDictionary<string, PreparedAiTool> availableTools,
        AiAgentProfile? profile,
        CancellationToken cancellationToken)
    {
        if (!ValidRequest(request, out var invalid))
            return AiResponse.Failure("AI_REQUEST_INVALID", invalid);
        if (!ResolveProvider(request.Provider, out var provider, out var providerFailure))
            return providerFailure!;

        var selectedTools = SelectTools(request.AllowedTools, availableTools, out var toolFailure);
        if (toolFailure is not null) return toolFailure;
        JsonSchema? responseSchema = null;
        if (!string.IsNullOrWhiteSpace(request.ResponseSchemaJson))
        {
            try
            {
                responseSchema = JsonSchema.FromText(request.ResponseSchemaJson,
                    new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            }
            catch (Exception exception) when (exception is JsonException or JsonSchemaException)
            {
                return AiResponse.Failure("AI_RESPONSE_SCHEMA_INVALID", Bound(exception.Message));
            }
        }

        var messages = request.Messages.ToList();
        if (profile is not null)
            messages.Insert(0, new(AiMessageRole.System, AgentSystemPrompt(profile, request, selectedTools.Values)));
        var observedCalls = new List<AiToolCall>();
        var attachedMedia = new List<AiMediaContent>();
        var activities = new List<AiExecutionActivity>();
        var activitySync = new object();
        var activitySequence = 0;
        void Activity(string kind, string status, string summary, AiToolCall? call = null,
            bool inputValidated = false, string errorCode = "")
        {
            lock (activitySync)
                activities.Add(new(++activitySequence, kind, status, Bound(summary), call?.Id ?? "",
                    call?.Name ?? "", inputValidated, errorCode));
        }
        var promptTokens = 0;
        var outputTokens = 0;
        long inputUsage = 0, outputUsage = 0, totalUsage = 0, responseBytes = 0;
        var hasUsage = false;
        var completeUsage = true;
        var toolAttempts = 0;
        var callLimitReached = 0;
        var executionClosed = 0;
        var callerCancellation = cancellationToken;
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestLifetime.CancelAfter(request.MaximumDuration ?? TimeSpan.FromMinutes(10));
        cancellationToken = requestLifetime.Token;
        AiTokenUsageEvidence? Usage() => hasUsage
            ? new(inputUsage, outputUsage, totalUsage, completeUsage) : null;
        void RecordUsage(AiTokenUsageEvidence? usage)
        {
            if (usage is null || usage.InputTokens < 0 || usage.OutputTokens < 0 || usage.TotalTokens < 0
                || usage.InputTokens > usage.TotalTokens || usage.OutputTokens > usage.TotalTokens
                || usage.InputTokens > usage.TotalTokens - usage.OutputTokens)
            {
                completeUsage = false;
                return;
            }
            try
            {
                var nextInput = checked(inputUsage + usage.InputTokens);
                var nextOutput = checked(outputUsage + usage.OutputTokens);
                var nextTotal = checked(totalUsage + usage.TotalTokens);
                inputUsage = nextInput;
                outputUsage = nextOutput;
                totalUsage = nextTotal;
                hasUsage = true;
                completeUsage &= usage.IsComplete;
            }
            catch (OverflowException) { completeUsage = false; }
        }
        AiResponse Failure(string code, string message)
        {
            Activity("request", "failed", message, errorCode: code);
            return AiResponse.Failure(code, message) with
            {
                PromptTokens = promptTokens, OutputTokens = outputTokens,
                ToolCalls = observedCalls.ToArray(), Activities = activities.ToArray(), Usage = Usage()
            };
        }
        void RejectedCallActivity(IEnumerable<AiToolCall> calls)
        {
            foreach (var call in calls)
                Activity("tool-call", "requested", $"The provider returned direct tool '{call.Name}'.", call);
        }
        try
        {
            for (var round = 0; round <= request.MaximumToolRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AiToolExecutor executor = async (call, token) =>
                {
                    using var callbackLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
                    token = callbackLifetime.Token;
                    token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref executionClosed) != 0)
                        return AiToolResult.Failure("AI_REQUEST_CLOSED", "The request is no longer executing.");
                    lock (activitySync)
                        if (!observedCalls.Contains(call)) observedCalls.Add(call);
                    Activity("tool-call", "requested", $"The assistant requested direct tool '{call.Name}'.", call);
                    if (Interlocked.Increment(ref toolAttempts) > request.MaximumToolCalls)
                    {
                        Interlocked.Exchange(ref callLimitReached, 1);
                        Activity("tool-call", "failed", "The total tool-call allowance is exhausted.", call,
                            errorCode: "AI_TOOL_CALL_LIMIT");
                        requestLifetime.Cancel();
                        return AiToolResult.Failure("AI_TOOL_CALL_LIMIT", "The total tool-call allowance is exhausted.");
                    }
                    var result = await InvokeToolAsync(call, request.Kind, selectedTools, token);
                    if (result.Ok && result.Media is { Count: > 0 })
                        lock (activitySync) attachedMedia.AddRange(result.Media);
                    var validated = result.ErrorCode is not ("AI_TOOL_UNKNOWN" or "AI_TOOL_ARGUMENTS_INVALID");
                    Activity("tool-call", result.Ok ? "completed" : "failed",
                        result.Ok ? $"Direct tool '{call.Name}' completed." : result.ErrorMessage,
                        call, validated, result.ErrorCode);
                    return result;
                };
                var result = await provider!.SendAsync(new(
                    request.Model,
                    messages,
                    request.Kind,
                    request.Reasoning,
                    request.ResponseSchemaJson,
                    selectedTools.Values.Select(value => value.Definition).ToArray(),
                    selectedTools.Count == 0 ? null : executor,
                    request.MaximumOutputTokens,
                    request.MaximumToolCalls,
                    request.MaximumResponseBytes,
                    request.MaximumDuration), cancellationToken);
                // Legacy counters are display-only; explicit long evidence owns accounting knownness.
                promptTokens = (int)Math.Min(int.MaxValue, (long)promptTokens + Math.Max(0, result.PromptTokens));
                outputTokens = (int)Math.Min(int.MaxValue, (long)outputTokens + Math.Max(0, result.OutputTokens));
                RecordUsage(result.Usage);
                // Preserve every provider-returned request in evidence, including calls rejected by a
                // provider failure, an empty authorization set, or the final round fence. Activities
                // remain owned by the executor so ordinary calls still have one "requested" record.
                lock (activitySync)
                    foreach (var call in result.ToolCalls)
                        if (!observedCalls.Contains(call)) observedCalls.Add(call);
                if (Volatile.Read(ref callLimitReached) != 0)
                    return Failure("AI_TOOL_CALL_LIMIT", "The total tool-call allowance is exhausted.");
                if (cancellationToken.IsCancellationRequested)
                {
                    completeUsage = false;
                    return Failure(callerCancellation.IsCancellationRequested ? "AI_REQUEST_CANCELLED" : "AI_REQUEST_TIMEOUT",
                        "The AI request was cancelled or exceeded its time limit.");
                }
                responseBytes += Encoding.UTF8.GetByteCount(result.Text);
                if (result.StructuredJson != result.Text)
                    responseBytes += Encoding.UTF8.GetByteCount(result.StructuredJson);
                if (responseBytes > request.MaximumResponseBytes)
                    return Failure("AI_RESPONSE_BYTE_LIMIT", "The response exceeds the host output-byte limit.");
                if (!result.Ok)
                {
                    RejectedCallActivity(result.ToolCalls);
                    return Failure(
                        string.IsNullOrWhiteSpace(result.ErrorCode) ? "AI_PROVIDER_FAILED" : result.ErrorCode,
                        string.IsNullOrWhiteSpace(result.ErrorMessage) ? "The AI provider did not return a result." : result.ErrorMessage);
                }

                if (result.ToolCalls.Count == 0)
                    return Complete(result, observedCalls, promptTokens, outputTokens, responseSchema,
                        activities, attachedMedia, Activity) with { Usage = Usage() };
                if (selectedTools.Count == 0)
                {
                    RejectedCallActivity(result.ToolCalls);
                    return Failure("AI_TOOL_CALL_UNEXPECTED", "The provider returned a tool call when no tools were allowed.");
                }
                if (round == request.MaximumToolRounds)
                {
                    RejectedCallActivity(result.ToolCalls);
                    return Failure("AI_TOOL_ROUND_LIMIT", "The AI did not finish within the configured tool-call limit.");
                }

                messages.Add(new(AiMessageRole.Assistant, result.Text, ToolCalls: result.ToolCalls));
                foreach (var call in result.ToolCalls)
                {
                    var toolResult = await executor(call, cancellationToken);
                    var content = toolResult.Ok
                        ? toolResult.Content
                        : JsonSerializer.Serialize(new { error = toolResult.ErrorCode, message = toolResult.ErrorMessage });
                    messages.Add(new(AiMessageRole.Tool, content, call.Id, Media: toolResult.Media));
                }
            }

            return Failure("AI_TOOL_ROUND_LIMIT", "The AI did not finish within the configured tool-call limit.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completeUsage = false;
            return Failure(Volatile.Read(ref callLimitReached) != 0 ? "AI_TOOL_CALL_LIMIT"
                : callerCancellation.IsCancellationRequested ? "AI_REQUEST_CANCELLED" : "AI_REQUEST_TIMEOUT",
                "The AI request stopped at its host execution limit.");
        }
        finally { Interlocked.Exchange(ref executionClosed, 1); }
    }

    private AiResponse Complete(
        AiProviderResponse result,
        IReadOnlyList<AiToolCall> calls,
        int promptTokens,
        int outputTokens,
        JsonSchema? schema,
        List<AiExecutionActivity> activities,
        IReadOnlyList<AiMediaContent> media,
        Action<string, string, string, AiToolCall?, bool, string> activity)
    {
        JsonElement? structured = null;
        var candidate = string.IsNullOrWhiteSpace(result.StructuredJson) ? result.Text : result.StructuredJson;
        if (schema is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (!schema.Evaluate(document.RootElement).IsValid)
                {
                    activity("validation", "failed", "The AI response does not match the requested schema.",
                        null, false, "AI_RESPONSE_SCHEMA_MISMATCH");
                    return AiResponse.Failure("AI_RESPONSE_SCHEMA_MISMATCH", "The AI response does not match the requested schema.")
                        with { PromptTokens = promptTokens, OutputTokens = outputTokens,
                            ToolCalls = calls.ToArray(), Activities = activities.ToArray() };
                }
                structured = document.RootElement.Clone();
                activity("validation", "completed", "The structured AI response passed its declared schema.",
                    null, true, "");
            }
            catch (JsonException)
            {
                activity("validation", "failed", "The AI response is not valid structured JSON.",
                    null, false, "AI_RESPONSE_SCHEMA_MISMATCH");
                return AiResponse.Failure("AI_RESPONSE_SCHEMA_MISMATCH", "The AI response is not valid structured JSON.")
                    with { PromptTokens = promptTokens, OutputTokens = outputTokens,
                        ToolCalls = calls.ToArray(), Activities = activities.ToArray() };
            }
        }
        activity("result", "completed", "The AI request completed.", null, schema is null || structured is not null, "");
        return new(true, result.Model, result.Text, structured, calls, promptTokens, outputTokens,
            result.ConversationId, ReasoningSummary: result.ReasoningSummary, Activities: activities.ToArray(),
            Media: media
                .GroupBy(value => $"{value.EntityId}\0{value.MediaId}\0{value.Sha256}", StringComparer.Ordinal)
                .Select(value => value.First()).ToArray());
    }

    private async Task<AiToolResult> InvokeToolAsync(
        AiToolCall call,
        AiRequestKind requestKind,
        IReadOnlyDictionary<string, PreparedAiTool> tools,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(call.Id) || !ToolName().IsMatch(call.Name) ||
            !tools.TryGetValue(call.Name, out var tool))
            return AiToolResult.Failure("AI_TOOL_UNKNOWN", "The requested tool is not allowed.");
        try
        {
            using var arguments = JsonDocument.Parse(call.ArgumentsJson);
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
                return AiToolResult.Failure("AI_TOOL_ARGUMENTS_INVALID", "Tool arguments must be a JSON object.");
            if (!tool.InputSchema.Evaluate(arguments.RootElement).IsValid)
                return AiToolResult.Failure("AI_TOOL_ARGUMENTS_INVALID", "Tool arguments do not match the declared schema.");
            return await tool.Tool.InvokeAsync(
                new(call.Id, call.Name, arguments.RootElement.Clone(), requestKind), cancellationToken);
        }
        catch (JsonException)
        {
            return AiToolResult.Failure("AI_TOOL_ARGUMENTS_INVALID", "Tool arguments are not valid JSON.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AiToolResult.Failure("AI_TOOL_FAILED", Bound(exception.Message));
        }
    }

    private IReadOnlyDictionary<string, PreparedAiTool> SelectTools(
        IReadOnlyList<string>? allowed,
        IReadOnlyDictionary<string, PreparedAiTool> available,
        out AiResponse? failure)
    {
        failure = null;
        if (allowed is null or { Count: 0 }) return new Dictionary<string, PreparedAiTool>(StringComparer.Ordinal);
        var selected = new Dictionary<string, PreparedAiTool>(StringComparer.Ordinal);
        foreach (var name in allowed.Distinct(StringComparer.Ordinal))
        {
            if (!available.TryGetValue(name, out var tool))
            {
                failure = AiResponse.Failure("AI_TOOL_UNKNOWN", $"AI tool '{name}' is not registered.");
                return new Dictionary<string, PreparedAiTool>(StringComparer.Ordinal);
            }
            selected.Add(name, tool);
        }
        return selected;
    }

    private bool ResolveProvider(string id, out IAiProvider? provider, out AiResponse? failure)
    {
        provider = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(id) || !_providers.TryGetValue(id, out provider))
        {
            failure = AiResponse.Failure("AI_PROVIDER_UNKNOWN", "The requested AI provider is not registered.");
            return false;
        }
        return true;
    }

    private static bool ValidRequest(AiRequest? request, out string error)
    {
        error = "";
        if (request is null || string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.Model) || request.Messages is null or { Count: 0 } ||
            request.Messages.Any(message => message is null || string.IsNullOrWhiteSpace(message.Content) &&
                (message.ToolCalls is null or { Count: 0 })) ||
            request.MaximumToolRounds is < 0 or > 16 || request.MaximumOutputTokens is < 1 or > 131_072 ||
            request.MaximumToolCalls is < 0 or > 16 || request.MaximumResponseBytes is < 1 or > 1_048_576 ||
            request.MaximumDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)))
        {
            error = "Provider, model, messages, and bounded execution limits are required.";
            return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, IAiProvider> UniqueProviders(IEnumerable<IAiProvider> values)
    {
        var result = new Dictionary<string, IAiProvider>(StringComparer.Ordinal);
        foreach (var value in values)
            if (value is null || string.IsNullOrWhiteSpace(value.Info.Id) || !result.TryAdd(value.Info.Id, value))
                throw new ArgumentException("AI providers must have unique, nonblank identifiers.", nameof(values));
        return result;
    }

    private IReadOnlyDictionary<string, PreparedAiTool> UniqueTools(IEnumerable<IAiTool> values)
    {
        var result = new Dictionary<string, PreparedAiTool>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null)
                throw new ArgumentException("AI tools must have unique valid names, descriptions, and schemas.", nameof(values));
            var definition = value.Definition;
            if (!ToolName().IsMatch(definition.Name) ||
                string.IsNullOrWhiteSpace(definition.Description) ||
                string.IsNullOrWhiteSpace(definition.InputSchemaJson) ||
                result.ContainsKey(definition.Name))
                throw new ArgumentException("AI tools must have unique valid names, descriptions, and schemas.", nameof(values));
            JsonSchema schema;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                schema = JsonSchema.FromText(definition.InputSchemaJson,
                    new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            }
            catch (Exception exception) when (exception is JsonException or JsonSchemaException)
            {
                throw new ArgumentException("AI tools must have unique valid names, descriptions, and schemas.",
                    nameof(values), exception);
            }
            finally
            {
                Interlocked.Increment(ref _toolSchemaCompilations);
                Interlocked.Add(ref _toolSchemaCompilationAllocatedBytes,
                    Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore));
            }
            result.Add(definition.Name, new(value, definition, schema));
        }
        return result;
    }

    private static bool ValidProfile(AiAgentProfile? profile, out string error)
    {
        error = "";
        if (profile is null || !AgentId().IsMatch(profile.Id) ||
            !BoundedRequired(profile.Name, 120) || !BoundedRequired(profile.Identity, 2_000) ||
            profile.Instructions is null || profile.Instructions.Length > 8_000)
        {
            error = "Agent id, name, identity, and bounded instructions are required.";
            return false;
        }
        return true;
    }

    private static string AgentSystemPrompt(
        AiAgentProfile profile,
        AiRequest request,
        IEnumerable<PreparedAiTool> tools)
    {
        var selected = tools.OrderBy(value => value.Definition.Name, StringComparer.Ordinal).ToArray();
        var prompt = new StringBuilder()
            .Append("You are ").Append(profile.Name).Append(" (agent id: ").Append(profile.Id).AppendLine(").")
            .AppendLine(profile.Identity.Trim())
            .Append("You are operating through provider '").Append(request.Provider)
            .Append("' with model '").Append(request.Model).AppendLine("'.")
            .AppendLine("You may interact with the application only by calling the direct in-process tools supplied with this request.")
            .AppendLine("Never invent tool results, claim an action succeeded before a successful tool result, or encode executable commands in prose.")
            .AppendLine("Tool failures are authoritative. A write tool may require trusted human confirmation; you cannot confirm your own write.")
            .AppendLine("When a response schema is supplied, return a final response that matches it exactly.");
        if (!string.IsNullOrWhiteSpace(profile.Instructions))
            prompt.AppendLine("Agent instructions:").AppendLine(profile.Instructions.Trim());
        prompt.AppendLine("Capabilities authorized for this request:");
        if (selected.Length == 0)
            prompt.AppendLine("- No system tools are authorized.");
        else
            foreach (var tool in selected)
                prompt.Append("- ").Append(tool.Definition.Name).Append(": ")
                    .AppendLine(Bound(tool.Definition.Description));
        return prompt.ToString();
    }

    private static bool BoundedRequired(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static string Bound(string value) => value.Length <= 500 ? value : value[..500];

    internal AiToolSchemaPreparationSnapshot ToolSchemaPreparation => new(
        Interlocked.Read(ref _toolSchemaCompilations),
        Interlocked.Read(ref _toolSchemaCompilationAllocatedBytes));

    private sealed record PreparedAiTool(
        IAiTool Tool,
        AiToolDefinition Definition,
        JsonSchema InputSchema);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentId();
}

internal sealed record AiToolSchemaPreparationSnapshot(long Compilations, long AllocatedBytes);
