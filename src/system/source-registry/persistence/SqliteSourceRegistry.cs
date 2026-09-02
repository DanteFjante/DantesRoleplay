using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.Sources;

/// <summary>SQLite persistence for source specifications; it deliberately cannot resolve paths or scan files.</summary>
public sealed class SqliteSourceRegistry(DantesRoleplayDbContext db) : ISourceRegistry
{
    public SourceRegistration Register(SourceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        using var transaction = db.Database.CurrentTransaction is null
            ? db.Database.BeginTransaction()
            : null;
        if (!db.Set<ApplicationRegistryRecord>().Any(x => x.Id == registration.ApplicationId.Value))
            throw new ArgumentException("A source can only be registered for an existing application.", nameof(registration));

        // Retired rows stay in the table, so an ID that is invisible to resolution is still taken.
        // Registering over one would either violate the primary key or quietly resurrect a
        // registration somebody deliberately withdrew; both are worse than refusing.
        if (db.Set<ApplicationSourceRecord>().Any(x => x.ApplicationId == registration.ApplicationId.Value
            && x.SourceId == registration.SourceId && x.RetiredAtUtc != null))
            throw new InvalidOperationException(
                $"Source '{registration.SourceId}' was retired; source IDs are permanent and are not reused.");

        var existing = ReadFor(registration.ApplicationId);
        var validator = new InMemorySourceRegistry();
        foreach (var current in existing) validator.Register(current);
        var result = validator.Register(registration);
        if (existing.Any(x => x.SourceId == registration.SourceId))
        {
            transaction?.Commit();
            return result;
        }

        db.Add(new ApplicationSourceRecord
        {
            ApplicationId = registration.ApplicationId.Value,
            SourceId = registration.SourceId,
            AllowedRootId = registration.AllowedRootId,
            RelativePathOrGlob = registration.RelativePathOrGlob,
            Trust = registration.Trust,
            Precedence = registration.Precedence,
            LogicalIdentity = registration.LogicalIdentity,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        transaction?.Commit();
        return result;
    }

    public IReadOnlyList<SourceRegistration> For(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return ReadFor(applicationId);
    }

    public SourceRegistration? Get(ApplicationIdentifier applicationId, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var row = db.Set<ApplicationSourceRecord>().AsNoTracking()
            .SingleOrDefault(x => x.ApplicationId == applicationId.Value && x.SourceId == sourceId
                && x.RetiredAtUtc == null);
        return row is null ? null : ToContract(row);
    }

    public IReadOnlyList<SourceRegistration> List(ApplicationIdentifier applicationId, int limit)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return Query(applicationId).Take(limit).ToArray();
    }

    public RetiredSource Retire(ApplicationIdentifier applicationId, string sourceId, string reason)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var trimmed = reason.Trim();
        if (trimmed.Length > 500)
            throw new ArgumentException("A retirement reason is at most 500 characters.", nameof(reason));
        var row = db.Set<ApplicationSourceRecord>()
            .SingleOrDefault(x => x.ApplicationId == applicationId.Value && x.SourceId == sourceId)
            ?? throw new InvalidOperationException($"Source '{sourceId}' is not registered for '{applicationId.Value}'.");
        if (row.RetiredAtUtc is not null)
            throw new InvalidOperationException($"Source '{sourceId}' is already retired.");
        row.RetiredAtUtc = DateTime.UtcNow;
        row.RetiredReason = trimmed;
        db.SaveChanges();
        return new(ToContract(row), row.RetiredAtUtc.Value, trimmed);
    }

    public IReadOnlyList<RetiredSource> Retired(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return db.Set<ApplicationSourceRecord>().AsNoTracking()
            .Where(x => x.ApplicationId == applicationId.Value && x.RetiredAtUtc != null)
            .OrderBy(x => x.SourceId)
            .ToArray()
            .Select(x => new RetiredSource(ToContract(x), x.RetiredAtUtc!.Value, x.RetiredReason ?? ""))
            .ToArray();
    }

    private IReadOnlyList<SourceRegistration> ReadFor(ApplicationIdentifier applicationId) => Query(applicationId).ToArray();

    private IQueryable<SourceRegistration> Query(ApplicationIdentifier applicationId) => db.Set<ApplicationSourceRecord>()
        .AsNoTracking()
        .Where(x => x.ApplicationId == applicationId.Value && x.RetiredAtUtc == null)
        .OrderByDescending(x => x.Precedence).ThenBy(x => x.SourceId)
        .Select(x => new SourceRegistration(
            ApplicationIdentifier.Parse(x.ApplicationId), x.SourceId, x.AllowedRootId,
            x.RelativePathOrGlob, x.Trust, x.Precedence, x.LogicalIdentity));

    private static SourceRegistration ToContract(ApplicationSourceRecord row) => new(
        ApplicationIdentifier.Parse(row.ApplicationId), row.SourceId, row.AllowedRootId,
        row.RelativePathOrGlob, row.Trust, row.Precedence, row.LogicalIdentity);
}

/// <summary>Append-only source-scan evidence. Scanning and overlay decisions remain later owners.</summary>
public sealed class SqliteSourceScanReceiptStore(DantesRoleplayDbContext db) : ISourceScanReceiptStore
{
    public SourceScanReceipt Record(SourceScanReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(receipt);
        using var transaction = db.Database.BeginTransaction();
        if (!db.Set<ApplicationSourceRecord>().Any(x => x.ApplicationId == receipt.ApplicationId.Value && x.SourceId == receipt.SourceId))
            throw new ArgumentException("A scan receipt requires a registered source.", nameof(receipt));

        var existing = db.Set<ApplicationSourceScanRecord>().SingleOrDefault(x =>
            x.ApplicationId == receipt.ApplicationId.Value && x.SourceId == receipt.SourceId && x.Generation == receipt.Generation);
        if (existing is not null)
        {
            var persisted = ToContract(existing);
            if (persisted != receipt)
                throw new InvalidOperationException("A scan generation is immutable.");
            transaction.Commit();
            return persisted;
        }

        var lastGeneration = db.Set<ApplicationSourceScanRecord>()
            .Where(x => x.ApplicationId == receipt.ApplicationId.Value && x.SourceId == receipt.SourceId)
            .Max(x => (int?)x.Generation) ?? 0;
        if (receipt.Generation != lastGeneration + 1)
            throw new InvalidOperationException("Scan generations must append contiguously.");

        db.Add(new ApplicationSourceScanRecord
        {
            ApplicationId = receipt.ApplicationId.Value,
            SourceId = receipt.SourceId,
            Generation = receipt.Generation,
            Status = receipt.Status,
            ContentFingerprint = receipt.ContentFingerprint,
            RecordedAtUtc = receipt.RecordedAtUtc
        });
        db.SaveChanges();
        transaction.Commit();
        return receipt;
    }

    public IReadOnlyList<SourceScanReceipt> For(ApplicationIdentifier applicationId, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return db.Set<ApplicationSourceScanRecord>().AsNoTracking()
            .Where(x => x.ApplicationId == applicationId.Value && x.SourceId == sourceId)
            .OrderBy(x => x.Generation)
            .Select(ToContract)
            .ToArray();
    }

    public SourceScanReceipt? Latest(ApplicationIdentifier applicationId, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var row = db.Set<ApplicationSourceScanRecord>().AsNoTracking()
            .Where(x => x.ApplicationId == applicationId.Value && x.SourceId == sourceId)
            .OrderByDescending(x => x.Generation).FirstOrDefault();
        return row is null ? null : ToContract(row);
    }

    private static void Validate(SourceScanReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.SourceId)
            || receipt.Generation <= 0
            || !Enum.IsDefined(receipt.Status)
            || receipt.ContentFingerprint.Length != 64
            || receipt.ContentFingerprint.Any(c => !(char.IsAsciiDigit(c) || (c is >= 'A' and <= 'F')))
            || receipt.RecordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A scan receipt needs a source, positive generation, known status, uppercase SHA-256 fingerprint, and UTC timestamp.", nameof(receipt));
    }

    private static SourceScanReceipt ToContract(ApplicationSourceScanRecord record) => new(
        ApplicationIdentifier.Parse(record.ApplicationId), record.SourceId, record.Generation,
        record.Status, record.ContentFingerprint, record.RecordedAtUtc);
}

internal sealed class ApplicationSourceRecord
{
    public required string ApplicationId { get; set; }
    public required string SourceId { get; set; }
    public required string AllowedRootId { get; set; }
    public required string RelativePathOrGlob { get; set; }
    public SourceTrust Trust { get; set; }
    public int Precedence { get; set; }
    public required string LogicalIdentity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public string? RetiredReason { get; set; }
}

internal sealed class ApplicationSourceScanRecord
{
    public required string ApplicationId { get; set; }
    public required string SourceId { get; set; }
    public int Generation { get; set; }
    public SourceScanStatus Status { get; set; }
    public required string ContentFingerprint { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

/// <summary>SQLite persistence for immutable application-extension registrations.</summary>
public sealed class SqliteApplicationExtensionRegistry(
    DantesRoleplayDbContext db,
    ISourceRegistry sources) : IApplicationExtensionRegistry
{
    public ApplicationExtensionRegistration Register(ApplicationExtensionRegistration registration)
    {
        if (!db.Set<ApplicationRegistryRecord>().Any(value => value.Id == registration.ApplicationId.Value))
            throw new ArgumentException("An extension can only target a registered application.", nameof(registration));
        var current = For(registration.ApplicationId);
        var normalized = ApplicationExtensionValidation.Normalize(registration, sources,
            current.Where(value => value.ExtensionId != registration.ExtensionId).ToArray());
        var existing = db.Set<ApplicationExtensionRecord>().SingleOrDefault(value =>
            value.ApplicationId == normalized.ApplicationId.Value
            && value.ExtensionId == normalized.ExtensionId);
        if (existing is not null)
        {
            var persisted = ToContract(existing);
            if (ApplicationExtensionRegistrationFingerprint.Compute(persisted)
                != ApplicationExtensionRegistrationFingerprint.Compute(normalized))
                throw new InvalidOperationException("Extension registrations are immutable.");
            return persisted;
        }
        db.Add(new ApplicationExtensionRecord
        {
            ApplicationId = normalized.ApplicationId.Value,
            ExtensionId = normalized.ExtensionId,
            DisplayName = normalized.DisplayName,
            Description = normalized.Description,
            Classification = normalized.Classification,
            SourceIdsJson = JsonSerializer.Serialize(normalized.SourceIds),
            NamespaceIdsJson = JsonSerializer.Serialize(normalized.NamespaceIds),
            DependenciesJson = JsonSerializer.Serialize(normalized.Dependencies),
            ConflictsWithJson = JsonSerializer.Serialize(normalized.ConflictsWith),
            HigherPriorityThanJson = JsonSerializer.Serialize(normalized.HigherPriorityThan),
            OverridesBase = normalized.OverridesBase,
            RegistrationFingerprint = ApplicationExtensionRegistrationFingerprint.Compute(normalized),
            RegistrationSchemaVersion = 2,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        return normalized;
    }

    public ApplicationExtensionRegistration? Get(
        ApplicationIdentifier applicationId, string extensionId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        var row = db.Set<ApplicationExtensionRecord>().AsNoTracking().SingleOrDefault(value =>
            value.ApplicationId == applicationId.Value && value.ExtensionId == extensionId);
        return row is null ? null : ToContract(row);
    }

    public IReadOnlyList<ApplicationExtensionRegistration> For(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return db.Set<ApplicationExtensionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value)
            .OrderBy(value => value.ExtensionId).AsEnumerable().Select(ToContract).ToArray();
    }

    private static ApplicationExtensionRegistration ToContract(ApplicationExtensionRecord row)
    {
        var result = new ApplicationExtensionRegistration(
            ApplicationIdentifier.Parse(row.ApplicationId), row.ExtensionId,
            row.RegistrationSchemaVersion >= 2 ? row.DisplayName : row.ExtensionId,
            row.Description,
            row.RegistrationSchemaVersion >= 2 ? row.Classification : ApplicationExtensionClassifications.ThirdParty,
            Strings(row.SourceIdsJson), Strings(row.NamespaceIdsJson), Strings(row.DependenciesJson),
            Strings(row.ConflictsWithJson), Strings(row.HigherPriorityThanJson), row.OverridesBase);
        if (row.RegistrationSchemaVersion >= 2
            && ApplicationExtensionRegistrationFingerprint.Compute(result) != row.RegistrationFingerprint)
            throw new InvalidOperationException("The stored extension registration fingerprint is inconsistent.");
        return result;
    }

    private static IReadOnlyList<string> Strings(string json) =>
        Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidOperationException("Stored extension metadata is invalid."));
}

internal sealed class ApplicationExtensionRecord
{
    public required string ApplicationId { get; set; }
    public required string ExtensionId { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string Classification { get; set; }
    public required string SourceIdsJson { get; set; }
    public required string NamespaceIdsJson { get; set; }
    public required string DependenciesJson { get; set; }
    public required string ConflictsWithJson { get; set; }
    public required string HigherPriorityThanJson { get; set; }
    public bool OverridesBase { get; set; }
    public required string RegistrationFingerprint { get; set; }
    public int RegistrationSchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
