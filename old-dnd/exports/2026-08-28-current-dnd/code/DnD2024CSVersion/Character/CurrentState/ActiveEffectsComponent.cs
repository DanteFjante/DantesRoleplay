namespace DnD2024CSVersion.Character.CurrentState;

public sealed record ActiveEffectsComponent
{
    public IReadOnlyList<ActiveEffect> Effects { get; init; } = [];
}
