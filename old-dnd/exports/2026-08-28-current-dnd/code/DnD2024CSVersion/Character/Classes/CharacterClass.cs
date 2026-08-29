namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// A reusable definition of one character class and its level progression.
/// Individual characters refer to this definition by its Id.
/// </summary>
public sealed class CharacterClass
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Abilities required by the class. PrimaryAbilityRule specifies whether all
    /// listed abilities are primary or the player chooses one of them.
    /// </summary>
    public required IReadOnlyList<string> PrimaryAbilityIds { get; init; }

    public PrimaryAbilityRule PrimaryAbilityRule { get; init; } = PrimaryAbilityRule.All;

    /// <summary>
    /// References the die record gained for each level in this class.
    /// </summary>
    public required string HitPointDieId { get; init; }

    public ClassProficiencies StartingProficiencies { get; init; } = new();

    public ClassProficiencies MulticlassProficiencies { get; init; } = new();

    public IReadOnlyList<ClassAbilityRequirement> MulticlassRequirements { get; init; } = [];

    public IReadOnlyList<ClassEquipmentOption> StartingEquipment { get; init; } = [];

    public IReadOnlyList<ClassLevel> Levels { get; init; } = [];
}
