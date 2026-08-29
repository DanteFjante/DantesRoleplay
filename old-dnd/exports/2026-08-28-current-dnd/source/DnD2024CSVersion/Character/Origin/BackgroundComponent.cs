namespace DnD2024CSVersion.Character.Origin;

/// <summary>
/// Identifies the selected background. State granted by the background belongs to the component
/// that owns that state.
/// </summary>
public sealed record BackgroundComponent
{
    public required string BackgroundId { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Choices { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
