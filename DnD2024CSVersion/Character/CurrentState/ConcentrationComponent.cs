namespace DnD2024CSVersion.Character.CurrentState;

public sealed record ConcentrationComponent
{
    public required string SpellId { get; init; }
    public string? SpellcastingSourceId { get; init; }
}
