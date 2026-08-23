using DantesRoleplay.Effects;

namespace DantesRoleplay.Characters;

public sealed record CharacterAbilityScoreRecordRequest(string ActorId, string CanonicalScoresJson);

/// <summary>A C15-scoped, add-only ability-score effect fragment for a later CH5 root.</summary>
public sealed record CharacterAbilityScoreRecordPlan(
    string Status,
    string ActorId,
    string? CampaignId,
    string? CanonicalScoresJson,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<CharacterAbilityAssignmentProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface ICharacterAbilityScoreRecorder
{
    Task<CharacterAbilityScoreRecordPlan> PlanAsync(
        CharacterAbilityScoreRecordRequest request,
        CancellationToken cancellationToken = default);
}
