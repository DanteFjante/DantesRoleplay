using DantesRoleplay.Effects;

namespace DantesRoleplay.Characters;

public sealed record CharacterSpeciesSelectionProblem(string Code, string Path, string Reason, string Recovery);

/// <summary>CH5's trusted binding to one immutable, source-cited species definition.</summary>
public sealed record CharacterSpeciesSelectionRequest(string ActorId, string SpeciesDefinitionId);

/// <summary>A zero-write, add-only selected-species fragment with no trait or base-fact consequence.</summary>
public sealed record CharacterSpeciesSelectionPlan(
    string Status,
    string ActorId,
    string SpeciesDefinitionId,
    string? CampaignId,
    string? CanonicalSelectionJson,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<CharacterSpeciesSelectionProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface ICharacterSpeciesSelectionResolver
{
    Task<CharacterSpeciesSelectionPlan> PlanAsync(
        CharacterSpeciesSelectionRequest request,
        CancellationToken cancellationToken = default);
}
