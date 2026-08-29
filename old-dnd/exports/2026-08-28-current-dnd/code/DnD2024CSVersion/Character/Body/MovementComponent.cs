namespace DnD2024CSVersion.Character.Body;

public sealed record MovementComponent
{
    public IReadOnlyList<MovementSpeed> Speeds { get; init; } = [];
}
