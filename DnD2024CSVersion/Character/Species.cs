using DnD2024CSVersion.Enums;
using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character;

/// <summary>
/// A reusable species definition. A species describes inherited game traits;
/// it does not contain a character's background, ability-score increases, or class.
/// </summary>
public sealed class Species
{
    /// <summary>
    /// Stable identifier used when the model is later stored as JSON.
    /// </summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public CreatureType CreatureType { get; init; } = CreatureType.Humanoid;

    /// <summary>
    /// Sizes a character may choose when selecting this species.
    /// </summary>
    public required IReadOnlyList<CreatureSize> AvailableSizes { get; init; }

    /// <summary>
    /// Base walking Speed in feet.
    /// </summary>
    public int SpeedFeet { get; init; } = 30;

    /// <summary>
    /// Typical maximum life span when the species specifies one.
    /// </summary>
    public int? TypicalLifespanYears { get; init; }

    public IReadOnlyList<SpeciesTrait> Traits { get; init; } = [];

    public IReadOnlyList<SpeciesChoice> Choices { get; init; } = [];
}
