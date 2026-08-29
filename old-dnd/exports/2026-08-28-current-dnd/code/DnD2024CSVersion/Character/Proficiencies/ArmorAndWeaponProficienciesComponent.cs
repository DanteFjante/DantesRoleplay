namespace DnD2024CSVersion.Character.Proficiencies;

public sealed record ArmorAndWeaponProficienciesComponent
{
    public IReadOnlySet<string> ArmorTrainingIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> WeaponProficiencyIds { get; init; } = new HashSet<string>();
}
