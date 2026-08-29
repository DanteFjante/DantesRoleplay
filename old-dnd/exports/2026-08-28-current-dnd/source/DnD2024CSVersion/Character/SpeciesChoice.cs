namespace DnD2024CSVersion.Character;

/// <summary>
/// A decision required by a species, such as an ancestry, lineage, Size,
/// spellcasting ability, skill, or feat choice.
/// </summary>
public sealed class SpeciesChoice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public int NumberOfSelections { get; init; } = 1;

    public required IReadOnlyList<SpeciesChoiceOption> Options { get; init; }
}
