using System.Text;
using System.Text.Json;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Interactions;

/// <summary>Host-selected provider mode. It is deliberately not a player or model input.</summary>
public sealed class InteractionOuterProviderSelectionOptions
{
    public InteractionOuterProviderKind Provider { get; init; } = InteractionOuterProviderKind.Local;
}

/// <summary>Expected identity and outer-output cap for a dedicated local outer profile.</summary>
public sealed class LocalInteractionOuterProviderOptions
{
    public string Model { get; init; } = "qwen3:8b";
    public string Profile { get; init; } = "outer";
    public int MaximumOutputBytes { get; init; } = InteractionContractLimits.JsonBytes;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model != Model.Trim())
            return "The local outer model must be trimmed, nonblank, and at most 200 characters.";
        if (string.IsNullOrWhiteSpace(Profile) || Profile.Length > 100 || Profile != Profile.Trim())
            return "The local outer profile must be trimmed, nonblank, and at most 100 characters.";
        if (MaximumOutputBytes is < 1_000 or > InteractionContractLimits.JsonBytes)
            return "The local outer output budget is outside the closed range.";
        return null;
    }
}

/// <summary>Dedicated local outer completion seam shared by outer conversation and outer planning.</summary>
public interface IInteractionOuterLocalCompletionProvider
{
    Task<StructuredCompletionResult> CompleteAsync(
        StructuredCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InteractionOuterLocalCompletionProvider(
    ILocalStructuredCompletionProvider? local,
    LocalInteractionOuterProviderOptions options) : IInteractionOuterLocalCompletionProvider
{
    public async Task<StructuredCompletionResult> CompleteAsync(
        StructuredCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var invalid = options.Validate();
        if (invalid is not null)
            return StructuredCompletionResult.Failure("LOCAL_OUTER_MODEL_CONFIG_INVALID", invalid);
        if (local is null)
            return StructuredCompletionResult.Failure(
                "LOCAL_OUTER_MODEL_DISABLED", "The local outer model is disabled.");
        var result = await local.CompleteAsync(request, cancellationToken);
        if (!result.Ok || result.Identity is null) return result;
        if (!string.Equals(result.Identity.Model, options.Model, StringComparison.Ordinal)
            || !string.Equals(result.Identity.Profile, options.Profile, StringComparison.Ordinal))
            return StructuredCompletionResult.Failure(
                "LOCAL_OUTER_MODEL_IDENTITY_MISMATCH", "The local outer model identity changed.",
                result.ElapsedMilliseconds);
        if (Encoding.UTF8.GetByteCount(result.Json) > options.MaximumOutputBytes)
            return StructuredCompletionResult.Failure(
                "LOCAL_OUTER_MODEL_OUTPUT_BUDGET_EXCEEDED", "The local outer model exceeded its output limit.",
                result.ElapsedMilliseconds);
        return result;
    }
}

/// <summary>
/// Fixed no-tools local outer adapter. The underlying provider enforces the loopback, model,
/// allowlisted task, prompt, schema, and output limits; this adapter adds outer response parsing.
/// </summary>
public sealed class LocalInteractionOuterProvider(
    IInteractionOuterLocalCompletionProvider? local) : IInteractionOuterProviderAdapter
{
    public InteractionOuterProviderKind Kind => InteractionOuterProviderKind.Local;

    public async Task<InteractionOuterTurnResult> DecideAsync(
        InteractionOuterTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlayerText) || request.PlayerText.Length > 4_000)
            return InteractionOuterTurnResult.Unavailable("OUTER_REQUEST_INVALID");
        var output = await CompleteAsync(InteractionOuterProtocol.OuterTurnTask,
            InteractionOuterProtocol.OuterTurnPrompt, InteractionOuterProtocol.OuterTurnSchema,
            JsonSerializer.Serialize(request), cancellationToken);
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
            InteractionOuterProtocol.NarrationPrompt, InteractionOuterProtocol.NarrationSchema,
            JsonSerializer.Serialize(request), cancellationToken);
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

    public async Task<InteractionTaskAgendaResult> CreateAgendaAsync(
        InteractionTaskAgendaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GoalText)
            || request.GoalText.Length > InteractionContractLimits.IntentText
            || request.GoalText.Any(char.IsControl))
            return InteractionTaskAgendaResult.Unavailable("TASK_AGENDA_REQUEST_INVALID");
        var output = await CompleteAsync(InteractionOuterProtocol.TaskAgendaTask,
            InteractionOuterProtocol.TaskAgendaPrompt, InteractionOuterProtocol.TaskAgendaSchema,
            JsonSerializer.Serialize(request), cancellationToken);
        if (!output.Ok) return InteractionTaskAgendaResult.Unavailable(output.Code);
        try
        {
            return new(true, InteractionTaskAgenda.Parse(output.Json), "TASK_AGENDA_COMPLETED");
        }
        catch (InteractionContractException)
        {
            return InteractionTaskAgendaResult.Unavailable("TASK_AGENDA_INVALID");
        }
    }

    private async Task<(bool Ok, string Json, string Code)> CompleteAsync(
        string task, string instructions, string schema, string input, CancellationToken cancellationToken)
    {
        if (local is null) return (false, "", "LOCAL_OUTER_MODEL_DISABLED");
        if (Encoding.UTF8.GetByteCount(input) > InteractionContractLimits.JsonBytes)
            return (false, "", "OUTER_REQUEST_TOO_LARGE");
        StructuredCompletionResult result;
        try
        {
            result = await local.CompleteAsync(new(task, instructions, input, schema,
                LocalModelPriority.Interactive), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return (false, "", "LOCAL_OUTER_MODEL_UNAVAILABLE");
        }
        if (!result.Ok || result.Identity is null)
            return (false, "", string.IsNullOrWhiteSpace(result.ErrorCode)
                ? "LOCAL_OUTER_MODEL_UNAVAILABLE" : result.ErrorCode);
        return (true, result.Json, "");
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
}

/// <summary>Dispatches to exactly one host-selected adapter. It never retries or changes provider.</summary>
public sealed class SelectedInteractionOuterProvider : IInteractionOuterProviderAdapter
{
    private readonly IInteractionOuterProviderAdapter selected;
    public InteractionOuterProviderKind Kind => selected.Kind;

    public SelectedInteractionOuterProvider(
        InteractionOuterProviderSelectionOptions options,
        IEnumerable<IInteractionOuterProviderAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(adapters);
        var registered = adapters.ToArray();
        var matches = registered.Where(adapter => adapter.Kind == options.Provider).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(matches.Length == 0
                ? $"The selected outer provider '{options.Provider}' is not registered."
                : $"The selected outer provider '{options.Provider}' is registered more than once.");
        selected = matches[0];
    }

    public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request,
        CancellationToken cancellationToken = default) => selected.DecideAsync(request, cancellationToken);

    public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request,
        CancellationToken cancellationToken = default) => selected.NarrateAsync(request, cancellationToken);

    public Task<InteractionTaskAgendaResult> CreateAgendaAsync(InteractionTaskAgendaRequest request,
        CancellationToken cancellationToken = default) => selected.CreateAgendaAsync(request, cancellationToken);
}
