using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.Proficiencies;

public sealed record SavingThrowProficienciesComponent
{
    public IReadOnlySet<Ability> Abilities { get; init; } = new HashSet<Ability>();
}
