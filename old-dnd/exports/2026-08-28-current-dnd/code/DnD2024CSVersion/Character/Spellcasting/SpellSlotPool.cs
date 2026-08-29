namespace DnD2024CSVersion.Character.Spellcasting;

public sealed record SpellSlotPool(
    int SpellLevel,
    int Maximum,
    int Expended,
    string SlotKind = "spell");
