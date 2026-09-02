using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Writes the live ruleset out as a folder of ordinary files.
///
/// The latest version of every mechanic, procedure contract and component definition — not the
/// version history. A revision chain is what the database is for; the catalog is what a developer
/// edits, a linter reads and git diffs, and forty superseded copies of a rule in it would help
/// none of those.
///
/// <b>Strictly read-only.</b> No SaveChanges, no operation log entry, no UpdatedAt touch, and
/// every query is AsNoTracking. That is not fastidiousness: capturing live work is something you
/// want to be able to do without thinking, and a tool that modifies the thing it captures is one
/// people hesitate over. Hesitating is how the database and the catalog drift apart.
/// </summary>
public sealed class CatalogExporter(DantesRoleplayDbContext db)
{
    private readonly DantesRoleplayDbContext _db = db;

    public Task<CatalogExportResult> ExportAsync(
        string root,
        CancellationToken cancellationToken = default) =>
        ExportAsync(root, new CatalogExportOptions(), cancellationToken);

    public async Task<CatalogExportResult> ExportAsync(
        string root,
        CatalogExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);

        var written = new List<string>();
        var entries = new List<CatalogManifestEntry>();

        await ExportNamespacesAsync(fullRoot, written, cancellationToken);

        var mechanics = await ExportMechanicsAsync(fullRoot, written, entries, cancellationToken);
        var procedures = await ExportProceduresAsync(fullRoot, written, entries, cancellationToken);
        var components = await ExportComponentsAsync(fullRoot, written, entries, cancellationToken);
        var eventTypes = await ExportEventTypesAsync(fullRoot, written, entries, cancellationToken);
        var subscriptions = await ExportSubscriptionsAsync(fullRoot, written, entries, cancellationToken);

        var entities = 0;

        if (!options.RulesOnly)
        {
            entities = await ExportWorldAsync(fullRoot, written, entries, cancellationToken);
        }

        var operations = options.WithHistory
            ? await ExportHistoryAsync(fullRoot, written, cancellationToken)
            : 0;

        var manifest = new CatalogManifest
        {
            ExportedAt = DateTime.UtcNow,
            SourceDatabase = _db.Database.GetDbConnection().DataSource,
            IncludesWorld = !options.RulesOnly,
            Records = entries
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList()
        };

        var manifestPath = Path.Combine(fullRoot, CatalogLayout.ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), cancellationToken);
        written.Add(CatalogLayout.ManifestFileName);

        return new CatalogExportResult(
            fullRoot,
            mechanics,
            procedures,
            components,
            eventTypes,
            subscriptions,
            entities,
            operations,
            FindOrphans(fullRoot, written));
    }

    private async Task ExportNamespacesAsync(
        string root,
        List<string> written,
        CancellationToken cancellationToken)
    {
        var identities = await StoredIdentitiesAsync(cancellationToken);
        var stored = await _db.Set<CatalogNamespaceRecord>().AsNoTracking()
            .OrderBy(value => value.Id).ToArrayAsync(cancellationToken);
        IReadOnlyList<CatalogNamespaceFile> files;
        if (stored.Length == 0)
        {
            files = InferNamespaces(identities);
        }
        else
        {
            files = stored.Select(value => new CatalogNamespaceFile(
                value.Id, value.Owner, value.Description,
                JsonSerializer.Deserialize<string[]>(value.AllowedKindsJson) ?? [],
                JsonSerializer.Deserialize<string[]>(value.AliasesJson) ?? [],
                value.DisabledAtUtc is null,
                value.ReviewStatus,
                value.ReviewNote)).ToArray();
            ValidateRegisteredNamespaces(files, identities);
        }

        foreach (var file in files)
            await WriteAsync(root, CatalogLayout.Namespace(file.Id), file.ToJson(), written, cancellationToken);
    }

    private async Task<IReadOnlyList<(string Id, string Kind)>> StoredIdentitiesAsync(CancellationToken cancellationToken)
    {
        var values = new List<(string, string)>();
        values.AddRange((await _db.Mechanics.AsNoTracking().Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.Mechanic)));
        values.AddRange((await _db.ProcedureContracts.AsNoTracking().Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.Procedure)));
        values.AddRange((await _db.ComponentDefinitions.AsNoTracking().Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.ComponentDefinition)));
        values.AddRange((await _db.Set<ComponentTypeRecord>().AsNoTracking().Select(value => value.QualifiedId)
                .ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.ComponentType)));
        values.AddRange((await _db.EventTypes.AsNoTracking().Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.EventType)));
        values.AddRange((await _db.Subscriptions.AsNoTracking().Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.Subscription)));
        values.AddRange((await _db.Entities.AsNoTracking().Where(value => value.DeletedAt == null)
                .Select(value => value.Id).ToArrayAsync(cancellationToken))
            .Select(value => (value, CatalogNamespaceKinds.Entity)));
        foreach (var value in values) CatalogNamespaceIdentity.ValidateRecordId(value.Item1);
        return values;
    }

    private static IReadOnlyList<CatalogNamespaceFile> InferNamespaces(
        IReadOnlyList<(string Id, string Kind)> identities)
    {
        var direct = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var leaf = CatalogNamespaceIdentity.NamespaceOf(identity.Id);
            if (!direct.TryGetValue(leaf, out var kinds)) direct[leaf] = kinds = new(StringComparer.Ordinal);
            kinds.Add(identity.Kind);
            foreach (var ancestor in CatalogNamespaceIdentity.NamespaceChain(identity.Id))
                direct.TryAdd(ancestor, new(StringComparer.Ordinal));
        }
        foreach (var namespaceId in direct.Keys.OrderByDescending(value => value.Length).ToArray())
        {
            var parent = namespaceId == CatalogNamespaceIdentity.RootNamespaceId ? null
                : namespaceId.Contains('.') ? namespaceId[..namespaceId.LastIndexOf('.')] : null;
            if (parent is not null && direct.TryGetValue(parent, out var parentKinds))
                parentKinds.UnionWith(direct[namespaceId]);
        }
        return direct.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => new CatalogNamespaceFile(
            value.Key,
            value.Key == CatalogNamespaceIdentity.RootNamespaceId ? "legacy" : value.Key.Split('.')[0],
            $"Namespace for {value.Key.Replace('.', ' ')} catalog records.",
            value.Value.Count == 0 ? [CatalogNamespaceKinds.Document] : value.Value.Order(StringComparer.Ordinal).ToArray(),
            [], true,
            CatalogNamespaceReviewStatuses.NeedsReview,
            "Inferred during export; an owner must review this namespace before normal writes can use it.")).ToArray();
    }

    private static void ValidateRegisteredNamespaces(
        IReadOnlyList<CatalogNamespaceFile> namespaces,
        IReadOnlyList<(string Id, string Kind)> identities)
    {
        var byId = namespaces.ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(identity.Id);
            if (!byId.TryGetValue(namespaceId, out var definition))
                throw new InvalidOperationException($"Cannot export '{identity.Id}': namespace '{namespaceId}' is not registered.");
            if (!definition.AllowedKinds.Contains(identity.Kind, StringComparer.Ordinal))
                throw new InvalidOperationException($"Cannot export '{identity.Id}': namespace '{namespaceId}' does not allow '{identity.Kind}'.");
        }
    }

    /// <summary>
    /// Entities with their components and their container, plus the relationship set.
    ///
    /// Soft-deleted entities are skipped. A catalog is what the world IS, and re-importing a
    /// tombstone would resurrect a row somebody deleted on purpose.
    /// </summary>
    private async Task<int> ExportWorldAsync(
        string root,
        List<string> written,
        List<CatalogManifestEntry> entries,
        CancellationToken cancellationToken)
    {
        var entities = await _db.Entities
            .AsNoTracking()
            .Where(e => e.DeletedAt == null)
            .Select(e => new { e.Id, e.Name })
            .ToListAsync(cancellationToken);

        var components = (await _db.Components
                .AsNoTracking()
                .Select(c => new { c.EntityId, c.DefinitionId, c.Data })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.EntityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var containers = await _db.Containments
            .AsNoTracking()
            .Select(c => new { c.ContainedId, c.ContainerId, c.Slot })
            .ToListAsync(cancellationToken);

        var containerOf = containers.ToDictionary(c => c.ContainedId, c => c, StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            containerOf.TryGetValue(entity.Id, out var container);

            var file = new EntityFile(
                entity.Id,
                entity.Name,
                container?.ContainerId,
                container?.Slot ?? string.Empty,
                components.TryGetValue(entity.Id, out var attached)
                    ? attached
                        .Select(c => new EntityComponent(
                            c.DefinitionId,
                            EntityFile.CanonicalData(c.Data, entity.Id, c.DefinitionId)))
                        .ToList()
                    : []);

            var path = CatalogLayout.Entity(file.Id);
            await WriteAsync(root, path, file.ToJson(), written, cancellationToken);

            entries.Add(new CatalogManifestEntry(
                CatalogRecordKind.Entity,
                file.Id,
                0,
                file.ContentHash,
                path));
        }

        var relationships = new RelationshipsFile(
            (await _db.Relationships
                .AsNoTracking()
                .Select(r => new { r.FromEntityId, r.ToEntityId, r.Kind, r.Data })
                .ToListAsync(cancellationToken))
            .Select(r => new RelationshipEntry(r.FromEntityId, r.ToEntityId, r.Kind, r.Data ?? "{}"))
            .ToList());

        // Written even when empty, so "there are no relationships" is a fact the catalog states
        // rather than a file somebody forgot to export.
        await WriteAsync(
            root,
            CatalogLayout.RelationshipsFileName,
            relationships.ToJson(),
            written,
            cancellationToken);

        entries.Add(new CatalogManifestEntry(
            CatalogRecordKind.Relationships,
            CatalogLayout.RelationshipsFileName,
            0,
            relationships.ContentHash,
            CatalogLayout.RelationshipsFileName));

        return entities.Count;
    }

    /// <summary>
    /// The operation log, one JSON object per line.
    ///
    /// Export only, and there is no import counterpart anywhere in this feature. An operation id
    /// and a seed are provenance — the claim that a particular rule ran, at a particular version,
    /// and produced a particular roll. A log that can be written from a file is not evidence of
    /// anything, so the ability to write one back is not a feature that was left out; it is one
    /// that must not exist.
    ///
    /// JSONL rather than a JSON array so it streams, and so a diff of two exports shows the
    /// operations that were added rather than a re-indented array.
    /// </summary>
    private async Task<int> ExportHistoryAsync(
        string root,
        List<string> written,
        CancellationToken cancellationToken)
    {
        // Ordered in memory, not in SQL. A custom comparer cannot be translated to a query, and
        // ordinal ordering is what makes two exports of one database byte-identical — leaving it to
        // the database's collation would make that depend on the provider.
        var operations = (await _db.Operations
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .OrderBy(o => o.Timestamp)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

        var builder = new System.Text.StringBuilder();

        foreach (var operation in operations)
        {
            builder.Append(JsonSerializer.Serialize(operation, HistoryJson)).Append('\n');
        }

        await WriteAsync(root, CatalogLayout.OperationsFileName, builder.ToString(), written, cancellationToken);

        return operations.Count;
    }

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private async Task<int> ExportMechanicsAsync(
        string root,
        List<string> written,
        List<CatalogManifestEntry> entries,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Mechanics
            .AsNoTracking()
            .Join(
                _db.MechanicVersions.AsNoTracking(),
                mechanic => new { MechanicId = mechanic.Id, Version = mechanic.CurrentVersion },
                version => new { version.MechanicId, version.Version },
                (mechanic, version) => new { mechanic, version })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var file = new MechanicFile(
                row.mechanic.Id,
                row.mechanic.Category,
                row.version.Name,
                row.version.Description,
                row.version.Matches,
                row.version.Requirements,
                row.version.Source,
                row.mechanic.Scope,
                row.mechanic.Status,
                row.version.CreatedBy,
                row.version.ChangeNote);

            GuardFingerprint(row.version.SourceHash, file.ContentHash, "mechanic", file.Id, row.version.Version);

            var markdownPath = CatalogLayout.MechanicMarkdown(file.Category, file.Id);
            var sourcePath = CatalogLayout.MechanicSource(file.Category, file.Id);

            await WriteAsync(root, markdownPath, file.ToMarkdown(), written, cancellationToken);
            await WriteAsync(root, sourcePath, ContentHash.Normalise(file.Source) + "\n", written, cancellationToken);

            entries.Add(new CatalogManifestEntry(
                CatalogRecordKind.Mechanic,
                file.Id,
                row.version.Version,
                file.ContentHash,
                markdownPath));
        }

        return rows.Count;
    }

    private async Task<int> ExportProceduresAsync(
        string root,
        List<string> written,
        List<CatalogManifestEntry> entries,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProcedureContracts
            .AsNoTracking()
            .Join(
                _db.ProcedureContractVersions.AsNoTracking(),
                contract => new { ContractId = contract.Id, Version = contract.CurrentVersion },
                version => new { version.ContractId, version.Version },
                (contract, version) => new { contract, version })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var file = new ProcedureFile(
                row.contract.Id,
                row.contract.Category,
                row.version.Name,
                row.version.Description,
                row.version.Governs,
                row.version.Instructions,
                row.version.Constraints,
                row.contract.Status,
                row.version.CreatedBy,
                row.version.ChangeNote,
                row.version.Matches);

            GuardFingerprint(row.version.SourceHash, file.ContentHash, "procedure", file.Id, row.version.Version);

            var markdownPath = CatalogLayout.ProcedureMarkdown(file.Category, file.Id);
            await WriteAsync(root, markdownPath, file.ToMarkdown(), written, cancellationToken);

            entries.Add(new CatalogManifestEntry(
                CatalogRecordKind.Procedure,
                file.Id,
                row.version.Version,
                file.ContentHash,
                markdownPath));
        }

        return rows.Count;
    }

    private async Task<int> ExportComponentsAsync(
        string root,
        List<string> written,
        List<CatalogManifestEntry> entries,
        CancellationToken cancellationToken)
    {
        var definitions = await _db.ComponentDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            var file = ComponentDefinitionFile.FromDefinition(definition);
            var definitionPath = CatalogLayout.Component(file.Id);

            await WriteAsync(root, definitionPath, file.ToJson(), written, cancellationToken);

            // Only when there is one. An empty sidecar and a missing sidecar mean the same thing,
            // and writing both forms would make two catalogs of the same database differ.
            if (file.Schema.Length > 0)
            {
                await WriteAsync(
                    root,
                    CatalogLayout.ComponentSchema(file.Id),
                    ContentHash.Normalise(file.Schema) + "\n",
                    written,
                    cancellationToken);
            }

            entries.Add(new CatalogManifestEntry(
                CatalogRecordKind.ComponentDefinition,
                file.Id,
                0,
                file.ContentHash,
                definitionPath));
        }

        return definitions.Count;
    }

    private async Task<int> ExportEventTypesAsync(string root, List<string> written, List<CatalogManifestEntry> entries, CancellationToken cancellationToken)
    {
        var rows = await _db.EventTypes.AsNoTracking().Join(_db.EventTypeVersions.AsNoTracking(), e => new { EventTypeId = e.Id, Version = e.CurrentVersion }, v => new { v.EventTypeId, v.Version }, (e, v) => new { e, v }).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var file = new EventTypeFile(row.e.Id, row.e.Category, row.v.Name, row.v.Description, row.e.Scope, row.e.Status, row.v.PayloadSchema, row.v.CreatedBy, row.v.ChangeNote);
            GuardFingerprint(row.v.SourceHash, file.ContentHash, "event type", file.Id, row.v.Version);
            var path = CatalogLayout.EventType(file.Id);
            await WriteAsync(root, path, file.ToJson(), written, cancellationToken);
            await WriteAsync(root, CatalogLayout.EventTypeSchema(file.Id), ContentHash.Normalise(file.Schema) + "\n", written, cancellationToken);
            entries.Add(new CatalogManifestEntry(CatalogRecordKind.EventType, file.Id, row.v.Version, file.ContentHash, path));
        }
        return rows.Count;
    }

    private async Task<int> ExportSubscriptionsAsync(string root, List<string> written, List<CatalogManifestEntry> entries, CancellationToken cancellationToken)
    {
        var rows = await _db.Subscriptions.AsNoTracking().Join(_db.SubscriptionVersions.AsNoTracking(), s => new { SubscriptionId = s.Id, Version = s.CurrentVersion }, v => new { v.SubscriptionId, v.Version }, (s, v) => new { s, v }).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var file = new SubscriptionFile(row.s.Id, row.s.Category, row.v.EventTypeId, row.v.EventMechanicId, row.v.Mode, row.v.Order, row.v.FixedRoleEntityIdsJson, row.v.TrackedEntityIdsJson, row.v.PayloadEqualsJson, row.v.MaxExecutionsPerChain, row.s.Scope, row.s.Status, row.v.RoleFromEventPayloadJson, row.v.FanoutSelectorJson, row.v.CreatedBy, row.v.ChangeNote);
            GuardFingerprint(row.v.SourceHash, file.ContentHash, "subscription", file.Id, row.v.Version);
            var path = CatalogLayout.Subscription(file.Id); await WriteAsync(root, path, file.ToJson(), written, cancellationToken);
            entries.Add(new CatalogManifestEntry(CatalogRecordKind.Subscription, file.Id, row.v.Version, file.ContentHash, path));
        }
        return rows.Count;
    }

    /// <summary>
    /// Refuses to export a row whose stored fingerprint disagrees with its content.
    ///
    /// The manifest is the common ancestor import compares against, so a catalog built on
    /// fingerprints the database does not itself agree with would have import confidently
    /// misjudging which side of a divergence is newer. Better to stop with the fix in the message.
    /// </summary>
    private static void GuardFingerprint(string stored, string computed, string kind, string id, int version)
    {
        if (string.Equals(stored, computed, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The stored fingerprint of {kind} '{id}' v{version} does not match its content"
            + (stored.Length == 0 ? " (it has none)" : string.Empty)
            + ". Run `roleplay backfill-hashes`, or start the server, then export again.");
    }

    private static async Task WriteAsync(
        string root,
        string relativePath,
        string content,
        List<string> written,
        CancellationToken cancellationToken)
    {
        var path = CatalogLayout.ToFileSystemPath(root, relativePath);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
        written.Add(relativePath);
    }

    /// <summary>
    /// Files under the catalog roots that this export did not write — usually a record that has
    /// since been renamed or removed from the database.
    ///
    /// Reported, never deleted. Import does not delete either, and a tool that quietly removed a
    /// file a developer had just written would be the one behaviour that makes people stop
    /// trusting it. Knowing is the useful part; acting on it is a decision with a name on it.
    /// </summary>
    private static IReadOnlyList<string> FindOrphans(string root, IReadOnlyList<string> written)
    {
        var expected = written.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = new List<string>();

        foreach (var area in new[]
        {
            CatalogLayout.MechanicsRoot,
            CatalogLayout.ProceduresRoot,
            CatalogLayout.ComponentsRoot,
            CatalogLayout.EventTypesRoot,
            CatalogLayout.SubscriptionsRoot,
            CatalogLayout.NamespacesRoot,
            CatalogLayout.WorldRoot
        })
        {
            var directory = Path.Combine(root, area);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

                if (!expected.Contains(relative))
                {
                    orphans.Add(relative);
                }
            }
        }

        return orphans.OrderBy(o => o, StringComparer.Ordinal).ToList();
    }
}

/// <param name="RulesOnly">Exclude world state — entities, components, containment, relationships.</param>
/// <param name="WithHistory">Also write the operation log. Export only; nothing imports it.</param>
public sealed record CatalogExportOptions(bool RulesOnly = false, bool WithHistory = false);

/// <param name="Orphans">Files already under the catalog roots that this export did not write.</param>
public sealed record CatalogExportResult(
    string Root,
    int Mechanics,
    int Procedures,
    int ComponentDefinitions,
    int EventTypes,
    int Subscriptions,
    int Entities,
    int Operations,
    IReadOnlyList<string> Orphans)
{
    public int Records => Mechanics + Procedures + ComponentDefinitions + EventTypes + Subscriptions + Entities;
}
