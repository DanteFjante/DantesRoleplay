using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.CatalogNamespaces;

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
/// Front matter remains authoritative for schema-version-1 catalogs. Schema version 2 also treats
/// the registered namespace as a placement invariant: a file in the wrong directory is refused so
/// an accidental move cannot silently claim a different organization than its qualified ID.
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

        var strict = manifest?.SchemaVersion >= 2;
        var namespaces = await ReadNamespacesAsync(fullRoot, strict, cancellationToken);
        var entities = await ReadEntitiesAsync(fullRoot, strict, cancellationToken);
        var relationshipsPath = CatalogLayout.ToFileSystemPath(fullRoot, CatalogLayout.RelationshipsFileName);

        var relationships = File.Exists(relationshipsPath)
            ? RelationshipsFile.Parse(
                await File.ReadAllTextAsync(relationshipsPath, cancellationToken),
                CatalogLayout.RelationshipsFileName)
            : null;

        var contents = new CatalogContents(
            manifest,
            await ReadMechanicsAsync(fullRoot, strict, cancellationToken),
            await ReadProceduresAsync(fullRoot, strict, cancellationToken),
            await ReadComponentsAsync(fullRoot, strict, cancellationToken),
            await ReadEventTypesAsync(fullRoot, strict, cancellationToken),
            await ReadSubscriptionsAsync(fullRoot, strict, cancellationToken),
            entities,
            relationships,
            namespaces);
        if (strict)
        {
            ValidateNamespaces(contents);
            ValidateManifestPaths(contents.Manifest!);
            ValidateManagedPaths(fullRoot, contents);
        }
        return contents;
    }

    private static async Task<IReadOnlyList<CatalogNamespaceFile>> ReadNamespacesAsync(
        string root, bool strict, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, CatalogLayout.NamespacesRoot);
        if (!Directory.Exists(directory)) return [];
        var result = new List<CatalogNamespaceFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            var file = CatalogNamespaceFile.Parse(await File.ReadAllTextAsync(path, cancellationToken), Relative(root, path));
            if (strict) RequirePath(root, path, CatalogLayout.Namespace(file.Id), file.Id);
            result.Add(file);
        }
        return Unique(result, value => value.Id, "namespace");
    }

    private static async Task<IReadOnlyList<EventTypeFile>> ReadEventTypesAsync(string root, bool strict, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, CatalogLayout.EventTypesRoot); var files = new List<EventTypeFile>(); if (!Directory.Exists(directory)) return files;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => !p.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
        { var schemaPath = path[..^CatalogLayout.DefinitionExtension.Length] + ".schema.json"; var file = EventTypeFile.Parse(await File.ReadAllTextAsync(path, cancellationToken), Relative(root, path), File.Exists(schemaPath) ? await File.ReadAllTextAsync(schemaPath, cancellationToken) : null); if (strict) RequirePath(root, path, CatalogLayout.EventType(file.Id), file.Id); files.Add(file); }
        return Unique(files, x => x.Id, "event type");
    }

    private static async Task<IReadOnlyList<SubscriptionFile>> ReadSubscriptionsAsync(string root, bool strict, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, CatalogLayout.SubscriptionsRoot); var files = new List<SubscriptionFile>(); if (!Directory.Exists(directory)) return files;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)) { var file = SubscriptionFile.Parse(await File.ReadAllTextAsync(path, cancellationToken), Relative(root, path)); if (strict) RequirePath(root, path, CatalogLayout.Subscription(file.Id), file.Id); files.Add(file); }
        return Unique(files, x => x.Id, "subscription");
    }

    private static async Task<IReadOnlyList<EntityFile>> ReadEntitiesAsync(
        string root,
        bool strict,
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
            var file = EntityFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path));
            if (strict) RequirePath(root, path, CatalogLayout.Entity(file.Id), file.Id);
            files.Add(file);
        }

        return Unique(files, f => f.Id, "entity");
    }

    private static async Task<IReadOnlyList<MechanicFile>> ReadMechanicsAsync(
        string root,
        bool strict,
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

            var file = MechanicFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path),
                sidecar);
            if (strict) RequirePath(root, path, CatalogLayout.MechanicMarkdown(file.Category, file.Id), file.Id);
            files.Add(file);
        }

        return Unique(files, f => f.Id, "rule");
    }

    private static async Task<IReadOnlyList<ProcedureFile>> ReadProceduresAsync(
        string root,
        bool strict,
        CancellationToken cancellationToken)
    {
        var files = new List<ProcedureFile>();

        foreach (var path in Markdown(root, CatalogLayout.ProceduresRoot))
        {
            var file = ProcedureFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path));
            if (strict) RequirePath(root, path, CatalogLayout.ProcedureMarkdown(file.Category, file.Id), file.Id);
            files.Add(file);
        }

        return Unique(files, f => f.Id, "contract");
    }

    private static async Task<IReadOnlyList<ComponentDefinitionFile>> ReadComponentsAsync(
        string root,
        bool strict,
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

            var file = ComponentDefinitionFile.Parse(
                await File.ReadAllTextAsync(path, cancellationToken),
                Relative(root, path),
                schema);
            if (strict) RequirePath(root, path, CatalogLayout.Component(file.Id), file.Id);
            files.Add(file);
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

    private static void RequirePath(string root, string actualPath, string expectedRelativePath, string id)
    {
        var actual = Relative(root, actualPath);
        if (!string.Equals(actual, expectedRelativePath, StringComparison.Ordinal))
            throw new InvalidOperationException($"Catalog record '{id}' is stored at '{actual}', but its namespace requires '{expectedRelativePath}'. Move the file or correct its ID.");
    }

    private static void ValidateNamespaces(CatalogContents contents)
    {
        var definitions = contents.Namespaces.ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (var definition in contents.Namespaces)
        {
            var parent = definition.Id == CatalogNamespaceIdentity.RootNamespaceId ? null
                : definition.Id.Contains('.') ? definition.Id[..definition.Id.LastIndexOf('.')] : null;
            if (parent is not null && !definitions.ContainsKey(parent))
                throw new InvalidOperationException($"Namespace '{definition.Id}' requires registered parent '{parent}'.");
            if (parent is not null && definitions[parent].Owner != definition.Owner)
                throw new InvalidOperationException($"Namespace '{definition.Id}' must have the same owner as parent '{parent}'.");
            if (definition.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed && parent is not null
                && definitions[parent].ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
                throw new InvalidOperationException($"Namespace '{definition.Id}' cannot be reviewed before parent '{parent}'.");
        }
        foreach (var (id, kind) in RecordIdentities(contents))
        {
            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(id);
            if (!definitions.TryGetValue(namespaceId, out var definition))
                throw new InvalidOperationException($"Catalog record '{id}' uses unregistered namespace '{namespaceId}'.");
            if (!definition.AllowedKinds.Contains(kind, StringComparer.Ordinal))
                throw new InvalidOperationException($"Namespace '{namespaceId}' does not allow '{kind}' records such as '{id}'.");
        }
    }

    private static void ValidateManifestPaths(CatalogManifest manifest)
    {
        var duplicates = manifest.Records
            .GroupBy(value => (value.Kind, value.Id))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Kind}:{group.Key.Id}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
            throw new InvalidOperationException(
                "The catalog manifest contains duplicate record identities: " + string.Join(", ", duplicates));

        var misplaced = manifest.Records.Select(value => new
            {
                Value = value,
                Expected = CatalogLayout.Record(value.Kind, value.Id)
            })
            .Where(value => !string.Equals(value.Value.Path, value.Expected, StringComparison.Ordinal))
            .OrderBy(value => value.Value.Path, StringComparer.Ordinal)
            .Select(value => $"Manifest record '{value.Value.Id}' uses '{value.Value.Path}', but its namespace requires '{value.Expected}'.")
            .ToArray();
        if (misplaced.Length != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, misplaced));
    }

    private static void ValidateManagedPaths(string root, CatalogContents contents)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        void Add(string relativePath) => expected.Add(relativePath);
        bool Exists(string relativePath) => File.Exists(CatalogLayout.ToFileSystemPath(root, relativePath));

        foreach (var value in contents.Namespaces) Add(CatalogLayout.Namespace(value.Id));
        foreach (var value in contents.Mechanics)
        {
            Add(CatalogLayout.MechanicMarkdown(value.Category, value.Id));
            var source = CatalogLayout.MechanicSource(value.Category, value.Id);
            if (Exists(source)) Add(source);
        }
        foreach (var value in contents.Procedures) Add(CatalogLayout.ProcedureMarkdown(value.Category, value.Id));
        foreach (var value in contents.Components)
        {
            Add(CatalogLayout.Component(value.Id));
            var schema = CatalogLayout.ComponentSchema(value.Id);
            if (Exists(schema)) Add(schema);
        }
        foreach (var value in contents.EventTypes)
        {
            Add(CatalogLayout.EventType(value.Id));
            var schema = CatalogLayout.EventTypeSchema(value.Id);
            if (Exists(schema)) Add(schema);
        }
        foreach (var value in contents.Subscriptions) Add(CatalogLayout.Subscription(value.Id));
        foreach (var value in contents.Entities) Add(CatalogLayout.Entity(value.Id));
        if (contents.Relationships is not null) Add(CatalogLayout.RelationshipsFileName);

        var roots = new[]
        {
            CatalogLayout.MechanicsRoot,
            CatalogLayout.ProceduresRoot,
            CatalogLayout.ComponentsRoot,
            CatalogLayout.EventTypesRoot,
            CatalogLayout.SubscriptionsRoot,
            CatalogLayout.NamespacesRoot,
            CatalogLayout.WorldRoot
        };
        var actual = roots.Select(value => Path.Combine(root, value))
            .Where(Directory.Exists)
            .SelectMany(value => Directory.EnumerateFiles(value, "*", SearchOption.AllDirectories))
            .Where(IsManagedCatalogFile)
            .Select(value => Relative(root, value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unexpected = actual.Where(value => !expected.Contains(value)).ToArray();
        if (unexpected.Length != 0)
            throw new InvalidOperationException(
                "Schema-version-2 catalogs only allow namespace-derived managed paths. Misplaced or orphan files:"
                + Environment.NewLine + string.Join(Environment.NewLine, unexpected.Select(value => $"- {value}")));
    }

    private static bool IsManagedCatalogFile(string path) =>
        path.EndsWith(CatalogLayout.MarkdownExtension, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(CatalogLayout.SourceExtension, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(CatalogLayout.DefinitionExtension, StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<(string Id, string Kind)> RecordIdentities(CatalogContents contents)
    {
        foreach (var value in contents.Mechanics) yield return (value.Id, CatalogNamespaceKinds.Mechanic);
        foreach (var value in contents.Procedures) yield return (value.Id, CatalogNamespaceKinds.Procedure);
        foreach (var value in contents.Components) yield return (value.Id, CatalogNamespaceKinds.ComponentDefinition);
        foreach (var value in contents.EventTypes) yield return (value.Id, CatalogNamespaceKinds.EventType);
        foreach (var value in contents.Subscriptions) yield return (value.Id, CatalogNamespaceKinds.Subscription);
        foreach (var value in contents.Entities) yield return (value.Id, CatalogNamespaceKinds.Entity);
    }

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
    IReadOnlyList<EventTypeFile> EventTypes,
    IReadOnlyList<SubscriptionFile> Subscriptions,
    IReadOnlyList<EntityFile> Entities,
    RelationshipsFile? Relationships,
    IReadOnlyList<CatalogNamespaceFile>? NamespaceFiles = null)
{
    public IReadOnlyList<CatalogNamespaceFile> Namespaces => NamespaceFiles ?? [];
    public int Records => Mechanics.Count + Procedures.Count + Components.Count + EventTypes.Count + Subscriptions.Count + Entities.Count;

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
