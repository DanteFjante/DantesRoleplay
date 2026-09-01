using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

/// <summary>Version-append-only SQLite owner for active application component type contracts.</summary>
public sealed class SqliteComponentTypeRegistry(
    DantesRoleplayDbContext db,
    IBoundedJsonSchemaValidator validator) : IApplicationComponentTypeRegistry
{
    public RegisteredComponentTypeVersion Define(ComponentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId);
        var compilation = validator.Compile(definition.SchemaJson);
        if (!compilation.IsAccepted)
            throw new ArgumentException(
                "The component schema is not accepted by the bounded schema profile: " +
                string.Join("; ", compilation.Diagnostics.Select(value => $"{value.Code} {value.Pointer}: {value.Message}")),
                nameof(definition));
        EcsComponentRolePolicyParser.Parse(compilation.NormalizedSchema);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        using var transaction = ownsTransaction ? db.Database.BeginTransaction() : null;
        if (!definition.Owner.IsSystem
            && !db.Set<ApplicationRegistryRecord>().Any(x => x.Id == definition.Owner.Value))
            throw new ArgumentException("A component type can only be registered for an existing application.", nameof(definition));

        var existingType = db.Set<ComponentTypeRecord>().SingleOrDefault(x => x.QualifiedId == definition.QualifiedId);
        if (existingType is not null && existingType.ApplicationId != definition.Owner.Value)
            throw new InvalidOperationException("A qualified component type belongs to a different application.");
        if (existingType?.DisabledAtUtc is not null)
            throw new InvalidOperationException("A disabled component type must be re-enabled before a version can be defined.");

        var replay = db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(x => x.QualifiedId == definition.QualifiedId
                && x.ProfileId == compilation.ProfileId
                && x.SchemaHash == compilation.SchemaHash
                && x.SchemaJson == compilation.NormalizedSchema)
            .OrderBy(x => x.Version)
            .FirstOrDefault();
        if (replay is not null)
        {
            if (ownsTransaction) transaction!.Commit();
            return ToContract(replay, definition.Owner);
        }

        var nextVersion = db.Set<ComponentTypeVersionRecord>()
            .Where(x => x.QualifiedId == definition.QualifiedId)
            .Max(x => (int?)x.Version).GetValueOrDefault() + 1;
        var now = DateTime.UtcNow;
        if (existingType is null)
            db.Add(new ComponentTypeRecord
            {
                QualifiedId = definition.QualifiedId,
                ApplicationId = definition.Owner.Value,
                CreatedAtUtc = now
            });

        var version = new ComponentTypeVersionRecord
        {
            QualifiedId = definition.QualifiedId,
            Version = nextVersion,
            ProfileId = compilation.ProfileId,
            SchemaJson = compilation.NormalizedSchema,
            SchemaHash = compilation.SchemaHash,
            CreatedAtUtc = now
        };
        db.Add(version);
        db.SaveChanges();
        if (ownsTransaction) transaction!.Commit();
        return ToContract(version, definition.Owner);
    }

    public RegisteredComponentTypeVersion? Get(string qualifiedId, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedId);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        var row = db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .SingleOrDefault(x => x.QualifiedId == qualifiedId && x.Version == version);
        if (row is null) return null;
        var owner = db.Set<ComponentTypeRecord>().AsNoTracking()
            .Where(x => x.QualifiedId == qualifiedId).Select(x => x.ApplicationId).Single();
        return ToContract(row, ParseOwner(owner));
    }

    public RegisteredComponentTypeVersion? GetLatest(string qualifiedId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedId);
        var row = db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Join(db.Set<ComponentTypeRecord>().AsNoTracking(), version => version.QualifiedId, type => type.QualifiedId,
                (version, type) => new { version, type })
            .Where(value => value.version.QualifiedId == qualifiedId && value.type.DisabledAtUtc == null)
            .OrderByDescending(value => value.version.Version).Select(value => value.version).FirstOrDefault();
        return row is null ? null : ToContract(row, Owner(qualifiedId));
    }

    public RegisteredComponentTypeVersion? GetBySchemaHash(string qualifiedId, string profileId, string schemaHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedId);
        var row = db.Set<ComponentTypeVersionRecord>().AsNoTracking().SingleOrDefault(value =>
            value.QualifiedId == qualifiedId && value.ProfileId == profileId && value.SchemaHash == schemaHash);
        return row is null ? null : ToContract(row, Owner(qualifiedId));
    }

    private ApplicationIdentifier Owner(string qualifiedId) => ParseOwner(db.Set<ComponentTypeRecord>()
        .AsNoTracking().Where(value => value.QualifiedId == qualifiedId).Select(value => value.ApplicationId).Single());

    public ComponentTypeDiscoveryPage ListLatestPage(
        ApplicationIdentifier owner,
        string? afterQualifiedId,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateLimit(limit);
        if (!owner.IsSystem
            && !db.Set<ApplicationRegistryRecord>().AsNoTracking().Any(value => value.Id == owner.Value))
            throw new KeyNotFoundException("APPLICATION_UNKNOWN");
        var after = ValidateCursor(owner, afterQualifiedId);
        var typeIds = db.Set<ComponentTypeRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == owner.Value && value.DisabledAtUtc == null &&
                (after == null || string.Compare(value.QualifiedId, after) > 0))
            .OrderBy(value => value.QualifiedId)
            .Select(value => value.QualifiedId)
            .Take(limit + 1)
            .ToArray();
        var hasMore = typeIds.Length > limit;
        var pageIds = hasMore ? typeIds[..limit] : typeIds;
        var versions = db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(value => pageIds.Contains(value.QualifiedId))
            .OrderBy(value => value.QualifiedId)
            .ThenByDescending(value => value.Version)
            .AsEnumerable()
            .GroupBy(value => value.QualifiedId, StringComparer.Ordinal)
            .Select(group => ToContract(group.First(), owner))
            .ToArray();
        return new(Array.AsReadOnly(versions), hasMore ? pageIds[^1] : null);
    }

    private string? ValidateCursor(ApplicationIdentifier owner, string? afterQualifiedId)
    {
        if (afterQualifiedId is null) return null;
        ComponentTypeIdentifier.Validate(owner, afterQualifiedId);
        if (!db.Set<ComponentTypeRecord>().AsNoTracking().Any(value =>
                value.ApplicationId == owner.Value && value.QualifiedId == afterQualifiedId
                && value.DisabledAtUtc == null))
            throw new InvalidOperationException("CURSOR_STALE");
        return afterQualifiedId;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static RegisteredComponentTypeVersion ToContract(ComponentTypeVersionRecord row, ApplicationIdentifier owner) =>
        new(owner, row.QualifiedId, row.Version, row.ProfileId, row.SchemaJson, row.SchemaHash, row.CreatedAtUtc);

    private static ApplicationIdentifier ParseOwner(string value) =>
        value == ApplicationIdentifier.System.Value ? ApplicationIdentifier.System : ApplicationIdentifier.Parse(value);
}

internal sealed class ComponentTypeRecord
{
    public required string QualifiedId { get; set; }
    public required string ApplicationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
}

internal sealed class ComponentTypeVersionRecord
{
    public required string QualifiedId { get; set; }
    public int Version { get; set; }
    public required string ProfileId { get; set; }
    public required string SchemaJson { get; set; }
    public required string SchemaHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
