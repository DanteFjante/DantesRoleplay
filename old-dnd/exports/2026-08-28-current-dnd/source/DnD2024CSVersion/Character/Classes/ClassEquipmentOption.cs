namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// One selectable starting-equipment package or its monetary alternative.
/// Item references can replace the display text when equipment models exist.
/// </summary>
public sealed record ClassEquipmentOption(
    string Id,
    string Name,
    string Description);
