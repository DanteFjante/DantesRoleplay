namespace DnD2024CSVersion.Character.Derived;

/// <summary>
/// Values calculated from authoritative components.
/// </summary>
public sealed record DerivedStatisticsComponent
{
    public int ProficiencyBonus { get; init; }
    public int ArmorClass { get; init; }
    public int InitiativeModifier { get; init; }
    public int PassivePerception { get; init; }
    public IReadOnlyDictionary<string, int> SavingThrowModifiers { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> SkillModifiers { get; init; } =
        new Dictionary<string, int>();
}
