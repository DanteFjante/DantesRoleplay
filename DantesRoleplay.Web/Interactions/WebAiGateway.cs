using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Web.Interactions;

public sealed record WebAiProviderView(string Id, string DisplayName);

public sealed record WebAiModelView(
    string Provider,
    string Id,
    string DisplayName,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ReasoningEfforts,
    string Revision,
    bool IsDefault);

public sealed record WebAiRequest(
    string Surface,
    string Provider,
    string Model,
    string Operation,
    string Input,
    string IdempotencyKey,
    string? ApplicationId = null,
    string? ResolutionFingerprint = null,
    string? StateSpaceId = null,
    string Reasoning = "none",
    JsonElement? StructuredInput = null,
    JsonElement? ResponseSchema = null,
    string? ConversationId = null,
    int? ExpectedRevision = null,
    int MaximumToolRounds = 4,
    int MaximumOutputTokens = 2_048);

public sealed record WebAiToolCallView(
    string Id,
    string Name,
    bool InputValidated,
    string Status,
    string ErrorCode);

public sealed record WebAiMediaAttachmentView(
    string EntityId,
    string MediaId,
    string Role,
    string MediaType,
    int Width,
    int Height,
    string Alt,
    string Caption,
    string ContentUrl);

public sealed record WebAiExecutionView(
    bool Ok,
    string Surface,
    string Provider,
    string Model,
    string Operation,
    string? ApplicationId,
    string ResolutionFingerprint,
    string? StateSpaceId,
    string StateSpaceResolutionFingerprint,
    string ConversationId,
    int ConversationRevision,
    string Status,
    string AssistantMessage,
    string ReasoningSummary,
    JsonElement? StructuredData,
    bool StructuredDataValidated,
    IReadOnlyList<WebAiToolCallView> ToolCalls,
    IReadOnlyList<WebAiMediaAttachmentView> MediaAttachments,
    IReadOnlyList<AiExecutionActivity> Activities,
    IReadOnlyList<string> RequiredConfirmations,
    string ErrorCode,
    string ErrorMessage,
    int PromptTokens,
    int OutputTokens);

public sealed class WebAiException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface IWebAiGateway
{
    IReadOnlyList<WebAiProviderView> ListProviders();
    Task<IReadOnlyList<WebAiModelView>> ListModelsAsync(string provider, CancellationToken cancellationToken = default);
    Task<WebAiExecutionView> ExecuteAsync(
        AuthorizationAuditEvidence authorization,
        WebAiRequest request,
        CancellationToken cancellationToken = default);
    Task<AssistantConversationPage> ListConversationsAsync(
        AuthorizationAuditEvidence authorization,
        string provider,
        string surface,
        CancellationToken cancellationToken = default);
    Task<AssistantConversationDocument?> GetConversationAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        int expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-neutral web boundary for both AI surfaces. The browser selects provider, model,
/// operation, and application context; the host alone supplies identity, prompts, and direct tools.
/// </summary>
public sealed class WebAiGateway(
    IAiService? ai,
    ISystemAiAgentService? agents,
    IAiAgentProfileRegistry? profiles,
    IAssistantConversationStore? conversations,
    IApplicationRegistry? applications,
    IApplicationActivationReader? activations,
    IStateSpaceRegistry? stateSpaces) : IWebAiGateway
{
    private const int MaximumInputLength = 8_000;
    private const int MaximumSchemaLength = 16_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<WebAiProviderView> ListProviders() => ai is null
        ? []
        : ai.ListProviders().Select(value => new WebAiProviderView(value.Id, value.DisplayName)).ToArray();

    public async Task<IReadOnlyList<WebAiModelView>> ListModelsAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        RequireProvider(provider);
        if (ai is null) return [];
        if (ai.ListProviders().All(value => value.Id != provider)) throw Error(
            "AI_PROVIDER_UNKNOWN", "The requested AI provider is not registered.", 404);
        var models = await ai.ListModelsAsync(provider, cancellationToken);
        return models.Select(value => new WebAiModelView(
                value.Provider,
                value.Id,
                value.DisplayName,
                CapabilityNames(value.Capabilities),
                value.ReasoningEfforts.Select(ReasoningName).Prepend("none")
                    .Distinct(StringComparer.Ordinal).ToArray(),
                value.Revision,
                value.IsDefault))
            .OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<WebAiExecutionView> ExecuteAsync(
        AuthorizationAuditEvidence authorization,
        WebAiRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ai is null || agents is null || conversations is null || applications is null)
            throw Error("AI_SERVICE_UNAVAILABLE", "The provider-neutral AI service is not configured.", 503);
        var principal = Principal(authorization);
        var normalized = Validate(request);
        var models = await ai.ListModelsAsync(normalized.Provider, cancellationToken);
        var model = models.SingleOrDefault(value => value.Id == normalized.Model)
            ?? throw Error("AI_MODEL_UNAVAILABLE", "The selected provider did not report that model.", 409);
        ValidateModel(model, normalized);
        var binding = ResolveBinding(normalized);
        var profile = Profile(normalized.Surface, binding);
        var kind = RequestKind(normalized.Operation);
        var message = UserMessage(normalized, binding);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                normalized.Surface,
                normalized.Provider,
                normalized.Model,
                normalized.Operation,
                message,
                binding.ApplicationId,
                binding.ResolutionFingerprint,
                binding.StateSpaceId,
                normalized.Reasoning,
                responseSchema = normalized.ResponseSchema?.GetRawText()
            }, Json))));
        if (normalized.ConversationId is not null)
        {
            var existing = await ExactConversation(principal.PrincipalId, normalized.ConversationId, cancellationToken);
            var priorContext = existing.Turns.LastOrDefault(value => value.Context is not null)?.Context;
            if (priorContext is not null && priorContext.Fingerprint != binding.ContextFingerprint)
                throw Error("AI_CONVERSATION_CONTEXT_STALE",
                    "This conversation belongs to a different application or runtime-state context.", 409);
        }
        var begin = await conversations.BeginTurnAsync(new(
            principal.PrincipalId,
            ConversationProvider(normalized.Provider),
            normalized.ConversationId,
            normalized.ExpectedRevision,
            message,
            normalized.IdempotencyKey,
            requestHash,
            AssistantConversationScopes.System), cancellationToken);
        if (begin.Replay)
        {
            var replay = await ExactConversation(principal.PrincipalId, begin.ConversationId, cancellationToken);
            return Replay(normalized, binding, replay);
        }

        await conversations.MarkRunningAsync(begin.TurnId, cancellationToken);
        var invocation = new SystemCapabilityInvocationContext(
            principal,
            authorization.Scope,
            authorization.CorrelationId)
        {
            ApplicationId = binding.Application,
            ResolutionFingerprint = binding.ResolutionFingerprint,
            StateSpaceId = binding.StateSpaceId ?? ""
        };
        AiResponse response;
        try
        {
            response = await agents.SendAsync(
                profile,
                new(
                    normalized.Provider,
                    normalized.Model,
                    [new(AiMessageRole.User, message)],
                    kind,
                    ParseReasoning(normalized.Reasoning),
                    normalized.ResponseSchema?.GetRawText() ?? "",
                    AllowedTools: null,
                    normalized.MaximumToolRounds,
                    normalized.MaximumOutputTokens),
                invocation,
                writeApprovalGate: null,
                toolApprovalGate: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await conversations.CompleteTurnAsync(new(
                begin.TurnId, AssistantConversationStatuses.Cancelled, null,
                "AI_REQUEST_CANCELLED", "The AI request was cancelled.", "", "", "", "", 0, 0, 0),
                CancellationToken.None);
            throw;
        }

        var current = ResolveBinding(normalized with { ResolutionFingerprint = null });
        if (binding.Application is not null &&
            !string.Equals(binding.ResolutionFingerprint, current.ResolutionFingerprint, StringComparison.Ordinal))
            response = AiResponse.Failure(
                "AI_APPLICATION_CONTEXT_STALE",
                "The application changed while the AI request was running; the result was not accepted.") with
            {
                Activities = [.. response.Activities ?? [], new(
                    (response.Activities?.Count ?? 0) + 1,
                    "validation", "failed", "Application resolution changed during the AI request.",
                    ErrorCode: "AI_APPLICATION_CONTEXT_STALE")]
            };

        var activities = response.Activities ?? [];
        foreach (var activity in activities)
            await conversations.AppendActivityAsync(new(
                begin.TurnId,
                ActivityIdentity(activity),
                activity.Sequence,
                ActivityKind(activity.Kind),
                activity.Status,
                ActivitySummary(activity.Kind, activity.Summary)), CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(response.ReasoningSummary))
            await conversations.AppendActivityAsync(new(
                begin.TurnId, $"reasoning-{begin.TurnId}", activities.Count + 1,
                ActivityKind("reasoning"), "completed",
                ActivitySummary("reasoning", response.ReasoningSummary)), CancellationToken.None);

        var reply = response.Text;
        if (response.Ok && string.IsNullOrWhiteSpace(reply) && response.StructuredData is not null)
            reply = response.StructuredData.Value.GetRawText();
        if (response.Ok && (string.IsNullOrWhiteSpace(reply) || reply.Length > 8_000))
            response = AiResponse.Failure(
                "AI_RESPONSE_INVALID", "The AI returned an empty or oversized final message.") with
            {
                Activities = activities
            };
        var completionStatus = response.Ok
            ? AssistantConversationStatuses.Completed
            : AssistantConversationStatuses.Failed;
        await conversations.CompleteTurnAsync(new(
            begin.TurnId,
            completionStatus,
            response.Ok ? reply : null,
            response.ErrorCode,
            response.ErrorMessage,
            response.Model?.Provider ?? normalized.Provider,
            response.Model?.Id ?? normalized.Model,
            response.Model?.Revision ?? "",
            normalized.Surface,
            0,
            response.PromptTokens,
            response.OutputTokens,
            Context: response.Ok ? new(
                AssistantTurnContextProfiles.SystemReadV1,
                binding.ContextFingerprint,
                binding.References,
                AssistantTurnResponseDispositions.Answered) : null),
            CancellationToken.None);
        var conversation = await ExactConversation(principal.PrincipalId, begin.ConversationId, CancellationToken.None);
        return View(normalized, binding, response, conversation);
    }

    public async Task<AssistantConversationPage> ListConversationsAsync(
        AuthorizationAuditEvidence authorization,
        string provider,
        string surface,
        CancellationToken cancellationToken = default)
    {
        RequireProvider(provider);
        RequireSurface(surface);
        if (conversations is null) return new([], null);
        var principalId = Principal(authorization).PrincipalId;
        var rows = await conversations.ListAsync(
            principalId,
            ConversationProvider(provider),
            null,
            null,
            100,
            cancellationToken,
            AssistantConversationScopes.System);
        var matching = new List<AssistantConversationSummary>(rows.Count);
        foreach (var row in rows)
        {
            var document = await conversations.GetAsync(
                principalId, row.Id, cancellationToken, AssistantConversationScopes.System);
            var profile = document?.Turns.LastOrDefault(value =>
                !string.IsNullOrWhiteSpace(value.ModelProfile))?.ModelProfile;
            if (string.IsNullOrWhiteSpace(profile) ||
                string.Equals(profile, surface, StringComparison.Ordinal)) matching.Add(row);
        }
        return new(matching, null);
    }

    public async Task<AssistantConversationDocument?> GetConversationAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        CancellationToken cancellationToken = default) => conversations is null
        ? null
        : await conversations.GetAsync(
            Principal(authorization).PrincipalId,
            conversationId,
            cancellationToken,
            AssistantConversationScopes.System);

    public Task<bool> DeleteConversationAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (conversations is null)
            throw Error("AI_SERVICE_UNAVAILABLE", "The provider-neutral AI service is not configured.", 503);
        if (!Bounded(conversationId, 200) || expectedRevision < 1)
            throw Error("AI_CONVERSATION_DELETE_INVALID",
                "Removing a conversation requires its exact identity and current positive revision.");
        return conversations.DeleteAsync(
            Principal(authorization).PrincipalId,
            conversationId,
            expectedRevision,
            cancellationToken,
            AssistantConversationScopes.System);
    }

    private ContextBinding ResolveBinding(WebAiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationId))
        {
            if (!string.IsNullOrWhiteSpace(request.StateSpaceId)) throw Error(
                "AI_APPLICATION_CONTEXT_REQUIRED",
                "A runtime state space can only be selected with its application.");
            var systemFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("system-ai-context-v1")));
            return new(null, null, null, systemFingerprint, systemFingerprint, []);
        }
        ApplicationIdentifier application;
        try { application = ApplicationIdentifier.Parse(request.ApplicationId); }
        catch (ArgumentException) { throw Error("AI_APPLICATION_INVALID", "The selected application identity is invalid."); }
        var revision = applications!.Get(application)
            ?? throw Error("AI_APPLICATION_UNKNOWN", "The selected application is not registered.", 404);
        var activation = activations?.Current(application);
        var current = activation?.ResolutionFingerprint ?? revision.Fingerprint;
        if (!string.IsNullOrWhiteSpace(request.ResolutionFingerprint) &&
            !string.Equals(request.ResolutionFingerprint, current, StringComparison.Ordinal))
            throw Error("AI_APPLICATION_CONTEXT_STALE",
                "The selected application changed; refresh its effective context before continuing.", 409);
        string? stateSpaceId = null;
        var stateFingerprint = "";
        if (!string.IsNullOrWhiteSpace(request.StateSpaceId))
        {
            var state = stateSpaces?.Get(request.StateSpaceId) ?? throw Error(
                "AI_STATE_SPACE_UNKNOWN", "The selected runtime state space was not found.", 404);
            if (state.ApplicationRevision.ApplicationId != application || state.Scope != EcsStateSpaceScope.Runtime)
                throw Error("AI_STATE_SPACE_CONTEXT_INVALID",
                    "The selected state space is not runtime state for this application.", 409);
            if (activation is null ||
                state.ApplicationRevision.Revision != activation.ApplicationRevision ||
                !string.Equals(state.ApplicationRevision.Fingerprint, activation.ApplicationFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(state.ManifestFingerprint, activation.ActivationFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(state.ResolutionFingerprint, activation.ResolutionFingerprint,
                    StringComparison.Ordinal))
                throw Error("AI_STATE_SPACE_CONTEXT_STALE",
                    "The selected runtime state belongs to an older application activation. Refresh the context and choose a current state space.",
                    409);
            stateSpaceId = state.StateSpaceId;
            stateFingerprint = state.ResolutionFingerprint;
        }
        var contextFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Surface + "\0" + application.Value + "\0" + current + "\0" + stateSpaceId + "\0" + stateFingerprint)));
        var references = new List<string> { $"application:{application.Value}@{current}" };
        if (stateSpaceId is not null) references.Add($"state-space:{stateSpaceId}@{stateFingerprint}");
        references.Sort(StringComparer.Ordinal);
        return new(application, application.Value, stateSpaceId, current, contextFingerprint,
            references.AsReadOnly(), stateFingerprint);
    }

    private async Task<AssistantConversationDocument> ExactConversation(
        string principalId,
        string conversationId,
        CancellationToken cancellationToken) =>
        await conversations!.GetAsync(principalId, conversationId, cancellationToken, AssistantConversationScopes.System)
        ?? throw Error("ASSISTANT_CONVERSATION_UNKNOWN", "The AI conversation was not found.", 404);

    private static WebAiRequest Validate(WebAiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Surface is not ("outer" or "inner")) throw Error(
            "AI_SURFACE_INVALID", "The AI surface must be outer or inner.");
        RequireProvider(request.Provider);
        if (!Bounded(request.Model, 200) || !Bounded(request.Operation, 40) ||
            !Bounded(request.Input, MaximumInputLength) ||
            !Bounded(request.IdempotencyKey, 100) ||
            request.MaximumToolRounds is < 0 or > 8 ||
            request.MaximumOutputTokens is < 1 or > 8_192)
            throw Error("AI_REQUEST_INVALID", "The AI request contains invalid or unbounded values.");
        _ = RequestKind(request.Operation);
        _ = ParseReasoning(request.Reasoning);
        if (request.ResponseSchema is { } schema &&
            (schema.ValueKind != JsonValueKind.Object || schema.GetRawText().Length > MaximumSchemaLength))
            throw Error("AI_RESPONSE_SCHEMA_INVALID", "The response schema must be one bounded JSON object.");
        if (request.Operation == "structured-request" && request.ResponseSchema is null)
            throw Error("AI_RESPONSE_SCHEMA_REQUIRED", "A structured request requires a response schema.");
        if ((request.ConversationId is null && request.ExpectedRevision is not null) ||
            (request.ConversationId is not null && (request.ExpectedRevision is null or < 1)))
            throw Error("AI_CONVERSATION_REVISION_INVALID",
                "A continued conversation requires its current positive revision.");
        if (request.Operation == "continued-subtask" && request.ConversationId is null)
            throw Error("AI_CONVERSATION_REQUIRED", "A continued subtask requires an existing conversation.");
        return request with
        {
            Input = request.Input.Trim(),
            Provider = request.Provider.Trim(),
            Model = request.Model.Trim(),
            Operation = request.Operation.Trim(),
            Reasoning = request.Reasoning.Trim().ToLowerInvariant()
        };
    }

    private static void ValidateModel(AiModel model, WebAiRequest request)
    {
        var reasoning = ParseReasoning(request.Reasoning);
        if (reasoning != AiReasoningEffort.None &&
            (!model.Capabilities.HasFlag(AiModelCapabilities.Reasoning) ||
             !model.ReasoningEfforts.Contains(reasoning)))
            throw Error("AI_REASONING_UNSUPPORTED",
                "The selected provider model does not support that reasoning effort.", 409);
        var required = request.Operation switch
        {
            "structured-request" => AiModelCapabilities.StructuredOutput,
            "message" => AiModelCapabilities.Messages,
            _ => AiModelCapabilities.Tasks
        };
        if (!model.Capabilities.HasFlag(required)) throw Error(
            "AI_OPERATION_UNSUPPORTED", "The selected provider model does not support this operation.", 409);
    }

    private AiAgentProfile Profile(string surface, ContextBinding binding)
    {
        var id = surface switch
        {
            "outer" => "web.outer",
            "inner" => "web.inner",
            _ => throw Error("AI_SURFACE_INVALID", "The AI surface is invalid.")
        };
        var registered = profiles?.Get(id) ?? throw Error(
            "AI_AGENT_PROFILE_UNAVAILABLE", "The selected AI identity is not registered.", 503);
        return registered with
        {
            Instructions = Instructions(binding, registered.Instructions)
        };
    }

    private static string Instructions(ContextBinding binding, string purpose)
    {
        var context = binding.ApplicationId is null
            ? "No application is selected; use only system-scoped capabilities."
            : $"The originating application is '{binding.ApplicationId}' with resolution fingerprint '{binding.ResolutionFingerprint}'.";
        if (binding.StateSpaceId is not null)
            context += $" Runtime state is separately bound to state space '{binding.StateSpaceId}'.";
        return purpose + " " + context +
            " Never choose an extension preset or substitute a different application or state space. Provider output is not authorization.";
    }

    private static string UserMessage(WebAiRequest request, ContextBinding binding)
    {
        var builder = new StringBuilder()
            .Append("Operation: ").AppendLine(request.Operation)
            .Append("Request: ").AppendLine(request.Input);
        if (request.StructuredInput is { } structured)
            builder.Append("Structured input: ").AppendLine(structured.GetRawText());
        if (request.Operation == "recipe-execution")
            builder.AppendLine("Find and run the matching verified recipe through the direct recipe tools.");
        else if (request.Operation == "scheduled-task")
            builder.AppendLine("Prepare the scheduled task through the registered scheduling tools; do not bypass confirmation.");
        else if (request.Operation == "plan")
            builder.AppendLine("Produce or prepare a durable reviewable plan through the registered task tools.");
        else if (request.Operation == "continued-subtask")
            builder.AppendLine("Continue the bounded subtask from the visible conversation and direct tool evidence.");
        return builder.ToString().Trim();
    }

    private static WebAiExecutionView View(
        WebAiRequest request,
        ContextBinding binding,
        AiResponse response,
        AssistantConversationDocument conversation)
    {
        var activities = response.Activities ?? [];
        var toolCalls = response.ToolCalls.Select(call =>
        {
            var activity = activities.LastOrDefault(value => value.ToolCallId == call.Id);
            return new WebAiToolCallView(
                call.Id,
                call.Name,
                activity?.InputValidated == true,
                activity?.Status ?? "requested",
                activity?.ErrorCode ?? "");
        }).ToArray();
        var confirmations = activities
            .Where(value => value.ErrorCode.EndsWith("CONFIRMATION_REQUIRED", StringComparison.Ordinal))
            .Select(value => value.ToolName)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var media = MediaAttachments(binding, response.Media);
        return new(
            response.Ok,
            request.Surface,
            request.Provider,
            request.Model,
            request.Operation,
            binding.ApplicationId,
            binding.ResolutionFingerprint,
            binding.StateSpaceId,
            binding.StateSpaceResolutionFingerprint,
            conversation.Summary.Id,
            conversation.Summary.Revision,
            conversation.Summary.Status,
            response.Text,
            Bound(response.ReasoningSummary, 2_000),
            response.StructuredData,
            response.StructuredData is not null,
            toolCalls,
            media,
            activities,
            confirmations,
            response.ErrorCode,
            response.ErrorMessage,
            response.PromptTokens,
            response.OutputTokens);
    }

    private static WebAiExecutionView Replay(
        WebAiRequest request,
        ContextBinding binding,
        AssistantConversationDocument conversation)
    {
        var turn = conversation.Turns[^1];
        var message = conversation.Messages.LastOrDefault(value => value.Role == "assistant")?.Content ?? "";
        return new(
            turn.Status == AssistantConversationStatuses.Completed,
            request.Surface,
            request.Provider,
            request.Model,
            request.Operation,
            binding.ApplicationId,
            binding.ResolutionFingerprint,
            binding.StateSpaceId,
            binding.StateSpaceResolutionFingerprint,
            conversation.Summary.Id,
            conversation.Summary.Revision,
            turn.Status,
            message,
            "",
            null,
            false,
            [],
            [],
            conversation.Activities.Select(value => new AiExecutionActivity(
                value.Sequence, value.Kind, value.Status, value.Summary)).ToArray(),
            [],
            turn.ErrorCode,
            turn.ErrorMessage,
            turn.PromptTokens,
            turn.OutputTokens);
    }

    private static IReadOnlyList<WebAiMediaAttachmentView> MediaAttachments(
        ContextBinding binding,
        IReadOnlyList<AiMediaContent>? values)
    {
        if (binding.ApplicationId is null || binding.StateSpaceId is null || values is null) return [];
        return values.Where(ValidMedia).Select(value => new WebAiMediaAttachmentView(
                value.EntityId,
                value.MediaId,
                value.Role,
                value.MediaType,
                value.Width,
                value.Height,
                value.Alt,
                value.Caption,
                $"/api/applications/{Uri.EscapeDataString(binding.ApplicationId)}" +
                $"/state-spaces/{Uri.EscapeDataString(binding.StateSpaceId)}" +
                $"/entities/{Uri.EscapeDataString(value.EntityId)}" +
                $"/media/{Uri.EscapeDataString(value.MediaId)}/content")).ToArray();
    }

    private static bool ValidMedia(AiMediaContent value) =>
        Bounded(value.EntityId, 200) && Bounded(value.MediaId, 200) &&
        value.Role is "portrait" or "setting" or "map" or "illustration" or "icon" or "scene" or "handout" &&
        value.MediaType is "image/png" or "image/jpeg" or "image/webp" &&
        value.Width is >= 1 and <= 10_000 && value.Height is >= 1 and <= 10_000 &&
        Bounded(value.Alt, 500) && value.Caption.Length <= 1_000 && !value.Caption.Any(char.IsControl);

    private static string ActivityIdentity(AiExecutionActivity activity)
    {
        var suffix = string.IsNullOrWhiteSpace(activity.ToolCallId)
            ? activity.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : activity.ToolCallId;
        suffix = new(suffix.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        return "ai-" + activity.Sequence + "-" + Bound(suffix.Length == 0 ? "activity" : suffix, 160);
    }

    private static string ActivityKind(string value) => value switch
    {
        "warning" => "warning",
        "error" => "error",
        _ => "dynamic-tool"
    };

    private static string ActivitySummary(string kind, string summary) =>
        Bound($"{kind}: {summary}", 500);

    private static string ConversationProvider(string provider) => provider switch
    {
        "ollama" => "local",
        "codex" => "codex",
        _ => throw Error("AI_PROVIDER_UNKNOWN", "The requested AI provider cannot retain conversations.", 409)
    };

    private static AiRequestKind RequestKind(string value) => value switch
    {
        "message" => AiRequestKind.Message,
        "structured-request" => AiRequestKind.StructuredRequest,
        "task" => AiRequestKind.Task,
        "plan" => AiRequestKind.Plan,
        "recipe-execution" => AiRequestKind.RecipeExecution,
        "scheduled-task" => AiRequestKind.ScheduledTask,
        "continued-subtask" => AiRequestKind.ContinuedSubtask,
        _ => throw Error("AI_OPERATION_INVALID", "The requested AI operation is not registered.")
    };

    private static AiReasoningEffort ParseReasoning(string value) => value.ToLowerInvariant() switch
    {
        "none" => AiReasoningEffort.None,
        "minimal" => AiReasoningEffort.Minimal,
        "low" => AiReasoningEffort.Low,
        "medium" => AiReasoningEffort.Medium,
        "high" => AiReasoningEffort.High,
        "xhigh" => AiReasoningEffort.XHigh,
        "max" => AiReasoningEffort.Max,
        "ultra" => AiReasoningEffort.Ultra,
        _ => throw Error("AI_REASONING_INVALID", "The requested reasoning effort is invalid.")
    };

    private static string ReasoningName(AiReasoningEffort value) => value switch
    {
        AiReasoningEffort.Minimal => "minimal",
        AiReasoningEffort.Low => "low",
        AiReasoningEffort.Medium => "medium",
        AiReasoningEffort.High => "high",
        AiReasoningEffort.XHigh => "xhigh",
        AiReasoningEffort.Max => "max",
        AiReasoningEffort.Ultra => "ultra",
        _ => "none"
    };

    private static IReadOnlyList<string> CapabilityNames(AiModelCapabilities capabilities)
    {
        var values = new List<string>();
        if (capabilities.HasFlag(AiModelCapabilities.Messages)) values.Add("messages");
        if (capabilities.HasFlag(AiModelCapabilities.Reasoning)) values.Add("reasoning");
        if (capabilities.HasFlag(AiModelCapabilities.StructuredOutput)) values.Add("structured-output");
        if (capabilities.HasFlag(AiModelCapabilities.Tools)) values.Add("tools");
        if (capabilities.HasFlag(AiModelCapabilities.Tasks)) values.Add("tasks");
        return values;
    }

    private static TrustedPrincipalContext Principal(AuthorizationAuditEvidence authorization)
    {
        var context = SystemCapabilityInvocationContext.FromAuthorization(authorization);
        if (!context.Principal.Verified) throw Error(
            "PRIVATE_OPERATOR_UNAUTHENTICATED", "Private operator authorization is required.", 403);
        return context.Principal;
    }

    private static void RequireProvider(string value)
    {
        if (!Bounded(value, 64) || value.Any(character =>
                !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_')))
            throw Error("AI_PROVIDER_INVALID", "The AI provider identity is invalid.");
    }

    private static void RequireSurface(string value)
    {
        if (value is not ("outer" or "inner"))
            throw Error("AI_SURFACE_INVALID", "The AI surface must be outer or inner.");
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static string Bound(string? value, int maximum) => string.IsNullOrEmpty(value)
        ? ""
        : value.Length <= maximum ? value : value[..maximum];

    private static WebAiException Error(string code, string message, int status = 400) => new(code, message, status);

    private sealed record ContextBinding(
        ApplicationIdentifier? Application,
        string? ApplicationId,
        string? StateSpaceId,
        string ResolutionFingerprint,
        string ContextFingerprint,
        IReadOnlyList<string> References,
        string StateSpaceResolutionFingerprint = "");
}
