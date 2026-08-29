namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// Display and rules-reference information for one of the six abilities.
/// </summary>
public sealed record AbilityDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> CommonUses);
