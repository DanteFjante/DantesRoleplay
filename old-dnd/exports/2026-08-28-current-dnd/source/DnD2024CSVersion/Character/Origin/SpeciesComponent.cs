namespace DnD2024CSVersion.Character.Origin;

/// <summary>
/// Identifies the selected species. State granted by the species belongs to the component that owns
/// that state.
/// </summary>
public sealed record SpeciesComponent
{
    public required string SpeciesId { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Choices { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
