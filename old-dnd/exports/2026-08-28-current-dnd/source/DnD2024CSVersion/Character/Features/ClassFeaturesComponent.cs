namespace DnD2024CSVersion.Character.Features;

public sealed record ClassFeaturesComponent
{
    public IReadOnlySet<string> FeatureIds { get; init; } = new HashSet<string>();
}
