using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Applications;

/// <summary>SQLite persistence for the immutable application-registration contract.</summary>
public sealed class SqliteApplicationRegistry(DantesRoleplayDbContext db) : IApplicationRegistry
{
    public ApplicationRevision Register(ApplicationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var normalized = Copy(registration);
        Validate(normalized);

        using var transaction = db.Database.CurrentTransaction is null
            ? db.Database.BeginTransaction()
            : null;
        var existing = db.Set<ApplicationRegistryRecord>().SingleOrDefault(x => x.Id == normalized.Id.Value);
        if (existing is not null)
        {
            var existingRevision = Get(normalized.Id)!;
            if (!SameRegistration(normalized, existing, existingRevision.BaseApplications))
                throw new InvalidOperationException($"Application '{normalized.Id}' already has a different immutable registration.");

            transaction?.Commit();
            return existingRevision;
        }

        var missingBase = normalized.BaseApplications
            .Select(x => x.Value)
            .Except(db.Set<ApplicationRegistryRecord>().Select(x => x.Id), StringComparer.Ordinal)
            .FirstOrDefault();
        if (missingBase is not null)
            throw new ArgumentException("Every base application must already be registered.", nameof(registration));

        var revision = new ApplicationRevision(normalized.Id, 1, ApplicationRegistrationFingerprint.Compute(normalized), ReadOnly(normalized.BaseApplications));
        db.Add(new ApplicationRegistryRecord
        {
            Id = normalized.Id.Value,
            DisplayName = normalized.DisplayName,
            Description = normalized.Description,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Add(new ApplicationRevisionRecord
        {
            ApplicationId = normalized.Id.Value,
            Revision = revision.Revision,
            Fingerprint = revision.Fingerprint,
            CreatedAtUtc = DateTime.UtcNow
        });
        foreach (var (baseApplication, ordinal) in normalized.BaseApplications.Select((value, index) => (value, index)))
        {
            db.Add(new ApplicationRevisionBaseRecord
            {
                ApplicationId = normalized.Id.Value,
                Revision = revision.Revision,
                Ordinal = ordinal,
                BaseApplicationId = baseApplication.Value
            });
        }

        db.SaveChanges();
        transaction?.Commit();
        return revision;
    }

    public ApplicationRevision? Get(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (!db.Set<ApplicationRegistryRecord>().AsNoTracking().Any(x => x.Id == applicationId.Value)) return null;
        var revision = db.Set<ApplicationRevisionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value)
            .Max(value => value.Revision);
        return ReadRevision(applicationId.Value, revision);
    }

    public ApplicationRevision? Get(ApplicationIdentifier applicationId, int revision)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        return db.Set<ApplicationRevisionRecord>().AsNoTracking().Any(value =>
            value.ApplicationId == applicationId.Value && value.Revision == revision)
            ? ReadRevision(applicationId.Value, revision) : null;
    }

    public ApplicationRevision ReviseBaseApplications(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationIdentifier> baseApplications,
        int expectedRevision,
        string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(baseApplications);
        var existing = db.Set<ApplicationRegistryRecord>().SingleOrDefault(value => value.Id == applicationId.Value)
            ?? throw new KeyNotFoundException("Application is not registered.");
        var current = Get(applicationId)!;
        if (current.Revision != expectedRevision || current.Fingerprint != expectedFingerprint)
            throw new InvalidOperationException("The application revision is stale.");
        var registration = new ApplicationRegistration(applicationId, existing.DisplayName,
            existing.Description, ReadOnly(baseApplications));
        Validate(registration);
        var missingBase = registration.BaseApplications.Select(value => value.Value)
            .Except(db.Set<ApplicationRegistryRecord>().Select(value => value.Id), StringComparer.Ordinal)
            .FirstOrDefault();
        if (missingBase is not null)
            throw new ArgumentException("Every base application must already be registered.", nameof(baseApplications));
        if (registration.BaseApplications.SequenceEqual(current.BaseApplications)) return current;

        var revision = new ApplicationRevision(applicationId, current.Revision + 1,
            ApplicationRegistrationFingerprint.Compute(registration), ReadOnly(registration.BaseApplications));
        db.Add(new ApplicationRevisionRecord
        {
            ApplicationId = applicationId.Value,
            Revision = revision.Revision,
            Fingerprint = revision.Fingerprint,
            CreatedAtUtc = DateTime.UtcNow
        });
        foreach (var (baseApplication, ordinal) in registration.BaseApplications.Select((value, index) => (value, index)))
            db.Add(new ApplicationRevisionBaseRecord
            {
                ApplicationId = applicationId.Value,
                Revision = revision.Revision,
                Ordinal = ordinal,
                BaseApplicationId = baseApplication.Value
            });
        db.SaveChanges();
        return revision;
    }

    public ApplicationRegistration? Describe(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var row = db.Set<ApplicationRegistryRecord>().AsNoTracking()
            .SingleOrDefault(x => x.Id == applicationId.Value);
        return row is null ? null : ToRegistration(row);
    }

    public IReadOnlyList<ApplicationRegistration> List(int limit)
    {
        ValidateLimit(limit);
        return db.Set<ApplicationRegistryRecord>().AsNoTracking()
            .Where(x => x.Id != ApplicationIdentifier.System.Value)
            .OrderBy(x => x.Id).Take(limit).AsEnumerable().Select(ToRegistration).ToArray();
    }

    public ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit)
    {
        ValidateLimit(limit);
        var after = ValidateAfter(afterApplicationId);
        var rows = db.Set<ApplicationRegistryRecord>().AsNoTracking()
            .Where(value => value.Id != ApplicationIdentifier.System.Value
                && (after == null || string.Compare(value.Id, after) > 0))
            .OrderBy(value => value.Id)
            .Take(limit + 1)
            .AsEnumerable()
            .Select(ToRegistration)
            .ToArray();
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        return new(Array.AsReadOnly(page), hasMore ? page[^1].Id.Value : null);
    }

    private ApplicationRevision ReadRevision(string applicationId, int revision)
    {
        var row = db.Set<ApplicationRevisionRecord>().AsNoTracking()
            .Single(x => x.ApplicationId == applicationId && x.Revision == revision);
        var bases = db.Set<ApplicationRevisionBaseRecord>().AsNoTracking()
            .Where(x => x.ApplicationId == applicationId && x.Revision == revision)
            .OrderBy(x => x.Ordinal)
            .Select(x => ApplicationIdentifier.Parse(x.BaseApplicationId))
            .ToArray();
        return new(ApplicationIdentifier.Parse(row.ApplicationId), row.Revision, row.Fingerprint, ReadOnly(bases));
    }

    private ApplicationRegistration ToRegistration(ApplicationRegistryRecord row)
    {
        var revision = Get(ApplicationIdentifier.Parse(row.Id))!;
        return new(ApplicationIdentifier.Parse(row.Id), row.DisplayName, row.Description, revision.BaseApplications);
    }

    private static void Validate(ApplicationRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.DisplayName))
            throw new ArgumentException("An application display name is required.", nameof(registration));
        if (registration.BaseApplications.Distinct().Count() != registration.BaseApplications.Count)
            throw new ArgumentException("An application may list each base only once.", nameof(registration));
        if (registration.BaseApplications.Contains(registration.Id))
            throw new ArgumentException("An application cannot be its own base.", nameof(registration));
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private string? ValidateAfter(string? afterApplicationId)
    {
        if (afterApplicationId is null) return null;
        var parsed = ApplicationIdentifier.Parse(afterApplicationId);
        if (!db.Set<ApplicationRegistryRecord>().AsNoTracking().Any(value => value.Id == parsed.Value))
            throw new InvalidOperationException("CURSOR_STALE");
        return parsed.Value;
    }

    private static bool SameRegistration(ApplicationRegistration registration, ApplicationRegistryRecord record, IReadOnlyList<ApplicationIdentifier> bases) =>
        registration.Id.Value == record.Id
        && registration.DisplayName == record.DisplayName
        && registration.Description == record.Description
        && registration.BaseApplications.SequenceEqual(bases);

    private static ApplicationRegistration Copy(ApplicationRegistration registration) =>
        new(registration.Id, registration.DisplayName, registration.Description, ReadOnly(registration.BaseApplications));

    private static IReadOnlyList<ApplicationIdentifier> ReadOnly(IEnumerable<ApplicationIdentifier> values) =>
        Array.AsReadOnly(values.ToArray());

}

internal sealed class ApplicationRegistryRecord
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class ApplicationRevisionRecord
{
    public required string ApplicationId { get; set; }
    public int Revision { get; set; }
    public required string Fingerprint { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class ApplicationRevisionBaseRecord
{
    public required string ApplicationId { get; set; }
    public int Revision { get; set; }
    public int Ordinal { get; set; }
    public required string BaseApplicationId { get; set; }
}
