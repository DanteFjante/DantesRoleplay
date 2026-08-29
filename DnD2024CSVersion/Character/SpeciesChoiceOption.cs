namespace DnD2024CSVersion.Character;

/// <summary>
/// One legal option in a species choice.
/// Granted trait IDs refer back to traits on the species definition.
/// </summary>
public sealed record SpeciesChoiceOption(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> GrantedTraitIds);
