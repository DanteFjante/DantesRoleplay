using DnD2024CSVersion.Enums;

namespace DnD2024CSVersion.Character.Body;

public sealed record SizeComponent
{
    public CreatureSize Size { get; set; } = CreatureSize.Medium;
}
