using System.Text.Json;

namespace DantesRoleplay.Interactions;

public static class InteractionOuterProtocol
{
    public const string OuterTurnTask = "system.interaction.outer-turn";
    public const string NarrationTask = "system.interaction.narration";
    public const string OuterTurnSchemaName = "interaction_outer_turn_v1";
    public const string NarrationSchemaName = "interaction_narration_v1";

    public const string OuterTurnPrompt = """
        You are the application-facing conversation coordinator. Return only the closed JSON decision.
        Use respond for an ordinary non-action reply, delegate when the local application planner should
        resolve an action, or direct-plan when the outer planner should resolve it. Never claim an action
        happened and never request tools.
        """;

    public const string NarrationPrompt = """
        Narrate only the supplied safe execution result. Do not invent effects, state, rolls, success,
        or hidden context. Preserve failures and partial progress. Return only the closed JSON object.
        """;

    public const string OuterTurnSchema = """
        {"type":"object","oneOf":[
          {"properties":{"decision":{"const":"respond"},"text":{"type":"string","minLength":1,"maxLength":4000}},"required":["decision","text"],"additionalProperties":false},
          {"properties":{"decision":{"const":"delegate"},"intentText":{"type":"string","minLength":1,"maxLength":4000}},"required":["decision","intentText"],"additionalProperties":false},
          {"properties":{"decision":{"const":"direct-plan"},"intentText":{"type":"string","minLength":1,"maxLength":4000}},"required":["decision","intentText"],"additionalProperties":false}
        ]}
        """;

    public const string NarrationSchema = """
        {"type":"object","properties":{"narration":{"type":"string","minLength":1,"maxLength":4000}},"required":["narration"],"additionalProperties":false}
        """;
}

public enum InteractionOuterDecision { Respond, Delegate, DirectPlan }

public sealed record InteractionOuterTurnRequest(string PlayerText, string? PriorSafeResultCode = null);
public sealed record InteractionOuterTurnResult(
    bool Available, InteractionOuterDecision? Decision, string Text, string Code)
{
    public static InteractionOuterTurnResult Unavailable(string code) => new(false, null, "", code);
}

public sealed record InteractionNarrationRequest(
    string PlayerText,
    string ExecutionStatus,
    string ExecutionCode,
    IReadOnlyList<string> MechanicNarration,
    IReadOnlyList<string> ReceiptReferences);
public sealed record InteractionNarrationResult(bool Available, string Narration, string Code)
{
    public static InteractionNarrationResult Unavailable(string code) => new(false, "", code);
}

public interface IInteractionOuterTurnProvider
{
    Task<InteractionOuterTurnResult> DecideAsync(
        InteractionOuterTurnRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInteractionNarrationProvider
{
    Task<InteractionNarrationResult> NarrateAsync(
        InteractionNarrationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UnavailableInteractionOuterProvider : IInteractionOuterTurnProvider, IInteractionNarrationProvider
{
    public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionOuterTurnResult.Unavailable("OUTER_MODEL_UNAVAILABLE"));
    public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionNarrationResult.Unavailable("NARRATION_MODEL_UNAVAILABLE"));
}
