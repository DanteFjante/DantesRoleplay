namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// One multiclass prerequisite. AnyOfAbilityIds supports requirements such as
/// Strength or Dexterity 13 for the Fighter class.
/// </summary>
public sealed record ClassAbilityRequirement(
    IReadOnlyList<string> AnyOfAbilityIds,
    int MinimumScore = 13);
