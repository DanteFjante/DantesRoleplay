using DantesRoleplay.DataAccess.Bootstrap;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Reads a catalog directory back into records.
///
/// It reuses <see cref="MechanicFile"/> and <see cref="ProcedureFile"/> rather than parsing
/// anything itself. That is the "do not write a second loader" rule from the plan, and it is
/// narrower than it sounds: what must not be duplicated is the PARSER. The bootstrap seeders keep
/// their own job — idempotently installing the shipped manual from embedded resources — because
/// seeding and drift-aware synchronisation are different questions, and a class that did both
/// would have to guess which one it was being asked.
///
/// Front matter is authoritative for a record's id and category, not its location on disk. A file
/// moved to the wrong directory still imports correctly and the next export puts it back, which is
/// friendlier than refusing to read a catalog somebody reorganised by hand.
/// </summary>
public static class CatalogReader
{
    public static async Task<CatalogContents> ReadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fullRoot = Path.GetFullPath(root);

        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"No catalog at '{fullRoot}'.");
        }

        var manifestPath = Path.Combine(fullRoot, CatalogLayout.ManifestFileName);

        var manifest = File.Exists(manifestPath)
            ? CatalogManifest.FromJson(
                await File.ReadAllTextAsync(manifestPath, cancellationToken),
                CatalogLayout.ManifestFileName)
            : null;

        var entities = await ReadEntitiesAsync(fullRoot, cancellationToken);
        var relationshipsPath = CatalogLayout.ToFileSystemPath(fullRoot, CatalogLayout.RelationshipsFileName);

        var relationships = File.Exists(relationshipsPath)
            ? RelationshipsFile.Parse(
                await File.ReadAllTextAsync(relationshipsPath, cancellationToken),
                CatalogLayout.RelationshipsFileName)
            : null;

        return new CatalogContents(
            manifest,
            await ReadMechanicsAsync(fullRoot, cancellationToken),
            await ReadProceduresAsync(fullRoot, cancellationToken),
            await ReadComponentsAsync(fullRoot, cancellationToken),
            entities,
            relationships);
    }

    private static async Task<IReadOnlyList<EntityFile>> ReadEntitiesAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, CatalogLayout.EntitiesRoot.Replace('/', Path.DirectorySeparatorChar));
        var files = new List<EntityFile>();

        if (!Directory.Exists(directory))
        {
            return files;
        }

        foreach (var path in Directory
            .EnumerateFiles(directory, "*" + CatalogLayout.DefinitionExtension, SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            files.Add(EntityFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path)));
        }

        return Unique(files, f => f.Id, "entity");
    }

    private static async Task<IReadOnlyList<MechanicFile>> ReadMechanicsAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<MechanicFile>();

        foreach (var path in Markdown(root, CatalogLayout.MechanicsRoot))
        {
            var sourcePath = Path.ChangeExtension(path, CatalogLayout.SourceExtension);

            // Passed only when it exists. A hand-authored rule may still carry its source in a
            // '## Source' section, and Parse refuses the case where both are present.
            var sidecar = File.Exists(sourcePath)
                ? await File.ReadAllTextAsync(sourcePath, cancellationToken)
                : null;

            files.Add(MechanicFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path),
                sidecar));
        }

        return Unique(files, f => f.Id, "rule");
    }

    private static async Task<IReadOnlyList<ProcedureFile>> ReadProceduresAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<ProcedureFile>();

        foreach (var path in Markdown(root, CatalogLayout.ProceduresRoot))
        {
            files.Add(ProcedureFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path)));
        }

        return Unique(files, f => f.Id, "contract");
    }

    private static async Task<IReadOnlyList<ComponentDefinitionFile>> ReadComponentsAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, CatalogLayout.ComponentsRoot);
        var files = new List<ComponentDefinitionFile>();

        if (!Directory.Exists(directory))
        {
            return files;
        }

        foreach (var path in Directory
            .EnumerateFiles(directory, "*" + CatalogLayout.DefinitionExtension, SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".schema" + CatalogLayout.DefinitionExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            var schemaPath = path[..^CatalogLayout.DefinitionExtension.Length]
                             + ".schema" + CatalogLayout.DefinitionExtension;

            var schema = File.Exists(schemaPath)
                ? await File.ReadAllTextAsync(schemaPath, cancellationToken)
                : null;

            files.Add(ComponentDefinitionFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path),
                schema));
        }

        return Unique(files, f => f.Id, "component definition");
    }

    private static IEnumerable<string> Markdown(string root, string area)
    {
        var directory = Path.Combine(root, area);

        return Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*" + CatalogLayout.MarkdownExtension, SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
            : [];
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Two files claiming one id is refused rather than resolved. Whichever won would depend on
    /// directory enumeration order, so the same catalog would import differently on two machines.
    /// </summary>
    private static IReadOnlyList<T> Unique<T>(List<T> files, Func<T, string> id, string what)
    {
        var duplicate = files.GroupBy(id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);

        return duplicate is null
            ? files
            : throw new InvalidOperationException(
                $"Two {what} files in the catalog declare id '{duplicate.Key}'. Remove one — which "
                + "of them won would otherwise depend on the order the filesystem listed them.");
    }
}

/// <param name="Manifest">Null when the catalog has none, which forces two-way comparison.</param>
/// <param name="Relationships">Null when the catalog has no relationships file at all.</param>
public sealed record CatalogContents(
    CatalogManifest? Manifest,
    IReadOnlyList<MechanicFile> Mechanics,
    IReadOnlyList<ProcedureFile> Procedures,
    IReadOnlyList<ComponentDefinitionFile> Components,
    IReadOnlyList<EntityFile> Entities,
    RelationshipsFile? Relationships)
{
    public int Records => Mechanics.Count + Procedures.Count + Components.Count + Entities.Count;

    /// <summary>
    /// Whether world state is in scope for this catalog.
    ///
    /// True when the manifest says so, and ALSO when world files are simply present. A --rules-only
    /// catalog that somebody has since hand-added entities to should have those entities read,
    /// rather than silently ignored because a flag written weeks ago says the world is not here.
    /// </summary>
    public bool HasWorld =>
        Manifest?.IncludesWorld == true || Entities.Count > 0 || Relationships is not null;
}
