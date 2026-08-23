using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Read-only S2 composition of one validated active session and C3's bounded campaign view.</summary>
public sealed class CampaignSessionResumeReader(IWorldStore world, ICampaignResumeReader campaigns) : ICampaignSessionResumeReader
{
    private const string Session = "game.core.campaign.session";
    private const string HasSession = "game.core.campaign.has-session";
    private readonly IWorldStore _world = world;
    private readonly ICampaignResumeReader _campaigns = campaigns;

    public async Task<CampaignSessionResumeResult> GetAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        var campaign = await _campaigns.GetAsync(campaignId, cancellationToken);
        if (campaign is null)
            return Failure("CAMPAIGN_NOT_FOUND", "campaignId", "campaignId does not name a readable active C3 campaign.", "query(kind: \"campaign-resume\", id: \"...\")");

        try
        {
            var links = (await _world.GetRelationshipsAsync(campaignId, false, cancellationToken)).Where(link => link.Kind == HasSession).ToArray();
            if (links.Select(link => link.ToEntityId).Distinct(StringComparer.Ordinal).Count() != links.Length)
                return Failure("SESSION_GRAPH_INVALID", "campaignId", "Campaign session links must have unique targets.", ReadFix(campaignId));

            var sessions = new List<SessionState>();
            foreach (var link in links)
            {
                var entity = await _world.GetEntityAsync(link.ToEntityId, cancellationToken);
                var state = Parse(entity?.Id, Component(entity, Session));
                var scopes = (await _world.GetRelationshipsAsync(link.ToEntityId, true, cancellationToken)).Where(value => value.Kind == HasSession && value.ToEntityId == link.ToEntityId).ToArray();
                if (state is null || scopes.Length != 1 || scopes[0].FromEntityId != campaignId || scopes[0].Data != "{}")
                    return Failure("SESSION_GRAPH_INVALID", "campaignId", "Every session must have one complete lifecycle component and one empty-data campaign scope link.", ReadFix(campaignId));
                sessions.Add(state);
            }

            if (sessions.Select(value => value.Ordinal).Distinct().Count() != sessions.Count || !sessions.Select(value => value.Ordinal).OrderBy(value => value).SequenceEqual(Enumerable.Range(1, sessions.Count)))
                return Failure("SESSION_GRAPH_INVALID", "campaignId", "Campaign session ordinals must be unique and contiguous.", ReadFix(campaignId));
            var active = sessions.Where(value => value.Status == "active").ToArray();
            if (active.Length > 1)
                return Failure("SESSION_GRAPH_INVALID", "campaignId", "Campaign has multiple active sessions.", ReadFix(campaignId));
            if (active.Length == 0)
                return Failure("NO_ACTIVE_SESSION", "campaignId", "Campaign has no active session to resume.", "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"start-session\\\",\\\"campaignId\\\":\\\"" + campaignId + "\\\",\\\"sessionId\\\":\\\"...\\\"}\")");

            var session = active[0];
            return new("resumed", new(session.Id, campaignId, session.Status, session.Ordinal), campaign, [], $"query(kind: \"campaign-resume\", id: \"{campaignId}\", includeSession: true)");
        }
        catch (InvalidOperationException)
        {
            return Failure("SESSION_GRAPH_INVALID", "campaignId", "Campaign session records must have one lifecycle component and one scope link.", ReadFix(campaignId));
        }
    }

    private static CampaignSessionResumeResult Failure(string code, string path, string reason, string recovery) => new("unavailable", null, null, [new(code, path, reason, recovery)], recovery);
    private static string ReadFix(string campaignId) => $"query(kind: \"campaign-resume\", id: \"{campaignId}\")";
    private static string? Component(EntitySnapshot? entity, string definitionId) => entity?.Components.SingleOrDefault(value => value.DefinitionId == definitionId)?.Data;
    private static SessionState? Parse(string? id, string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty); var root = document.RootElement;
            var status = root.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return Id(id) && status is ("active" or "ended") && root.TryGetProperty("ordinal", out var ordinal) && ordinal.ValueKind == JsonValueKind.Number && ordinal.TryGetInt32(out var number) && number > 0 ? new(id!, status!, number) : null;
        }
        catch { return null; }
    }
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith("session.", StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private sealed record SessionState(string Id, string Status, int Ordinal);
}
