using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Events;
using DantesRoleplay.Quest;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Fixed C3 trusted-host read model. It is deliberately not a general campaign graph API.</summary>
public sealed class CampaignResumeReader(IWorldStore world, IEventLedger events, IQuestSummaryReader? quests = null) : ICampaignResumeReader
{
    private readonly IWorldStore _world = world; private readonly IEventLedger _events = events; private readonly IQuestSummaryReader? _quests = quests;

    public async Task<CampaignResume?> GetAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        var campaign = await _world.GetEntityAsync(campaignId, cancellationToken); var root = Object(Component(campaign, "game.core.campaign.root"));
        if (campaign is null || root is null || String(root, "status") != "active") return null;
        var links = await _world.GetRelationshipsAsync(campaignId, false, cancellationToken);
        var worldLink = links.SingleOrDefault(x => x.Kind == "game.core.campaign.in-world"); if (worldLink is null) return null;
        var chapters = await ReadChapters(links, cancellationToken); var arcs = await ReadArcs(links, cancellationToken);
        var references = await ReadReferences(links, cancellationToken);
        var milestones = await ReadMilestones(chapters.Where(x => x.Status == "closed"), cancellationToken);
        var quests = await ReadQuests(campaignId, arcs, chapters, cancellationToken); if (quests is null) return null;
        return new CampaignResume(campaignId, String(root, "title") ?? string.Empty, String(root, "premise") ?? string.Empty, Strings(root, "partyGoals"), Strings(root, "toneAndBoundaries"), worldLink.ToEntityId,
            chapters.SingleOrDefault(x => x.Status == "active"), arcs.SingleOrDefault(x => x.Status == "active"), references, milestones,
            "Trusted-host view only. Descriptive visibility is editorial metadata, not authorization.") { Quests = quests };
    }

    private async Task<IReadOnlyList<CampaignResumeQuest>?> ReadQuests(string campaignId, IReadOnlyList<CampaignResumeArc> arcs, IReadOnlyList<CampaignResumeChapter> chapters, CancellationToken ct)
    {
        if (_quests is null) return [];
        var chapterArc = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            var links = (await _world.GetRelationshipsAsync(chapter.Id, false, ct)).Where(link => link.Kind == "game.core.campaign.chapter.in-arc").ToArray();
            if (links.Length != 1 || !chapterArc.TryAdd(chapter.Id, links[0].ToEntityId)) return null;
        }
        var arcByQuest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arc in arcs)
            foreach (var link in (await _world.GetRelationshipsAsync(arc.Id, false, ct)).Where(link => link.Kind == "game.core.campaign.arc.features-quest"))
            {
                if (link.Data != "{}" || !arcByQuest.TryAdd(link.ToEntityId, arc.Id)) return null;
            }
        var chapterByQuest = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chapter in chapters)
            foreach (var link in (await _world.GetRelationshipsAsync(chapter.Id, false, ct)).Where(link => link.Kind == "game.core.campaign.chapter.features-quest"))
            {
                if (link.Data != "{}" || !arcByQuest.TryGetValue(link.ToEntityId, out var questArc) || chapterArc[chapter.Id] != questArc) return null;
                if (!chapterByQuest.TryGetValue(link.ToEntityId, out var linked)) chapterByQuest.Add(link.ToEntityId, linked = []);
                if (linked.Contains(chapter.Id, StringComparer.Ordinal)) return null;
                linked.Add(chapter.Id);
            }
        var result = new List<CampaignResumeQuest>();
        foreach (var pair in arcByQuest.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var summary = await _quests.GetAsync(pair.Key, ct);
            if (summary is null) continue;
            if (!chapterByQuest.TryGetValue(pair.Key, out var linked) || linked.Count == 0) return null;
            var ownerLinks = await _world.GetRelationshipsAsync(pair.Key, false, ct);
            if (ownerLinks.Count(link => link.Kind == "game.core.quest.in-campaign" && link.ToEntityId == campaignId) != 1 ||
                ownerLinks.Count(link => link.Kind == "game.core.quest.in-arc" && link.ToEntityId == pair.Value) != 1 ||
                linked.Any(chapterId => ownerLinks.Count(link => link.Kind == "game.core.quest.in-chapter" && link.ToEntityId == chapterId) != 1))
                return null;
            result.Add(new(summary.QuestId, summary.Title, summary.Status, summary.Summary, summary.Visibility, pair.Value,
                linked.Order(StringComparer.Ordinal).ToList(), summary.Objectives.OrderBy(objective => objective.DisplayOrder).ThenBy(objective => objective.Id, StringComparer.Ordinal).Take(3).ToList()));
            if (result.Count == 3) break;
        }
        return result;
    }

    private async Task<IReadOnlyList<CampaignResumeChapter>> ReadChapters(IReadOnlyList<RelationshipView> links, CancellationToken ct)
    {
        var result = new List<CampaignResumeChapter>(); foreach (var link in links.Where(x => x.Kind == "game.core.campaign.has-chapter")) { var entity = await _world.GetEntityAsync(link.ToEntityId, ct); var data = Object(Component(entity, "game.core.campaign.chapter")); if (data is null) continue; var status = String(data, "status"); var title = String(data, "title"); var question = String(data, "partyQuestion"); if (status is "active" or "closed" && title is not null && question is not null) result.Add(new(entity!.Id, status, title, question, String(data, "gmContext"))); } return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }
    private async Task<IReadOnlyList<CampaignResumeArc>> ReadArcs(IReadOnlyList<RelationshipView> links, CancellationToken ct)
    {
        var result = new List<CampaignResumeArc>(); foreach (var link in links.Where(x => x.Kind == "game.core.campaign.has-arc")) { var entity = await _world.GetEntityAsync(link.ToEntityId, ct); var data = Object(Component(entity, "game.core.campaign.arc")); if (data is null) continue; var status = String(data, "status"); var title = String(data, "title"); var stake = String(data, "partyStake"); if (status is "active" or "resolved" or "abandoned" && title is not null && stake is not null) result.Add(new(entity!.Id, status, title, stake, String(data, "gmContext"))); } return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }
    private async Task<IReadOnlyList<CampaignResumeReference>> ReadReferences(IReadOnlyList<RelationshipView> links, CancellationToken ct)
    {
        var result = new List<CampaignResumeReference>(); foreach (var link in links.Where(x => x.Kind == "game.core.campaign.references")) { var metadata = Object(link.Data); if (metadata is null) continue; var entity = await _world.GetEntityAsync(link.ToEntityId, ct); if (entity is null) continue; var component = entity.Components.FirstOrDefault(); var data = Object(component?.Data); result.Add(new(entity.Id, String(metadata, "role") ?? string.Empty, String(metadata, "audience") ?? string.Empty, entity.Name, Summary(data), String(data, "visibility"))); } return result.OrderBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.Audience, StringComparer.Ordinal).ThenBy(x => x.EntityId, StringComparer.Ordinal).ToList();
    }
    private async Task<IReadOnlyList<CampaignClosedChapterMilestone>> ReadMilestones(IEnumerable<CampaignResumeChapter> chapters, CancellationToken ct)
    {
        var milestones = new List<CampaignClosedChapterMilestone>(); foreach (var chapter in chapters) foreach (var row in await _events.FindAsync(type: "world.component.replaced", entityId: chapter.Id, limit: 100, cancellationToken: ct)) { var detail = await _events.GetAsync(row.Id, ct); var data = detail is null ? null : Object(detail.PayloadJson); var after = data is null || String(data, "definitionId") != "game.core.campaign.chapter" ? null : data.Value.GetProperty("after").ValueKind == JsonValueKind.Object ? data.Value.GetProperty("after") : (JsonElement?)null; var summary = String(after, "closingSummary"); if (summary is not null) milestones.Add(new(chapter.Id, chapter.Title, summary, row.Timestamp, row.Sequence, row.Id)); } return milestones.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Sequence).ThenByDescending(x => x.EventId, StringComparer.Ordinal).Take(5).ToList();
    }
    private static JsonElement? Object(string? json) { try { using var doc = JsonDocument.Parse(json ?? ""); return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null; } catch { return null; } }
    private static string? Component(EntitySnapshot? entity, string id) => entity?.Components.SingleOrDefault(x => x.DefinitionId == id)?.Data;
    private static string? String(JsonElement? data, string property) => data is { } value && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static IReadOnlyList<string> Strings(JsonElement? data, string property) => data is { } value && value.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
    private static string Summary(JsonElement? data) => data is null ? string.Empty : String(data.Value, "summary") ?? String(data.Value, "description") ?? String(data.Value, "partyStatement") ?? string.Empty;
}
