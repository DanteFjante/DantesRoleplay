namespace DnD2024CSVersion.Character.CurrentState;

public sealed record LimitedUseResourcesComponent
{
    public IReadOnlyList<LimitedUseResource> Resources { get; init; } = [];
}
