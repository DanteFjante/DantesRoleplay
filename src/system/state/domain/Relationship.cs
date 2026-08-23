namespace DantesRoleplay.World;

/// <summary>
/// A named, directed link between two entities that is not containment — "owes a debt to",
/// "is loyal to", "connects to", "remembers".
///
/// Kept separate from <see cref="Containment"/> because containment has an exclusivity rule that
/// relationships must not have: an entity may relate to many others in many ways at once.
/// </summary>
public sealed class Relationship
{
    public long Id { get; set; }

    public required string FromEntityId { get; set; }

    public Entity? FromEntity { get; set; }

    public required string ToEntityId { get; set; }

    public Entity? ToEntity { get; set; }

    /// <summary>Free text, defined by the game. The kernel only guarantees uniqueness per (from, to, kind).</summary>
    public required string Kind { get; set; }

    /// <summary>Optional JSON payload — strength, since-when, notes.</summary>
    public string Data { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
}
