namespace DnD2024CSVersion.Character.Spellcasting;

public sealed record SpellcastingSourcesComponent
{
    public IReadOnlyList<SpellcastingSource> Sources { get; init; } = [];
}
