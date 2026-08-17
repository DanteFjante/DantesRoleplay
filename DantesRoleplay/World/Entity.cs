namespace DantesRoleplay.World;

/// <summary>
/// A thing in the world. Deliberately almost empty.
///
/// ARCHITECTURE.md §3.11: the kernel contains no game vocabulary. An entity is not a character,
/// a location or an item — it is an identity that components hang off. What it *is* comes
/// entirely from the components attached to it, which are data.
///
/// This is what makes the schema fixed: a new kind of thing is a row, never a column.
/// </summary>
public sealed class Entity
{
    /// <summary>Stable identifier. A slug when the author supplies one, otherwise a generated id.</summary>
    public required string Id { get; set; }

    /// <summary>
    /// Human- and LLM-readable label. Convenience only — nothing mechanical may depend on it,
    /// because two entities may legitimately share a name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>UTC — see <see cref="Procedures.ProcedureContract.CreatedAt"/> for why not DateTimeOffset.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Soft delete. Null means live. Deleting is soft because the event ledger will reference
    /// entities that no longer exist, and a dangling id in history is worse than a tombstone.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public ICollection<Component> Components { get; set; } = new List<Component>();
}
