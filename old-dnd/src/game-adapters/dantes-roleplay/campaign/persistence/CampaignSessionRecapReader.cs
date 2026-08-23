using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Trusted-host S3 historical reader for one immutable ended-session factual recap.</summary>
public sealed class CampaignSessionRecapReader(IWorldStore world) : ICampaignSessionRecapReader
{
    private const string Session = "game.core.campaign.session";
    private const string Recap = "game.core.campaign.session-recap";
    private const string HasSession = "game.core.campaign.has-session";
    private readonly IWorldStore _world = world;

    public async Task<CampaignSessionRecapReadResult> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!Id(sessionId, "session."))
            return Failure(sessionId, null, "INVALID_SESSION_ID", "id", "session-recap requires a canonical session.* id.");
        var entity = await _world.GetEntityAsync(sessionId, cancellationToken);
        var state = ParseState(entity);
        if (state is null)
            return Failure(sessionId, null, "SESSION_GRAPH_INVALID", "id", "sessionId must have one complete campaign-session lifecycle component.");
        if (state.Status != "ended")
            return Failure(sessionId, null, "SESSION_NOT_ENDED", "id", "Only an ended session has a historical factual recap.");
        var scopes = (await _world.GetRelationshipsAsync(sessionId, true, cancellationToken)).Where(link => link.Kind == HasSession && link.ToEntityId == sessionId).ToArray();
        if (scopes.Length != 1 || !Id(scopes[0].FromEntityId, "campaign.") || scopes[0].Data != "{}")
            return Failure(sessionId, null, "SESSION_GRAPH_INVALID", "id", "The ended session must retain exactly one empty-data campaign scope link.");

        var campaignId = scopes[0].FromEntityId;
        if (!await ValidCampaignGraphAsync(campaignId, sessionId, cancellationToken))
            return Failure(sessionId, campaignId, "SESSION_GRAPH_INVALID", "id", "Campaign session links, lifecycle states, and append-only ordinals must remain valid.");
        var recapComponents = entity!.Components.Where(component => component.DefinitionId == Recap).ToArray();
        if (recapComponents.Length != 1 || !TryParseRecap(recapComponents[0].Data, out var recap))
            return Failure(sessionId, campaignId, "SESSION_RECAP_INVALID", "id", "The ended session must have one complete immutable S0 factual recap.");
        return new("found", sessionId, campaignId, recap, [], $"query(kind: \"session-recap\", id: \"{sessionId}\")");
    }

    private async Task<bool> ValidCampaignGraphAsync(string campaignId, string sessionId, CancellationToken cancellationToken)
    {
        if (await _world.GetEntityAsync(campaignId, cancellationToken) is null) return false;
        var links = (await _world.GetRelationshipsAsync(campaignId, false, cancellationToken)).Where(link => link.Kind == HasSession).ToArray();
        if (links.Length == 0 || links.Select(link => link.ToEntityId).Distinct(StringComparer.Ordinal).Count() != links.Length || links.Count(link => link.ToEntityId == sessionId) != 1 || links.Any(link => link.Data != "{}")) return false;
        var states = new List<SessionState>();
        foreach (var link in links)
        {
            var entity = await _world.GetEntityAsync(link.ToEntityId, cancellationToken);
            var state = ParseState(entity);
            var scopes = (await _world.GetRelationshipsAsync(link.ToEntityId, true, cancellationToken)).Where(value => value.Kind == HasSession && value.ToEntityId == link.ToEntityId).ToArray();
            if (state is null || scopes.Length != 1 || scopes[0].FromEntityId != campaignId || scopes[0].Data != "{}") return false;
            states.Add(state);
        }
        return states.Select(state => state.Ordinal).Distinct().Count() == states.Count && states.Select(state => state.Ordinal).OrderBy(ordinal => ordinal).SequenceEqual(Enumerable.Range(1, states.Count)) && states.Count(state => state.Status == "active") <= 1;
    }

    private static bool TryParseRecap(string json, out CampaignSessionRecap? recap)
    {
        recap = null;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (!Object(root, ["protocolVersion", "chapter", "arc", "milestones"]) || String(root, "protocolVersion") != "session.s0.c3-only.v1" ||
                !Chapter(root.GetProperty("chapter"), out var chapter) || !Arc(root.GetProperty("arc"), out var arc) || root.GetProperty("milestones").ValueKind != JsonValueKind.Array)
                return false;
            var milestones = new List<CampaignSessionRecapMilestone>();
            foreach (var value in root.GetProperty("milestones").EnumerateArray())
            {
                if (milestones.Count == 5 || !Object(value, ["chapterId", "title", "closingSummary", "timestamp", "sequence"]) || !Id(String(value, "chapterId"), "") || !Text(String(value, "title"), 160) || !Text(String(value, "closingSummary"), 1000) || !value.TryGetProperty("timestamp", out var timestamp) || timestamp.ValueKind != JsonValueKind.String || !timestamp.TryGetDateTime(out var date) || !value.TryGetProperty("sequence", out var sequence) || !sequence.TryGetInt32(out var number) || number < 0)
                    return false;
                milestones.Add(new(String(value, "chapterId")!, String(value, "title")!, String(value, "closingSummary")!, date, number));
            }
            recap = new("session.s0.c3-only.v1", chapter!, arc!, milestones);
            return true;
        }
        catch { return false; }
    }

    private static bool Chapter(JsonElement value, out CampaignSessionRecapChapter? chapter)
    {
        chapter = Object(value, ["id", "status", "title", "partyQuestion"]) && Id(String(value, "id"), "") && String(value, "status") == "active" && Text(String(value, "title"), 160) && Text(String(value, "partyQuestion"), 500)
            ? new(String(value, "id")!, "active", String(value, "title")!, String(value, "partyQuestion")!) : null;
        return chapter is not null;
    }
    private static bool Arc(JsonElement value, out CampaignSessionRecapArc? arc)
    {
        arc = Object(value, ["id", "status", "title", "partyStake"]) && Id(String(value, "id"), "") && String(value, "status") == "active" && Text(String(value, "title"), 160) && Text(String(value, "partyStake"), 500)
            ? new(String(value, "id")!, "active", String(value, "title")!, String(value, "partyStake")!) : null;
        return arc is not null;
    }
    private static CampaignSessionRecapReadResult Failure(string? sessionId, string? campaignId, string code, string path, string reason) =>
        new("unavailable", sessionId ?? string.Empty, campaignId, null, [new(code, path, reason, "Read one valid ended session recap or repair its owning session graph.")], "query(kind: \"entities\", id: \"...\")");
    private static SessionState? ParseState(EntitySnapshot? entity)
    {
        try
        {
            var components = entity?.Components.Where(component => component.DefinitionId == Session).ToArray();
            if (components is null || components.Length != 1 || !Id(entity!.Id, "session.")) return null;
            using var document = JsonDocument.Parse(components[0].Data); var root = document.RootElement;
            return root.TryGetProperty("status", out var status) && status.GetString() is "active" or "ended" && root.TryGetProperty("ordinal", out var ordinal) && ordinal.TryGetInt32(out var value) && value > 0
                ? new(status.GetString()!, value) : null;
        }
        catch { return null; }
    }
    private static bool Object(JsonElement value, IReadOnlyCollection<string> expected) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected);
    private static string? String(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private sealed record SessionState(string Status, int Ordinal);
}
