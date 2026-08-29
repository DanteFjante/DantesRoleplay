using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.Derived;

public sealed record AttackOption(
    string Id,
    string Name,
    int? AttackBonus,
    int? SaveDc,
    DiceExpression? Damage,
    string? DamageTypeId,
    string? Range,
    string? Notes,
    string SourceId);
