using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.AI;

/// <summary>Host-only correlation data. Never expose these types through model tools or JSON.</summary>
public interface IAiHostOnlyLifecycleValue { }

[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public sealed record AiProviderCallDescriptor : IAiHostOnlyLifecycleValue
{
    public AiProviderCallDescriptor(string providerId, int round, AiProviderRequest request)
    {
        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 80 || round is < 0 or > 16)
            throw new AiLifecycleException("AI_LIFECYCLE_DESCRIPTOR_INVALID", "Provider call identity is invalid.");
        ProviderId = providerId;
        Round = round;
        Request = AiLifecycleSnapshots.Request(request);
    }
    public string ProviderId { get; }
    public int Round { get; }
    public AiProviderRequest Request { get; }
}

[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public sealed record AiToolDispatchDescriptor : IAiHostOnlyLifecycleValue
{
    public AiToolDispatchDescriptor(int dispatchOrdinal, AiToolDefinition definition, AiToolInvocation invocation)
    {
        if (dispatchOrdinal < 1)
            throw new AiLifecycleException("AI_LIFECYCLE_DESCRIPTOR_INVALID", "Tool dispatch ordinal must be positive.");
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invocation);
        DispatchOrdinal = dispatchOrdinal;
        Definition = definition with { };
        Invocation = invocation with { Arguments = invocation.Arguments.Clone() };
    }
    public int DispatchOrdinal { get; }
    public AiToolDefinition Definition { get; }
    public AiToolInvocation Invocation { get; }
}

public enum AiDispatchCompletionKind { Returned, Threw, Cancelled, NotStarted }

[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public sealed record AiProviderCallObservation : IAiHostOnlyLifecycleValue
{
    public AiProviderCallObservation(AiDispatchCompletionKind kind, AiProviderResponse? response, string failureCode = "")
    {
        AiLifecycleSnapshots.ValidateObservation(kind, response is not null, failureCode);
        Kind = kind;
        Response = response is null ? null : AiLifecycleSnapshots.Response(response);
        FailureCode = failureCode;
    }
    public AiDispatchCompletionKind Kind { get; }
    public AiProviderResponse? Response { get; }
    public string FailureCode { get; }
}

[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public sealed record AiToolDispatchObservation : IAiHostOnlyLifecycleValue
{
    public AiToolDispatchObservation(AiDispatchCompletionKind kind, AiToolResult? result, string failureCode = "")
    {
        AiLifecycleSnapshots.ValidateObservation(kind, result is not null, failureCode);
        Kind = kind;
        Result = result is null ? null : AiLifecycleSnapshots.ToolResult(result);
        FailureCode = failureCode;
    }
    public AiDispatchCompletionKind Kind { get; }
    public AiToolResult? Result { get; }
    public string FailureCode { get; }
}

/// <summary>
/// The durable owner privately binds trusted task/attempt/profile identity. Admit only after the
/// reservation commits; null is never an admission. Existing callers do not require this hook.
/// Concrete implementations must also reject JSON and keep authority in private host state.
/// </summary>
[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public interface IAiInvocationLifecycle : IAiHostOnlyLifecycleValue
{
    ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call, CancellationToken cancellationToken);
}

/// <summary>
/// One provider dispatch, including its dynamic and returned tool calls. Recording the provider
/// outcome settles provider usage only; tools reserve separately. This scope remains a correlator
/// after provider return, never renewed authority. Recheck authority and balances at tool admission.
/// Outcome recording owns a bounded persistence timeout independent of worker cancellation; it
/// must not start unbounded background retries. Late billing evidence cannot publish task results.
/// </summary>
[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public interface IAiProviderCallScope : IAiHostOnlyLifecycleValue
{
    ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome);
    ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch, CancellationToken cancellationToken);
}

/// <summary>One real tool execution. Preserve returned evidence even if recording fails.</summary>
[JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
public interface IAiToolDispatchScope : IAiHostOnlyLifecycleValue
{
    ValueTask RecordToolOutcomeAsync(AiToolDispatchObservation outcome);
}

public sealed class AiLifecycleException : Exception
{
    public AiLifecycleException(string code, string message) : base(Bound(message))
    {
        if (!ValidCode(code)) throw new ArgumentException("Lifecycle errors require a bounded safe code.", nameof(code));
        Code = code;
    }
    public string Code { get; }
    internal static bool ValidCode(string value) => !string.IsNullOrEmpty(value) && value.Length <= 80
        && value[0] is >= 'A' and <= 'Z' && value.All(symbol => symbol is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    private static string Bound(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Length <= 512 ? message : message[..512];
    }
}

/// <summary>Rejects both JSON directions, including interface-typed serialization of opaque scopes.</summary>
public sealed class AiHostOnlyLifecycleJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeof(IAiHostOnlyLifecycleValue).IsAssignableFrom(typeToConvert);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Reject<>).MakeGenericType(typeToConvert))!;
    private sealed class Reject<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("AI lifecycle values are host-only and cannot be imported.");
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            throw new JsonException("AI lifecycle values are host-only and cannot be exported.");
    }
}

internal static class AiLifecycleSnapshots
{
    internal static AiProviderRequest Request(AiProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with
        {
            ToolExecutor = null,
            Messages = Array.AsReadOnly(request.Messages.Select(message => message with
            {
                ToolCalls = message.ToolCalls is null ? null : Array.AsReadOnly(message.ToolCalls.ToArray()),
                Media = Media(message.Media)
            }).ToArray()),
            Tools = Array.AsReadOnly(request.Tools.Select(tool => tool with { }).ToArray())
        };
    }
    internal static AiProviderResponse Response(AiProviderResponse response) => response with
    {
        ToolCalls = Array.AsReadOnly(response.ToolCalls.ToArray()),
        Model = response.Model is null ? null : response.Model with
        {
            ReasoningEfforts = Array.AsReadOnly(response.Model.ReasoningEfforts.ToArray())
        }
    };
    internal static AiToolResult ToolResult(AiToolResult result) => result with { Media = Media(result.Media) };
    private static IReadOnlyList<AiMediaContent>? Media(IReadOnlyList<AiMediaContent>? media) =>
        media is null ? null : Array.AsReadOnly(media.Select(value => value with { }).ToArray());
    internal static void ValidateObservation(AiDispatchCompletionKind kind, bool hasResult, string failureCode)
    {
        if (!Enum.IsDefined(kind) || (kind == AiDispatchCompletionKind.Returned) != hasResult
            || (failureCode != "" && !AiLifecycleException.ValidCode(failureCode)))
            throw new AiLifecycleException("AI_LIFECYCLE_OBSERVATION_INVALID", "Dispatch completion and retained result do not agree.");
    }
}
