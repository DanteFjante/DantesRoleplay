using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>S1 Slice 1's zero-effect session-start validator. Atomic creation belongs to Slice 2.</summary>
public sealed class CampaignSessionValidator(IWorldStore world, ICampaignResumeReader resumes) : ICampaignSessionValidator
{
    private const string Session = "game.core.campaign.session";
    private const string HasSession = "game.core.campaign.has-session";
    private readonly IWorldStore _world = world;
    private readonly ICampaignResumeReader _resumes = resumes;

    public async Task<CampaignSessionValidationResult> ValidateAsync(CampaignSessionValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Operation != "validate-session" || !Id(request.CampaignId, "campaign.") || !Id(request.SessionId, "session."))
            return Invalid(request?.CampaignId, request?.SessionId, "INVALID_SESSION_REQUEST", "payload", "Session validation requires operation validate-session and canonical campaign.* and session.* ids.");

        var resume = await _resumes.GetAsync(request.CampaignId, cancellationToken);
        if (resume is null)
            return Invalid(request.CampaignId, request.SessionId, "SESSION_CAMPAIGN_UNAVAILABLE", "campaignId", "campaignId must name an active campaign with valid C3 continuity.");
        if (await _world.GetEntityAsync(request.SessionId, cancellationToken) is not null)
            return Invalid(request.CampaignId, request.SessionId, "SESSION_ID_TAKEN", "sessionId", "sessionId is already permanently taken.");

        var links = (await _world.GetRelationshipsAsync(request.CampaignId, false, cancellationToken)).Where(x => x.Kind == HasSession).ToArray();
        if (links.Select(x => x.ToEntityId).Distinct(StringComparer.Ordinal).Count() != links.Length)
            return Invalid(request.CampaignId, request.SessionId, "SESSION_GRAPH_INVALID", "campaignId", "Campaign session links must have unique targets.");

        var states = new List<SessionState>();
        try
        {
            foreach (var link in links)
            {
                var entity = await _world.GetEntityAsync(link.ToEntityId, cancellationToken);
                var state = Parse(entity?.Id, Component(entity, Session));
                var scopes = (await _world.GetRelationshipsAsync(link.ToEntityId, true, cancellationToken))
                    .Where(x => x.Kind == HasSession && x.ToEntityId == link.ToEntityId).ToArray();
                if (state is null || scopes.Length != 1 || scopes[0].FromEntityId != request.CampaignId || scopes[0].Data != "{}")
                    return Invalid(request.CampaignId, request.SessionId, "SESSION_GRAPH_INVALID", "campaignId", "Every linked session must have one complete lifecycle component and one empty-data campaign scope link.");
                states.Add(state);
            }
        }
        catch (InvalidOperationException)
        {
            return Invalid(request.CampaignId, request.SessionId, "SESSION_GRAPH_INVALID", "campaignId", "Campaign session records must have one lifecycle component and one scope link.");
        }

        if (states.Select(x => x.Ordinal).Distinct().Count() != states.Count || !states.Select(x => x.Ordinal).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, states.Count)))
            return Invalid(request.CampaignId, request.SessionId, "SESSION_GRAPH_INVALID", "campaignId", "Campaign session ordinals must be unique and append-only without gaps.");
        if (states.Count(x => x.Status == "active") > 1)
            return Invalid(request.CampaignId, request.SessionId, "SESSION_GRAPH_INVALID", "campaignId", "Campaign has multiple active sessions.");
        if (states.Any(x => x.Status == "active"))
            return Invalid(request.CampaignId, request.SessionId, "ACTIVE_SESSION_EXISTS", "campaignId", "Campaign already has an active session.");

        return new("valid", request.CampaignId, request.SessionId, states.Count + 1, [], "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"start-session\\\",...}\")");
    }

    private static CampaignSessionValidationResult Invalid(string? campaignId, string? sessionId, string code, string path, string reason) =>
        new("invalid", campaignId ?? string.Empty, sessionId ?? string.Empty, null, [new(code, path, reason, "Correct the request or session graph and validate again.")], "query(kind: \"entities\", withDefinitionId: \"game.core.campaign.session\")");
    private static string? Component(EntitySnapshot? entity, string definitionId) => entity?.Components.SingleOrDefault(x => x.DefinitionId == definitionId)?.Data;
    private static SessionState? Parse(string? id, string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.String ? statusValue.GetString() : null;
            return Id(id, "session.") && status is ("active" or "ended") && root.TryGetProperty("ordinal", out var ordinal) && ordinal.ValueKind == JsonValueKind.Number && ordinal.TryGetInt32(out var value) && value > 0
                ? new(id!, status!, value) : null;
        }
        catch { return null; }
    }
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private sealed record SessionState(string Id, string Status, int Ordinal);
}
