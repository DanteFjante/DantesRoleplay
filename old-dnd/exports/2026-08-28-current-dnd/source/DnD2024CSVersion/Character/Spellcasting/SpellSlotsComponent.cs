namespace DnD2024CSVersion.Character.Spellcasting;

public sealed record SpellSlotsComponent
{
    public IReadOnlyList<SpellSlotPool> Pools { get; init; } = [];
}
