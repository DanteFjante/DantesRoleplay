using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.CatalogNamespaces;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Validates repository-authored catalog content without reading or changing the live database.
///
/// The catalog is copied, migrated into a fresh disposable SQLite database, imported through the
/// production importer, checked through the same write-side validators used by MCP dry runs, and
/// planned once more to prove the imported database and copied files agree. This replaces a series
/// of token-heavy live calls with one deterministic developer gate; behavioral mechanics still
/// belong in focused tests and the feature acceptance suite.
/// </summary>
public static class CatalogValidator
{
    public static async Task<CatalogValidationResult> ValidateAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var source = Path.GetFullPath(root);

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"No catalog at '{source}'.");
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"dantesroleplay-catalog-validation-{Guid.NewGuid():n}");
        var catalogCopy = Path.Combine(temporaryRoot, "catalog");
        var databasePath = Path.Combine(temporaryRoot, "validation.db");

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            CopyDirectory(source, catalogCopy);
            return await ValidateCopyAsync(catalogCopy, databasePath, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static async Task<CatalogValidationResult> ValidateCopyAsync(
        string root,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            // A disposable validation file must be deletable as soon as the context closes.
            // SQLite connection pooling otherwise keeps the Windows file handle alive.
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        var eventTypes = new EventTypeStore(db);
        var subscriptions = new SubscriptionStore(db);
        var importer = new CatalogImporter(
            db,
            mechanics,
            procedures,
            new WorldStore(db),
            eventTypes,
            subscriptions);

        var contents = await CatalogReader.ReadAsync(root, cancellationToken);
        var issues = new List<CatalogValidationIssue>();
        issues.AddRange(CatalogNamespaceConformance.FindUnreviewedRecords(contents).Select(issue =>
            new CatalogValidationIssue(issue.Kind, issue.Id, "namespace-review", issue.Detail, Warning: true)));

        var imported = await importer.ApplyAsync(root, new CatalogImportOptions(), cancellationToken);

        if (imported.Aborted)
        {
            foreach (var conflict in imported.Plan.Conflicts)
            {
                issues.Add(new CatalogValidationIssue(
                    conflict.Kind.ToString(),
                    conflict.Id,
                    "fresh-import",
                    conflict.Detail,
                    Warning: false));
            }

            return Result(contents, issues);
        }

        foreach (var file in contents.Mechanics)
        {
            var checks = await mechanics.CheckAsync(
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
                    CreatedBy = file.CreatedBy,
                    ChangeNote = ValidationChangeNote(file.ChangeNote)
                },
                cancellationToken);

            issues.AddRange(checks
                .Where(check => !check.Passed)
                .Where(check => check.Blocking || IsChanged(
                    contents.Manifest, CatalogRecordKind.Mechanic, file.Id, file.ContentHash))
                .Select(check => new CatalogValidationIssue(
                    "mechanic", file.Id, check.Name, check.Detail, Warning: !check.Blocking)));
        }

        foreach (var file in contents.Procedures)
        {
            var checks = await procedures.CheckAsync(
                new WriteProcedureRequest
                {
                    Id = file.Id,
                    Category = file.Category,
                    Name = file.Name,
                    Description = file.Description,
                    Governs = file.Governs,
                    Matches = file.Matches,
                    Instructions = file.Instructions,
                    Constraints = file.Constraints,
                    Status = file.Status,
                    CreatedBy = file.CreatedBy,
                    ChangeNote = ValidationChangeNote(file.ChangeNote)
                },
                cancellationToken);

            issues.AddRange(checks
                .Where(check => !check.Passed)
                .Where(check => check.Name != "no-near-duplicate" || IsChanged(
                    contents.Manifest, CatalogRecordKind.Procedure, file.Id, file.ContentHash))
                .Select(check => new CatalogValidationIssue(
                    "procedure",
                    file.Id,
                    check.Name,
                    check.Detail,
                    Warning: check.Name == "no-near-duplicate")));
        }

        foreach (var file in contents.EventTypes)
        {
            var checks = await eventTypes.CheckAsync(
                new WriteEventTypeRequest
                {
                    Id = file.Id,
                    Category = file.Category,
                    Name = file.Name,
                    Description = file.Description,
                    PayloadSchema = file.Schema,
                    Scope = file.Scope,
                    Status = file.Status,
                    CreatedBy = file.CreatedBy,
                    ChangeNote = ValidationChangeNote(file.ChangeNote)
                },
                cancellationToken);

            issues.AddRange(checks
                .Where(check => !check.Passed)
                .Where(check => check.Blocking || IsChanged(
                    contents.Manifest, CatalogRecordKind.EventType, file.Id, file.ContentHash))
                .Select(check => new CatalogValidationIssue(
                    "event-type", file.Id, check.Name, check.Detail, Warning: !check.Blocking)));
        }

        foreach (var file in contents.Subscriptions)
        {
            var checks = await subscriptions.CheckAsync(
                new WriteSubscriptionRequest
                {
                    Id = file.Id,
                    Category = file.Category,
                    EventTypeId = file.EventTypeId,
                    EventMechanicId = file.EventMechanicId,
                    Mode = file.Mode,
                    Order = file.Order,
                    FixedRoleEntityIdsJson = file.FixedRoleEntityIdsJson,
                    RoleFromEventPayloadJson = file.RoleFromEventPayloadJson,
                    FanoutSelectorJson = file.FanoutSelectorJson,
                    TrackedEntityIdsJson = file.TrackedEntityIdsJson,
                    PayloadEqualsJson = file.PayloadEqualsJson,
                    MaxExecutionsPerChain = file.MaxExecutionsPerChain,
                    Scope = file.Scope,
                    Status = file.Status,
                    CreatedBy = file.CreatedBy,
                    ChangeNote = ValidationChangeNote(file.ChangeNote)
                },
                cancellationToken);

            issues.AddRange(checks
                .Where(check => !check.Passed)
                .Where(check => check.Blocking || IsChanged(
                    contents.Manifest, CatalogRecordKind.Subscription, file.Id, file.ContentHash))
                .Select(check => new CatalogValidationIssue(
                    "subscription", file.Id, check.Name, check.Detail, Warning: !check.Blocking)));
        }

        var finalPlan = await importer.PlanAsync(root, cancellationToken);

        foreach (var difference in finalPlan.Entries.Where(entry =>
                     entry.Change is not CatalogChange.Unchanged and not CatalogChange.GoneFromBoth))
        {
            issues.Add(new CatalogValidationIssue(
                difference.Kind.ToString(),
                difference.Id,
                "round-trip",
                difference.Detail,
                Warning: false));
        }

        return Result(contents, issues);
    }

    private static CatalogValidationResult Result(
        CatalogContents contents,
        IReadOnlyList<CatalogValidationIssue> issues) =>
        new(
            contents.Records,
            contents.Mechanics.Count,
            contents.Procedures.Count,
            contents.Components.Count,
            contents.EventTypes.Count,
            contents.Subscriptions.Count,
            contents.Entities.Count,
            issues);

    // Checks run after the fresh import so every dependency exists. At that point each record is
    // technically a revision, even when the source file represented version one. Supply the same
    // administrative fallback import uses; provenance is outside the content fingerprint and an
    // empty original change note is not a malformed catalog.
    private static string ValidationChangeNote(string changeNote) =>
        string.IsNullOrWhiteSpace(changeNote) ? "Validated from the catalog." : changeNote;

    // Existing warning baselines are not useful on every run and eventually train the reader to
    // ignore the gate. A changed or new record still receives the same overlap warning; unchanged
    // records have already crossed that decision boundary.
    private static bool IsChanged(
        CatalogManifest? manifest,
        CatalogRecordKind kind,
        string id,
        string contentHash) =>
        manifest is null
        || !string.Equals(manifest.FingerprintOf(kind, id), contentHash, StringComparison.Ordinal);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}

public static class CatalogNamespaceConformance
{
    public static IReadOnlyList<CatalogNamespaceConformanceIssue> FindUnreviewedRecords(CatalogContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var namespaces = contents.Namespaces.ToDictionary(value => value.Id, StringComparer.Ordinal);
        return CatalogReader.RecordIdentities(contents)
            .Select(value => (value.Id, value.Kind, NamespaceId: CatalogNamespaceIdentity.NamespaceOf(value.Id)))
            .Where(value => namespaces.TryGetValue(value.NamespaceId, out var definition)
                && definition.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
            .Select(value => new CatalogNamespaceConformanceIssue(
                value.Kind,
                value.Id,
                value.NamespaceId,
                $"Record '{value.Id}' uses namespace '{value.NamespaceId}', which is registered but still needs review. "
                + namespaces[value.NamespaceId].ReviewNote))
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record CatalogNamespaceConformanceIssue(
    string Kind,
    string Id,
    string NamespaceId,
    string Detail);

public sealed record CatalogValidationIssue(
    string Kind,
    string Id,
    string Check,
    string Detail,
    bool Warning);

public sealed record CatalogValidationResult(
    int Records,
    int Mechanics,
    int Procedures,
    int Components,
    int EventTypes,
    int Subscriptions,
    int Entities,
    IReadOnlyList<CatalogValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Warning);
    public int Errors => Issues.Count(issue => !issue.Warning);
    public int Warnings => Issues.Count(issue => issue.Warning);
}
