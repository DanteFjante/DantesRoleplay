using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

public sealed class SqliteStateSpaceRegistry(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications) : IStateSpaceRegistry
{
    public StateSpaceView Create(StateSpaceBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var application = applications.Get(binding.ApplicationRevision.ApplicationId);
        if (application is null || !SameRevision(application, binding.ApplicationRevision))
            throw new ArgumentException("A state space must bind to an exact registered application revision.", nameof(binding));

        using var transaction = db.Database.CurrentTransaction is null
            ? db.Database.BeginTransaction()
            : null;
        var existing = db.Set<ApplicationStateSpaceRecord>().SingleOrDefault(x => x.Id == binding.StateSpaceId);
        if (existing is not null)
        {
            var stored = ToView(existing, application);
            if (stored.ApplicationRevision != binding.ApplicationRevision || stored.ManifestFingerprint != binding.ManifestFingerprint)
                throw new InvalidOperationException("A state-space binding is immutable.");
            transaction?.Commit();
            return stored;
        }

        var now = DateTime.UtcNow;
        var row = new ApplicationStateSpaceRecord
        {
            Id = binding.StateSpaceId,
            ApplicationId = binding.ApplicationRevision.ApplicationId.Value,
            ApplicationRevision = binding.ApplicationRevision.Revision,
            ManifestFingerprint = binding.ManifestFingerprint,
            BindingRevision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Add(row);
        db.SaveChanges();
        transaction?.Commit();
        return ToView(row, application);
    }

    public StateSpaceView? Get(string stateSpaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        var row = db.Set<ApplicationStateSpaceRecord>().AsNoTracking().SingleOrDefault(x => x.Id == stateSpaceId);
        if (row is null) return null;
        var application = applications.Get(ApplicationIdentifier.Parse(row.ApplicationId));
        if (application is null || application.Revision != row.ApplicationRevision)
            throw new InvalidOperationException("The stored state space has no matching application revision.");
        return ToView(row, application);
    }

    public StateSpaceDiscoveryPage ListPage(
        ApplicationIdentifier applicationId,
        string? afterStateSpaceId,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ValidateLimit(limit);
        if (applications.Get(applicationId) is null)
            throw new KeyNotFoundException("APPLICATION_UNKNOWN");
        var after = ValidateStateSpaceCursor(applicationId, afterStateSpaceId);
        var rows = db.Set<ApplicationStateSpaceRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value &&
                (after == null || string.Compare(value.Id, after) > 0))
            .OrderBy(value => value.Id)
            .Take(limit + 1)
            .ToArray();
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        return new(
            Array.AsReadOnly(page.Select(value => ToView(
                value,
                applications.Get(applicationId) ?? throw new InvalidOperationException(
                    "The stored state space has no matching application revision."))).ToArray()),
            hasMore ? page[^1].Id : null);
    }

    private string? ValidateStateSpaceCursor(ApplicationIdentifier applicationId, string? afterStateSpaceId)
    {
        if (afterStateSpaceId is null) return null;
        ValidateBoundedId(afterStateSpaceId, nameof(afterStateSpaceId));
        if (!db.Set<ApplicationStateSpaceRecord>().AsNoTracking().Any(value =>
                value.ApplicationId == applicationId.Value && value.Id == afterStateSpaceId))
            throw new InvalidOperationException("CURSOR_STALE");
        return afterStateSpaceId;
    }

    private static StateSpaceView ToView(ApplicationStateSpaceRecord row, ApplicationRevision application) =>
        new(row.Id, application, row.ManifestFingerprint, row.BindingRevision, row.CreatedAtUtc,
            row.UpdatedAtUtc ?? row.CreatedAtUtc);

    private static bool SameRevision(ApplicationRevision left, ApplicationRevision right) =>
        left.ApplicationId == right.ApplicationId
        && left.Revision == right.Revision
        && left.Fingerprint == right.Fingerprint
        && left.BaseApplications.SequenceEqual(right.BaseApplications);

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static void ValidateBoundedId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("Discovery IDs are required and may not exceed 200 characters.", parameterName);
    }
}

public sealed class SqliteEntityComponentStore(
    DantesRoleplayDbContext db,
    IApplicationComponentTypeRegistry types,
    IBoundedJsonSchemaValidator validator) : IEntityComponentStore
{
    public async Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name, CancellationToken cancellationToken = default)
    {
        ValidateEntity(stateSpaceId, entityId, name);
        await RequireStateSpaceAsync(stateSpaceId, cancellationToken);
        if (await db.Set<ApplicationEcsEntityRecord>().AnyAsync(x => x.StateSpaceId == stateSpaceId && x.Id == entityId, cancellationToken))
            throw new InvalidOperationException("An entity ID is immutable within its state space.");

        var row = new ApplicationEcsEntityRecord
        {
            StateSpaceId = stateSpaceId,
            Id = entityId,
            Name = name.Trim(),
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    public async Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken = default)
    {
        ValidateEntityId(stateSpaceId, entityId);
        await RequireStateSpaceAsync(stateSpaceId, cancellationToken);
        var row = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StateSpaceId == stateSpaceId && x.Id == entityId && x.DeletedAtUtc == null, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<EcsEntityDiscoveryPage> ListEntitiesAsync(
        string stateSpaceId,
        string? afterEntityId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        ValidateBoundedId(stateSpaceId, nameof(stateSpaceId));
        ValidateLimit(limit);
        await RequireStateSpaceAsync(stateSpaceId, cancellationToken);
        var after = await ValidateEntityCursorAsync(stateSpaceId, afterEntityId, cancellationToken);
        var rows = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.DeletedAtUtc == null &&
                (after == null || string.Compare(value.Id, after) > 0))
            .OrderBy(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        return new(Array.AsReadOnly(page.Select(ToView).ToArray()), hasMore ? page[^1].Id : null);
    }

    public async Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateEntityId(stateSpaceId, entityId);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var row = await db.Set<ApplicationEcsEntityRecord>()
            .SingleOrDefaultAsync(x => x.StateSpaceId == stateSpaceId && x.Id == entityId && x.DeletedAtUtc == null, cancellationToken);
        if (row is null) return false;
        if (row.Revision != expectedRevision) throw new InvalidOperationException("The entity revision is stale.");
        row.DeletedAtUtc = DateTime.UtcNow;
        row.Revision++;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId, string qualifiedTypeId, CancellationToken cancellationToken = default)
    {
        ValidateEntityId(stateSpaceId, entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedTypeId);
        await RequireStateSpaceAsync(stateSpaceId, cancellationToken);
        var row = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Join(db.Set<ApplicationEcsEntityRecord>().AsNoTracking(), component => new { component.StateSpaceId, component.EntityId }, entity => new { entity.StateSpaceId, EntityId = entity.Id }, (component, entity) => new { component, entity })
            .Where(x => x.component.StateSpaceId == stateSpaceId && x.component.EntityId == entityId
                && x.component.QualifiedTypeId == qualifiedTypeId && x.entity.DeletedAtUtc == null)
            .Select(x => x.component)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId, IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        ArgumentNullException.ThrowIfNull(locators);
        if (locators.Count > 256) throw new ArgumentOutOfRangeException(nameof(locators), "A structural read may request at most 256 components.");
        foreach (var locator in locators) { ArgumentNullException.ThrowIfNull(locator); locator.Validate(); }
        await RequireStateSpaceAsync(stateSpaceId, cancellationToken);
        if (locators.Count == 0) return Array.Empty<EcsComponentView>();

        var requested = locators.Select(x => (x.EntityId, x.QualifiedTypeId)).ToHashSet();
        var entityIds = requested.Select(x => x.EntityId).Distinct().ToArray();
        var typeIds = requested.Select(x => x.QualifiedTypeId).Distinct().ToArray();
        var rows = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Join(db.Set<ApplicationEcsEntityRecord>().AsNoTracking(), component => new { component.StateSpaceId, component.EntityId }, entity => new { entity.StateSpaceId, EntityId = entity.Id }, (component, entity) => new { component, entity })
            .Where(x => x.component.StateSpaceId == stateSpaceId && x.entity.DeletedAtUtc == null
                && entityIds.Contains(x.component.EntityId) && typeIds.Contains(x.component.QualifiedTypeId))
            .Select(x => x.component).ToListAsync(cancellationToken);
        return rows.Where(x => requested.Contains((x.EntityId, x.QualifiedTypeId)))
            .OrderBy(x => x.EntityId, StringComparer.Ordinal).ThenBy(x => x.QualifiedTypeId, StringComparer.Ordinal)
            .Select(ToView).ToArray();
    }

    public async Task<EcsComponentDiscoveryPage> ListComponentsAsync(
        string stateSpaceId,
        string entityId,
        string? afterQualifiedTypeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityId(stateSpaceId, entityId);
        ValidateLimit(limit);
        await RequireLiveEntityAsync(stateSpaceId, entityId, cancellationToken);
        var after = await ValidateComponentCursorAsync(
            stateSpaceId, entityId, afterQualifiedTypeId, cancellationToken);
        var rows = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.EntityId == entityId &&
                (after == null || string.Compare(value.QualifiedTypeId, after) > 0))
            .OrderBy(value => value.QualifiedTypeId)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        return new(
            Array.AsReadOnly(page.Select(ToView).ToArray()),
            hasMore ? page[^1].QualifiedTypeId : null);
    }

    public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) =>
        WriteAsync(write, WriteMode.Add, cancellationToken);

    public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) =>
        WriteAsync(write, WriteMode.Set, cancellationToken);

    public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) =>
        WriteAsync(write, WriteMode.Merge, cancellationToken);

    public async Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateEntityId(stateSpaceId, entityId);
        ArgumentNullException.ThrowIfNull(type);
        type.Validate();
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        await RequireLiveEntityAsync(stateSpaceId, entityId, cancellationToken);
        var row = await db.Set<ApplicationEcsComponentRecord>().SingleOrDefaultAsync(x =>
            x.StateSpaceId == stateSpaceId && x.EntityId == entityId && x.QualifiedTypeId == type.QualifiedTypeId, cancellationToken);
        if (row is null) return false;
        if (row.TypeVersion != type.TypeVersion || row.SchemaHash != type.SchemaHash || row.Revision != expectedRevision)
            throw new InvalidOperationException("The component contract or revision is stale.");
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<EcsComponentView> WriteAsync(EcsComponentWrite write, WriteMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        write.Validate();
        var stateSpace = await RequireStateSpaceAsync(write.StateSpaceId, cancellationToken);
        var type = types.Get(write.Type.QualifiedTypeId, write.Type.TypeVersion);
        var exactBaseOwner = type is not null && await db.Set<ApplicationRevisionBaseRecord>()
            .AsNoTracking()
            .AnyAsync(value => value.ApplicationId == stateSpace.ApplicationId
                && value.Revision == stateSpace.ApplicationRevision
                && value.BaseApplicationId == type.Owner.Value, cancellationToken);
        if (type is null
            || (type.Owner.Value != stateSpace.ApplicationId && !exactBaseOwner)
            || type.SchemaHash != write.Type.SchemaHash)
            throw new InvalidOperationException("The component type is unknown, stale, or outside this state space's application.");

        var validated = mode == WriteMode.Merge
            ? validator.Validate(SystemJsonSchemaProfile.Version1Id, "true", write.ValueJson)
            : validator.Validate(type.ProfileId, type.SchemaJson, write.ValueJson);
        if (validated.Status != SchemaValueStatus.Valid)
            throw new ArgumentException("The component value does not satisfy its exact bounded schema.", nameof(write));
        var incoming = RootJson(write.ValueJson);
        await RequireLiveEntityAsync(write.StateSpaceId, write.EntityId, cancellationToken);
        var existing = await db.Set<ApplicationEcsComponentRecord>().SingleOrDefaultAsync(x =>
            x.StateSpaceId == write.StateSpaceId && x.EntityId == write.EntityId && x.QualifiedTypeId == write.Type.QualifiedTypeId, cancellationToken);

        if (mode == WriteMode.Add && existing is not null)
            throw new InvalidOperationException("The component already exists.");
        if (existing is null && (write.ExpectedRevision != 0 || mode == WriteMode.Merge))
            throw new InvalidOperationException("The component is absent or its revision is stale.");
        if (existing is not null && existing.Revision != write.ExpectedRevision)
            throw new InvalidOperationException("The component revision is stale.");

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            existing = new ApplicationEcsComponentRecord
            {
                StateSpaceId = write.StateSpaceId,
                EntityId = write.EntityId,
                QualifiedTypeId = write.Type.QualifiedTypeId,
                TypeVersion = write.Type.TypeVersion,
                SchemaHash = write.Type.SchemaHash,
                Data = incoming,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(existing);
        }
        else
        {
            if (existing.TypeVersion != write.Type.TypeVersion || existing.SchemaHash != write.Type.SchemaHash)
                throw new InvalidOperationException("A component cannot change its immutable type contract in place.");
            if (mode == WriteMode.Merge)
            {
                var merged = Merge(existing.Data, incoming);
                var mergedValidation = validator.Validate(type.ProfileId, type.SchemaJson, merged);
                if (mergedValidation.Status != SchemaValueStatus.Valid)
                    throw new ArgumentException("The merged component value does not satisfy its exact bounded schema.", nameof(write));
                incoming = merged;
            }
            existing.Data = incoming;
            existing.Revision++;
            existing.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToView(existing);
    }

    private async Task<ApplicationStateSpaceRecord> RequireStateSpaceAsync(string stateSpaceId, CancellationToken cancellationToken) =>
        await db.Set<ApplicationStateSpaceRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == stateSpaceId, cancellationToken)
        ?? throw new InvalidOperationException("Unknown state space.");

    private async Task RequireLiveEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken)
    {
        if (!await db.Set<ApplicationEcsEntityRecord>().AnyAsync(x => x.StateSpaceId == stateSpaceId && x.Id == entityId && x.DeletedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("Unknown or deleted entity in this state space.");
    }

    private static string RootJson(string valueJson)
    {
        using var document = JsonDocument.Parse(valueJson, new JsonDocumentOptions { MaxDepth = SystemJsonSchemaProfile.MaximumValueDepth });
        return document.RootElement.GetRawText();
    }

    private static string Merge(string existingJson, string incomingJson)
    {
        var existing = JsonNode.Parse(existingJson) as JsonObject;
        var incoming = JsonNode.Parse(incomingJson) as JsonObject;
        if (existing is null || incoming is null)
            throw new ArgumentException("Component merge requires existing and supplied object values.");
        foreach (var property in incoming)
            existing[property.Key] = property.Value?.DeepClone();
        return existing.ToJsonString();
    }

    private static void ValidateEntity(string stateSpaceId, string entityId, string name)
    {
        ValidateEntityId(stateSpaceId, entityId);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 400)
            throw new ArgumentException("An entity name is required and may not exceed 400 characters.", nameof(name));
    }

    private static void ValidateEntityId(string stateSpaceId, string entityId)
    {
        if (string.IsNullOrWhiteSpace(stateSpaceId) || stateSpaceId.Length > 200
            || string.IsNullOrWhiteSpace(entityId) || entityId.Length > 200)
            throw new ArgumentException("State-space and entity IDs are required and may not exceed 200 characters.");
    }

    private async Task<string?> ValidateEntityCursorAsync(
        string stateSpaceId,
        string? afterEntityId,
        CancellationToken cancellationToken)
    {
        if (afterEntityId is null) return null;
        ValidateBoundedId(afterEntityId, nameof(afterEntityId));
        if (!await db.Set<ApplicationEcsEntityRecord>().AsNoTracking().AnyAsync(value =>
                value.StateSpaceId == stateSpaceId && value.Id == afterEntityId &&
                value.DeletedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("CURSOR_STALE");
        return afterEntityId;
    }

    private async Task<string?> ValidateComponentCursorAsync(
        string stateSpaceId,
        string entityId,
        string? afterQualifiedTypeId,
        CancellationToken cancellationToken)
    {
        if (afterQualifiedTypeId is null) return null;
        ValidateBoundedId(afterQualifiedTypeId, nameof(afterQualifiedTypeId));
        if (!await db.Set<ApplicationEcsComponentRecord>().AsNoTracking().AnyAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId &&
                value.QualifiedTypeId == afterQualifiedTypeId, cancellationToken))
            throw new InvalidOperationException("CURSOR_STALE");
        return afterQualifiedTypeId;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static void ValidateBoundedId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("Discovery IDs are required and may not exceed 200 characters.", parameterName);
    }

    private static EcsEntityView ToView(ApplicationEcsEntityRecord value) =>
        new(value.StateSpaceId, value.Id, value.Name, value.Revision, value.CreatedAtUtc, value.DeletedAtUtc);

    private static EcsComponentView ToView(ApplicationEcsComponentRecord value) =>
        new(value.StateSpaceId, value.EntityId,
            new EcsComponentReference(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash),
            value.Data, value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);

    private enum WriteMode { Add, Set, Merge }
}

internal sealed class ApplicationStateSpaceRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ManifestFingerprint { get; set; }
    public int BindingRevision { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

internal sealed class ApplicationEcsEntityRecord
{
    public required string StateSpaceId { get; set; }
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

internal sealed class ApplicationEcsComponentRecord
{
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public required string QualifiedTypeId { get; set; }
    public int TypeVersion { get; set; }
    public required string SchemaHash { get; set; }
    public required string Data { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
