using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.AbilityScores;

public sealed record AbilityScoresComponent
{
    public int Strength { get; set; } = 10;
    public int Dexterity { get; set; } = 10;
    public int Constitution { get; set; } = 10;
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;

    public int Score(Ability ability) => ability switch
    {
        Ability.Strength => Strength,
        Ability.Dexterity => Dexterity,
        Ability.Constitution => Constitution,
        Ability.Intelligence => Intelligence,
        Ability.Wisdom => Wisdom,
        Ability.Charisma => Charisma,
        _ => throw new ArgumentOutOfRangeException(nameof(ability))
    };

    public int Modifier(Ability ability) => (int)Math.Floor((Score(ability) - 10) / 2m);
}
