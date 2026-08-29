using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class LocalInteractionPlanningProvider(
    ILocalStructuredCompletionProvider? inner,
    IInteractionOuterLocalCompletionProvider? outer = null)
    : IInteractionPlanningCompletionProvider
{
    public InteractionPlannerKind Kind => InteractionPlannerKind.Local;
    public InteractionProviderIsolation Isolation { get; } = new(true, true, true, true, true, true);

    public async Task<InteractionPlanningCompletionResult> CompleteAsync(
        InteractionPlanningCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RoleProfile.Role == InteractionAiRole.Inner && inner is null)
            return InteractionPlanningCompletionResult.Failure("LOCAL_MODEL_DISABLED", "The local inner planner provider is disabled.");
        if (request.RoleProfile.Role == InteractionAiRole.Outer && outer is null)
            return InteractionPlanningCompletionResult.Failure("LOCAL_OUTER_MODEL_DISABLED", "The local outer planner provider is disabled.");
        StructuredCompletionResult result;
        try
        {
            var completion = new StructuredCompletionRequest(
                InteractionPlannerProtocol.TaskClass, InteractionPlannerProtocol.SystemPrompt,
                request.ObservationJson, InteractionPlannerProtocol.ResponseSchema,
                LocalModelPriority.Interactive);
            result = request.RoleProfile.Role == InteractionAiRole.Outer
                ? await outer!.CompleteAsync(completion, cancellationToken)
                : await inner!.CompleteAsync(completion, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        if (!result.Ok || result.Identity is null)
            return InteractionPlanningCompletionResult.Failure(
                string.IsNullOrWhiteSpace(result.ErrorCode) ? "LOCAL_MODEL_UNAVAILABLE" : result.ErrorCode,
                string.IsNullOrWhiteSpace(result.ErrorMessage) ? "The local planner did not return a result." : result.ErrorMessage);
        if (Encoding.UTF8.GetByteCount(result.Json) > request.MaximumOutputBytes)
            return InteractionPlanningCompletionResult.Failure("LOCAL_MODEL_OUTPUT_BUDGET_EXCEEDED", "The local planner exceeded the host-owned output limit.");
        return new(new(
            InteractionPlannerKind.Local,
            result.Identity.Provider,
            result.Identity.Model,
            result.Identity.Revision,
            result.Identity.Profile), result.Json);
    }
}

public sealed class OpenAiInteractionPlanningOptions
{
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1/responses");
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(150);

    public string? Validate()
    {
        if (!Enabled) return "The remote interaction planner is disabled.";
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length > 2_000 || ApiKey.Any(char.IsControl))
            return "The remote interaction planner credential is unavailable.";
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps
            || !string.Equals(Endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
            || Endpoint.AbsolutePath.TrimEnd('/') != "/v1/responses"
            || !string.IsNullOrEmpty(Endpoint.Query) || !string.IsNullOrEmpty(Endpoint.Fragment))
            return "The remote interaction planner endpoint is not the closed OpenAI Responses endpoint.";
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(175))
            return "The remote interaction planner timeout must be positive and below the orchestration deadline.";
        return null;
    }
}

public sealed class OpenAiResponsesInteractionPlanningProvider(
    HttpClient http,
    OpenAiInteractionPlanningOptions options) : IInteractionPlanningCompletionProvider
{
    private const int MaximumResponseEnvelopeBytes = 256 * 1024;
    public InteractionPlannerKind Kind => InteractionPlannerKind.Remote;
    public InteractionProviderIsolation Isolation { get; } = new(true, true, true, true, true, true);

    public async Task<InteractionPlanningCompletionResult> CompleteAsync(
        InteractionPlanningCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invalid = options.Validate();
        if (invalid is not null)
            return InteractionPlanningCompletionResult.Failure(
                options.Enabled ? "REMOTE_MODEL_CONFIG_INVALID" : "REMOTE_MODEL_DISABLED", invalid);
        var expected = InteractionRoleProfile.For(request.RoleProfile.Role);
        try { InteractionRoleProfile.EnsureResumeCompatible(expected, request.RoleProfile); }
        catch (InteractionContractException)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_ROLE_PROFILE_MISMATCH", "The remote planner role profile changed.");
        }

        JsonElement schema;
        try
        {
            using var schemaDocument = JsonDocument.Parse(InteractionPlannerProtocol.ResponseSchema);
            schema = schemaDocument.RootElement.Clone();
        }
        catch (JsonException)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_SCHEMA_INVALID", "The fixed planner response schema is invalid.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = expected.Model,
            instructions = InteractionPlannerProtocol.SystemPrompt,
            input = request.ObservationJson,
            reasoning = new { effort = expected.ReasoningEffort },
            max_output_tokens = Math.Clamp(request.MaximumOutputBytes / 4, 64, 16_384),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = InteractionPlannerProtocol.ResponseSchemaName,
                    strict = true,
                    schema
                }
            },
            tools = Array.Empty<object>(),
            tool_choice = "none",
            parallel_tool_calls = false,
            store = false
        });

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return InteractionPlanningCompletionResult.Failure(
                    "REMOTE_MODEL_UNAVAILABLE", $"The remote planner returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaximumResponseEnvelopeBytes)
                return InteractionPlanningCompletionResult.Failure("REMOTE_RESPONSE_TOO_LARGE", "The remote planner response envelope is oversized.");
            var bytes = await ReadBoundedAsync(response.Content, MaximumResponseEnvelopeBytes, timeout.Token);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!TryString(root, "status", out var status) || status != "completed"
                || !TryString(root, "model", out var observedModel)
                || !(observedModel == expected.Model || observedModel.StartsWith(expected.Model + "-", StringComparison.Ordinal)))
                return InteractionPlanningCompletionResult.Failure("REMOTE_MODEL_RESPONSE_INVALID", "The remote planner returned an incomplete or mismatched response.");
            if (!TryOutputText(root, out var output, out var forbiddenOutput))
                return InteractionPlanningCompletionResult.Failure(
                    forbiddenOutput ? "REMOTE_MODEL_TOOL_OUTPUT_FORBIDDEN" : "REMOTE_MODEL_RESPONSE_INVALID",
                    forbiddenOutput ? "The remote planner returned a tool or non-text output." : "The remote planner returned no structured text output.");
            if (Encoding.UTF8.GetByteCount(output) > request.MaximumOutputBytes)
                return InteractionPlanningCompletionResult.Failure("REMOTE_MODEL_OUTPUT_BUDGET_EXCEEDED", "The remote planner exceeded the host-owned output limit.");
            return new(new(
                InteractionPlannerKind.Remote,
                "openai-responses",
                expected.Model,
                observedModel,
                "responses-v1",
                expected.ReasoningEffort), output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_MODEL_TIMEOUT", "The remote planner timed out.");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_MODEL_UNAVAILABLE", "The remote planner is unavailable.");
        }
        catch (JsonException)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_MODEL_RESPONSE_INVALID", "The remote planner returned malformed JSON.");
        }
        catch (InvalidDataException)
        {
            return InteractionPlanningCompletionResult.Failure("REMOTE_RESPONSE_TOO_LARGE", "The remote planner response envelope is oversized.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximum) throw new InvalidDataException();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = element.GetString()!);
    }

    private static bool TryOutputText(JsonElement root, out string text, out bool forbidden)
    {
        text = string.Empty;
        forbidden = false;
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return false;
        var values = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!TryString(item, "type", out var type)) { forbidden = true; continue; }
            if (type == "reasoning") continue;
            if (type != "message") { forbidden = true; continue; }
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (!TryString(part, "type", out var partType) || partType != "output_text"
                    || !TryString(part, "text", out var value))
                {
                    forbidden = true;
                    continue;
                }
                values.Add(value);
            }
        }
        if (forbidden || values.Count != 1) return false;
        text = values[0];
        return true;
    }
}

internal sealed class UnavailableRemoteInteractionPlanningProvider : IInteractionPlanningCompletionProvider
{
    public InteractionPlannerKind Kind => InteractionPlannerKind.Remote;
    public InteractionProviderIsolation Isolation { get; } = new(true, true, true, true, true, true);
    public Task<InteractionPlanningCompletionResult> CompleteAsync(
        InteractionPlanningCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionPlanningCompletionResult.Failure(
            "REMOTE_MODEL_DISABLED", "The remote interaction planner is disabled."));
}
