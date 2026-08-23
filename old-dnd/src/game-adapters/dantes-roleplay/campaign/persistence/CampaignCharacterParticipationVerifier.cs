using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>C15 Slice 1's read-only canonical campaign-to-actor scope verifier.</summary>
public sealed class CampaignCharacterParticipationVerifier(IWorldStore world) : ICampaignCharacterParticipationVerifier
{
    private const string CampaignRoot = "game.core.campaign.root";
    private const string Participation = "game.core.campaign.character-participation";
    private const string HasParticipation = "game.core.campaign.has-character-participation";
    private const string ForActor = "game.core.campaign.character-participation.for-actor";
    private readonly IWorldStore _world = world;

    public async Task<CampaignCharacterParticipationScope> ResolveActiveScopeAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (!Id(actorId))
            return Invalid(actorId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical lowercase dotted id.");

        if (await _world.GetEntityAsync(actorId, cancellationToken) is null)
            return Invalid(actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing actor entity.");

        var incoming = (await _world.GetRelationshipsAsync(actorId, true, cancellationToken))
            .Where(x => x.Kind == ForActor && x.ToEntityId == actorId)
            .ToArray();
        if (incoming.Length == 0)
            return Invalid(actorId, "ACTOR_NOT_ATTACHED", "actorId", "actorId has no campaign character participation.");
        if (incoming.Length != 1 || incoming[0].Data != "{}" || !Id(incoming[0].FromEntityId))
            return Invalid(actorId, "PARTICIPATION_GRAPH_INVALID", "actorId", "Actor participation links must be unique, canonical, and empty-data.");

        var participationId = incoming[0].FromEntityId;
        var participation = await _world.GetEntityAsync(participationId, cancellationToken);
        if (participation is null || participation.Components.Count(x => x.DefinitionId == Participation) != 1 || !Status(Component(participation, Participation), "active"))
            return Invalid(actorId, "PARTICIPATION_NOT_ACTIVE", "actorId", "Actor participation must have one closed active state component.", participationId);

        var links = await _world.GetRelationshipsAsync(participationId, true, cancellationToken);
        var actorLinks = links.Where(x => x.Kind == ForActor && x.FromEntityId == participationId).ToArray();
        var campaignLinks = links.Where(x => x.Kind == HasParticipation && x.ToEntityId == participationId).ToArray();
        if (actorLinks.Length != 1 || actorLinks[0].ToEntityId != actorId || actorLinks[0].Data != "{}" ||
            campaignLinks.Length != 1 || campaignLinks[0].Data != "{}" || !Id(campaignLinks[0].FromEntityId))
            return Invalid(actorId, "PARTICIPATION_GRAPH_INVALID", "actorId", "Participation requires one empty-data actor link and one empty-data campaign link.", participationId);

        var campaignId = campaignLinks[0].FromEntityId;
        var campaign = await _world.GetEntityAsync(campaignId, cancellationToken);
        if (campaign is null || campaign.Components.Count(x => x.DefinitionId == CampaignRoot) != 1 || !ActiveCampaign(Component(campaign, CampaignRoot)))
            return Invalid(actorId, "CAMPAIGN_NOT_ACTIVE", "actorId", "Participation must belong to one active campaign root.", participationId);

        return new("active", actorId, campaignId, participationId, []);
    }

    private static CampaignCharacterParticipationScope Invalid(string? actorId, string code, string path, string reason, string? participationId = null) =>
        new("invalid", actorId ?? string.Empty, null, participationId, [new(code, path, reason, "Repair the campaign participation through its owning workflow.")]);

    private static string? Component(EntitySnapshot entity, string definitionId) => entity.Components.SingleOrDefault(x => x.DefinitionId == definitionId)?.Data;
    private static bool Status(string? json, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1 &&
                   root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == expected;
        }
        catch { return false; }
    }
    private static bool ActiveCampaign(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("status", out var status) &&
                   status.ValueKind == JsonValueKind.String && status.GetString() == "active";
        }
        catch { return false; }
    }
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value == value.ToLowerInvariant() && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
}
