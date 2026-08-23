using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>C15's reusable, effect-free attachment fragment planner.</summary>
public sealed class CampaignCharacterParticipationPlanner : ICampaignCharacterParticipationPlanner
{
    private const string CampaignRoot = "game.core.campaign.root";
    private const string Participation = "game.core.campaign.character-participation";
    private const string HasParticipation = "game.core.campaign.has-character-participation";
    private const string ForActor = "game.core.campaign.character-participation.for-actor";

    public async Task<CampaignCharacterParticipationPlan> PlanAsync(
        CampaignCharacterParticipationPlanRequest request,
        IWorldStore world,
        CancellationToken cancellationToken = default)
    {
        var campaignId = request?.CampaignId ?? string.Empty;
        var actorId = request?.ActorId ?? string.Empty;
        if (world is null || !Id(campaignId, "campaign.") || !Id(actorId, "actor."))
            return Invalid(campaignId, actorId, "INVALID_PARTICIPATION_REQUEST", "payload", "Attachment requires canonical campaign.* and actor.* ids.");

        var campaign = await world.GetEntityAsync(campaignId, cancellationToken);
        if (campaign is null || !Active(Component(campaign, CampaignRoot)))
            return Invalid(campaignId, actorId, "CAMPAIGN_NOT_ACTIVE", "campaignId", "campaignId must name an active campaign root.");
        if (await world.GetEntityAsync(actorId, cancellationToken) is null)
            return Invalid(campaignId, actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing or staged actor entity.");

        var existing = (await world.GetRelationshipsAsync(actorId, true, cancellationToken))
            .Where(link => link.Kind == ForActor && link.ToEntityId == actorId)
            .ToArray();
        if (existing.Length != 0)
            return Invalid(campaignId, actorId, "ACTOR_ALREADY_ATTACHED", "actorId", "Actor already has campaign participation history and cannot be attached again by C15.");

        var participationId = ParticipationId(campaignId, actorId);
        if (!Id(participationId, "campaign."))
            return Invalid(campaignId, actorId, "INVALID_PARTICIPATION_ID", "actorId", "The confirmed derived participation id exceeds the canonical id boundary.");
        if (await world.GetEntityAsync(participationId, cancellationToken) is not null)
            return Invalid(campaignId, actorId, "PARTICIPATION_ID_TAKEN", "actorId", "The server-derived participation id is already in use.");

        return new("valid", campaignId, actorId, participationId,
        [
            new Effect { Type = EffectType.EntityCreate, EntityId = participationId, Name = "Campaign character participation" },
            new Effect { Type = EffectType.ComponentAdd, EntityId = participationId, DefinitionId = Participation, Data = "{\"status\":\"active\"}" },
            new Effect { Type = EffectType.RelationshipCreate, EntityId = campaignId, ToEntityId = participationId, Kind = HasParticipation, Data = "{}" },
            new Effect { Type = EffectType.RelationshipCreate, EntityId = participationId, ToEntityId = actorId, Kind = ForActor, Data = "{}" }
        ], []);
    }

    private static CampaignCharacterParticipationPlan Invalid(string campaignId, string actorId, string code, string path, string reason) =>
        new("invalid", campaignId, actorId, null, [], [new(code, path, reason, "Repair the campaign participation through its owning workflow.")]);
    private static string ParticipationId(string campaignId, string actorId) => $"{campaignId}.participation.{actorId}";
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static string? Component(EntitySnapshot entity, string definitionId) => entity.Components.SingleOrDefault(component => component.DefinitionId == definitionId)?.Data;
    private static bool Active(string? json) { try { using var document = JsonDocument.Parse(json ?? string.Empty); return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "active"; } catch { return false; } }
}
