namespace DnD2024CSVersion.Character.Advancement;

public sealed record ClassesComponent
{
    public required string StartingClassId { get; init; }
    public IReadOnlyList<CharacterClassLevel> Classes { get; init; } = [];
    public int TotalLevel => Classes.Sum(entry => entry.Level);
}
