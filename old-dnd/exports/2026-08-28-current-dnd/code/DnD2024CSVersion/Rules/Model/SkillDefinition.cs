namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// Display and rules-reference information for one skill.
/// </summary>
public sealed record SkillDefinition(
    string Id,
    string Name,
    string AbilityId,
    string Description);
