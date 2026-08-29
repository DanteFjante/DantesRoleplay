namespace DnD2024CSVersion.Character.Spellcasting;

public sealed record SpellsComponent
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CantripIdsBySource { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> KnownSpellIdsBySource { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PreparedSpellIdsBySource { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
