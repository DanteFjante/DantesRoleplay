namespace DnD2024CSVersion.Character.CurrentState;

public sealed record FeatureStateComponent
{
    public IReadOnlyList<FeatureState> Features { get; init; } = [];
}
