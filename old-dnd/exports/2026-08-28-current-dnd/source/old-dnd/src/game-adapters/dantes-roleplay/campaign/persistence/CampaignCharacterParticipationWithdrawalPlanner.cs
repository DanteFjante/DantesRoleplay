using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// C15's internal withdrawal composition seam. A consuming root supplies no campaign id and can
/// append the returned effect to its own transaction with its own lifecycle effects.
/// </summary>
public sealed class CampaignCharacterParticipationWithdrawalPlanner(
    ICampaignCharacterParticipationVerifier participation) : ICampaignCharacterParticipationWithdrawalPlanner
{
    private const string Participation = "game.core.campaign.character-participation";
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<CampaignCharacterParticipationPlan> PlanWithdrawalAsync(
        CampaignCharacterParticipationWithdrawalPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid || string.IsNullOrWhiteSpace(scope.CampaignId) || string.IsNullOrWhiteSpace(scope.ParticipationId))
            return new("invalid", scope.CampaignId ?? string.Empty, scope.ActorId, scope.ParticipationId, [], scope.Problems);

        return new("valid", scope.CampaignId, scope.ActorId, scope.ParticipationId,
        [
            new Effect
            {
                Type = EffectType.ComponentSet,
                EntityId = scope.ParticipationId,
                DefinitionId = Participation,
                Data = "{\"status\":\"withdrawn\"}"
            }
        ], []);
    }
}
