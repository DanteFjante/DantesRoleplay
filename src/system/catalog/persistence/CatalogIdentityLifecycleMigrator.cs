using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// One reviewed catalog identity correction. The kind is explicit because two catalog populations
/// may legitimately use the same local name, while the corrected ID is validated through the
/// registered namespace before any file is changed.
/// </summary>
public sealed record CatalogIdentityRename(CatalogRecordKind Kind, string SourceId, string CorrectedId);

public sealed record CatalogIdentityMigrationPlan(
    IReadOnlyList<CatalogNamespaceFile> Namespaces,
    IReadOnlyList<CatalogIdentityRename> Renames)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<CatalogIdentityMigrationPlan> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Identity migration plan not found.", fullPath);
        var plan = JsonSerializer.Deserialize<CatalogIdentityMigrationPlan>(
            await File.ReadAllTextAsync(fullPath, cancellationToken), Json)
            ?? throw new InvalidOperationException("The identity migration plan is empty.");
        return plan with { Namespaces = plan.Namespaces ?? [], Renames = plan.Renames ?? [] };
    }
}

public sealed record CatalogIdentityMigrationResult(
    string CatalogRoot,
    int RenamedRecords,
    int RewrittenFiles,
    int RegisteredNamespaces,
    bool Applied);

/// <summary>
/// Applies reviewed identity corrections to an authored catalog as one lifecycle operation.
/// Work happens in a complete sibling staging copy, is imported and exported through the
/// production catalog stores, and passes fresh-database validation before the source directory is
/// committed with a temporary rollback copy. The live game database is never opened.
/// </summary>
public static class CatalogIdentityLifecycleMigrator
{
    private static readonly string[] TextExtensions = [".json", ".md", ".js"];

    public static async Task<CatalogIdentityMigrationResult> MigrateAsync(
        string root,
        CatalogIdentityMigrationPlan plan,
        bool apply,
        bool referencesOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(plan);
        var source = Path.GetFullPath(root);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"No catalog at '{source}'.");

        var parent = Directory.GetParent(source)?.FullName
            ?? throw new InvalidOperationException("The catalog must have a parent directory.");
        var token = Guid.NewGuid().ToString("n");
        var stage = Path.Combine(parent, $".{Path.GetFileName(source)}.identity-stage-{token}");
        var rollback = Path.Combine(parent, $".{Path.GetFileName(source)}.identity-rollback-{token}");
        var databasePath = Path.Combine(parent, $".identity-migration-{token}.db");
        try
        {
            CopyDirectory(source, stage);
            var original = await CatalogReader.ReadAsync(stage, cancellationToken);
            if (referencesOnly) ValidateReferenceRepair(original, plan.Renames);
            else ValidatePlan(original, plan);
            if (!referencesOnly)
                await WriteNamespacesAsync(stage, original, plan.Namespaces, cancellationToken);
            var rewritten = await RewriteReferencesAsync(stage, plan.Renames, cancellationToken);

            var manifestPath = CatalogLayout.ToFileSystemPath(stage, CatalogLayout.ManifestFileName);
            File.Delete(manifestPath);

            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.MigrateAsync(cancellationToken);
                var imported = await new CatalogImporter(
                    db,
                    new MechanicStore(db),
                    new ProcedureStore(db),
                    new WorldStore(db),
                    new EventTypeStore(db),
                    new SubscriptionStore(db))
                    .ApplyAsync(stage, new CatalogImportOptions(), cancellationToken);
                if (imported.Aborted)
                    throw new InvalidOperationException("The staged identity migration did not import cleanly.");

                await new CatalogExporter(db).ExportAsync(stage, cancellationToken);
            }
            await StampManifestAsync(stage, cancellationToken);

            if (!referencesOnly) DeleteFormerRecordFiles(stage, original, plan.Renames);
            var validation = await CatalogValidator.ValidateAsync(stage, cancellationToken);
            if (!validation.IsValid || validation.Warnings != 0)
                throw new InvalidOperationException(
                    $"The staged catalog failed validation with {validation.Errors} error(s) and "
                    + $"{validation.Warnings} warning(s).");

            if (!apply)
                return new(source, referencesOnly ? 0 : plan.Renames.Count, rewritten,
                    referencesOnly ? 0 : plan.Namespaces.Count, Applied: false);

            CopyDirectory(source, rollback);
            try
            {
                ReplaceDirectoryContents(stage, source);
                var appliedValidation = await CatalogValidator.ValidateAsync(source, cancellationToken);
                if (!appliedValidation.IsValid || appliedValidation.Warnings != 0)
                    throw new InvalidOperationException("The applied catalog did not match its validated staging copy.");
            }
            catch
            {
                ReplaceDirectoryContents(rollback, source);
                throw;
            }
            Directory.Delete(rollback, recursive: true);
            return new(source, referencesOnly ? 0 : plan.Renames.Count, rewritten,
                referencesOnly ? 0 : plan.Namespaces.Count, Applied: true);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            if (Directory.Exists(rollback)) Directory.Delete(rollback, recursive: true);
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static void ValidatePlan(CatalogContents contents, CatalogIdentityMigrationPlan plan)
    {
        if (plan.Renames.Count == 0) throw new InvalidOperationException("The migration has no identity renames.");
        var duplicateSource = plan.Renames.GroupBy(value => (value.Kind, value.SourceId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
            throw new InvalidOperationException($"Identity '{duplicateSource.Key.SourceId}' is renamed more than once.");
        var duplicateTarget = plan.Renames.GroupBy(value => (value.Kind, value.CorrectedId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
            throw new InvalidOperationException($"Corrected identity '{duplicateTarget.Key.CorrectedId}' is used more than once.");

        var namespaces = contents.Namespaces.Concat(plan.Namespaces)
            .GroupBy(value => value.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (namespaces.Any(pair => pair.Value.Length > 1))
            throw new InvalidOperationException("A namespace in the migration is already registered.");

        foreach (var rename in plan.Renames)
        {
            CatalogNamespaceIdentity.ValidateRecordId(rename.SourceId);
            CatalogNamespaceIdentity.ValidateRecordId(rename.CorrectedId);
            if (rename.SourceId == rename.CorrectedId)
                throw new InvalidOperationException($"Identity '{rename.SourceId}' does not change.");
            if (!Contains(contents, rename.Kind, rename.SourceId))
                throw new InvalidOperationException($"{rename.Kind} '{rename.SourceId}' does not exist in the catalog.");
            if (Contains(contents, rename.Kind, rename.CorrectedId))
                throw new InvalidOperationException($"{rename.Kind} '{rename.CorrectedId}' already exists in this catalog source.");

            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(rename.CorrectedId);
            if (!namespaces.TryGetValue(namespaceId, out var definitions))
                throw new InvalidOperationException($"Corrected namespace '{namespaceId}' is not registered.");
            var definition = definitions[0];
            var kind = NamespaceKind(rename.Kind);
            if (!definition.Enabled || definition.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed
                || !definition.AllowedKinds.Contains(kind, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Corrected namespace '{namespaceId}' is not an enabled, reviewed owner of '{kind}'.");
        }

        var allNamespaces = namespaces.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var definition in plan.Namespaces)
        {
            var parent = definition.Id.Contains('.') ? definition.Id[..definition.Id.LastIndexOf('.')] : null;
            if (parent is not null && !allNamespaces.Contains(parent))
                throw new InvalidOperationException($"Namespace '{definition.Id}' is missing parent '{parent}'.");
        }
    }

    private static void ValidateReferenceRepair(
        CatalogContents contents,
        IReadOnlyList<CatalogIdentityRename> renames)
    {
        if (renames.Count == 0) throw new InvalidOperationException("The repair has no identity references.");
        foreach (var rename in renames)
        {
            CatalogNamespaceIdentity.ValidateRecordId(rename.SourceId);
            CatalogNamespaceIdentity.ValidateRecordId(rename.CorrectedId);
            if (Contains(contents, rename.Kind, rename.SourceId))
                throw new InvalidOperationException(
                    $"{rename.Kind} '{rename.SourceId}' still exists; run the identity migration first.");
            if (!Contains(contents, rename.Kind, rename.CorrectedId))
                throw new InvalidOperationException(
                    $"Corrected {rename.Kind} '{rename.CorrectedId}' does not exist in this catalog source.");
        }
    }

    private static async Task WriteNamespacesAsync(
        string root,
        CatalogContents original,
        IReadOnlyList<CatalogNamespaceFile> additions,
        CancellationToken cancellationToken)
    {
        var ownerById = original.Namespaces.Concat(additions)
            .ToDictionary(value => value.Id, value => value.Owner, StringComparer.Ordinal);
        foreach (var definition in additions.OrderBy(value => value.Id.Count(character => character == '.'))
                     .ThenBy(value => value.Id, StringComparer.Ordinal))
        {
            var parent = definition.Id.Contains('.') ? definition.Id[..definition.Id.LastIndexOf('.')] : null;
            if (parent is not null && ownerById[parent] != definition.Owner)
                throw new InvalidOperationException($"Namespace '{definition.Id}' must have the same owner as '{parent}'.");
            var path = CatalogLayout.ToFileSystemPath(root, CatalogLayout.Namespace(definition.Id));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, definition.ToJson(), cancellationToken);
        }
    }

    private static async Task<int> RewriteReferencesAsync(
        string root,
        IReadOnlyList<CatalogIdentityRename> renames,
        CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            if (string.Equals(Path.GetFileName(path), CatalogLayout.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            var current = await File.ReadAllTextAsync(path, cancellationToken);
            var rewritten = current;
            foreach (var rename in renames.OrderByDescending(value => value.SourceId.Length))
                rewritten = RewriteReference(rewritten, rename);
            if (rewritten == current) continue;
            await File.WriteAllTextAsync(path, rewritten, cancellationToken);
            changed++;
        }
        return changed;
    }

    private static string RewriteReference(string text, CatalogIdentityRename rename)
    {
        var source = Regex.Escape(rename.SourceId);
        if (rename.SourceId.Contains('.') || rename.SourceId.Contains('-'))
        {
            // A terminal full stop is punctuation unless another identifier segment follows it.
            return Regex.Replace(text, $"(?<![A-Za-z0-9_.-]){source}(?![A-Za-z0-9_-]|\\.[A-Za-z0-9_-])",
                rename.CorrectedId, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }

        // A short legacy ID such as "stats" or "lantern" is also ordinary prose and may be a
        // JavaScript variable. Only exact string tokens and front-matter identities are references.
        // Component property access is the one structured unquoted form; a dotted corrected ID
        // must use bracket notation.
        if (rename.Kind == CatalogRecordKind.ComponentDefinition)
        {
            text = Regex.Replace(text, $@"\.components\.{source}(?![A-Za-z0-9_-])",
                $".components['{rename.CorrectedId}']", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
        }
        text = Regex.Replace(text, $"(?<quote>[\"']){source}\\k<quote>",
            match => $"{match.Groups["quote"].Value}{rename.CorrectedId}{match.Groups["quote"].Value}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        return Regex.Replace(text, $@"(?m)^(?<prefix>id:\s*){source}(?<suffix>\s*)$",
            match => $"{match.Groups["prefix"].Value}{rename.CorrectedId}{match.Groups["suffix"].Value}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    private static void DeleteFormerRecordFiles(
        string root,
        CatalogContents original,
        IReadOnlyList<CatalogIdentityRename> renames)
    {
        foreach (var rename in renames)
        {
            foreach (var relative in FormerPaths(original, rename))
            {
                var path = CatalogLayout.ToFileSystemPath(root, relative);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> FormerPaths(CatalogContents contents, CatalogIdentityRename rename)
    {
        switch (rename.Kind)
        {
            case CatalogRecordKind.Mechanic:
                var mechanic = contents.Mechanics.Single(value => value.Id == rename.SourceId);
                yield return CatalogLayout.MechanicMarkdown(mechanic.Category, mechanic.Id);
                yield return CatalogLayout.MechanicSource(mechanic.Category, mechanic.Id);
                break;
            case CatalogRecordKind.Procedure:
                var procedure = contents.Procedures.Single(value => value.Id == rename.SourceId);
                yield return CatalogLayout.ProcedureMarkdown(procedure.Category, procedure.Id);
                break;
            case CatalogRecordKind.ComponentDefinition:
                yield return CatalogLayout.Component(rename.SourceId);
                yield return CatalogLayout.ComponentSchema(rename.SourceId);
                break;
            case CatalogRecordKind.EventType:
                yield return CatalogLayout.EventType(rename.SourceId);
                yield return CatalogLayout.EventTypeSchema(rename.SourceId);
                break;
            case CatalogRecordKind.Subscription:
                yield return CatalogLayout.Subscription(rename.SourceId);
                break;
            case CatalogRecordKind.Entity:
                yield return CatalogLayout.Entity(rename.SourceId);
                break;
            default:
                throw new InvalidOperationException($"{rename.Kind} identities cannot be renamed.");
        }
    }

    private static bool Contains(CatalogContents contents, CatalogRecordKind kind, string id) => kind switch
    {
        CatalogRecordKind.Mechanic => contents.Mechanics.Any(value => value.Id == id),
        CatalogRecordKind.Procedure => contents.Procedures.Any(value => value.Id == id),
        CatalogRecordKind.ComponentDefinition => contents.Components.Any(value => value.Id == id),
        CatalogRecordKind.EventType => contents.EventTypes.Any(value => value.Id == id),
        CatalogRecordKind.Subscription => contents.Subscriptions.Any(value => value.Id == id),
        CatalogRecordKind.Entity => contents.Entities.Any(value => value.Id == id),
        _ => throw new InvalidOperationException($"{kind} identities cannot be renamed.")
    };

    private static string NamespaceKind(CatalogRecordKind kind) => kind switch
    {
        CatalogRecordKind.Mechanic => CatalogNamespaceKinds.Mechanic,
        CatalogRecordKind.Procedure => CatalogNamespaceKinds.Procedure,
        CatalogRecordKind.ComponentDefinition => CatalogNamespaceKinds.ComponentDefinition,
        CatalogRecordKind.EventType => CatalogNamespaceKinds.EventType,
        CatalogRecordKind.Subscription => CatalogNamespaceKinds.Subscription,
        CatalogRecordKind.Entity => CatalogNamespaceKinds.Entity,
        _ => throw new InvalidOperationException($"{kind} identities cannot be renamed.")
    };

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private static void ReplaceDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var expectedFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(source, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            if (!expectedFiles.Contains(Path.GetRelativePath(destination, existing))) File.Delete(existing);
        }
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(destination, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    private static async Task StampManifestAsync(string root, CancellationToken cancellationToken)
    {
        var path = CatalogLayout.ToFileSystemPath(root, CatalogLayout.ManifestFileName);
        var manifest = CatalogManifest.FromJson(
            await File.ReadAllTextAsync(path, cancellationToken), CatalogLayout.ManifestFileName);
        await File.WriteAllTextAsync(path, (manifest with
        {
            SourceDatabase = "catalog identity lifecycle migration; live database untouched"
        }).ToJson(), cancellationToken);
    }
}
