using System.Text.Json;

namespace DantesRoleplay.AI;

[Flags]
public enum AiModelCapabilities
{
    None = 0,
    Messages = 1,
    Reasoning = 2,
    StructuredOutput = 4,
    Tools = 8,
    Tasks = 16,
    Images = 32
}

public enum AiReasoningEffort
{
    None,
    Minimal,
    Low,
    Medium,
    High,
    XHigh,
    Max,
    Ultra
}

public enum AiRequestKind
{
    Message,
    Task,
    StructuredRequest,
    Plan,
    RecipeExecution,
    ScheduledTask,
    ContinuedSubtask
}

public enum AiMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AiModel(
    string Provider,
    string Id,
    string DisplayName,
    AiModelCapabilities Capabilities,
    IReadOnlyList<AiReasoningEffort> ReasoningEfforts,
    string Revision = "",
    bool IsDefault = false);

public sealed record AiProviderInfo(string Id, string DisplayName);

/// <summary>
/// Stable identity and operating instructions for a model acting as an in-process agent.
/// The host combines this with the exact tools authorized for each request.
/// </summary>
public sealed record AiAgentProfile(
    string Id,
    string Name,
    string Identity,
    string Instructions = "");

public interface IAiAgentProfileRegistry
{
    IReadOnlyList<AiAgentProfile> List();
    AiAgentProfile? Get(string id);
}

public sealed class AiAgentProfileRegistry(IEnumerable<AiAgentProfile> profiles) : IAiAgentProfileRegistry
{
    private readonly IReadOnlyDictionary<string, AiAgentProfile> _profiles = profiles
        .ToDictionary(profile => profile.Id, StringComparer.Ordinal);

    public IReadOnlyList<AiAgentProfile> List() => _profiles.Values
        .OrderBy(profile => profile.Id, StringComparer.Ordinal)
        .ToArray();

    public AiAgentProfile? Get(string id) =>
        _profiles.TryGetValue(id, out var profile) ? profile : null;
}

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record AiMediaContent(
    string MediaType,
    string Base64Data,
    string Sha256 = "",
    string Alt = "",
    string EntityId = "",
    string MediaId = "",
    string Role = "",
    int Width = 0,
    int Height = 0,
    string Caption = "");

public sealed record AiMessage(
    AiMessageRole Role,
    string Content,
    string ToolCallId = "",
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    IReadOnlyList<AiMediaContent>? Media = null);

public sealed record AiToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson);

public sealed record AiToolInvocation(
    string CallId,
    string Name,
    JsonElement Arguments,
    AiRequestKind RequestKind);

public sealed record AiToolResult(
    bool Ok,
    string Content,
    string ErrorCode = "",
    string ErrorMessage = "",
    IReadOnlyList<AiMediaContent>? Media = null)
{
    public static AiToolResult Success(string content) => new(true, content);

    public static AiToolResult Success(string content, IReadOnlyList<AiMediaContent> media) =>
        new(true, content, Media: media);

    public static AiToolResult Failure(string code, string message) =>
        new(false, "", code, message);
}

public interface IAiTool
{
    AiToolDefinition Definition { get; }

    Task<AiToolResult> InvokeAsync(
        AiToolInvocation invocation,
        CancellationToken cancellationToken = default);
}

public sealed record AiToolApprovalRequest(
    string ToolName,
    string Description,
    JsonElement Arguments);

public interface IAiToolApprovalGate
{
    Task<bool> ConfirmAsync(
        AiToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}

public delegate Task<AiToolResult> AiToolExecutor(
    AiToolCall call,
    CancellationToken cancellationToken);

public sealed record AiProviderRequest(
    string Model,
    IReadOnlyList<AiMessage> Messages,
    AiRequestKind Kind,
    AiReasoningEffort Reasoning,
    string ResponseSchemaJson,
    IReadOnlyList<AiToolDefinition> Tools,
    AiToolExecutor? ToolExecutor,
    int MaximumOutputTokens);

public sealed record AiProviderResponse(
    bool Ok,
    AiModel? Model,
    string Text,
    string StructuredJson,
    IReadOnlyList<AiToolCall> ToolCalls,
    int PromptTokens = 0,
    int OutputTokens = 0,
    string ConversationId = "",
    string ErrorCode = "",
    string ErrorMessage = "",
    string ReasoningSummary = "")
{
    public static AiProviderResponse Failure(string code, string message) =>
        new(false, null, "", "", [], ErrorCode: code, ErrorMessage: message);
}

public interface IAiProvider
{
    AiProviderInfo Info { get; }

    Task<IReadOnlyList<AiModel>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    Task<AiProviderResponse> SendAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AiRequest(
    string Provider,
    string Model,
    IReadOnlyList<AiMessage> Messages,
    AiRequestKind Kind = AiRequestKind.Message,
    AiReasoningEffort Reasoning = AiReasoningEffort.None,
    string ResponseSchemaJson = "",
    IReadOnlyList<string>? AllowedTools = null,
    int MaximumToolRounds = 4,
    int MaximumOutputTokens = 2_048);

public sealed record AiExecutionActivity(
    int Sequence,
    string Kind,
    string Status,
    string Summary,
    string ToolCallId = "",
    string ToolName = "",
    bool InputValidated = false,
    string ErrorCode = "");

public sealed record AiResponse(
    bool Ok,
    AiModel? Model,
    string Text,
    JsonElement? StructuredData,
    IReadOnlyList<AiToolCall> ToolCalls,
    int PromptTokens,
    int OutputTokens,
    string ConversationId = "",
    string ErrorCode = "",
    string ErrorMessage = "",
    string ReasoningSummary = "",
    IReadOnlyList<AiExecutionActivity>? Activities = null,
    IReadOnlyList<AiMediaContent>? Media = null)
{
    public static AiResponse Failure(string code, string message) =>
        new(false, null, "", null, [], 0, 0, ErrorCode: code, ErrorMessage: message);
}

public static class AiRequestKinds
{
    public static bool IsBackground(AiRequestKind kind) => kind is
        AiRequestKind.Task or AiRequestKind.Plan or AiRequestKind.RecipeExecution or
        AiRequestKind.ScheduledTask or AiRequestKind.ContinuedSubtask;
}

public interface IAiService
{
    IReadOnlyList<AiProviderInfo> ListProviders();

    Task<IReadOnlyList<AiModel>> ListModelsAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task<AiResponse> SendMessageAsync(
        string provider,
        string model,
        IReadOnlyList<AiMessage> messages,
        AiReasoningEffort reasoning = AiReasoningEffort.None,
        IReadOnlyList<string>? allowedTools = null,
        CancellationToken cancellationToken = default);

    Task<AiResponse> SendTaskAsync(
        string provider,
        string model,
        string task,
        AiReasoningEffort reasoning = AiReasoningEffort.None,
        IReadOnlyList<string>? allowedTools = null,
        CancellationToken cancellationToken = default);

    Task<AiResponse> SendRequestAsync(
        AiRequest request,
        CancellationToken cancellationToken = default);

    Task<AiResponse> SendAgentRequestAsync(
        AiAgentProfile profile,
        AiRequest request,
        IReadOnlyList<IAiTool> authorizedTools,
        CancellationToken cancellationToken = default);
}
