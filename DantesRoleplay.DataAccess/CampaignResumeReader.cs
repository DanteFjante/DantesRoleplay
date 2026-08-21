using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Events;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Fixed C3 trusted-host read model. It is deliberately not a general campaign graph API.</summary>
public sealed class CampaignResumeReader(IWorldStore world, IEventLedger events) : ICampaignResumeReader
{
    private readonly IWorldStore _world = world; private readonly IEventLedger _events = events;

    public async Task<CampaignResume?> GetAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        var campaign = await _world.GetEntityAsync(campaignId, cancellationToken); var root = Object(Component(campaign, "game.core.campaign.root"));
        if (campaign is null || root is null || String(root, "status") != "active") return null;
        var links = await _world.GetRelationshipsAsync(campaignId, false, cancellationToken);
        var worldLink = links.SingleOrDefault(x => x.Kind == "game.core.campaign.in-world"); if (worldLink is null) return null;
        var chapters = await ReadChapters(links, cancellationToken); var arcs = await ReadArcs(links, cancellationToken);
        var references = await ReadReferences(links, cancellationToken);
        var milestones = await ReadMilestones(chapters.Where(x => x.Status == "closed"), cancellationToken);
        return new(campaignId, String(root, "title") ?? string.Empty, String(root, "premise") ?? string.Empty, Strings(root, "partyGoals"), Strings(root, "toneAndBoundaries"), worldLink.ToEntityId,
            chapters.SingleOrDefault(x => x.Status == "active"), arcs.SingleOrDefault(x => x.Status == "active"), references, milestones,
            "Trusted-host view only. Descriptive visibility is editorial metadata, not authorization.");
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
