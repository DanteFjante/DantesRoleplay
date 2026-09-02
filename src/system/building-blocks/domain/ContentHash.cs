using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Content;

/// <summary>
/// The fingerprint of authored content, and the ONE definition of it.
///
/// Every layer that needs to answer "is this the same content?" asks here: the bootstrap file
/// parsers, the stores as they write, and — once catalog export lands — the drift detector that
/// decides whether a file edit or a live MCP edit is the newer one.
///
/// That single-definition rule is the whole point. Two hash functions over the same content
/// disagree the moment either one is touched, and the disagreement is silent: a drift detector
/// built on two definitions reports conflicts that are not there, or misses ones that are, and
/// nothing in either code path looks wrong. This class existing is what makes that impossible.
///
/// Three properties it guarantees, each of which was a live bug before it existed:
///
/// 1. <b>Fields cannot bleed into each other.</b> Values are joined with the ASCII unit separator
///    rather than concatenated. Without it, ("ab", "c") and ("a", "bc") hash identically and two
///    genuinely different rules read as unchanged copies. The contract file parser had the
///    separator; the mechanic one did not, and nothing tested for it.
/// 2. <b>Line endings do not change the answer.</b> The bootstrap parsers rebuild sections with
///    StringBuilder.AppendLine, which emits Environment.NewLine — so the same file seeded on
///    Windows and on Linux produced different fingerprints, and a catalog exported on one and
///    imported on the other would have reported every record as drifted.
/// 3. <b>Surrounding whitespace does not change the answer.</b> Content arriving over MCP is not
///    trimmed the way a parsed markdown section is, so the same rule authored through the two
///    channels would otherwise fingerprint differently.
///
/// What is deliberately NOT in the hash: the id (identity, not content — a record whose id changed
/// is a different record, not an edited one), and CreatedBy / ChangeNote / timestamps (provenance
/// about an edit, not the edited thing; including them would make every rewrite look like a change
/// even when the content is identical).
/// </summary>
public static class ContentHash
{
    /// <summary>
    /// ASCII unit separator (U+001F). Chosen because it cannot occur in authored text — a
    /// printable delimiter such as '|' can, and a value containing the delimiter would let two
    /// different field splits produce one hash, which is the bug this is here to prevent.
    /// </summary>
    private const char FieldSeparator = '\u001f';

    /// <summary>
    /// Uppercase hex SHA-256 over the canonicalised, separator-joined fields.
    ///
    /// Order is significant and is fixed by the callers below. A field must never be removed from
    /// one of those calls: a field outside the hash cannot be edited at all, because the
    /// fingerprint does not move, the seeder concludes nothing changed, and the edit is discarded
    /// forever. That shipped once with a contract's Governs field and is guarded by tests that
    /// vary one field at a time.
    /// </summary>
    public static string Of(params string?[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var canonical = string.Join(
            FieldSeparator,
            fields.Select(Normalise));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Every authored field of a mechanic, in a fixed order.
    ///
    /// Takes loose values rather than a parsed bootstrap file or a stored row on purpose. The
    /// file layer and the storage layer both call it with their own values, and the whole point of
    /// the exercise is that they arrive at the same answer — which also keeps this in the core
    /// project, where neither of those layers is visible.
    /// </summary>
    public static string ForMechanic(
        string? category,
        string? name,
        string? description,
        string? matches,
        string? requirements,
        string? source,
        string? scope,
        MechanicStatus status) =>
        Of(category, name, description, matches, requirements, source, scope, status.ToString());

    /// <summary>
    /// Every authored field of a component definition, in a fixed order.
    ///
    /// Component definitions are not versioned and carry no fingerprint column, so this is not
    /// stored anywhere — it exists so the catalog can compare them the same way it compares
    /// everything else, rather than having one kind of record that drift detection cannot see.
    /// </summary>
    public static string ForComponentDefinition(
        string? name,
        string? description,
        string? schema) =>
        Of(name, description, schema);

    /// <summary>
    /// An entity and everything attached to it: its name, where it sits, and each of its components.
    ///
    /// Components are folded in rather than fingerprinted separately because a component has no
    /// identity apart from the entity carrying it — "orban's abilities" is the whole of what it is.
    /// The caller must supply them in a stable order; dictionary order is not one.
    /// </summary>
    public static string ForEntity(
        string? name,
        string? containerId,
        string? containerSlot,
        IEnumerable<(string DefinitionId, string Data)> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var fields = new List<string?> { name, containerId, containerSlot };

        foreach (var component in components)
        {
            fields.Add(component.DefinitionId);
            fields.Add(component.Data);
        }

        return Of(fields.ToArray());
    }

    /// <summary>Every authored field of a procedure contract, in a fixed order.</summary>
    public static string ForProcedure(
        string? category,
        string? name,
        string? description,
        string? governs,
        string? instructions,
        string? constraints,
        ProcedureStatus status) =>
        Of(category, name, description, governs, instructions, constraints, status.ToString());

    /// <summary>
    /// Line endings to LF, then trim. Applied to every field before hashing, so the fingerprint
    /// describes the content rather than the platform or the transport that carried it.
    ///
    /// A lone CR is folded too. It is not a line ending anything in this system emits, but leaving
    /// one path unnormalised is how the next platform-dependent fingerprint gets introduced.
    ///
    /// Public because the catalog writer applies it to every field it writes out. An exported file
    /// and the fingerprint of the row it came from have to agree about whitespace, and the only way
    /// to guarantee that is for both to go through this.
    /// </summary>
    public static string Normalise(string? value) => value is null
        ? string.Empty
        : value.Replace("\r\n", "\n", StringComparison.Ordinal)
               .Replace('\r', '\n')
               .Trim();
}
