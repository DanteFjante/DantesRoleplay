using System.Text;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Story;

/// <summary>Closed vocabulary for a durable, backend-owned story plan.</summary>
public static class StoryPlanStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string? value) => value is Completed or Blocked or Failed or Cancelled;
    public static bool IsKnown(string? value) => value is Pending or Running or Completed or Blocked or Failed or Cancelled;
}

public static class StoryPlanStepStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    public static bool IsTerminal(string? value) => value is Completed or Blocked or Failed or Skipped;
    public static bool IsKnown(string? value) => value is Pending or Running or Completed or Blocked or Failed or Skipped;
}

public static class StoryPlanStepKind
{
    public const string CampaignContext = "campaign-context";
    public const string Knowledge = "knowledge";
    public const string Action = "action";

    public static bool IsKnown(string? value) => value is CampaignContext or Knowledge or Action;
}

public sealed record StoryPlanStartRequest(
    string Operation,
    string RequestToken,
    string CampaignId,
    string Objective,
    IReadOnlyList<StoryPlanStepRequest> Steps);

public sealed record StoryPlanStepRequest(
    string Id,
    string Kind,
    string Intent,
    IReadOnlyDictionary<string, string>? RoleEntityIds = null,
    string Input = "{}");

public sealed record StoryPlanCancelRequest(
    string Operation,
    string StoryPlanId,
    int ExpectedRevision);

public sealed record StoryPlanQueryRequest(
    string StoryPlanId,
    int? AfterRevision = null,
    int WaitSeconds = 0);

public sealed record StoryPlanStepResult(
    string Id,
    string Kind,
    string Status,
    string Summary,
    IReadOnlyList<string> Findings,
    string Narration,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> AffectedEntityIds,
    string OperationId = "");

public sealed record StoryHandoff(
    string Objective,
    string Outcome,
    IReadOnlyList<string> ContextSummaries,
    IReadOnlyList<string> FactsLearned,
    IReadOnlyList<string> ActionNarrations,
    IReadOnlyList<string> AffectedEntityIds,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> ProcedureIdsForNextTurn);

public sealed record StoryPlanResult(
    string StoryPlanId,
    string CampaignId,
    string Status,
    int Revision,
    string Objective,
    int CompletedStepCount,
    IReadOnlyList<StoryPlanStepResult> Steps,
    StoryHandoff? Handoff,
    string StopCode = "",
    string StopMessage = "");

public sealed record StoryPlanProblem(string Code, string Message);

public sealed record StoryPlanValidationResult(bool Valid, StoryPlanProblem? Problem = null)
{
    public static StoryPlanValidationResult Invalid(string code, string message) => new(false, new(code, message));
    public static readonly StoryPlanValidationResult Success = new(true);
}

public interface IStoryPlanCoordinator
{
    Task<StoryPlanResult> StartAsync(StoryPlanStartRequest request, CancellationToken cancellationToken = default);
    Task<StoryPlanResult> CancelAsync(StoryPlanCancelRequest request, CancellationToken cancellationToken = default);
    Task<StoryPlanResult> GetAsync(StoryPlanQueryRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProcedureEvidence(string Id, int Version, string SourceHash);

public sealed record ProcedureBoundActionVerification(
    string Status,
    string Reason,
    IReadOnlyList<string> MissingInformation,
    string ErrorCode = "")
{
    public bool Ready => Status == "ready";
}

public interface IProcedureBoundActionVerifier
{
    Task<ProcedureBoundActionVerification> VerifyAsync(
        string objective,
        LocalActionProposal proposal,
        int mechanicVersion,
        IReadOnlyList<ProcedureDetail> procedures,
        IReadOnlyList<string> priorSummaries,
        CancellationToken cancellationToken = default);
}

public sealed record StoryActionPreparation(
    LocalActionProposal? Proposal,
    IReadOnlyList<ProcedureEvidence> ProcedureEvidence,
    string MechanicId,
    int? MechanicVersion,
    string ErrorCode = "",
    string ErrorMessage = "",
    IReadOnlyList<string>? MissingInformation = null)
{
    public bool Ready => Proposal is not null && ErrorCode.Length == 0;
}

/// <summary>Pure request limits. No persistence, authorization, model, or world reads occur here.</summary>
public static class StoryPlanValidator
{
    public static StoryPlanValidationResult Validate(StoryPlanStartRequest? request, string? serializedRequest = null)
    {
        if (request is null) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A story-plan start request is required.");
        if (request.Operation != "start") return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "operation must be exactly 'start'.");
        if (!Match(request.RequestToken, "^[A-Za-z0-9][A-Za-z0-9.-]{7,99}$")) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "requestToken has an invalid format.");
        if (!Id(request.CampaignId)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "campaignId must be a canonical dotted id.");
        if (!Text(request.Objective, 1, 1000)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "objective must be trimmed and 1–1000 characters.");
        if (request.Steps is null || request.Steps.Count is < 1 or > 6) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "steps must contain 1–6 items.");
        // Direct callers have no transport payload, but the same public byte budget still applies.
        // MCP supplies its exact raw payload so duplicate-property rejection and the transport limit
        // are evaluated over precisely what the caller sent.
        if (Encoding.UTF8.GetByteCount(serializedRequest ?? JsonSerializer.Serialize(request)) > 16_000)
            return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "The serialized request exceeds 16000 bytes.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var actions = 0;
        var contexts = 0;
        for (var index = 0; index < request.Steps.Count; index++)
        {
            var step = request.Steps[index];
            if (step is null || !Match(step.Id, "^[a-z][a-z0-9-]{0,39}$") || !ids.Add(step.Id)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "Each step needs a unique valid id.");
            if (!StoryPlanStepKind.IsKnown(step.Kind)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A step kind is not supported.");
            if (!Text(step.Intent, 1, 500)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A step intent must be trimmed and 1–500 characters.");
            var emptyRoles = step.RoleEntityIds is null || step.RoleEntityIds.Count == 0;
            if (step.Kind is StoryPlanStepKind.CampaignContext or StoryPlanStepKind.Knowledge)
            {
                if (!emptyRoles || step.Input != "{}") return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "Context and knowledge steps allow no roles and require input {}.");
            }
            if (step.Kind == StoryPlanStepKind.CampaignContext && (++contexts != 1 || index != 0)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "campaign-context may appear once and must be first.");
            if (step.Kind == StoryPlanStepKind.Action)
            {
                if (++actions > 4) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A plan may contain at most four action steps.");
                if (step.RoleEntityIds is { Count: > 12 }) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "An action has too many roles.");
                if (step.RoleEntityIds is not null && step.RoleEntityIds.Any(pair => !Text(pair.Key, 1, 100) || !Id(pair.Value))) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "An action role is invalid.");
                if (!JsonObject(step.Input) || Encoding.UTF8.GetByteCount(step.Input) > 4_000) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "Action input must be a JSON object of at most 4000 bytes.");
            }
        }
        return StoryPlanValidationResult.Success;
    }

    public static StoryPlanValidationResult Validate(StoryPlanCancelRequest? request) => request is null || request.Operation != "cancel" || !StoryPlanId(request.StoryPlanId) || request.ExpectedRevision < 1
        ? StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "The cancel request is invalid.")
        : StoryPlanValidationResult.Success;

    public static StoryPlanValidationResult Validate(StoryPlanQueryRequest? request) => request is null || !StoryPlanId(request.StoryPlanId) || request.AfterRevision is < 0 || request.WaitSeconds is < 0 or > 20
        ? StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "The story-plan query is invalid.")
        : StoryPlanValidationResult.Success;

    public static bool StoryPlanId(string? value) => value is { Length: 43 } && value.StartsWith("story-plan.", StringComparison.Ordinal) && value[11..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Id(string? value) => value is { Length: > 0 and <= 200 } && value == value.Trim() && value.All(c => char.IsLower(c) || char.IsDigit(c) || c is '.' or '-');
    private static bool Text(string? value, int min, int max) => value is not null && value == value.Trim() && value.Length >= min && value.Length <= max;
    private static bool Match(string? value, string expression) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, expression, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static bool JsonObject(string? text)
    {
        try { using var document = JsonDocument.Parse(text!); return document.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}

/// <summary>Exact JSON transport parser. It rejects duplicate and unknown properties before model binding.</summary>
public static class StoryPlanJsonParser
{
    public static StoryPlanValidationResult TryParseStart(JsonElement payload, out StoryPlanStartRequest? request)
    {
        request = null;
        if (!ExactObject(payload, ["operation", "requestToken", "campaignId", "objective", "steps"], [], out var values, out var error)) return error!;
        if (!String(values, "operation", out var operation) || !String(values, "requestToken", out var token) || !String(values, "campaignId", out var campaign) || !String(values, "objective", out var objective) || !values["steps"].ValueKind.Equals(JsonValueKind.Array)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "The start payload has invalid field types.");
        var steps = new List<StoryPlanStepRequest>();
        foreach (var item in values["steps"].EnumerateArray())
        {
            if (!ExactObject(item, ["id", "kind", "intent"], ["roleEntityIds", "input"], out var stepValues, out error)) return error!;
            if (!String(stepValues, "id", out var id) || !String(stepValues, "kind", out var kind) || !String(stepValues, "intent", out var intent)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A step has invalid required fields.");
            // The only collection default in the transport is an empty role map. Explicit null is
            // accepted for compatibility but has the same canonical in-memory meaning; downstream
            // validation and persistence never need to distinguish the three spellings.
            IReadOnlyDictionary<string, string> roles = new Dictionary<string, string>(StringComparer.Ordinal);
            if (stepValues.TryGetValue("roleEntityIds", out var roleValue) && roleValue.ValueKind != JsonValueKind.Null)
            {
                if (roleValue.ValueKind != JsonValueKind.Object) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "roleEntityIds must be an object or null.");
                var roleMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in roleValue.EnumerateObject())
                {
                    if (!roleMap.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty) || property.Value.ValueKind != JsonValueKind.String) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "roleEntityIds must have unique string values.");
                }
                roles = roleMap;
            }
            var input = "{}";
            if (stepValues.TryGetValue("input", out var inputValue))
            {
                if (inputValue.ValueKind != JsonValueKind.String) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "input must be a string.");
                input = inputValue.GetString() ?? string.Empty;
            }
            steps.Add(new(id, kind, intent, roles, input));
        }
        request = new(operation, token, campaign, objective, steps);
        return StoryPlanValidator.Validate(request, payload.GetRawText());
    }

    public static StoryPlanValidationResult TryParseCancel(JsonElement payload, out StoryPlanCancelRequest? request)
    {
        request = null;
        if (!ExactObject(payload, ["operation", "storyPlanId", "expectedRevision"], [], out var values, out var error)) return error!;
        if (!String(values, "operation", out var operation) || !String(values, "storyPlanId", out var id) || values["expectedRevision"].ValueKind != JsonValueKind.Number || !values["expectedRevision"].TryGetInt32(out var revision)) return StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "The cancel payload has invalid field types.");
        request = new(operation, id, revision);
        return StoryPlanValidator.Validate(request);
    }

    private static bool ExactObject(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional, out Dictionary<string, JsonElement> properties, out StoryPlanValidationResult? error)
    {
        properties = new(StringComparer.Ordinal); error = null;
        if (value.ValueKind != JsonValueKind.Object) { error = StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A story-plan payload object is required."); return false; }
        var allowed = new HashSet<string>(required.Concat(optional), StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !properties.TryAdd(property.Name, property.Value)) { error = StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A story-plan payload contains an unknown or duplicate property."); return false; }
        }
        foreach (var name in required)
            if (!properties.ContainsKey(name)) { error = StoryPlanValidationResult.Invalid("INVALID_STORY_PLAN", "A story-plan payload is missing a required property."); return false; }
        return true;
    }

    private static bool String(IReadOnlyDictionary<string, JsonElement> values, string name, out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(name, out var item) || item.ValueKind != JsonValueKind.String) return false;
        value = item.GetString() ?? string.Empty;
        return true;
    }
}
