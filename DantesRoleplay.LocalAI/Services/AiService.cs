using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Json.Schema;

namespace DantesRoleplay.AI;

public sealed partial class AiService : IAiService
{
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;
    private readonly IReadOnlyDictionary<string, IAiTool> _tools;

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

        IReadOnlyDictionary<string, IAiTool> available;
        try { available = MergeTools(_tools, UniqueTools(authorizedTools)); }
        catch (ArgumentException exception)
        {
            return AiResponse.Failure("AI_TOOL_INVALID", Bound(exception.Message));
        }

        var allowed = request.AllowedTools ?? authorizedTools.Select(value => value.Definition.Name).ToArray();
        return await SendRequestCoreAsync(request with { AllowedTools = allowed }, available, profile, cancellationToken);
    }

    private async Task<AiResponse> SendRequestCoreAsync(
        AiRequest request,
        IReadOnlyDictionary<string, IAiTool> availableTools,
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
        AiResponse Failure(string code, string message)
        {
            Activity("request", "failed", message, errorCode: code);
            return AiResponse.Failure(code, message) with { Activities = activities.ToArray() };
        }
        var promptTokens = 0;
        var outputTokens = 0;
        for (var round = 0; round <= request.MaximumToolRounds; round++)
        {
            AiToolExecutor executor = async (call, token) =>
            {
                Activity("tool-call", "requested", $"The assistant requested direct tool '{call.Name}'.", call);
                var result = await InvokeToolAsync(call, request.Kind, selectedTools, token);
                if (result.Ok && result.Media is { Count: > 0 })
                    attachedMedia.AddRange(result.Media);
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
                request.MaximumOutputTokens), cancellationToken);
            if (!result.Ok)
                return Failure(
                    string.IsNullOrWhiteSpace(result.ErrorCode) ? "AI_PROVIDER_FAILED" : result.ErrorCode,
                    string.IsNullOrWhiteSpace(result.ErrorMessage) ? "The AI provider did not return a result." : result.ErrorMessage);

            promptTokens += result.PromptTokens;
            outputTokens += result.OutputTokens;
            if (result.ToolCalls.Count == 0)
                return Complete(result, observedCalls, promptTokens, outputTokens, responseSchema,
                    activities, attachedMedia, Activity);
            if (selectedTools.Count == 0)
                return Failure("AI_TOOL_CALL_UNEXPECTED", "The provider returned a tool call when no tools were allowed.");
            if (round == request.MaximumToolRounds)
                return Failure("AI_TOOL_ROUND_LIMIT", "The AI did not finish within the configured tool-call limit.");

            observedCalls.AddRange(result.ToolCalls);
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
                        with { Activities = activities.ToArray() };
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
                    with { Activities = activities.ToArray() };
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
        IReadOnlyDictionary<string, IAiTool> tools,
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
            var schema = JsonSchema.FromText(tool.Definition.InputSchemaJson,
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            if (!schema.Evaluate(arguments.RootElement).IsValid)
                return AiToolResult.Failure("AI_TOOL_ARGUMENTS_INVALID", "Tool arguments do not match the declared schema.");
            return await tool.InvokeAsync(
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

    private IReadOnlyDictionary<string, IAiTool> SelectTools(
        IReadOnlyList<string>? allowed,
        IReadOnlyDictionary<string, IAiTool> available,
        out AiResponse? failure)
    {
        failure = null;
        if (allowed is null or { Count: 0 }) return new Dictionary<string, IAiTool>(StringComparer.Ordinal);
        var selected = new Dictionary<string, IAiTool>(StringComparer.Ordinal);
        foreach (var name in allowed.Distinct(StringComparer.Ordinal))
        {
            if (!available.TryGetValue(name, out var tool))
            {
                failure = AiResponse.Failure("AI_TOOL_UNKNOWN", $"AI tool '{name}' is not registered.");
                return new Dictionary<string, IAiTool>(StringComparer.Ordinal);
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
            request.MaximumToolRounds is < 0 or > 16 || request.MaximumOutputTokens is < 1 or > 131_072)
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

    private static IReadOnlyDictionary<string, IAiTool> UniqueTools(IEnumerable<IAiTool> values)
    {
        var result = new Dictionary<string, IAiTool>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || !ToolName().IsMatch(value.Definition.Name) ||
                string.IsNullOrWhiteSpace(value.Definition.Description) ||
                string.IsNullOrWhiteSpace(value.Definition.InputSchemaJson) ||
                !result.TryAdd(value.Definition.Name, value))
                throw new ArgumentException("AI tools must have unique valid names, descriptions, and schemas.", nameof(values));
            _ = JsonSchema.FromText(value.Definition.InputSchemaJson,
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        }
        return result;
    }

    private static IReadOnlyDictionary<string, IAiTool> MergeTools(
        IReadOnlyDictionary<string, IAiTool> registered,
        IReadOnlyDictionary<string, IAiTool> authorized)
    {
        var result = new Dictionary<string, IAiTool>(registered, StringComparer.Ordinal);
        foreach (var (name, tool) in authorized)
            if (!result.TryAdd(name, tool) && !ReferenceEquals(result[name], tool))
                throw new ArgumentException($"AI tool '{name}' has conflicting registrations.", nameof(authorized));
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
        IEnumerable<IAiTool> tools)
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

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentId();
}
