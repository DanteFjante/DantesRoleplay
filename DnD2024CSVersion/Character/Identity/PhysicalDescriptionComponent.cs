namespace DnD2024CSVersion.Character.Identity;

public sealed record PhysicalDescriptionComponent
{
    public string Summary { get; init; } = string.Empty;
    public string? Pronouns { get; init; }
    public string? Gender { get; init; }
    public string? Age { get; init; }
    public string? Height { get; init; }
    public string? Weight { get; init; }
    public string? Eyes { get; init; }
    public string? Skin { get; init; }
    public string? Hair { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalDetails { get; init; } =
        new Dictionary<string, string>();
}
