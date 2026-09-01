using DantesRoleplay.Content;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using DantesRoleplay.CatalogNamespaces;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Brings a catalog folder back into the database, without either side quietly destroying the
/// other's work.
///
/// Two populations author this system and neither can use the other's tools: a developer with the
/// solution source edits files; an agent connected only over MCP writes into the database. Both are
/// legitimate, so import cannot simply overwrite. It compares three fingerprints per record — the
/// file's, the row's, and the manifest's record of the last state at which they agreed — and only
/// writes when the file is the only side that moved.
///
/// Where it cannot tell, it stops. A conflict aborts the whole import before anything is written:
/// a half-applied catalog is harder to reason about than an unapplied one, and whoever resolves it
/// needs the database in the state their mental model describes.
/// </summary>
/// <remarks>
/// The stores passed in must be backed by the SAME <see cref="DantesRoleplayDbContext"/> as
/// <paramref name="db"/>. They call SaveChanges themselves, so a store on a different context
/// would write outside the transaction below and a failed import could leave half a catalog
/// applied. Scoped registration gives this for free; a test constructing them by hand has to.
/// </remarks>
public sealed class CatalogImporter(
    DantesRoleplayDbContext db,
    IMechanicStore mechanics,
    IProcedureStore procedures,
    IWorldStore world,
    IEventTypeStore? eventTypes = null,
    ISubscriptionStore? subscriptions = null)
{
    private const string ImportAuthor = "import";
    private const string ImportChangeNote = "Imported from the catalog.";

    private readonly DantesRoleplayDbContext _db = db;
    private readonly IMechanicStore _mechanics = mechanics;
    private readonly IProcedureStore _procedures = procedures;
    private readonly IWorldStore _world = world;
    private readonly IEventTypeStore? _eventTypes = eventTypes;
    private readonly ISubscriptionStore? _subscriptions = subscriptions;

    // ---- planning ------------------------------------------------------------------------

    public async Task<CatalogNamespaceImportResult> ApplyNamespacesOnlyAsync(
        string root,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var contents = await CatalogReader.ReadAsync(root, cancellationToken);
        var stored = await _db.Set<CatalogNamespaceRecord>().AsNoTracking()
            .ToDictionaryAsync(value => value.Id, StringComparer.Ordinal, cancellationToken);
        var created = contents.Namespaces.Count(value => !stored.ContainsKey(value.Id));
        var updated = contents.Namespaces.Count(value => stored.TryGetValue(value.Id, out var row)
            && !NamespaceMatches(row, value));
        var unchanged = contents.Namespaces.Count - created - updated;
        if (dryRun) return new(contents.Namespaces.Count, created, updated, unchanged, Applied: false);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await ApplyNamespacesAsync(contents.Namespaces, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(contents.Namespaces.Count, created, updated, unchanged, Applied: true);
    }

    /// <summary>What an import would do. Reads only.</summary>
    public async Task<CatalogImportPlan> PlanAsync(
        string root,
        CancellationToken cancellationToken = default) =>
        await PlanAsync(root, await CatalogReader.ReadAsync(root, cancellationToken), cancellationToken);

    private async Task<CatalogImportPlan> PlanAsync(
        string root,
        CatalogContents contents,
        CancellationToken cancellationToken)
    {
        var entries = new List<CatalogImportPlanEntry>();

        entries.AddRange(Classify(
            CatalogRecordKind.Mechanic,
            Fingerprints(contents.Mechanics, f => f.Id, f => f.ContentHash),
            await StoredMechanicsAsync(cancellationToken),
            contents.Manifest));

        entries.AddRange(Classify(
            CatalogRecordKind.Procedure,
            Fingerprints(contents.Procedures, f => f.Id, f => f.ContentHash),
            await StoredProceduresAsync(cancellationToken),
            contents.Manifest));

        entries.AddRange(Classify(
            CatalogRecordKind.ComponentDefinition,
            Fingerprints(contents.Components, f => f.Id, f => f.ContentHash),
            await StoredComponentsAsync(cancellationToken),
            contents.Manifest));
        if (_eventTypes is not null)
        {
            entries.AddRange(Classify(CatalogRecordKind.EventType, Fingerprints(contents.EventTypes, f => f.Id, f => f.ContentHash), await StoredEventTypesAsync(cancellationToken), contents.Manifest));
        }
        if (_subscriptions is not null)
        {
            entries.AddRange(Classify(CatalogRecordKind.Subscription, Fingerprints(contents.Subscriptions, f => f.Id, f => f.ContentHash), await StoredSubscriptionsAsync(cancellationToken), contents.Manifest));
        }

        // World state only when the catalog is in scope for it. A --rules-only catalog otherwise
        // reports every entity in the database as "authored live and never exported" on every run
        // — true, useless, and the kind of noise that trains people to skip the output.
        if (contents.HasWorld)
        {
            entries.AddRange(Classify(
                CatalogRecordKind.Entity,
                Fingerprints(contents.Entities, f => f.Id, f => f.ContentHash),
                await StoredEntitiesAsync(cancellationToken),
                contents.Manifest));

            var storedRelationships = await StoredRelationshipsAsync(cancellationToken);
            var fileRelationships = new Dictionary<string, string>(StringComparer.Ordinal);

            if (contents.Relationships is not null)
            {
                fileRelationships[CatalogLayout.RelationshipsFileName] = contents.Relationships.ContentHash;
            }

            // Only when at least one side actually has an edge.
            //
            // The relationship set is one record, so unlike every other kind it cannot be "absent"
            // by having no rows — and an empty set that always looked present made importing into a
            // fresh database read as "the database dropped every edge" rather than "these are new",
            // which left the edges unwritten. Skipping when both sides are empty also stops a world
            // with no relationships from reporting a no-op write on every import.
            if (storedRelationships.Count > 0 || contents.Relationships?.Relationships.Count > 0)
            {
                entries.AddRange(Classify(
                    CatalogRecordKind.Relationships,
                    fileRelationships,
                    storedRelationships,
                    contents.Manifest));
            }
        }

        return new CatalogImportPlan(
            Path.GetFullPath(root),
            contents.Manifest is not null,
            entries.OrderBy(e => e.Kind).ThenBy(e => e.Id, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The drift table. Everything this feature is for is decided here.
    /// </summary>
    private static IEnumerable<CatalogImportPlanEntry> Classify(
        CatalogRecordKind kind,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> stored,
        CatalogManifest? manifest)
    {
        var ids = files.Keys
            .Concat(stored.Keys)
            .Concat(manifest is null
                ? []
                : manifest.Records.Where(r => r.Kind == kind).Select(r => r.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

        foreach (var id in ids)
        {
            var inFile = files.TryGetValue(id, out var file);
            var inDatabase = stored.TryGetValue(id, out var database);
            var ancestor = manifest?.FingerprintOf(kind, id);

            var (change, detail) = Decide(inFile, file, inDatabase, database, ancestor);

            yield return new CatalogImportPlanEntry(kind, id, change, detail);
        }
    }

    private static (CatalogChange Change, string Detail) Decide(
        bool inFile,
        string? file,
        bool inDatabase,
        string? database,
        string? ancestor)
    {
        if (!inFile && !inDatabase)
        {
            return (CatalogChange.GoneFromBoth,
                "In the manifest but in neither the catalog nor the database. Re-export to forget it.");
        }

        if (inFile && !inDatabase)
        {
            return (CatalogChange.NewInFiles, "In the catalog, not in the database. Will be created.");
        }

        if (!inFile && inDatabase)
        {
            return ancestor is null
                ? (CatalogChange.NewInDatabase,
                    "Authored live and never exported. Left alone — run export to capture it.")
                : (CatalogChange.MissingFromFiles,
                    "Was exported, now absent from the catalog. Left alone; import never deletes.");
        }

        // Identical on both sides is unchanged whatever the manifest says — including the case
        // where the two were edited separately into the same content, which is agreement, not a
        // conflict.
        if (string.Equals(file, database, StringComparison.Ordinal))
        {
            return (CatalogChange.Unchanged, "Catalog and database agree.");
        }

        if (ancestor is null)
        {
            return (CatalogChange.Conflict,
                "Differs from the database and there is no manifest entry saying which side moved. "
                + "Re-export, or force a side.");
        }

        var fileMoved = !string.Equals(file, ancestor, StringComparison.Ordinal);
        var databaseMoved = !string.Equals(database, ancestor, StringComparison.Ordinal);

        return (fileMoved, databaseMoved) switch
        {
            (true, false) => (CatalogChange.FileEdited, "Edited in the catalog. Will be written as a new version."),
            (false, true) => (CatalogChange.DatabaseEdited,
                "Authored live since the last export. Left alone — run export to capture it."),
            _ => (CatalogChange.Conflict,
                "Edited in the catalog AND authored live since the last export. Resolve, or force a side.")
        };
    }

    // ---- applying ------------------------------------------------------------------------

    public async Task<CatalogImportResult> ApplyAsync(
        string root,
        CatalogImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var contents = await CatalogReader.ReadAsync(root, cancellationToken);
        var plan = await PlanAsync(root, contents, cancellationToken);

        // Before anything is written, and before the transaction — an import that cannot finish
        // should not have started.
        if (options.Force == CatalogForce.None && plan.Conflicts.Any())
        {
            return new CatalogImportResult(plan, 0, 0, 0, Aborted: true, ManifestUpdated: false);
        }

        // Component definitions first. A mechanic's requirements name them, and although the
        // store does not enforce that on write, applying a ruleset in an order that only works
        // because nothing checks is the kind of thing that becomes load-bearing by accident.
        var applying = plan.Entries
            .Where(e => ShouldWrite(e.Change, options.Force))
            .OrderBy(e => ApplyOrder(e.Kind))
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        if (options.DryRun)
        {
            return new CatalogImportResult(
                plan,
                applying.Count(e => e.Change == CatalogChange.NewInFiles),
                applying.Count(e => e.Change != CatalogChange.NewInFiles),
                plan.Entries.Count - applying.Count,
                Aborted: false,
                ManifestUpdated: false);
        }

        var created = 0;
        var updated = 0;
        var namespaceChanges = await NamespacesNeedWriteAsync(contents.Namespaces, cancellationToken);

        if (applying.Count == 0 && !namespaceChanges)
        {
            var unchangedVersions = await CurrentVersionsAsync(cancellationToken);

            return new CatalogImportResult(
                plan,
                0,
                0,
                plan.Entries.Count,
                Aborted: false,
                await UpdateManifestAsync(root, contents, plan, options, unchangedVersions, cancellationToken));
        }

        // Import is the one migration boundary allowed to preserve already-authored records in a
        // namespace that still needs review. Ordinary stores remain blocked, and validation emits
        // one finding for every such record so this exception cannot make the debt invisible.
        using var namespaceImport = _db.PermitUnreviewedNamespaceImport();

        // One transaction for the whole apply. Import is a synchronisation, and a partly
        // synchronised catalog is a state neither side's author can reason about.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await ApplyNamespacesAsync(contents.Namespaces, cancellationToken);

        // A containment points from an entity to another entity. File names are stable ids, not
        // a topological sort of that graph ("lantern" may belong to "orban"), so materialise
        // every entity identity before applying its components and containment. Otherwise a
        // perfectly valid fresh import depends on a parent happening to sort before its child.
        foreach (var entry in applying.Where(e => e.Kind == CatalogRecordKind.ComponentDefinition))
        {
            var wasCreated = await WriteComponentAsync(
                contents.Components.Single(f => f.Id == entry.Id), cancellationToken);

            if (wasCreated)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        var entityFiles = applying
            .Where(e => e.Kind == CatalogRecordKind.Entity)
            .Select(e => contents.Entities.Single(f => f.Id == e.Id))
            .ToList();

        var entitiesCreated = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var file in entityFiles)
        {
            entitiesCreated[file.Id] = await CreateOrRestoreEntityAsync(file, cancellationToken);
        }

        foreach (var file in entityFiles)
        {
            await WriteEntityStateAsync(file, cancellationToken);

            if (entitiesCreated[file.Id])
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        foreach (var entry in applying.Where(e =>
                     e.Kind != CatalogRecordKind.ComponentDefinition &&
                     e.Kind != CatalogRecordKind.Entity))
        {
            var wasCreated = entry.Kind switch
            {
                CatalogRecordKind.Mechanic => await WriteMechanicAsync(
                    contents.Mechanics.Single(f => f.Id == entry.Id), cancellationToken),
                CatalogRecordKind.Procedure => await WriteProcedureAsync(
                    contents.Procedures.Single(f => f.Id == entry.Id), cancellationToken),
                CatalogRecordKind.EventType => await WriteEventTypeAsync(contents.EventTypes.Single(f => f.Id == entry.Id), cancellationToken),
                CatalogRecordKind.Subscription => await WriteSubscriptionAsync(contents.Subscriptions.Single(f => f.Id == entry.Id), cancellationToken),
                CatalogRecordKind.Relationships => await WriteRelationshipsAsync(
                    contents.Relationships!, cancellationToken),
                _ => throw new InvalidOperationException($"Unhandled record kind '{entry.Kind}'.")
            };

            if (wasCreated)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        var versions = await CurrentVersionsAsync(cancellationToken);
        var manifestUpdated = await UpdateManifestAsync(root, contents, plan, options, versions, cancellationToken);

        return new CatalogImportResult(
            plan,
            created,
            updated,
            plan.Entries.Count - applying.Count,
            Aborted: false,
            manifestUpdated);
    }

    /// <summary>
    /// Definitions before the entities that carry them, entities before the edges between them.
    ///
    /// Attaching a component whose definition does not exist fails on purpose — it is almost always
    /// a typo — and relating two entities needs both to be there. Rules sit in the middle: their
    /// requirements name component definitions, and although the store does not enforce that on
    /// write, an order that works only because nothing checks becomes load-bearing by accident.
    /// </summary>
    private static int ApplyOrder(CatalogRecordKind kind) => kind switch
    {
        CatalogRecordKind.ComponentDefinition => 0,
        CatalogRecordKind.Entity => 1,
        CatalogRecordKind.EventType => 2,
        CatalogRecordKind.Mechanic => 3,
        CatalogRecordKind.Procedure => 4,
        CatalogRecordKind.Subscription => 5,
        CatalogRecordKind.Relationships => 5,
        _ => 6
    };

    private static bool ShouldWrite(CatalogChange change, CatalogForce force) => change switch
    {
        CatalogChange.NewInFiles => true,
        CatalogChange.FileEdited => true,

        // Forcing the files means the catalog is the answer, including where the database moved
        // on its own. Forcing the database means neither is written and the files stay as they are
        // for an export to reconcile.
        CatalogChange.Conflict => force == CatalogForce.Files,
        CatalogChange.DatabaseEdited => force == CatalogForce.Files,

        _ => false
    };

    private async Task<bool> WriteMechanicAsync(MechanicFile file, CancellationToken cancellationToken)
    {
        var result = await _mechanics.WriteAsync(
            new WriteMechanicRequest
            {
                Id = file.Id,
                Category = file.Category,
                Name = file.Name,
                Description = file.Description,
                Matches = file.Matches,
                Requirements = file.Requirements,
                Source = file.Source,
                Scope = file.Scope,
                Status = file.Status,
                CreatedBy = AuthorFor(file.CreatedBy),
                ChangeNote = ChangeNoteFor(file.ChangeNote)
            },
            cancellationToken);

        return result.Created;
    }

    private async Task<bool> WriteProcedureAsync(ProcedureFile file, CancellationToken cancellationToken)
    {
        var result = await _procedures.WriteAsync(
            new WriteProcedureRequest
            {
                Id = file.Id,
                Category = file.Category,
                Name = file.Name,
                Description = file.Description,
                Governs = file.Governs,
                Instructions = file.Instructions,
                Constraints = file.Constraints,
                Status = file.Status,
                CreatedBy = AuthorFor(file.CreatedBy),
                ChangeNote = ChangeNoteFor(file.ChangeNote)
            },
            cancellationToken);

        return result.Created;
    }

    private async Task<bool> WriteEventTypeAsync(EventTypeFile file, CancellationToken cancellationToken)
    {
        if (_eventTypes is null) throw new InvalidOperationException("Event type import requires an event type store.");
        var result = await _eventTypes.WriteAsync(new WriteEventTypeRequest { Id = file.Id, Category = file.Category, Name = file.Name, Description = file.Description, PayloadSchema = file.Schema, Scope = file.Scope, Status = file.Status, CreatedBy = AuthorFor(file.CreatedBy), ChangeNote = ChangeNoteFor(file.ChangeNote) }, cancellationToken);
        return result.Created;
    }

    private async Task<bool> WriteSubscriptionAsync(SubscriptionFile file, CancellationToken cancellationToken)
    {
        if (_subscriptions is null) throw new InvalidOperationException("Subscription import requires a subscription store.");
        var result = await _subscriptions.WriteAsync(new WriteSubscriptionRequest { Id = file.Id, Category = file.Category, EventTypeId = file.EventTypeId, EventMechanicId = file.EventMechanicId, Mode = file.Mode, Order = file.Order, FixedRoleEntityIdsJson = file.FixedRoleEntityIdsJson, RoleFromEventPayloadJson = file.RoleFromEventPayloadJson, FanoutSelectorJson = file.FanoutSelectorJson, TrackedEntityIdsJson = file.TrackedEntityIdsJson, PayloadEqualsJson = file.PayloadEqualsJson, MaxExecutionsPerChain = file.MaxExecutionsPerChain, Scope = file.Scope, Status = file.Status, CreatedBy = AuthorFor(file.CreatedBy), ChangeNote = ChangeNoteFor(file.ChangeNote) }, cancellationToken);
        return result.Created;
    }

    private async Task<bool> NamespacesNeedWriteAsync(
        IReadOnlyList<CatalogNamespaceFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0) return false;
        var stored = await _db.Set<CatalogNamespaceRecord>().AsNoTracking().ToDictionaryAsync(value => value.Id,
            StringComparer.Ordinal, cancellationToken);
        return files.Any(file => !stored.TryGetValue(file.Id, out var row) || !NamespaceMatches(row, file));
    }

    private static bool NamespaceMatches(CatalogNamespaceRecord row, CatalogNamespaceFile file) =>
        row.Owner == file.Owner && row.Description == file.Description
        && row.AllowedKindsJson == System.Text.Json.JsonSerializer.Serialize(file.AllowedKinds)
        && row.AliasesJson == System.Text.Json.JsonSerializer.Serialize(file.Aliases)
        && row.ReviewStatus == file.ReviewStatus
        && row.ReviewNote == file.ReviewNote
        && (row.DisabledAtUtc is null) == file.Enabled;

    private async Task ApplyNamespacesAsync(
        IReadOnlyList<CatalogNamespaceFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var file in files.OrderBy(value => value.Id.Count(character => character == '.'))
                     .ThenBy(value => value.Id, StringComparer.Ordinal))
        {
            var row = await _db.Set<CatalogNamespaceRecord>().SingleOrDefaultAsync(
                value => value.Id == file.Id, cancellationToken);
            var parent = file.Id == CatalogNamespaceIdentity.RootNamespaceId ? null
                : file.Id.Contains('.') ? file.Id[..file.Id.LastIndexOf('.')] : null;
            if (row is null)
            {
                var now = DateTime.UtcNow;
                row = new CatalogNamespaceRecord
                {
                    Id = file.Id,
                    ParentId = parent,
                    Owner = file.Owner,
                    Description = file.Description,
                    AllowedKindsJson = System.Text.Json.JsonSerializer.Serialize(file.AllowedKinds),
                    AliasesJson = System.Text.Json.JsonSerializer.Serialize(file.Aliases),
                    ReviewStatus = file.ReviewStatus,
                    ReviewNote = file.ReviewNote,
                    ReviewedAtUtc = file.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed ? now : null,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    DisabledAtUtc = file.Enabled ? null : now
                };
                _db.Add(row);
            }
            else
            {
                row.Owner = file.Owner;
                row.Description = file.Description;
                row.AllowedKindsJson = System.Text.Json.JsonSerializer.Serialize(file.AllowedKinds);
                row.AliasesJson = System.Text.Json.JsonSerializer.Serialize(file.Aliases);
                row.ReviewStatus = file.ReviewStatus;
                row.ReviewNote = file.ReviewNote;
                row.ReviewedAtUtc = file.ReviewStatus == CatalogNamespaceReviewStatuses.Reviewed
                    ? row.ReviewedAtUtc ?? DateTime.UtcNow
                    : null;
                row.DisabledAtUtc = file.Enabled ? null : row.DisabledAtUtc ?? DateTime.UtcNow;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string AuthorFor(string createdBy) =>
        string.IsNullOrWhiteSpace(createdBy) ? ImportAuthor : createdBy;

    private static string ChangeNoteFor(string changeNote) =>
        string.IsNullOrWhiteSpace(changeNote) ? ImportChangeNote : changeNote;

    /// <summary>
    /// Component definitions are the one write in this system that is not append-only: there is no
    /// version chain and no previous copy to go back to. Import can create or replace one and
    /// nothing else, which is why nothing here removes one.
    /// </summary>
    private async Task<bool> WriteComponentAsync(ComponentDefinitionFile file, CancellationToken cancellationToken)
    {
        var existed = await _db.ComponentDefinitions
            .AsNoTracking()
            .AnyAsync(d => d.Id == file.Id, cancellationToken);

        await _world.DefineComponentAsync(
            file.Id,
            file.Name,
            file.Description,
            file.Schema,
            cancellationToken);

        return !existed;
    }

    /// <summary>
    /// Creates or restores one entity before the second import pass writes its components and
    /// containment. Keeping identities separate makes a catalog's containment graph independent
    /// of lexicographic file order.
    ///
    /// The name is written straight to the row rather than through <see cref="IWorldStore"/>,
    /// which is the ONE place this importer reaches past that interface. There is no rename in the
    /// store because the store is the effect vocabulary a MECHANIC gets, and a rule renaming a
    /// creature mid-play is not something that vocabulary should allow. A developer editing a
    /// catalog file is a different actor, and silently discarding their rename would be worse than
    /// this exception to the rule.
    ///
    /// Components present in the database but absent from the file are NOT removed. Import never
    /// deletes; something else may depend on them, and a component vanishing as a side effect of a
    /// sync is exactly the surprise this whole feature is built to avoid.
    /// </summary>
    private async Task<bool> CreateOrRestoreEntityAsync(EntityFile file, CancellationToken cancellationToken)
    {
        var existing = await _db.Entities.FirstOrDefaultAsync(e => e.Id == file.Id, cancellationToken);
        var created = existing is null;

        if (existing is null)
        {
            await _world.CreateEntityAsync(file.Name, file.Id, cancellationToken);
        }
        else
        {
            if (!string.Equals(existing.Name, file.Name, StringComparison.Ordinal))
            {
                existing.Name = file.Name;
            }

            // An entity deleted in the database but still in the catalog comes back. That is the
            // same rule as everywhere else here: the file is an instruction, absence is not.
            existing.DeletedAt = null;

            await _db.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    /// <summary>Writes an entity's declared component state and containment after every entity exists.</summary>
    private async Task WriteEntityStateAsync(EntityFile file, CancellationToken cancellationToken)
    {
        foreach (var component in file.Ordered())
        {
            await _world.SetComponentAsync(file.Id, component.DefinitionId, component.Data, cancellationToken);
        }

        await _world.MoveAsync(
            file.Id,
            string.IsNullOrEmpty(file.ContainerId) ? null : file.ContainerId,
            file.ContainerSlot,
            cancellationToken);
    }

    /// <summary>
    /// Applies the relationship set: adds what the file has, removes what it no longer lists.
    ///
    /// This is the one place import removes anything, and it is not an exception to the no-delete
    /// rule so much as a consequence of the granularity. A relationship has no identity of its own,
    /// so the file states the whole set rather than a list of records — and "these are the edges"
    /// has to mean that, or the file cannot express an edge being cut at all.
    /// </summary>
    private async Task<bool> WriteRelationshipsAsync(
        RelationshipsFile file,
        CancellationToken cancellationToken)
    {
        var wanted = file.Ordered().ToList();

        var existing = await _db.Relationships
            .AsNoTracking()
            .Select(r => new { r.FromEntityId, r.ToEntityId, r.Kind })
            .ToListAsync(cancellationToken);

        var keys = wanted
            .Select(r => (r.From, r.To, r.Kind))
            .ToHashSet();

        foreach (var stale in existing.Where(r => !keys.Contains((r.FromEntityId, r.ToEntityId, r.Kind))))
        {
            await _world.UnrelateAsync(stale.FromEntityId, stale.ToEntityId, stale.Kind, cancellationToken);
        }

        foreach (var relationship in wanted)
        {
            await _world.RelateAsync(
                relationship.From,
                relationship.To,
                relationship.Kind,
                relationship.Data,
                cancellationToken);
        }

        return false;
    }

    // ---- the manifest --------------------------------------------------------------------

    /// <summary>
    /// Rewrites the manifest to the new common ancestor — but only for records that now agree.
    ///
    /// A record the import skipped keeps its OLD entry on purpose. Recording the database's new
    /// fingerprint for it would make the next import read the untouched file as a catalog edit and
    /// overwrite the very live work this import just protected. Leaving the entry stale means the
    /// next run reports it again, and keeps reporting until somebody exports — which is the
    /// behaviour that actually resolves it.
    /// </summary>
    private async Task<bool> UpdateManifestAsync(
        string root,
        CatalogContents contents,
        CatalogImportPlan plan,
        CatalogImportOptions options,
        IReadOnlyDictionary<(CatalogRecordKind, string), int> versions,
        CancellationToken cancellationToken)
    {
        if (options.DryRun || contents.Manifest is null)
        {
            return false;
        }

        var previous = contents.Manifest.Records
            .ToDictionary(r => (r.Kind, r.Id), r => r, ValueTupleComparer.Instance);

        var entries = new List<CatalogManifestEntry>();

        foreach (var entry in plan.Entries)
        {
            // Derived from ShouldWrite rather than restated, so the manifest cannot claim
            // agreement for a record the apply pass skipped.
            var agrees = entry.Change == CatalogChange.Unchanged
                         || ShouldWrite(entry.Change, options.Force);

            if (agrees && TryDescribe(contents, entry, versions, out var described))
            {
                entries.Add(described);
                continue;
            }

            if (previous.TryGetValue((entry.Kind, entry.Id), out var carried))
            {
                entries.Add(carried);
            }
        }

        // ExportedAt is left as it was. An import is not an export, and moving the timestamp
        // would suggest the catalog had been refreshed from the database when it had not.
        var manifest = contents.Manifest with
        {
            Records = entries
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList()
        };

        await File.WriteAllTextAsync(
            Path.Combine(Path.GetFullPath(root), CatalogLayout.ManifestFileName),
            manifest.ToJson(),
            cancellationToken);

        return true;
    }

    private static bool TryDescribe(
        CatalogContents contents,
        CatalogImportPlanEntry entry,
        IReadOnlyDictionary<(CatalogRecordKind, string), int> versions,
        out CatalogManifestEntry described)
    {
        var version = versions.GetValueOrDefault((entry.Kind, entry.Id));

        switch (entry.Kind)
        {
            case CatalogRecordKind.Mechanic:
                var mechanic = contents.Mechanics.FirstOrDefault(f => f.Id == entry.Id);

                if (mechanic is not null)
                {
                    described = new CatalogManifestEntry(
                        entry.Kind,
                        mechanic.Id,
                        version,
                        mechanic.ContentHash,
                        ManifestPath(contents, entry, CatalogLayout.MechanicMarkdown(mechanic.Category, mechanic.Id)));

                    return true;
                }

                break;

            case CatalogRecordKind.Procedure:
                var procedure = contents.Procedures.FirstOrDefault(f => f.Id == entry.Id);

                if (procedure is not null)
                {
                    described = new CatalogManifestEntry(
                        entry.Kind,
                        procedure.Id,
                        version,
                        procedure.ContentHash,
                        ManifestPath(contents, entry, CatalogLayout.ProcedureMarkdown(procedure.Category, procedure.Id)));

                    return true;
                }

                break;

            case CatalogRecordKind.EventType:
                var eventType = contents.EventTypes.FirstOrDefault(f => f.Id == entry.Id);
                if (eventType is not null) { described = new CatalogManifestEntry(entry.Kind, eventType.Id, version, eventType.ContentHash, ManifestPath(contents, entry, CatalogLayout.EventType(eventType.Id))); return true; }
                break;

            case CatalogRecordKind.Subscription:
                var subscription = contents.Subscriptions.FirstOrDefault(f => f.Id == entry.Id);
                if (subscription is not null) { described = new CatalogManifestEntry(entry.Kind, subscription.Id, version, subscription.ContentHash, ManifestPath(contents, entry, CatalogLayout.Subscription(subscription.Id))); return true; }
                break;

            case CatalogRecordKind.Entity:
                var entity = contents.Entities.FirstOrDefault(f => f.Id == entry.Id);

                if (entity is not null)
                {
                    described = new CatalogManifestEntry(
                        entry.Kind,
                        entity.Id,
                        0,
                        entity.ContentHash,
                        ManifestPath(contents, entry, CatalogLayout.Entity(entity.Id)));

                    return true;
                }

                break;

            case CatalogRecordKind.Relationships:
                if (contents.Relationships is not null)
                {
                    described = new CatalogManifestEntry(
                        entry.Kind,
                        CatalogLayout.RelationshipsFileName,
                        0,
                        contents.Relationships.ContentHash,
                        CatalogLayout.RelationshipsFileName);

                    return true;
                }

                break;

            case CatalogRecordKind.ComponentDefinition:
                var component = contents.Components.FirstOrDefault(f => f.Id == entry.Id);

                if (component is not null)
                {
                    described = new CatalogManifestEntry(
                        entry.Kind,
                        component.Id,
                        version,
                        component.ContentHash,
                        CatalogLayout.Component(component.Id));

                    return true;
                }

                break;
        }

        described = null!;
        return false;
    }

    private static string ManifestPath(
        CatalogContents contents,
        CatalogImportPlanEntry entry,
        string canonicalPath)
    {
        if (contents.Manifest?.SchemaVersion != 1) return canonicalPath;
        return contents.Manifest.Records.FirstOrDefault(value => value.Kind == entry.Kind && value.Id == entry.Id)?.Path
            ?? canonicalPath;
    }

    // ---- what the database currently holds ------------------------------------------------

    private async Task<IReadOnlyDictionary<string, string>> StoredMechanicsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Mechanics
            .AsNoTracking()
            .Join(
                _db.MechanicVersions.AsNoTracking(),
                mechanic => new { MechanicId = mechanic.Id, Version = mechanic.CurrentVersion },
                version => new { version.MechanicId, version.Version },
                (mechanic, version) => new { mechanic.Id, version.SourceHash })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.SourceHash, StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> StoredProceduresAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.ProcedureContracts
            .AsNoTracking()
            .Join(
                _db.ProcedureContractVersions.AsNoTracking(),
                contract => new { ContractId = contract.Id, Version = contract.CurrentVersion },
                version => new { version.ContractId, version.Version },
                (contract, version) => new { contract.Id, version.SourceHash })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.SourceHash, StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> StoredEventTypesAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.EventTypes.AsNoTracking().Join(_db.EventTypeVersions.AsNoTracking(), e => new { EventTypeId = e.Id, Version = e.CurrentVersion }, v => new { v.EventTypeId, v.Version }, (e, v) => new { e.Id, v.SourceHash }).ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Id, x => x.SourceHash, StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> StoredSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Subscriptions.AsNoTracking().Join(_db.SubscriptionVersions.AsNoTracking(), s => new { SubscriptionId = s.Id, Version = s.CurrentVersion }, v => new { v.SubscriptionId, v.Version }, (s, v) => new { s.Id, v.SourceHash }).ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Id, x => x.SourceHash, StringComparer.Ordinal);
    }

    /// <summary>
    /// Component definitions carry no fingerprint column, so theirs is computed on read. They are
    /// not versioned and there is nowhere to store one — but leaving them out of drift detection
    /// would mean one kind of record the catalog cannot see changing.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> StoredComponentsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.ComponentDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => ContentHash.ForComponentDefinition(r.Name, r.Description, r.Schema),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Entities as the catalog would describe them, so the two sides are comparable at all.
    ///
    /// Component data goes through the same canonicaliser the file writer uses — otherwise a blob
    /// stored minified and the same blob written out indented would look like an edit nobody made,
    /// on every single entity, forever.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> StoredEntitiesAsync(CancellationToken cancellationToken)
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

        var containerOf = (await _db.Containments
                .AsNoTracking()
                .Select(c => new { c.ContainedId, c.ContainerId, c.Slot })
                .ToListAsync(cancellationToken))
            .ToDictionary(c => c.ContainedId, c => c, StringComparer.Ordinal);

        return entities.ToDictionary(
            e => e.Id,
            e =>
            {
                containerOf.TryGetValue(e.Id, out var container);

                List<(string DefinitionId, string Data)> attached = components.TryGetValue(e.Id, out var list)
                    ? list
                        .Select(c => (c.DefinitionId, Data: EntityFile.CanonicalData(c.Data, e.Id, c.DefinitionId)))
                        .OrderBy(c => c.DefinitionId, StringComparer.Ordinal)
                        .ToList()
                    : [];

                return ContentHash.ForEntity(
                    e.Name,
                    container?.ContainerId,
                    container?.Slot ?? string.Empty,
                    attached);
            },
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> StoredRelationshipsAsync(
        CancellationToken cancellationToken)
    {
        var edges = await _db.Relationships
            .AsNoTracking()
            .Select(r => new { r.FromEntityId, r.ToEntityId, r.Kind, r.Data })
            .ToListAsync(cancellationToken);

        // No edges means no record, so that a fresh database reads as "absent" rather than as
        // "present and empty". See the note at the call site.
        if (edges.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var file = new RelationshipsFile(edges
            .Select(r => new RelationshipEntry(r.FromEntityId, r.ToEntityId, r.Kind, r.Data ?? "{}"))
            .ToList());

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CatalogLayout.RelationshipsFileName] = file.ContentHash
        };
    }

    /// <summary>Current version per versioned record, so the manifest records the real number.</summary>
    private async Task<IReadOnlyDictionary<(CatalogRecordKind, string), int>> CurrentVersionsAsync(
        CancellationToken cancellationToken)
    {
        var versions = new Dictionary<(CatalogRecordKind, string), int>();

        foreach (var row in await _db.Mechanics
            .AsNoTracking()
            .Select(m => new { m.Id, m.CurrentVersion })
            .ToListAsync(cancellationToken))
        {
            versions[(CatalogRecordKind.Mechanic, row.Id)] = row.CurrentVersion;
        }

        foreach (var row in await _db.ProcedureContracts
            .AsNoTracking()
            .Select(c => new { c.Id, c.CurrentVersion })
            .ToListAsync(cancellationToken))
        {
            versions[(CatalogRecordKind.Procedure, row.Id)] = row.CurrentVersion;
        }
        foreach (var row in await _db.EventTypes.AsNoTracking().Select(e => new { e.Id, e.CurrentVersion }).ToListAsync(cancellationToken)) versions[(CatalogRecordKind.EventType, row.Id)] = row.CurrentVersion;
        foreach (var row in await _db.Subscriptions.AsNoTracking().Select(s => new { s.Id, s.CurrentVersion }).ToListAsync(cancellationToken)) versions[(CatalogRecordKind.Subscription, row.Id)] = row.CurrentVersion;

        // Component definitions are not versioned; they stay at zero, which is what the exporter
        // writes for them too.
        return versions;
    }

    private static IReadOnlyDictionary<string, string> Fingerprints<T>(
        IReadOnlyList<T> files,
        Func<T, string> id,
        Func<T, string> fingerprint) =>
        files.ToDictionary(id, fingerprint, StringComparer.Ordinal);

    /// <summary>Ordinal comparison for the (kind, id) key, so casing never merges two records.</summary>
    private sealed class ValueTupleComparer : IEqualityComparer<(CatalogRecordKind Kind, string Id)>
    {
        public static readonly ValueTupleComparer Instance = new();

        public bool Equals((CatalogRecordKind Kind, string Id) x, (CatalogRecordKind Kind, string Id) y) =>
            x.Kind == y.Kind && string.Equals(x.Id, y.Id, StringComparison.Ordinal);

        public int GetHashCode((CatalogRecordKind Kind, string Id) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.Id));
    }
}

public sealed record CatalogNamespaceImportResult(
    int Total,
    int Created,
    int Updated,
    int Unchanged,
    bool Applied);
