namespace DnD2024CSVersion.Character.Proficiencies;

public sealed record SkillProficienciesComponent
{
    public IReadOnlyDictionary<string, ProficiencyRank> Skills { get; init; } =
        new Dictionary<string, ProficiencyRank>();
}
