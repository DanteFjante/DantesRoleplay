using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// Proficiencies and training granted when a class is gained.
/// </summary>
public sealed class ClassProficiencies
{
    public IReadOnlyList<string> SavingThrowAbilityIds { get; init; } = [];

    public IReadOnlyList<string> GrantedSkills { get; init; } = [];

    /// <summary>
    /// References a reusable set of legal skills instead of embedding the list.
    /// </summary>
    public ChoiceSetReference? SkillChoice { get; init; }

    public IReadOnlyList<string> WeaponProficiencies { get; init; } = [];

    public IReadOnlyList<string> GrantedTools { get; init; } = [];

    public IReadOnlyList<string> ToolOptions { get; init; } = [];

    public int ToolChoices { get; init; }

    public IReadOnlyList<string> ArmorTraining { get; init; } = [];
}
