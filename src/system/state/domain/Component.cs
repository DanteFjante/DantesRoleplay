namespace DantesRoleplay.World;

/// <summary>
/// A piece of data attached to an entity. This is where every game concept actually lives.
///
/// An entity carries at most one component per definition — "Orban's stats" is singular. Two
/// poison stacks are one component holding a count, not two components, and that constraint is
/// enforced by a unique index rather than by convention.
/// </summary>
public sealed class Component
{
    public long Id { get; set; }

    public required string EntityId { get; set; }

    public Entity? Entity { get; set; }

    public required string DefinitionId { get; set; }

    public ComponentDefinition? Definition { get; set; }

    /// <summary>
    /// The payload, as a JSON object. Opaque to the kernel by design — this is the column that
    /// means the schema never has to change when the game gains a new idea.
    /// </summary>
    public string Data { get; set; } = "{}";

    /// <summary>
    /// Bumped on every write. Cheap change detection for the control room, and the hook a future
    /// optimistic-concurrency check would use.
    /// </summary>
    public int Revision { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
