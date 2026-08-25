using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess.Composition;

namespace DantesRoleplay.Interactions;

/// <summary>Fixed schema-only outer conversation adapter. It never sends tool definitions.</summary>
public sealed class OpenAiResponsesOuterInteractionProvider(
    HttpClient http,
    OpenAiInteractionPlanningOptions options) : IInteractionOuterTurnProvider, IInteractionNarrationProvider
{
    private const int MaximumEnvelopeBytes = 256 * 1024;

    public async Task<InteractionOuterTurnResult> DecideAsync(
        InteractionOuterTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlayerText) || request.PlayerText.Length > 4_000)
            return InteractionOuterTurnResult.Unavailable("OUTER_REQUEST_INVALID");
        var output = await CompleteAsync(InteractionOuterProtocol.OuterTurnTask,
            InteractionOuterProtocol.OuterTurnPrompt, InteractionOuterProtocol.OuterTurnSchemaName,
            InteractionOuterProtocol.OuterTurnSchema, JsonSerializer.Serialize(request), cancellationToken);
        if (!output.Ok) return InteractionOuterTurnResult.Unavailable(output.Code);
        try
        {
            using var document = JsonDocument.Parse(output.Json);
            var root = document.RootElement;
            var decision = RequiredString(root, "decision") switch
            {
                "respond" => InteractionOuterDecision.Respond,
                "delegate" => InteractionOuterDecision.Delegate,
                "direct-plan" => InteractionOuterDecision.DirectPlan,
                _ => throw new JsonException()
            };
            Exact(root, decision == InteractionOuterDecision.Respond
                ? ["decision", "text"] : ["decision", "intentText"]);
            var text = RequiredString(root, decision == InteractionOuterDecision.Respond ? "text" : "intentText");
            return text.Length <= 4_000
                ? new(true, decision, text, "OUTER_TURN_COMPLETED")
                : InteractionOuterTurnResult.Unavailable("OUTER_RESPONSE_INVALID");
        }
        catch (JsonException) { return InteractionOuterTurnResult.Unavailable("OUTER_RESPONSE_INVALID"); }
    }

    public async Task<InteractionNarrationResult> NarrateAsync(
        InteractionNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var output = await CompleteAsync(InteractionOuterProtocol.NarrationTask,
            InteractionOuterProtocol.NarrationPrompt, InteractionOuterProtocol.NarrationSchemaName,
            InteractionOuterProtocol.NarrationSchema, JsonSerializer.Serialize(request), cancellationToken);
        if (!output.Ok) return InteractionNarrationResult.Unavailable(output.Code);
        try
        {
            using var document = JsonDocument.Parse(output.Json);
            Exact(document.RootElement, ["narration"]);
            var narration = RequiredString(document.RootElement, "narration");
            return narration.Length <= 4_000
                ? new(true, narration, "NARRATION_COMPLETED")
                : InteractionNarrationResult.Unavailable("NARRATION_RESPONSE_INVALID");
        }
        catch (JsonException) { return InteractionNarrationResult.Unavailable("NARRATION_RESPONSE_INVALID"); }
    }

    private async Task<(bool Ok, string Json, string Code)> CompleteAsync(
        string task, string instructions, string schemaName, string schemaJson, string input,
        CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(input) > InteractionContractLimits.JsonBytes)
            return (false, "", "OUTER_REQUEST_TOO_LARGE");
        var invalid = options.Validate();
        if (invalid is not null) return (false, "", options.Enabled ? "REMOTE_MODEL_CONFIG_INVALID" : "REMOTE_MODEL_DISABLED");
        using var schemaDocument = JsonDocument.Parse(schemaJson);
        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Headers.Add("X-Dantes-Task-Class", task);
        message.Content = JsonContent.Create(new
        {
            model = InteractionRoleProfile.Outer.Model,
            instructions,
            input,
            reasoning = new { effort = InteractionRoleProfile.Outer.ReasoningEffort },
            max_output_tokens = 2_048,
            text = new { format = new { type = "json_schema", name = schemaName, strict = true, schema = schemaDocument.RootElement } },
            tools = Array.Empty<object>(), tool_choice = "none", parallel_tool_calls = false, store = false
        });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return (false, "", "REMOTE_MODEL_UNAVAILABLE");
            if (response.Content.Headers.ContentLength > MaximumEnvelopeBytes) return (false, "", "REMOTE_RESPONSE_TOO_LARGE");
            var bytes = await ReadBoundedAsync(response.Content, timeout.Token);
            using var document = JsonDocument.Parse(bytes);
            if (!TryOutputText(document.RootElement, out var text)) return (false, "", "REMOTE_MODEL_RESPONSE_INVALID");
            return (true, text, "");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (false, "", "REMOTE_MODEL_TIMEOUT"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        { return (false, "", "REMOTE_MODEL_UNAVAILABLE"); }
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new JsonException();

    private static void Exact(JsonElement root, IReadOnlyList<string> allowed)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Select(property => property.Name).Any(name => !allowed.Contains(name, StringComparer.Ordinal))
            || root.EnumerateObject().Count() != allowed.Count)
            throw new JsonException();
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > MaximumEnvelopeBytes) throw new InvalidDataException();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool TryOutputText(JsonElement root, out string text)
    {
        text = string.Empty;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || status.GetString() != "completed"
            || !root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String
            || !(model.GetString() == InteractionRoleProfile.Outer.Model
                || model.GetString()!.StartsWith(InteractionRoleProfile.Outer.Model + "-", StringComparison.Ordinal))
            || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return false;
        var values = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
            if (type.GetString() == "reasoning") continue;
            if (type.GetString() != "message" || !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array) return false;
            foreach (var part in content.EnumerateArray())
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("type", out var partType) && partType.ValueKind == JsonValueKind.String
                    && partType.GetString() == "output_text"
                    && part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                    values.Add(value.GetString()!);
                else return false;
        }
        if (values.Count != 1) return false;
        text = values[0];
        return Encoding.UTF8.GetByteCount(text) <= InteractionContractLimits.JsonBytes;
    }
}
