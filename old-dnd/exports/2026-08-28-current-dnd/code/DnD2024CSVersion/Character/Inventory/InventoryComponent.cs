namespace DnD2024CSVersion.Character.Inventory;

public sealed record InventoryComponent
{
    public IReadOnlyList<InventoryEntry> Items { get; init; } = [];
}
