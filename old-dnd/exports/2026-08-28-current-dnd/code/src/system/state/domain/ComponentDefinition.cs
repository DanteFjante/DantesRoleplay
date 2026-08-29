namespace DantesRoleplay.World;

/// <summary>
/// Declares that a kind of component may exist, e.g. "stats", "position", "inventory-slot",
/// "condition.poisoned".
///
/// The kernel does not interpret any of this. A definition exists so that (a) the LLM can
/// discover what kinds of data the world already uses before inventing a parallel one, and
/// (b) the control room can list them. Creating a definition is how a new game concept enters
/// the system — with no schema change.
/// </summary>
public sealed class ComponentDefinition
{
    /// <summary>Stable identifier, dotted or hyphenated by convention.</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>What this component means and when to attach it. Written for the LLM to read.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// Optional JSON describing the expected shape — field names, ranges, defaults.
    ///
    /// **The kernel does not enforce this.** It is documentation that travels with the data, and
    /// a place for JavaScript helpers to read a declared range from. Enforcement belongs in
    /// JavaScript per §3.11; validating here would make the kernel start knowing what a stat is.
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
