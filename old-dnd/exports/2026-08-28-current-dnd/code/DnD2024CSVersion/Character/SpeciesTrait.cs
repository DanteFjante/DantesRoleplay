namespace DnD2024CSVersion.Character;

/// <summary>
/// One special capability granted by a species.
/// Detailed rule execution can be implemented separately and associated by Id.
/// </summary>
public sealed record SpeciesTrait(
    string Id,
    string Name,
    string Description,
    int MinimumCharacterLevel = 1);
