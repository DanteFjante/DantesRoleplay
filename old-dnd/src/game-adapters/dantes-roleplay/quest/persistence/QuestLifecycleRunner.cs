using System.Text.Json;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.Quest;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Q2.1–Q2.2's sole lifecycle owner for closed quest transitions.</summary>
public sealed class QuestLifecycleRunner(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IEffectApplier effects,
    IOperationLog log) : IQuestLifecycleRunner
{
    private const string Procedure = "procedure.quest.modify";
    private const string QuestRoot = "game.core.quest.root";
    private const string QuestObjective = "game.core.quest.objective";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<QuestTransitionResult> TransitionAsync(
        QuestLifecycleRequest request,
        string intent = "",
        IReadOnlyList<string>? proceduresUsed = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(request?.QuestId ?? string.Empty, request?.Operation ?? string.Empty, request?.Reason ?? string.Empty, intent, proceduresUsed, state => Build(request, state), cancellationToken);

    public async Task<QuestTransitionResult> TransitionObjectiveAsync(
        QuestObjectiveTransitionRequest request,
        string intent = "",
        IReadOnlyList<string>? proceduresUsed = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(request?.QuestId ?? string.Empty, request?.Operation ?? string.Empty, request?.Reason ?? string.Empty, intent, proceduresUsed, state => BuildObjective(request, state), cancellationToken);

    private async Task<QuestTransitionResult> ExecuteAsync(
        string questId,
        string operationName,
        string reason,
        string intent,
        IReadOnlyList<string>? proceduresUsed,
        Func<State, Draft> build,
        CancellationToken cancellationToken)
    {
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Change quest lifecycle." : intent;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var loaded = await LoadAsync(questId, cancellationToken);
            if (loaded.State is null)
                return await RejectAfterRollbackAsync(questId, loaded.Problem!, auditIntent, cited, transaction, CancellationToken.None);

            var draft = build(loaded.State);
            if (draft.Problem is not null)
                return await RejectAfterRollbackAsync(questId, draft.Problem, auditIntent, cited, transaction, CancellationToken.None);

            var dry = await _effects.ApplyAsync(draft.Effects, true, cancellationToken);
            if (!dry.Valid || dry.Blocked)
                return await RejectAfterRollbackAsync(questId, Problem("QUEST_EFFECTS_REJECTED", "payload", Failure(dry)), auditIntent, cited, transaction, CancellationToken.None);

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(draft.Effects, false, cancellationToken, operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
                return await RejectAfterRollbackAsync(questId, Problem("QUEST_EFFECTS_REJECTED", "payload", Failure(applied)), auditIntent, cited, transaction, CancellationToken.None);

            var operation = await _log.RecordAsync(
                "commit",
                $"Quest '{questId}' was {operationName}: {reason}",
                true,
                auditIntent,
                questId,
                cited,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("succeeded", questId, operation.Id, applied.AcceptedEvents.Count, draft.ChangedObjectiveIds, []);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            return await RejectAfterRollbackAsync(questId, Problem("QUEST_LIFECYCLE_FAILED", "payload", ex.Message), auditIntent, cited, transaction, CancellationToken.None);
        }
    }

    private Draft Build(QuestLifecycleRequest? request, State state)
    {
        if (request is null || !Id(request.QuestId, "quest.") || !Text(request.Reason, 1000) ||
            request.Operation is not ("offer" or "accept" or "reconcile" or "fail" or "reopen-quest" or "archive") || !Text(request.ExpectedQuestStatus, 32))
            return Draft.Fail("INVALID_LIFECYCLE_REQUEST", "payload", "Quest lifecycle requires a closed quest id, expected status, supported operation, and factual reason.");

        var validStatus = request.Operation switch
        {
            "offer" => request.ExpectedQuestStatus == "draft" && state.Status == "draft",
            "accept" => request.ExpectedQuestStatus == "offered" && state.Status == "offered",
            "reconcile" or "fail" => request.ExpectedQuestStatus == "active" && state.Status == "active",
            "reopen-quest" => request.ExpectedQuestStatus is "completed" or "failed" && state.Status == request.ExpectedQuestStatus,
            "archive" => request.ExpectedQuestStatus == "offered" && state.Status == "offered",
            _ => false
        };
        if (!validStatus)
            return Draft.Fail("STALE_QUEST_STATUS", "expectedQuestStatus", "Quest status no longer permits the requested lifecycle operation.");

        if (request.Operation == "reconcile")
        {
            var nextStatus = state.Objectives.Any(objective => objective.Required && objective.Status == "failed") ? "failed"
                : state.Objectives.Where(objective => objective.Required).All(objective => objective.Status == "completed") ? "completed" : null;
            return nextStatus is null
                ? Draft.Fail("NO_RECONCILIATION_CHANGE", "operation", "Required objectives do not yet determine a terminal quest status.")
                : new Draft([Set(state.QuestId, QuestRoot, ReplaceStatus(state.RootData, nextStatus))], [], null);
        }

        var nextRootStatus = request.Operation switch { "offer" => "offered", "accept" => "active", "fail" => "failed", "reopen-quest" => "active", "archive" => "archived", _ => throw new InvalidOperationException("Unsupported validated quest operation.") };
        var effects = new List<Effect> { Set(state.QuestId, QuestRoot, ReplaceStatus(state.RootData, nextRootStatus)) };
        var changed = new List<string>();
        if (request.Operation == "accept")
        {
            foreach (var objective in state.Objectives.Where(objective => objective.Status == "dormant" && objective.PrerequisiteIds.All(id => state.Objectives.Single(candidate => candidate.Id == id).Status == "completed"))
                         .OrderBy(objective => objective.DisplayOrder).ThenBy(objective => objective.Id, StringComparer.Ordinal))
            {
                effects.Add(Set(objective.Id, QuestObjective, ReplaceStatus(objective.Data, "active")));
                changed.Add(objective.Id);
            }
        }

        return new Draft(effects, changed, null);
    }

    private Draft BuildObjective(QuestObjectiveTransitionRequest? request, State state)
    {
        if (request is null || !Id(request.QuestId, "quest.") || !Id(request.ObjectiveId, "quest.") || !Text(request.Reason, 1000) ||
            request.Operation is not ("set-objective" or "unblock-objective" or "reopen-objective") || request.ExpectedQuestStatus != "active" || !Text(request.ExpectedObjectiveStatus, 32))
            return Draft.Fail("INVALID_LIFECYCLE_REQUEST", "payload", "Objective progression requires a closed active-quest request with objective id, expected status, and factual reason.");

        if (state.Status != "active")
            return Draft.Fail("STALE_QUEST_STATUS", "expectedQuestStatus", "Quest must currently be 'active'.");

        var objective = state.Objectives.SingleOrDefault(candidate => candidate.Id == request.ObjectiveId);
        if (objective is null)
            return Draft.Fail("OBJECTIVE_NOT_IN_QUEST", "objectiveId", "objectiveId must name an objective owned by this quest.");
        if (objective.Status != request.ExpectedObjectiveStatus)
            return Draft.Fail("STALE_OBJECTIVE_STATUS", "expectedObjectiveStatus", "Objective status no longer matches the request.");

        if (request.Operation == "set-objective")
        {
            if (request.TargetStatus is not ("completed" or "blocked" or "failed") || objective.Status != "active")
                return Draft.Fail("ILLEGAL_OBJECTIVE_TARGET", "targetStatus", "Only an active owned objective may be completed, blocked, or failed.");
            if (request.TargetStatus == "completed" && !PrerequisitesCompleted(objective, state.Objectives))
                return Draft.Fail("OBJECTIVE_PREREQUISITES_UNMET", "objectiveId", "Objective prerequisites must be completed before completion.");

            var effects = new List<Effect> { Set(objective.Id, QuestObjective, ReplaceStatus(objective.Data, request.TargetStatus)) };
            var changed = new List<string> { objective.Id };
            if (request.TargetStatus == "completed")
            {
                foreach (var dependant in Eligible(state.Objectives, objective.Id))
                {
                    effects.Add(Set(dependant.Id, QuestObjective, ReplaceStatus(dependant.Data, "active")));
                    changed.Add(dependant.Id);
                }
            }
            return new Draft(effects, changed, null);
        }

        if (request.Operation == "reopen-objective")
        {
            if (request.TargetStatus is not null || objective.Status is not ("completed" or "failed"))
                return Draft.Fail("ILLEGAL_OBJECTIVE_TARGET", "targetStatus", "Only a completed or failed owned objective may be reopened.");
            if (!PrerequisitesCompleted(objective, state.Objectives))
                return Draft.Fail("OBJECTIVE_PREREQUISITES_UNMET", "objectiveId", "Objective prerequisites must be completed before reopening.");
            if (HasCompletedDependant(objective.Id, state.Objectives))
                return Draft.Fail("OBJECTIVE_HAS_COMPLETED_DEPENDANT", "objectiveId", "A completed dependant prevents reopening this objective.");
            return new Draft([Set(objective.Id, QuestObjective, ReplaceStatus(objective.Data, "active"))], [objective.Id], null);
        }

        if (request.TargetStatus is not null || objective.Status != "blocked")
            return Draft.Fail("ILLEGAL_OBJECTIVE_TARGET", "targetStatus", "Only a blocked owned objective may be unblocked.");
        if (!PrerequisitesCompleted(objective, state.Objectives))
            return Draft.Fail("OBJECTIVE_PREREQUISITES_UNMET", "objectiveId", "Objective prerequisites must be completed before unblocking.");
        return new Draft([Set(objective.Id, QuestObjective, ReplaceStatus(objective.Data, "active"))], [objective.Id], null);
    }

    private static IEnumerable<Objective> Eligible(IReadOnlyList<Objective> objectives, string completedObjectiveId) =>
        objectives.Where(candidate => candidate.Status == "dormant" && candidate.PrerequisiteIds.All(id => id == completedObjectiveId || objectives.Single(prerequisite => prerequisite.Id == id).Status == "completed"))
            .OrderBy(candidate => candidate.DisplayOrder).ThenBy(candidate => candidate.Id, StringComparer.Ordinal);

    private static bool PrerequisitesCompleted(Objective objective, IReadOnlyList<Objective> objectives) =>
        objective.PrerequisiteIds.All(id => objectives.Single(candidate => candidate.Id == id).Status == "completed");

    private static bool HasCompletedDependant(string objectiveId, IReadOnlyList<Objective> objectives)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { objectiveId };
        var frontier = new Queue<string>([objectiveId]);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var dependant in objectives.Where(candidate => candidate.PrerequisiteIds.Contains(current, StringComparer.Ordinal)))
            {
                if (!visited.Add(dependant.Id)) continue;
                if (dependant.Status == "completed") return true;
                frontier.Enqueue(dependant.Id);
            }
        }
        return false;
    }

    private async Task<(State? State, QuestProblem? Problem)> LoadAsync(string questId, CancellationToken ct)
    {
        if (!Text(questId, 200)) return (null, Problem("INVALID_LIFECYCLE_REQUEST", "questId", "questId must be a trimmed nonempty identifier."));
        var quest = await _world.GetEntityAsync(questId, ct);
        if (quest is null) return (null, Problem("QUEST_NOT_FOUND", "questId", "questId does not name a quest."));
        var rootData = Component(quest, QuestRoot);
        var status = RootStatus(rootData);
        if (status is null) return (null, Problem("QUEST_GRAPH_INVALID", "questId", "Quest root state is missing or malformed."));

        var links = await _world.GetRelationshipsAsync(questId, false, ct);
        var campaignLinks = links.Where(link => link.Kind == "game.core.quest.in-campaign").ToArray();
        var arcLinks = links.Where(link => link.Kind == "game.core.quest.in-arc").ToArray();
        var chapterLinks = links.Where(link => link.Kind == "game.core.quest.in-chapter").ToArray();
        var objectiveLinks = links.Where(link => link.Kind == "game.core.quest.has-objective").ToArray();
        if (campaignLinks.Length != 1 || arcLinks.Length != 1 || chapterLinks.Length is < 1 or > 2 || objectiveLinks.Length != 3 ||
            chapterLinks.Select(link => link.ToEntityId).Distinct(StringComparer.Ordinal).Count() != chapterLinks.Length ||
            objectiveLinks.Select(link => link.ToEntityId).Distinct(StringComparer.Ordinal).Count() != objectiveLinks.Length)
            return (null, Problem("QUEST_GRAPH_INVALID", "questId", "Quest context or objective membership links are invalid."));

        var campaign = await _world.GetEntityAsync(campaignLinks[0].ToEntityId, ct);
        if (campaign is null || Status(Component(campaign, "game.core.campaign.root")) != "active")
            return (null, Problem("QUEST_CONTEXT_INVALID", "questId", "Quest campaign must be active."));
        var campaignLinksOut = await _world.GetRelationshipsAsync(campaign.Id, false, ct);
        var worldLinks = campaignLinksOut.Where(link => link.Kind == "game.core.campaign.in-world").ToArray();
        var world = worldLinks.Length == 1 ? await _world.GetEntityAsync(worldLinks[0].ToEntityId, ct) : null;
        if (world is null || Status(Component(world, "game.core.world.root")) != "active")
            return (null, Problem("QUEST_CONTEXT_INVALID", "questId", "Quest campaign must have one active world."));

        var arc = await _world.GetEntityAsync(arcLinks[0].ToEntityId, ct);
        if (arc is null || Status(Component(arc, "game.core.campaign.arc")) != "active" ||
            campaignLinksOut.Count(link => link.Kind == "game.core.campaign.has-arc" && link.ToEntityId == arc.Id) != 1)
            return (null, Problem("QUEST_CONTEXT_INVALID", "questId", "Quest arc must be the campaign's active linked arc."));

        foreach (var chapterLink in chapterLinks)
        {
            var chapter = await _world.GetEntityAsync(chapterLink.ToEntityId, ct);
            if (chapter is null || Status(Component(chapter, "game.core.campaign.chapter")) is not ("active" or "closed") ||
                campaignLinksOut.Count(link => link.Kind == "game.core.campaign.has-chapter" && link.ToEntityId == chapter.Id) != 1)
                return (null, Problem("QUEST_CONTEXT_INVALID", "questId", "Quest chapters must be active-or-closed campaign chapters."));
            var chapterArcLinks = (await _world.GetRelationshipsAsync(chapter.Id, false, ct)).Where(link => link.Kind == "game.core.campaign.chapter.in-arc").ToArray();
            if (chapterArcLinks.Length != 1 || chapterArcLinks[0].ToEntityId != arc.Id)
                return (null, Problem("QUEST_CONTEXT_INVALID", "questId", "Every quest chapter must belong to the quest arc."));
        }

        var objectives = new List<Objective>();
        foreach (var objectiveLink in objectiveLinks)
        {
            var objective = await _world.GetEntityAsync(objectiveLink.ToEntityId, ct);
            var parsed = Objective.Parse(objective?.Id, Component(objective, QuestObjective));
            if (parsed is null) return (null, Problem("QUEST_GRAPH_INVALID", "questId", "An owned objective has missing or malformed state."));
            objectives.Add(parsed);
        }
        if (!objectives.Select(objective => objective.DisplayOrder).OrderBy(order => order).SequenceEqual([1, 2, 3]))
            return (null, Problem("QUEST_GRAPH_INVALID", "questId", "Quest objectives must have display orders one through three."));

        var owned = objectives.Select(objective => objective.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var objective in objectives)
        {
            var prerequisites = (await _world.GetRelationshipsAsync(objective.Id, false, ct))
                .Where(link => link.Kind == "game.core.quest.objective.depends-on").Select(link => link.ToEntityId).ToArray();
            if (prerequisites.Distinct(StringComparer.Ordinal).Count() != prerequisites.Length || prerequisites.Any(id => !owned.Contains(id)) ||
                prerequisites.Any(id => objectives.Single(candidate => candidate.Id == id).DisplayOrder >= objective.DisplayOrder))
                return (null, Problem("QUEST_GRAPH_INVALID", "questId", "Objective dependencies must be unique earlier owned objectives."));
            objective.PrerequisiteIds = prerequisites;
        }

        return (new State(questId, rootData!, status, objectives), null);
    }

    private async Task<QuestTransitionResult> RejectAfterRollbackAsync(string questId, QuestProblem problem, string intent, IReadOnlyList<string> cited, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
        var operation = await _log.RecordAsync("commit", "Quest lifecycle change was rejected; no state changed.", false, intent, questId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: ct);
        return new("rejected", questId, operation.Id, null, [], [problem]);
    }

    private static QuestProblem Problem(string code, string path, string reason) => new(code, path, reason, "Correct the request and retry.");
    private static Effect Set(string entityId, string definitionId, string data) => new() { Type = EffectType.ComponentSet, EntityId = entityId, DefinitionId = definitionId, Data = data };
    private static string? Component(EntitySnapshot? entity, string definitionId) => entity?.Components.SingleOrDefault(component => component.DefinitionId == definitionId)?.Data;
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Id(string? value, string prefix = "") => Text(value, 200) && value!.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static string? Status(string? data) { try { using var document = JsonDocument.Parse(data ?? string.Empty); return String(document.RootElement, "status"); } catch { return null; } }
    private static string? RootStatus(string? data)
    {
        try
        {
            using var document = JsonDocument.Parse(data ?? string.Empty);
            var root = document.RootElement;
            var status = String(root, "status");
            return status is "draft" or "offered" or "active" or "completed" or "failed" or "archived"
                && Text(String(root, "Premise"), 1000) && Text(String(root, "Summary"), 1000) && Visibility(String(root, "Visibility"))
                ? status : null;
        }
        catch { return null; }
    }
    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool Visibility(string? value) => value is "party" or "gm";
    private static string ReplaceStatus(string data, string status) { var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data)!; value["status"] = JsonSerializer.SerializeToElement(status); return JsonSerializer.Serialize(value); }
    private static string Failure(EffectResult result) => result.Blocked ? $"A guard blocked quest lifecycle: {result.BlockCode}: {result.BlockReason}" : result.Problems.Count == 0 ? "Derived effects did not apply." : string.Join(" ", result.Problems.Select(problem => problem.Problem));

    private sealed record Draft(IReadOnlyList<Effect> Effects, IReadOnlyList<string> ChangedObjectiveIds, QuestProblem? Problem)
    { public static Draft Fail(string code, string path, string reason) => new([], [], QuestLifecycleRunner.Problem(code, path, reason)); }
    private sealed record State(string QuestId, string RootData, string Status, IReadOnlyList<Objective> Objectives);
    private sealed class Objective
    {
        public required string Id { get; init; }
        public required string Data { get; init; }
        public required string Status { get; init; }
        public required int DisplayOrder { get; init; }
        public required bool Required { get; init; }
        public IReadOnlyList<string> PrerequisiteIds { get; set; } = [];
        public static Objective? Parse(string? id, string? data)
        {
            try
            {
                using var document = JsonDocument.Parse(data ?? string.Empty);
                var root = document.RootElement;
                var status = String(root, "status");
                var order = root.TryGetProperty("DisplayOrder", out var displayOrder) && displayOrder.ValueKind == JsonValueKind.Number ? displayOrder.GetInt32() : 0;
                return !Id(id, "quest.") || status is not ("dormant" or "active" or "blocked" or "completed" or "failed") || order is < 1 or > 3 ||
                       !Text(String(root, "ActionableSummary"), 1000) || !root.TryGetProperty("Required", out var required) || required.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !Visibility(String(root, "Visibility"))
                    ? null : new Objective { Id = id!, Data = data!, Status = status!, DisplayOrder = order, Required = required.GetBoolean() };
            }
            catch { return null; }
        }
    }
}
