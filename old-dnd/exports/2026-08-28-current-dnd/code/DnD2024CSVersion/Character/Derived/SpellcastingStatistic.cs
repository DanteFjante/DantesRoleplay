namespace DnD2024CSVersion.Character.Derived;

public sealed record SpellcastingStatistic(
    string SourceId,
    int? SpellAttackBonus,
    int? SpellSaveDc);
