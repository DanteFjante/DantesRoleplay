using DantesRoleplay.Categories;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Where every record lives in an exported catalog, and the only place that decides.
///
/// Export writes these paths and import reads them, so the two cannot disagree about where
/// anything is — the same argument that put the markdown writer next to its parser.
///
/// A category is a dot-delimited path and becomes a directory path, one segment per directory:
/// <c>ruleset.dnd2024.core.gameplay.ability-checks.fixed-dc</c> becomes six nested directories.
/// That is safe without any escaping because <see cref="CategoryPath"/> already restricts a
/// segment to lowercase letters, digits and hyphens — there is no '..', no separator, no drive
/// letter and no colon that could turn a category into a path outside the export root. It is
/// validated here anyway: a database row that predates that rule would otherwise be the one input
/// that turns a category into an arbitrary write.
///
/// Only mechanics and contracts have a category. Component definitions have no such column, so
/// they are grouped flat by kind rather than given an invented classification that nothing in the
/// database could validate.
///
/// Paths are returned with forward slashes, on every platform. They go into the manifest, and a
/// manifest full of backslashes is one that only reads back on the machine that wrote it.
/// </summary>
public static class CatalogLayout
{
    public const string ManifestFileName = "manifest.json";

    public const string MechanicsRoot = "mechanics";
    public const string ProceduresRoot = "procedures";
    public const string ComponentsRoot = "components";
    public const string EventTypesRoot = "event-types";
    public const string SubscriptionsRoot = "subscriptions";
    public const string WorldRoot = "world";
    public const string EntitiesRoot = WorldRoot + "/entities";
    public const string HistoryRoot = "history";

    public const string RelationshipsFileName = WorldRoot + "/relationships.json";
    public const string OperationsFileName = HistoryRoot + "/operations.jsonl";

    public const string MarkdownExtension = ".md";
    public const string SourceExtension = ".js";
    public const string DefinitionExtension = ".json";

    /// <summary>The .md holding a mechanic's front matter, description, match phrases and requirements.</summary>
    public static string MechanicMarkdown(string category, string id) =>
        Combine(MechanicsRoot, CategoryDirectory(category), id + MarkdownExtension);

    /// <summary>The .js holding a mechanic's JavaScript. Same basename as its .md, alongside it.</summary>
    public static string MechanicSource(string category, string id) =>
        Combine(MechanicsRoot, CategoryDirectory(category), id + SourceExtension);

    public static string ProcedureMarkdown(string category, string id) =>
        Combine(ProceduresRoot, CategoryDirectory(category), id + MarkdownExtension);

    /// <summary>The .json holding a component definition's id, name and description.</summary>
    public static string Component(string id) =>
        Combine(ComponentsRoot, id + DefinitionExtension);

    /// <summary>
    /// The sidecar holding a component definition's JSON Schema, verbatim.
    ///
    /// Separate from the definition file so the schema's own bytes survive a round trip. Inlined
    /// into the definition it would be reserialised, and a schema nobody touched would come back
    /// looking edited — the same reason a mechanic's JavaScript is not a JSON string.
    /// </summary>
    public static string ComponentSchema(string id) =>
        Combine(ComponentsRoot, id + ".schema" + DefinitionExtension);
    public static string EventType(string id) => Combine(EventTypesRoot, SafeFileName(id, "event type") + DefinitionExtension);
    public static string EventTypeSchema(string id) => Combine(EventTypesRoot, SafeFileName(id, "event type") + ".schema" + DefinitionExtension);
    public static string Subscription(string id) => Combine(SubscriptionsRoot, SafeFileName(id, "subscription") + DefinitionExtension);

    /// <summary>One entity, with its components and its container folded in.</summary>
    public static string Entity(string id) =>
        Combine(EntitiesRoot, SafeFileName(id, "entity") + DefinitionExtension);

    /// <summary>
    /// Refuses an id that cannot safely become a filename.
    ///
    /// Unlike a category, an entity id is not validated anywhere in the kernel — it is whatever the
    /// author passed to CreateEntityAsync, trimmed. So it is the one identifier in this system that
    /// could contain a path separator or "..", and turning that into a file path would let a
    /// database write outside the export root. Windows device names are refused too: an entity
    /// called "con" would produce a file that cannot be opened at all.
    /// </summary>
    public static string SafeFileName(string id, string what)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"An {what} has no id, so it has no filename.");
        }

        var trimmed = id.Trim();

        if (trimmed != id
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.EndsWith('.'))
        {
            throw new InvalidOperationException(
                $"The {what} id '{id}' cannot become a filename. Ids used in a catalog must have no "
                + "path separators, no '..', no trailing dot and no characters this platform "
                + "forbids in a filename.");
        }

        var stem = trimmed.Split('.')[0];

        if (ReservedNames.Contains(stem))
        {
            throw new InvalidOperationException(
                $"The {what} id '{id}' starts with '{stem}', which is a reserved device name on "
                + "Windows — a file called that cannot be opened. Rename the record.");
        }

        return trimmed;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>A dotted category as a relative directory path, or the empty string for no category.</summary>
    public static string CategoryDirectory(string category)
    {
        if (!CategoryPath.TryValidate(category, out var problem))
        {
            throw new InvalidOperationException(
                $"'{category}' cannot be turned into a directory path: {problem}");
        }

        return category.Replace('.', '/');
    }

    /// <summary>Turns a catalog-relative path into one this platform's file APIs accept.</summary>
    public static string ToFileSystemPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Combine(params string[] segments) =>
        string.Join('/', segments.Where(s => s.Length > 0));
}
