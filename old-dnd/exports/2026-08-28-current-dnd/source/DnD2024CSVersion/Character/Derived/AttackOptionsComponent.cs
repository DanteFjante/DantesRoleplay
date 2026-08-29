namespace DnD2024CSVersion.Character.Derived;

/// <summary>
/// Display-ready attacks derived from features, spells, and equipped items.
/// </summary>
public sealed record AttackOptionsComponent
{
    public IReadOnlyList<AttackOption> Attacks { get; init; } = [];
}
