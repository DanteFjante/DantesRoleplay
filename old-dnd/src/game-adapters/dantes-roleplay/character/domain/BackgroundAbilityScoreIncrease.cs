using DantesRoleplay.Effects;

namespace DantesRoleplay.Characters;

public sealed record BackgroundAbilityScoreIncreaseProblem(string Code, string Path, string Reason, string Recovery);

/// <summary>Trusted CH5 binding plus the closed ability-increase choice for that background.</summary>
public sealed record BackgroundAbilityScoreIncreaseRequest(string ActorId, string BackgroundDefinitionId, string SelectionJson);

/// <summary>A zero-write fragment that changes only the existing raw ability-score component.</summary>
public sealed record BackgroundAbilityScoreIncreasePlan(
    string Status,
    string ActorId,
    string BackgroundDefinitionId,
    string? CampaignId,
    string? CanonicalSelectionJson,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<BackgroundAbilityScoreIncreaseProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface IBackgroundAbilityScoreIncreaseResolver
{
    Task<BackgroundAbilityScoreIncreasePlan> PlanAsync(
        BackgroundAbilityScoreIncreaseRequest request,
        CancellationToken cancellationToken = default);
}
