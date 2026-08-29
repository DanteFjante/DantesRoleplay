namespace DnD2024CSVersion.Character.Inventory;

public sealed record InventoryEntry(
    string ItemInstanceId,
    string ItemDefinitionId,
    int Quantity,
    string? ContainerId);
