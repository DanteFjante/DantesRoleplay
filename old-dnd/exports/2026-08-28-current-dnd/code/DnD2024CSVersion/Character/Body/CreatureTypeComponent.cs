using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.Body;

public sealed record CreatureTypeComponent
{
    public CreatureType CreatureType { get; set; } = CreatureType.Humanoid;
}
