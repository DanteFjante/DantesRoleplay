namespace DantesRoleplay.World;

/// <summary>
/// "This entity is inside that one." Covers a character in a room, a coin in a purse, a purse in
/// a backpack — the kernel does not distinguish those cases, the components do.
///
/// <see cref="ContainedId"/> is unique: a thing is in at most one place. That single constraint
/// removes a whole family of bugs where an item ends up in two inventories, and it is far
/// cheaper to enforce here than to detect later.
/// </summary>
public sealed class Containment
{
    public long Id { get; set; }

    public required string ContainerId { get; set; }

    public Entity? Container { get; set; }

    public required string ContainedId { get; set; }

    public Entity? Contained { get; set; }

    /// <summary>
    /// Optional label distinguishing several ways of being inside something — "equipped",
    /// "carried", "standing-in". Free text: the meaning is the game's business, not the kernel's.
    /// </summary>
    public string Slot { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
