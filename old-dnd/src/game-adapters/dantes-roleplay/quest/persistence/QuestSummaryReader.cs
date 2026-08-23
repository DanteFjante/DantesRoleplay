using System.Text.Json;
using DantesRoleplay.Events;
using DantesRoleplay.Quest;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Q3.1's fixed trusted-host read model. Invalid present state is never projected.</summary>
public sealed class QuestSummaryReader(IWorldStore world, IEventLedger events) : IQuestSummaryReader
{
    private const string Root = "game.core.quest.root";
    private const string Objective = "game.core.quest.objective";
    private readonly IWorldStore _world = world;
    private readonly IEventLedger _events = events;

    public async Task<QuestSummary?> GetAsync(string questId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Id(questId, "quest.")) return null;
            var quest = await _world.GetEntityAsync(questId, cancellationToken);
            var root = RootData(Component(quest, Root));
            if (quest is null || root is null || root.Status != "active") return null;

            var links = await _world.GetRelationshipsAsync(questId, false, cancellationToken);
            var campaign = Single(links, "game.core.quest.in-campaign");
            var arc = Single(links, "game.core.quest.in-arc");
            var chapters = links.Where(x => x.Kind == "game.core.quest.in-chapter").ToArray();
            var objectiveLinks = links.Where(x => x.Kind == "game.core.quest.has-objective").ToArray();
            if (campaign is null || arc is null || chapters.Length is < 1 or > 2 || objectiveLinks.Length != 3 ||
                !Unique(chapters) || !Unique(objectiveLinks)) return null;
            if (!await ValidContextAsync(campaign.ToEntityId, arc.ToEntityId, chapters.Select(x => x.ToEntityId), cancellationToken)) return null;

            var objectives = new List<ObjectiveState>();
            foreach (var link in objectiveLinks)
            {
                var entity = await _world.GetEntityAsync(link.ToEntityId, cancellationToken);
                var parsed = ObjectiveData(entity?.Id, entity?.Name, Component(entity, Objective));
                if (parsed is null) return null;
                parsed.Evidence = await ReadEvidenceAsync(parsed.Id, cancellationToken);
                if (parsed.Evidence is null) return null;
                objectives.Add(parsed);
            }
            if (!objectives.Select(x => x.DisplayOrder).OrderBy(x => x).SequenceEqual([1, 2, 3])) return null;

            var owned = objectives.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var objective in objectives)
            {
                var prerequisites = (await _world.GetRelationshipsAsync(objective.Id, false, cancellationToken))
                    .Where(x => x.Kind == "game.core.quest.objective.depends-on").Select(x => x.ToEntityId).ToArray();
                if (prerequisites.Distinct(StringComparer.Ordinal).Count() != prerequisites.Length ||
                    prerequisites.Any(x => !owned.Contains(x)) ||
                    prerequisites.Any(x => objectives.Single(candidate => candidate.Id == x).DisplayOrder >= objective.DisplayOrder)) return null;
            }

            var ordered = objectives.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            var timeline = await ReadTimelineAsync(questId, ordered, cancellationToken);
            return new QuestSummary(questId, quest.Name, root.Status, root.Summary, root.Visibility,
                ordered.Select(x => new QuestObjectiveSummary(x.Id, x.Title, x.Status, x.ActionableSummary, x.Required, x.Visibility, x.DisplayOrder, x.Evidence!)).ToList(),
                timeline,
                "Trusted-host view only. Descriptive visibility is editorial metadata, not authorization.");
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private async Task<bool> ValidContextAsync(string campaignId, string arcId, IEnumerable<string> chapterIds, CancellationToken ct)
    {
        var campaign = await _world.GetEntityAsync(campaignId, ct);
        if (campaign is null || Status(Component(campaign, "game.core.campaign.root")) != "active") return false;
        var campaignLinks = await _world.GetRelationshipsAsync(campaignId, false, ct);
        var world = Single(campaignLinks, "game.core.campaign.in-world");
        if (world is null || Status(Component(await _world.GetEntityAsync(world.ToEntityId, ct), "game.core.world.root")) != "active") return false;
        if (campaignLinks.Count(x => x.Kind == "game.core.campaign.has-arc" && x.ToEntityId == arcId) != 1 ||
            Status(Component(await _world.GetEntityAsync(arcId, ct), "game.core.campaign.arc")) != "active") return false;
        foreach (var chapterId in chapterIds)
        {
            var chapter = await _world.GetEntityAsync(chapterId, ct);
            if (chapter is null || Status(Component(chapter, "game.core.campaign.chapter")) is not ("active" or "closed") ||
                campaignLinks.Count(x => x.Kind == "game.core.campaign.has-chapter" && x.ToEntityId == chapterId) != 1) return false;
            var arcLinks = (await _world.GetRelationshipsAsync(chapterId, false, ct)).Where(x => x.Kind == "game.core.campaign.chapter.in-arc").ToArray();
            if (arcLinks.Length != 1 || arcLinks[0].ToEntityId != arcId) return false;
        }
        return true;
    }

    private async Task<IReadOnlyList<QuestEvidenceSummary>?> ReadEvidenceAsync(string objectiveId, CancellationToken ct)
    {
        var result = new List<QuestEvidenceSummary>();
        foreach (var link in (await _world.GetRelationshipsAsync(objectiveId, false, ct)).Where(x => x.Kind == "game.core.quest.objective.references"))
        {
            var metadata = Object(link.Data);
            var role = String(metadata, "role");
            var audience = String(metadata, "audience");
            if (metadata is null || metadata.Value.EnumerateObject().Count() != 2 || role is not ("actor" or "location" or "knowledge" or "faction") || audience is not ("party" or "gm") || await _world.GetEntityAsync(link.ToEntityId, ct) is null) return null;
            result.Add(new(link.ToEntityId, role, audience));
            if (result.Count > 5) return null;
        }
        return result.Select(x => x.TargetId).Distinct(StringComparer.Ordinal).Count() == result.Count
            ? result.OrderBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.Audience, StringComparer.Ordinal).ThenBy(x => x.TargetId, StringComparer.Ordinal).ToList()
            : null;
    }

    private async Task<IReadOnlyList<QuestTransitionSummary>> ReadTimelineAsync(string questId, IReadOnlyList<ObjectiveState> objectives, CancellationToken ct)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal) { [questId] = "quest" };
        foreach (var objective in objectives) kinds.Add(objective.Id, "objective");
        var candidates = new Dictionary<string, EventSummary>(StringComparer.Ordinal);
        foreach (var id in kinds.Keys)
            foreach (var row in await _events.FindAsync(type: "world.component.replaced", entityId: id, limit: 50, cancellationToken: ct)) candidates.TryAdd(row.Id, row);

        var result = new List<QuestTransitionSummary>();
        foreach (var row in candidates.Values)
        {
            var detail = await _events.GetAsync(row.Id, ct);
            var payload = detail is null ? null : Object(detail.PayloadJson);
            var entityId = String(payload, "entityId");
            if (detail?.TypeId != "world.component.replaced" || payload is null || entityId is null || !kinds.TryGetValue(entityId, out var kind) || !detail.EntityIds.Contains(entityId, StringComparer.Ordinal) ||
                String(payload, "definitionId") != (kind == "quest" ? Root : Objective)) continue;
            var before = Status(ObjectProperty(payload, "before"));
            var after = Status(ObjectProperty(payload, "after"));
            if (!ValidTransition(kind, before, after)) continue;
            result.Add(new(detail.Id, detail.RootOperationId, detail.Timestamp, detail.Sequence, entityId, kind, before!, after!));
        }
        return result.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Sequence).ThenByDescending(x => x.EventId, StringComparer.Ordinal).Take(12).ToList();
    }

    private static bool ValidTransition(string kind, string? before, string? after) => kind == "quest"
        ? before is ("draft" or "offered" or "active" or "completed" or "failed" or "archived") && after is ("draft" or "offered" or "active" or "completed" or "failed" or "archived")
        : before is ("dormant" or "active" or "blocked" or "completed" or "failed") && after is ("dormant" or "active" or "blocked" or "completed" or "failed");

    private static RootState? RootData(string? json)
    {
        var data = Object(json); var status = String(data, "status"); var summary = String(data, "Summary"); var visibility = String(data, "Visibility");
        return status is ("draft" or "offered" or "active" or "completed" or "failed" or "archived") && Text(String(data, "Premise"), 1000) && Text(summary, 1000) && Visibility(visibility) ? new(status, summary!, visibility!) : null;
    }

    private static ObjectiveState? ObjectiveData(string? id, string? title, string? json)
    {
        var data = Object(json); var status = String(data, "status"); var actionable = String(data, "ActionableSummary"); var visibility = String(data, "Visibility");
        var displayOrder = 0;
        var required = default(JsonElement);
        var validOrder = data is { } value && value.TryGetProperty("DisplayOrder", out var order) && order.ValueKind == JsonValueKind.Number && order.TryGetInt32(out displayOrder) && displayOrder is >= 1 and <= 3;
        var validRequired = data is { } requiredData && requiredData.TryGetProperty("Required", out required) && required.ValueKind is JsonValueKind.True or JsonValueKind.False;
        return Id(id, "quest.") && Text(title, 160) && status is ("dormant" or "active" or "blocked" or "completed" or "failed") && Text(actionable, 1000) && Visibility(visibility) && validOrder && validRequired
            ? new(id!, title!, status!, actionable!, required.GetBoolean(), visibility!, displayOrder) : null;
    }

    private static RelationshipView? Single(IEnumerable<RelationshipView> links, string kind) { var found = links.Where(x => x.Kind == kind).ToArray(); return found.Length == 1 ? found[0] : null; }
    private static bool Unique(IEnumerable<RelationshipView> links) { var list = links.ToList(); return list.Select(x => x.ToEntityId).Distinct(StringComparer.Ordinal).Count() == list.Count; }
    private static string? Component(EntitySnapshot? entity, string definitionId) => entity?.Components.SingleOrDefault(x => x.DefinitionId == definitionId)?.Data;
    private static JsonElement? Object(string? json) { try { using var document = JsonDocument.Parse(json ?? string.Empty); return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null; } catch { return null; } }
    private static JsonElement? ObjectProperty(JsonElement? value, string property) => value is { } objectValue && objectValue.TryGetProperty(property, out var propertyValue) && propertyValue.ValueKind == JsonValueKind.Object ? propertyValue : null;
    private static string? String(JsonElement? value, string property) => value is { } objectValue && objectValue.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static string? Status(JsonElement? value) => String(value, "status");
    private static string? Status(string? json) => Status(Object(json));
    private static bool Visibility(string? value) => value is "party" or "gm";
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Id(string? value, string prefix) => Text(value, 200) && value!.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');

    private sealed record RootState(string Status, string Summary, string Visibility);
    private sealed class ObjectiveState(
        string id,
        string title,
        string status,
        string actionableSummary,
        bool required,
        string visibility,
        int displayOrder)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Status { get; } = status;
        public string ActionableSummary { get; } = actionableSummary;
        public bool Required { get; } = required;
        public string Visibility { get; } = visibility;
        public int DisplayOrder { get; } = displayOrder;
        public IReadOnlyList<QuestEvidenceSummary>? Evidence { get; set; }
    }
}
