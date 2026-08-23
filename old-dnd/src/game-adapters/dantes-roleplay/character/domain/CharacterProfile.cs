using DantesRoleplay.Effects;

namespace DantesRoleplay.Characters;

public sealed record CharacterProfileProblem(string Code, string Path, string Reason, string Recovery);

/// <summary>One internal CH1 profile request. ProfileJson preserves absent-versus-null semantics.</summary>
public sealed record CharacterProfileRecordRequest(string ActorId, string ProfileJson);

/// <summary>
/// A validated profile effect fragment for a later root owner. This service never opens a
/// transaction or writes the actor itself.
/// </summary>
public sealed record CharacterProfileRecordPlan(
    string Status,
    string ActorId,
    string? CampaignId,
    string? ProfileJson,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<CharacterProfileProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface ICharacterProfileRecorder
{
    Task<CharacterProfileRecordPlan> PlanAsync(
        CharacterProfileRecordRequest request,
        CancellationToken cancellationToken = default);
}
