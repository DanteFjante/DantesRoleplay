namespace DnD2024CSVersion.Character.Features;

public sealed record BackgroundFeatComponent
{
    public required string FeatId { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Choices { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
