namespace DnD2024CSVersion.Character.Features;

public sealed record SpeciesFeaturesComponent
{
    public IReadOnlySet<string> FeatureIds { get; init; } = new HashSet<string>();
}
