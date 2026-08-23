using DantesRoleplay.Effects;

namespace DantesRoleplay.Characters;

public sealed record CharacterOriginLanguageProblem(string Code, string Path, string Reason, string Recovery);

/// <summary>CH5's trusted actor binding and the two standard-language choices from character origin.</summary>
public sealed record CharacterOriginLanguageRequest(string ActorId, string SelectionJson);

/// <summary>A zero-write, add-only initial-language fragment for the existing language-state owner.</summary>
public sealed record CharacterOriginLanguagePlan(
    string Status,
    string ActorId,
    string? CampaignId,
    string? CanonicalLanguagesJson,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<CharacterOriginLanguageProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface ICharacterOriginLanguageResolver
{
    Task<CharacterOriginLanguagePlan> PlanAsync(
        CharacterOriginLanguageRequest request,
        CancellationToken cancellationToken = default);
}
