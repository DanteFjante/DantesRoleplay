namespace DnD2024CSVersion.Character.Proficiencies;

/// <summary>
/// Weapons whose mastery properties the character can use. The weapon definition owns the property.
/// </summary>
public sealed record WeaponMasteriesComponent
{
    public IReadOnlySet<string> WeaponDefinitionIds { get; init; } = new HashSet<string>();
}
