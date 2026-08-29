namespace DnD2024CSVersion.Character.Features;

public sealed record OtherFeaturesComponent
{
    public IReadOnlySet<string> FeatIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BoonIds { get; init; } = new HashSet<string>();
}
