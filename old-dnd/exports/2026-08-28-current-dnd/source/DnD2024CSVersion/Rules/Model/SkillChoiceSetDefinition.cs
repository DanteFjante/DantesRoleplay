namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// A reusable set of skills from which another rule can request selections.
/// The number selected belongs to the referencing rule, not the set itself.
/// </summary>
public sealed record SkillChoiceSetDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> SkillIds);
