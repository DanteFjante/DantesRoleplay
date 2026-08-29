namespace DnD2024CSVersion.Character.CurrentState;

public sealed record HitDiceComponent
{
    public IReadOnlyList<HitDicePool> Pools { get; init; } = [];
}
