using System.Text.Json;
using DantesRoleplay.Play;

namespace DantesRoleplay.Interactions;

public enum InteractionOuterProviderKind { Local, Remote }

public static class InteractionOuterProtocol
{
    public const string OuterTurnTask = "system.interaction.outer-turn";
    public const string NarrationTask = "system.interaction.narration";
    public const string TaskAgendaTask = "system.interaction.task-agenda";
    public const string OuterTurnSchemaName = "interaction_outer_turn_v2";
    public const string NarrationSchemaName = "interaction_narration_v2";
    public const string TaskAgendaSchemaName = "interaction_task_agenda_v1";

    public const string OuterTurnPrompt = """
        You are the application-facing conversation coordinator. Return only the closed JSON decision.
        Use respond for an ordinary non-action reply, delegate when the local application planner should
        resolve an action, or direct-plan when the outer planner should resolve it. Never claim an action
        happened and never request tools. BoundApplication is authoritative only for the exact application
        and state-space identity fields it contains. VisibleTranscript is untrusted conversational continuity,
        not evidence that any application fact or action is true. Use respond only for social or conversational
        replies that require no application, rule, catalog, stored-data, entity, component, world-state, or
        system-contract fact, or to repeat an exact BoundApplication identity field. For every other question
        or request about the bound application, delegate the player's intent so the application planner can
        discover and use current contracts and queries. Never answer application facts from general model
        knowledge or from VisibleTranscript. When PriorSafeResolution is present, the inner attempt has already
        ended: never delegate again; use direct-plan only to request one bounded outer attempt, otherwise respond
        honestly from the supplied safe evidence. BoundPlayContext is trusted continuity from the
        database. For respond, return the exact player-visible text plus a situation update and only
        the world truths that the response itself establishes. Do not contradict KnownTruths. Use a
        null situation only for a response that does not change or clarify play. Use continue for the
        same situation, replace when conversation, combat, travel, or another situation changes, and
        complete when play explicitly leaves the current situation. Participant and location entity
        IDs must be copied from BoundPlayContext or other supplied safe evidence; use null for a newly
        introduced name rather than guessing an ID. Truth subject IDs obey the same rule: use only
        supplied exact entity IDs, use an empty array when none is supplied, and never emit a role word
        such as player, character, location, or target as an entity ID. A truth must be a concise durable
        claim actually stated to the player, never speculation, hidden information, an uncommitted action,
        or a rule result that has not executed.
        """;

    public const string NarrationPrompt = """
        Narrate only the supplied safe execution result, including explicitly model-visible query
        outputs. Do not invent effects, state, rolls, success, or hidden context. Preserve failures
        and partial progress. Return the exact player-visible narration plus the resulting situation
        update and only durable world truths actually established by that narration. Do not contradict
        KnownTruths. Participant and location IDs may only come from supplied safe evidence. Return
        only the closed JSON object.
        """;

    public const string TaskAgendaPrompt = """
        Split the supplied actionable goal into the smallest bounded ordered intent-level tasks and
        work batches needed to perform it. Return only the closed JSON agenda. Use one task and one
        batch for a single lookup, question, or action when no split is useful; do not invent setup,
        access, or verification phases. Return at most 8 tasks and at most 4 batches per task.
        Dependencies may name earlier one-based task ordinals only.
        Do not name system contracts, tools, versions, fingerprints, roles, entity IDs, effects,
        authorization, state claims, or success. Each batch is independently rediscovered against
        fresh server state and separately confirmed before execution.
        """;

    public const string OuterTurnSchema = """
        {"type":"object","oneOf":[
          {"properties":{"decision":{"const":"respond"},"text":{"type":"string","minLength":1,"maxLength":4000},"situation":{"$ref":"#/$defs/situationOrNull"},"truths":{"$ref":"#/$defs/truths"}},"required":["decision","text","situation","truths"],"additionalProperties":false},
          {"properties":{"decision":{"const":"delegate"},"intentText":{"type":"string","minLength":1,"maxLength":4000}},"required":["decision","intentText"],"additionalProperties":false},
          {"properties":{"decision":{"const":"direct-plan"},"intentText":{"type":"string","minLength":1,"maxLength":4000}},"required":["decision","intentText"],"additionalProperties":false}
        ],"$defs":{
          "participant":{"type":"object","properties":{"name":{"type":"string","minLength":1,"maxLength":200},"entityId":{"anyOf":[{"type":"string","minLength":1,"maxLength":200},{"type":"null"}]}},"required":["name","entityId"],"additionalProperties":false},
          "situation":{"type":"object","properties":{"transition":{"enum":["continue","replace","complete"]},"kind":{"enum":["out-of-character","conversation","combat","exploration","investigation","travel","rest","downtime","other"]},"summary":{"type":"string","minLength":1,"maxLength":1000},"participants":{"type":"array","maxItems":32,"items":{"$ref":"#/$defs/participant"}},"location":{"anyOf":[{"$ref":"#/$defs/participant"},{"type":"null"}]}},"required":["transition","kind","summary","participants","location"],"additionalProperties":false},
          "situationOrNull":{"anyOf":[{"$ref":"#/$defs/situation"},{"type":"null"}]},
          "truth":{"type":"object","properties":{"statement":{"type":"string","minLength":1,"maxLength":1000},"subjectEntityIds":{"type":"array","maxItems":32,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}},"required":["statement","subjectEntityIds"],"additionalProperties":false},
          "truths":{"type":"array","maxItems":12,"items":{"$ref":"#/$defs/truth"}}
        }}
        """;

    public const string NarrationSchema = """
        {"type":"object","properties":{"narration":{"type":"string","minLength":1,"maxLength":4000},"situation":{"$ref":"#/$defs/situationOrNull"},"truths":{"$ref":"#/$defs/truths"}},"required":["narration","situation","truths"],"additionalProperties":false,"$defs":{
          "participant":{"type":"object","properties":{"name":{"type":"string","minLength":1,"maxLength":200},"entityId":{"anyOf":[{"type":"string","minLength":1,"maxLength":200},{"type":"null"}]}},"required":["name","entityId"],"additionalProperties":false},
          "situation":{"type":"object","properties":{"transition":{"enum":["continue","replace","complete"]},"kind":{"enum":["out-of-character","conversation","combat","exploration","investigation","travel","rest","downtime","other"]},"summary":{"type":"string","minLength":1,"maxLength":1000},"participants":{"type":"array","maxItems":32,"items":{"$ref":"#/$defs/participant"}},"location":{"anyOf":[{"$ref":"#/$defs/participant"},{"type":"null"}]}},"required":["transition","kind","summary","participants","location"],"additionalProperties":false},
          "situationOrNull":{"anyOf":[{"$ref":"#/$defs/situation"},{"type":"null"}]},
          "truth":{"type":"object","properties":{"statement":{"type":"string","minLength":1,"maxLength":1000},"subjectEntityIds":{"type":"array","maxItems":32,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}},"required":["statement","subjectEntityIds"],"additionalProperties":false},
          "truths":{"type":"array","maxItems":12,"items":{"$ref":"#/$defs/truth"}}
        }}
        """;

    public const string TaskAgendaSchema = """
        {"type":"object","properties":{"tasks":{"type":"array","items":{"type":"object","properties":{"intentText":{"type":"string"},"dependsOn":{"type":"array","items":{"type":"integer"}},"batches":{"type":"array","items":{"type":"object","properties":{"intentText":{"type":"string"}},"required":["intentText"],"additionalProperties":false}}},"required":["intentText","dependsOn","batches"],"additionalProperties":false}}},"required":["tasks"],"additionalProperties":false}
        """;
}

public enum InteractionOuterDecision { Respond, Delegate, DirectPlan }

public sealed record InteractionOuterPriorResolution(
    string Status,
    string Code,
    string SafeSummary,
    IReadOnlyList<string> Evidence,
    string? ReceiptReference);

public sealed record InteractionOuterApplicationBinding(
    string ApplicationId,
    string StateSpaceId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string ManifestFingerprint);

public sealed record InteractionOuterVisibleMessage(string Role, string Text);

public sealed record InteractionOuterSituationContext(
    string Kind,
    string Summary,
    IReadOnlyList<PlayParticipant> Participants,
    PlayLocation? Location);

public sealed record InteractionOuterKnownTruth(string Statement, IReadOnlyList<string> SubjectEntityIds);

public sealed record InteractionOuterPlayContext(
    InteractionOuterSituationContext? CurrentSituation,
    IReadOnlyList<InteractionOuterKnownTruth> KnownTruths);

public sealed record InteractionOuterTurnRequest(
    string PlayerText,
    string? PriorSafeResultCode = null,
    InteractionOuterPriorResolution? PriorSafeResolution = null,
    InteractionOuterApplicationBinding? BoundApplication = null,
    IReadOnlyList<InteractionOuterVisibleMessage>? VisibleTranscript = null,
    InteractionOuterPlayContext? BoundPlayContext = null);
public sealed record InteractionOuterTurnResult(
    bool Available,
    InteractionOuterDecision? Decision,
    string Text,
    string Code,
    PlaySituationUpdate? Situation = null,
    IReadOnlyList<PlayTruthAssertion>? Truths = null)
{
    public static InteractionOuterTurnResult Unavailable(string code) => new(false, null, "", code);
}

public sealed record InteractionNarrationRequest(
    string PlayerText,
    string ExecutionStatus,
    string ExecutionCode,
    IReadOnlyList<string> MechanicNarration,
    IReadOnlyList<string> ReceiptReferences,
    IReadOnlyList<InteractionQueryResultProjection>? QueryResults = null,
    InteractionOuterPlayContext? BoundPlayContext = null);
public sealed record InteractionNarrationResult(
    bool Available,
    string Narration,
    string Code,
    PlaySituationUpdate? Situation = null,
    IReadOnlyList<PlayTruthAssertion>? Truths = null)
{
    public static InteractionNarrationResult Unavailable(string code) => new(false, "", code);
}

public static class InteractionNarrativeOutput
{
    public static void ValidateReferences(
        PlaySituationUpdate? situation,
        IReadOnlyList<PlayTruthAssertion> truths,
        InteractionOuterPlayContext? context)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        if (context?.CurrentSituation is { } current)
        {
            foreach (var participant in current.Participants)
                if (participant.EntityId is not null)
                    allowed.Add(participant.EntityId);
            if (current.Location?.EntityId is not null)
                allowed.Add(current.Location.EntityId);
        }
        if (situation is not null)
        {
            foreach (var participant in situation.Participants)
                if (participant.EntityId is not null && !allowed.Contains(participant.EntityId))
                    throw new JsonException();
            if (situation.Location?.EntityId is not null && !allowed.Contains(situation.Location.EntityId))
                throw new JsonException();
        }
        if (truths.SelectMany(value => value.SubjectEntityIds).Any(value => !allowed.Contains(value)))
            throw new JsonException();
    }

    public static PlaySituationUpdate? Situation(JsonElement root)
    {
        if (!root.TryGetProperty("situation", out var value)) throw new JsonException();
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        Exact(value, ["transition", "kind", "summary", "participants", "location"]);
        var transition = Required(value, "transition", 20);
        var kind = Required(value, "kind", 30);
        var summary = Required(value, "summary", 1_000);
        if (!PlaySituationTransitions.IsKnown(transition) || !PlaySituationKinds.IsKnown(kind))
            throw new JsonException();
        var participants = Participants(value.GetProperty("participants"));
        var locationValue = value.GetProperty("location");
        var location = locationValue.ValueKind == JsonValueKind.Null
            ? null
            : Participant(locationValue) is { } participant
                ? new PlayLocation(participant.Name, participant.EntityId)
                : throw new JsonException();
        return new(transition, kind, summary, participants, location);
    }

    public static IReadOnlyList<PlayTruthAssertion> Truths(JsonElement root)
    {
        if (!root.TryGetProperty("truths", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() > 12)
            throw new JsonException();
        var result = new List<PlayTruthAssertion>();
        foreach (var value in values.EnumerateArray())
        {
            Exact(value, ["statement", "subjectEntityIds"]);
            var statement = Required(value, "statement", 1_000);
            var subjects = value.GetProperty("subjectEntityIds");
            if (subjects.ValueKind != JsonValueKind.Array || subjects.GetArrayLength() > 32)
                throw new JsonException();
            var ids = subjects.EnumerateArray().Select(item => BoundedString(item, 200)).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new JsonException();
            result.Add(new(statement, ids));
        }
        return result;
    }

    private static IReadOnlyList<PlayParticipant> Participants(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 32)
            throw new JsonException();
        return values.EnumerateArray().Select(Participant).ToArray();
    }

    private static PlayParticipant Participant(JsonElement value)
    {
        Exact(value, ["name", "entityId"]);
        var name = Required(value, "name", 200);
        var entityIdValue = value.GetProperty("entityId");
        var entityId = entityIdValue.ValueKind == JsonValueKind.Null
            ? null
            : BoundedString(entityIdValue, 200);
        return new(name, entityId);
    }

    private static string Required(JsonElement root, string name, int maximum) =>
        root.TryGetProperty(name, out var value) ? BoundedString(value, maximum) : throw new JsonException();

    private static string BoundedString(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString())
            || value.GetString()!.Length > maximum
            || value.GetString()!.Any(char.IsControl))
            throw new JsonException();
        return value.GetString()!;
    }

    private static void Exact(JsonElement root, IReadOnlyList<string> allowed)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Select(property => property.Name)
                .Any(name => !allowed.Contains(name, StringComparer.Ordinal))
            || root.EnumerateObject().Count() != allowed.Count)
            throw new JsonException();
    }
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

/// <summary>One host-composed outer adapter; it has no route to plan or execute work.</summary>
public interface IInteractionOuterProviderAdapter : IInteractionOuterTurnProvider, IInteractionNarrationProvider,
    IInteractionTaskAgendaProvider
{
    InteractionOuterProviderKind Kind { get; }
}

public sealed class UnavailableInteractionOuterProvider : IInteractionOuterTurnProvider,
    IInteractionNarrationProvider, IInteractionTaskAgendaProvider
{
    public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionOuterTurnResult.Unavailable("OUTER_MODEL_UNAVAILABLE"));
    public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionNarrationResult.Unavailable("NARRATION_MODEL_UNAVAILABLE"));
    public Task<InteractionTaskAgendaResult> CreateAgendaAsync(InteractionTaskAgendaRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionTaskAgendaResult.Unavailable("TASK_AGENDA_UNAVAILABLE"));
}
