namespace DnD2024CSVersion.Character.Inventory;

public sealed record EquipmentComponent
{
    public IReadOnlyDictionary<string, EquipmentState> ItemStates { get; init; } =
        new Dictionary<string, EquipmentState>();
}
