namespace DnD2024CSVersion.Character.Inventory;

public sealed record AttunementComponent
{
    public int MaximumItems { get; init; } = 3;
    public IReadOnlySet<string> AttunedItemInstanceIds { get; init; } = new HashSet<string>();
}
