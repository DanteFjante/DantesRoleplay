using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>S3 Slice 1's zero-effect, C3-only factual closure resolver. Ending remains Slice 2.</summary>
public sealed class CampaignSessionEndValidator(IWorldStore world, ICampaignSessionResumeReader sessions) : ICampaignSessionEndValidator
{
    private const string Session = "game.core.campaign.session";
    private const string Recap = "game.core.campaign.session-recap";
    private const string HasSession = "game.core.campaign.has-session";
    private const string ProtocolVersion = "session.s0.c3-only.v1";
    private readonly IWorldStore _world = world;
    private readonly ICampaignSessionResumeReader _sessions = sessions;

    public async Task<CampaignSessionEndValidationResult> ValidateAsync(CampaignSessionEndRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Operation != "validate-session-end" || !Id(request.SessionId, "session.") || request.ExpectedStatus != "active")
            return Failure(request?.SessionId, null, "INVALID_SESSION_END_REQUEST", "payload", "Session-end validation requires operation validate-session-end, a canonical session.* id, and expectedStatus active.");

        var entity = await _world.GetEntityAsync(request.SessionId, cancellationToken);
        var state = Parse(entity);
        if (state is null)
            return Failure(request.SessionId, null, "SESSION_GRAPH_INVALID", "sessionId", "sessionId must have one complete campaign-session lifecycle component.");
        if (state.Status != request.ExpectedStatus)
            return Failure(request.SessionId, null, "STALE_SESSION_STATUS", "expectedStatus", "The session is no longer active.");
        if (entity!.Components.Any(component => component.DefinitionId == Recap))
            return Failure(request.SessionId, null, "SESSION_RECAP_PRESENT", "sessionId", "An active session cannot already carry a factual recap.");

        var scopes = (await _world.GetRelationshipsAsync(request.SessionId, true, cancellationToken))
            .Where(link => link.Kind == HasSession && link.ToEntityId == request.SessionId).ToArray();
        if (scopes.Length != 1 || !Id(scopes[0].FromEntityId, "campaign.") || scopes[0].Data != "{}")
            return Failure(request.SessionId, null, "SESSION_GRAPH_INVALID", "sessionId", "The session must have exactly one empty-data campaign scope link.");

        var campaignId = scopes[0].FromEntityId;
        var resumed = await _sessions.GetAsync(campaignId, cancellationToken);
        if (!resumed.Resumed)
            return Failure(request.SessionId, campaignId, resumed.Problems[0].Code, resumed.Problems[0].Path, resumed.Problems[0].Reason);
        if (resumed.Session!.SessionId != request.SessionId || resumed.Session.Status != "active")
            return Failure(request.SessionId, campaignId, "STALE_SESSION", "sessionId", "sessionId is not the campaign's sole active session.");

        var recap = Compose(resumed.Campaign!);
        return recap is null
            ? Failure(request.SessionId, campaignId, "C3_RECAP_UNAVAILABLE", "campaign-resume", "C3 must expose one complete current chapter and arc with no more than five valid milestones.")
            : new("valid", request.SessionId, campaignId, state.Ordinal, recap, [], $"commit(kind: \"campaign\", payload: \"{{\\\"operation\\\":\\\"end-session\\\",\\\"sessionId\\\":\\\"{request.SessionId}\\\",\\\"expectedStatus\\\":\\\"active\\\"}}\")");
    }

    private static CampaignSessionRecap? Compose(CampaignResume campaign)
    {
        var chapter = campaign.CurrentChapter;
        var arc = campaign.CurrentArc;
        if (chapter is null || arc is null || chapter.Status != "active" || arc.Status != "active" ||
            !Id(chapter.Id, "") || !Text(chapter.Title, 160) || !Text(chapter.PartyQuestion, 500) ||
            !Id(arc.Id, "") || !Text(arc.Title, 160) || !Text(arc.PartyStake, 500) || campaign.RecentMilestones.Count > 5)
            return null;
        var milestones = new List<CampaignSessionRecapMilestone>();
        foreach (var milestone in campaign.RecentMilestones)
        {
            if (!Id(milestone.ChapterId, "") || !Text(milestone.Title, 160) || !Text(milestone.ClosingSummary, 1000) || milestone.Timestamp == default || milestone.Sequence < 0)
                return null;
            // Preserve C3's canonical order and intentionally omit its event id.
            milestones.Add(new(milestone.ChapterId, milestone.Title, milestone.ClosingSummary, milestone.Timestamp, milestone.Sequence));
        }
        return new(ProtocolVersion, new(chapter.Id, chapter.Status, chapter.Title, chapter.PartyQuestion), new(arc.Id, arc.Status, arc.Title, arc.PartyStake), milestones);
    }

    private static CampaignSessionEndValidationResult Failure(string? sessionId, string? campaignId, string code, string path, string reason) =>
        new("invalid", sessionId ?? string.Empty, campaignId, null, null, [new(code, path, reason, "Correct the session graph or C3 continuity and validate again.")], "query(kind: \"campaign-resume\", id: \"...\")");
    private static SessionState? Parse(EntitySnapshot? entity)
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
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private sealed record SessionState(string Status, int Ordinal);
}
