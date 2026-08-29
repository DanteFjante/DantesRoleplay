namespace DnD2024CSVersion.Character.Derived;

/// <summary>
/// Spell attack bonuses and save DCs calculated independently for each spellcasting source.
/// </summary>
public sealed record SpellcastingStatisticsComponent
{
    public IReadOnlyList<SpellcastingStatistic> Sources { get; init; } = [];
}
