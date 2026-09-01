using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.TriggerScheduling;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

public sealed class SqliteEcsLifecycleStore(
    DantesRoleplayDbContext db,
    IEcsRoleConstraintValidator? constraints = null,
    IBoundedJsonSchemaValidator? schemas = null) : IEcsLifecycleStore
{
    private readonly IBoundedJsonSchemaValidator _schemas = schemas ?? new BoundedJsonSchemaValidator();
    public async Task<EcsEntityDiscoveryPage> ListEntitiesIncludingDisabledAsync(
        string stateSpaceId,
        string? afterEntityId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterEntityId is not null) ValidateId(afterEntityId, nameof(afterEntityId));

        var rows = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId
                && (afterEntityId == null || string.Compare(value.Id, afterEntityId) > 0))
            .OrderBy(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        var values = page.Select(value => new EcsEntityView(
            value.StateSpaceId, value.Id, value.Name, value.Revision, value.CreatedAtUtc, value.DeletedAtUtc)).ToArray();
        return new(Array.AsReadOnly(values), hasMore ? values[^1].EntityId : null);
    }

    public async Task<EcsComponentView?> GetComponentIncludingDisabledAsync(
        string stateSpaceId,
        string entityId,
        string qualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        ValidateId(qualifiedTypeId, nameof(qualifiedTypeId));
        var row = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.StateSpaceId == stateSpaceId
                && value.EntityId == entityId
                && value.QualifiedTypeId == qualifiedTypeId, cancellationToken);
        return row is null ? null : new EcsComponentView(
            row.StateSpaceId,
            row.EntityId,
            new EcsComponentReference(row.QualifiedTypeId, row.TypeVersion, row.SchemaHash),
            row.Data,
            row.Revision,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    public async Task<ComponentTypeLifecycleView?> GetComponentTypeAsync(
        string qualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(qualifiedTypeId, nameof(qualifiedTypeId));
        var row = await db.Set<ComponentTypeRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.QualifiedId == qualifiedTypeId, cancellationToken);
        return row is null ? null : await ComponentTypeViewAsync(row, cancellationToken);
    }

    public async Task<RelationshipKindLifecycleView> GetRelationshipKindAsync(
        string qualifiedKind,
        CancellationToken cancellationToken = default)
    {
        ValidateId(qualifiedKind, nameof(qualifiedKind));
        _ = QualifiedOwner(qualifiedKind);
        var stateSpaces = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.QualifiedKind == qualifiedKind)
            .Select(value => value.StateSpaceId)
            .Distinct()
            .OrderBy(value => value)
            .ToArrayAsync(cancellationToken);
        var count = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .CountAsync(value => value.QualifiedKind == qualifiedKind, cancellationToken);
        return new(qualifiedKind, count, Array.AsReadOnly(stateSpaces));
    }

    public async Task<ComponentTypeLifecycleView> RenameComponentTypeAsync(
        string qualifiedTypeId,
        string correctedQualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(qualifiedTypeId, nameof(qualifiedTypeId));
        ValidateId(correctedQualifiedTypeId, nameof(correctedQualifiedTypeId));
        var row = await RequireComponentTypeAsync(qualifiedTypeId, cancellationToken);
        var owner = ApplicationIdentifier.Parse(row.ApplicationId);
        ComponentTypeIdentifier.Validate(owner, correctedQualifiedTypeId);
        if (qualifiedTypeId == correctedQualifiedTypeId)
            return await ComponentTypeViewAsync(row, cancellationToken);
        if (await db.Set<ComponentTypeRecord>().AnyAsync(
                value => value.QualifiedId == correctedQualifiedTypeId, cancellationToken))
            throw Error("COMPONENT_TYPE_ID_EXISTS", "The corrected component-type ID already exists.");

        var references = await ComponentTypeReferencesAsync(qualifiedTypeId, cancellationToken);
        var immutableReferences = references
            .Where(value => value.Kind != "components").ToArray();
        RequireUnused("COMPONENT_TYPE_IN_USE",
            "The component type is used by immutable definitions that cannot be silently rewritten.",
            immutableReferences);
        var hasComponents = references.Any(value => value.Kind == "components");
        var versions = await db.Set<ComponentTypeVersionRecord>()
            .Where(value => value.QualifiedId == qualifiedTypeId)
            .OrderBy(value => value.Version)
            .ToArrayAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replacement = new ComponentTypeRecord
            {
                QualifiedId = correctedQualifiedTypeId,
                ApplicationId = row.ApplicationId,
                CreatedAtUtc = row.CreatedAtUtc,
                DisabledAtUtc = row.DisabledAtUtc
            };
            db.Add(replacement);
            db.AddRange(versions.Select(value => new ComponentTypeVersionRecord
            {
                QualifiedId = correctedQualifiedTypeId,
                Version = value.Version,
                ProfileId = value.ProfileId,
                SchemaJson = value.SchemaJson,
                SchemaHash = value.SchemaHash,
                CreatedAtUtc = value.CreatedAtUtc
            }));
            await db.SaveChangesAsync(cancellationToken);
            if (hasComponents)
            {
                await db.Set<ApplicationEcsComponentRecord>()
                    .Where(value => value.QualifiedTypeId == qualifiedTypeId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.QualifiedTypeId, correctedQualifiedTypeId),
                        cancellationToken);
                row.DisabledAtUtc ??= DateTime.UtcNow;
            }
            else
            {
                db.RemoveRange(versions);
                db.Remove(row);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ComponentTypeViewAsync(replacement, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ComponentTypeMigrationResult> MigrateComponentTypeAsync(
        string sourceQualifiedTypeId,
        string targetQualifiedTypeId,
        IReadOnlyList<EcsComponentMigrationValue>? rewrittenValues = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(sourceQualifiedTypeId, nameof(sourceQualifiedTypeId));
        ValidateId(targetQualifiedTypeId, nameof(targetQualifiedTypeId));
        if (sourceQualifiedTypeId == targetQualifiedTypeId)
            throw Error("COMPONENT_TYPE_MIGRATION_SAME_ID", "Source and target component types must differ.");

        var source = await RequireComponentTypeAsync(sourceQualifiedTypeId, cancellationToken);
        var target = await RequireComponentTypeAsync(targetQualifiedTypeId, cancellationToken);
        if (target.DisabledAtUtc is not null)
            throw Error("COMPONENT_TYPE_TARGET_DISABLED", "The target component type is disabled.");

        var immutableReferences = (await ComponentTypeReferencesAsync(sourceQualifiedTypeId, cancellationToken))
            .Where(value => value.Kind != "components").ToArray();
        RequireUnused("COMPONENT_TYPE_IN_USE",
            "The component type is used by immutable definitions that cannot be silently rewritten.",
            immutableReferences);

        var targetVersion = await db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(value => value.QualifiedId == targetQualifiedTypeId)
            .OrderByDescending(value => value.Version)
            .FirstAsync(cancellationToken);
        Dictionary<(string StateSpaceId, string EntityId), EcsComponentMigrationValue> replacements;
        try
        {
            replacements = (rewrittenValues ?? []).ToDictionary(
                value => (value.StateSpaceId, value.EntityId));
        }
        catch (ArgumentException)
        {
            throw Error("COMPONENT_TYPE_MIGRATION_VALUE_DUPLICATE",
                "Each rewritten component value must identify a unique state-space entity.");
        }

        var components = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.QualifiedTypeId == sourceQualifiedTypeId)
            .OrderBy(value => value.StateSpaceId).ThenBy(value => value.EntityId)
            .ToArrayAsync(cancellationToken);
        var componentKeys = components.Select(value => (value.StateSpaceId, value.EntityId)).ToHashSet();
        if (replacements.Keys.Any(value => !componentKeys.Contains(value)))
            throw Error("COMPONENT_TYPE_MIGRATION_VALUE_UNKNOWN",
                "A rewritten component value does not identify a source component.");
        var collisions = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.QualifiedTypeId == targetQualifiedTypeId
                && components.Select(component => component.StateSpaceId).Contains(value.StateSpaceId)
                && components.Select(component => component.EntityId).Contains(value.EntityId))
            .Select(value => new { value.StateSpaceId, value.EntityId })
            .ToArrayAsync(cancellationToken);
        if (collisions.Any(value => componentKeys.Contains((value.StateSpaceId, value.EntityId))))
            throw Error("COMPONENT_TYPE_TARGET_EXISTS",
                "An entity already contains the target component type.");

        foreach (var component in components)
        {
            var key = (component.StateSpaceId, component.EntityId);
            var value = component.Data;
            if (replacements.TryGetValue(key, out var replacement))
            {
                if (replacement.ExpectedRevision < 1 || replacement.ExpectedRevision != component.Revision)
                    throw Error("COMPONENT_TYPE_MIGRATION_VALUE_STALE",
                        "A rewritten component value has a stale expected revision.");
                value = replacement.ValueJson;
            }
            var validation = _schemas.Validate(targetVersion.ProfileId, targetVersion.SchemaJson, value);
            if (validation.Status != SchemaValueStatus.Valid)
                throw Error("COMPONENT_TYPE_MIGRATION_VALUE_INVALID",
                    $"Component '{component.StateSpaceId}/{component.EntityId}' does not satisfy the target schema.");
        }

        var affectedStateSpaces = components.Select(value => value.StateSpaceId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            foreach (var component in components)
            {
                var value = replacements.TryGetValue(
                    (component.StateSpaceId, component.EntityId), out var replacement)
                    ? replacement.ValueJson : component.Data;
                var changed = await db.Set<ApplicationEcsComponentRecord>()
                    .Where(candidate => candidate.StateSpaceId == component.StateSpaceId
                        && candidate.EntityId == component.EntityId
                        && candidate.QualifiedTypeId == sourceQualifiedTypeId
                        && candidate.Revision == component.Revision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.QualifiedTypeId, targetQualifiedTypeId)
                        .SetProperty(candidate => candidate.TypeVersion, targetVersion.Version)
                        .SetProperty(candidate => candidate.SchemaHash, targetVersion.SchemaHash)
                        .SetProperty(candidate => candidate.Data, value)
                        .SetProperty(candidate => candidate.Revision, component.Revision + 1)
                        .SetProperty(candidate => candidate.UpdatedAtUtc, now), cancellationToken);
                if (changed != 1)
                    throw Error("COMPONENT_TYPE_MIGRATION_VALUE_STALE",
                        "A source component changed after migration validation.");
            }
            source.DisabledAtUtc ??= now;
            await db.SaveChangesAsync(cancellationToken);
            if (constraints is not null)
                foreach (var stateSpaceId in affectedStateSpaces)
                    await constraints.ValidateStateSpaceAsync(stateSpaceId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(sourceQualifiedTypeId, targetQualifiedTypeId, components.Length,
                replacements.Count, Array.AsReadOnly(affectedStateSpaces),
                await ComponentTypeViewAsync(target, cancellationToken));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ComponentTypeLifecycleView> SetComponentTypeEnabledAsync(
        string qualifiedTypeId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var row = await RequireComponentTypeAsync(qualifiedTypeId, cancellationToken);
        row.DisabledAtUtc = enabled ? null : row.DisabledAtUtc ?? DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await ComponentTypeViewAsync(row, cancellationToken);
    }

    public async Task<RelationshipKindMigrationResult> MigrateRelationshipKindAsync(
        string sourceQualifiedKind,
        string targetQualifiedKind,
        CancellationToken cancellationToken = default)
    {
        ValidateId(sourceQualifiedKind, nameof(sourceQualifiedKind));
        ValidateId(targetQualifiedKind, nameof(targetQualifiedKind));
        if (sourceQualifiedKind == targetQualifiedKind)
            throw Error("RELATIONSHIP_KIND_MIGRATION_SAME_ID",
                "Source and target relationship kinds must differ.");

        var relationships = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.QualifiedKind == sourceQualifiedKind)
            .OrderBy(value => value.StateSpaceId)
            .ThenBy(value => value.FromEntityId)
            .ThenBy(value => value.ToEntityId)
            .ToArrayAsync(cancellationToken);
        var affectedStateSpaces = relationships.Select(value => value.StateSpaceId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var stateSpaceId in affectedStateSpaces)
            await RequireRelationshipKindAllowedAsync(stateSpaceId, targetQualifiedKind, cancellationToken);

        var sourceKeys = relationships.Select(value =>
            (value.StateSpaceId, value.FromEntityId, value.ToEntityId)).ToHashSet();
        var collisions = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.QualifiedKind == targetQualifiedKind
                && affectedStateSpaces.Contains(value.StateSpaceId))
            .Select(value => new { value.StateSpaceId, value.FromEntityId, value.ToEntityId })
            .ToArrayAsync(cancellationToken);
        if (collisions.Any(value => sourceKeys.Contains(
                (value.StateSpaceId, value.FromEntityId, value.ToEntityId))))
            throw Error("RELATIONSHIP_KIND_TARGET_EXISTS",
                "A relationship with the target kind already exists for the same endpoints.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            foreach (var relationship in relationships)
            {
                var changed = await db.Set<ApplicationEcsRelationshipRecord>()
                    .Where(value => value.StateSpaceId == relationship.StateSpaceId
                        && value.FromEntityId == relationship.FromEntityId
                        && value.ToEntityId == relationship.ToEntityId
                        && value.QualifiedKind == sourceQualifiedKind
                        && value.Revision == relationship.Revision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.QualifiedKind, targetQualifiedKind)
                        .SetProperty(value => value.Revision, relationship.Revision + 1)
                        .SetProperty(value => value.UpdatedAtUtc, now), cancellationToken);
                if (changed != 1)
                    throw Error("RELATIONSHIP_KIND_MIGRATION_STALE",
                        "A source relationship changed after migration validation.");
            }
            if (constraints is not null)
                foreach (var stateSpaceId in affectedStateSpaces)
                    await constraints.ValidateStateSpaceAsync(stateSpaceId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(sourceQualifiedKind, targetQualifiedKind, relationships.Length,
                Array.AsReadOnly(affectedStateSpaces));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> DeleteComponentTypeAsync(
        string qualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(qualifiedTypeId, nameof(qualifiedTypeId));
        var row = await db.Set<ComponentTypeRecord>()
            .SingleOrDefaultAsync(value => value.QualifiedId == qualifiedTypeId, cancellationToken);
        if (row is null) return false;
        if (row.DisabledAtUtc is null)
            throw Error("COMPONENT_TYPE_ENABLED", "Disable the component type before deleting it permanently.");
        var references = await ComponentTypeReferencesAsync(qualifiedTypeId, cancellationToken);
        RequireUnused("COMPONENT_TYPE_IN_USE", "The disabled component type is still referenced.", references);
        var versions = await db.Set<ComponentTypeVersionRecord>()
            .Where(value => value.QualifiedId == qualifiedTypeId).ToArrayAsync(cancellationToken);
        db.RemoveRange(versions);
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EntityLifecycleView?> GetEntityAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        var row = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.Id == entityId, cancellationToken);
        return row is null ? null : await EntityViewAsync(row, cancellationToken);
    }

    public async Task<EntityLifecycleView> UpdateEntityAsync(
        string stateSpaceId,
        string entityId,
        string correctedEntityId,
        string name,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        ValidateEntityIds(stateSpaceId, correctedEntityId);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 400)
            throw new ArgumentException("An entity name is required and may not exceed 400 characters.", nameof(name));
        var row = await RequireEntityAsync(stateSpaceId, entityId, cancellationToken);
        RequireRevision(row, expectedRevision);

        if (correctedEntityId == entityId)
        {
            row.Name = name.Trim();
            row.Revision++;
            await db.SaveChangesAsync(cancellationToken);
            return await EntityViewAsync(row, cancellationToken);
        }

        if (await db.Set<ApplicationEcsEntityRecord>().AnyAsync(
                value => value.StateSpaceId == stateSpaceId && value.Id == correctedEntityId,
                cancellationToken))
            throw Error("ENTITY_ID_EXISTS", "The corrected entity ID already exists in this state space.");
        var references = await EntityReferencesAsync(stateSpaceId, entityId, cancellationToken);
        RequireUnused("ENTITY_IN_USE", "An entity ID can only be renamed while the entity is unused.", references);

        var replacement = new ApplicationEcsEntityRecord
        {
            StateSpaceId = stateSpaceId,
            Id = correctedEntityId,
            Name = name.Trim(),
            Revision = row.Revision + 1,
            CreatedAtUtc = row.CreatedAtUtc,
            DeletedAtUtc = row.DeletedAtUtc
        };
        db.Add(replacement);
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return await EntityViewAsync(replacement, cancellationToken);
    }

    public async Task<EntityLifecycleView> SetEntityEnabledAsync(
        string stateSpaceId,
        string entityId,
        bool enabled,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var transaction = await SqliteEcsConstraintTransaction.BeginIfNeededAsync(db, cancellationToken);
        try
        {
            var row = await RequireEntityAsync(stateSpaceId, entityId, cancellationToken);
            RequireRevision(row, expectedRevision);
            if ((row.DeletedAtUtc is null) == enabled)
            {
                if (enabled && constraints is not null)
                    await constraints.ValidateStateSpaceAsync(stateSpaceId, cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return await EntityViewAsync(row, cancellationToken);
            }
            row.DeletedAtUtc = enabled ? null : row.DeletedAtUtc ?? DateTime.UtcNow;
            row.Revision++;
            await db.SaveChangesAsync(cancellationToken);
            if (constraints is not null)
                await constraints.ValidateStateSpaceAsync(stateSpaceId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return await EntityViewAsync(row, cancellationToken);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<bool> DeleteEntityPermanentlyAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        var row = await db.Set<ApplicationEcsEntityRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.Id == entityId, cancellationToken);
        if (row is null) return false;
        if (row.DeletedAtUtc is null)
            throw Error("ENTITY_ENABLED", "Disable the entity before deleting it permanently.");
        var references = await EntityReferencesAsync(stateSpaceId, entityId, cancellationToken);
        RequireUnused("ENTITY_IN_USE", "The disabled entity is still referenced.", references);
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteEntityAndComponentsPermanentlyAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        var transaction = await SqliteEcsConstraintTransaction.BeginIfNeededAsync(db, cancellationToken);
        try
        {
            var row = await db.Set<ApplicationEcsEntityRecord>().SingleOrDefaultAsync(
                value => value.StateSpaceId == stateSpaceId && value.Id == entityId, cancellationToken);
            if (row is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return false;
            }
            if (row.DeletedAtUtc is null)
                throw Error("ENTITY_ENABLED", "Disable the entity before deleting it permanently.");
            var references = await EntityReferencesAsync(stateSpaceId, entityId, cancellationToken);
            var external = references.Where(value => value.Kind != "components").ToArray();
            RequireUnused("ENTITY_IN_USE", "The disabled entity is still externally referenced.", external);
            var components = await db.Set<ApplicationEcsComponentRecord>().Where(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId).ToListAsync(cancellationToken);
            db.RemoveRange(components);
            db.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            if (constraints is not null)
                await constraints.ValidateStateSpaceAsync(stateSpaceId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<ComponentTypeRecord> RequireComponentTypeAsync(string qualifiedTypeId, CancellationToken cancellationToken)
    {
        ValidateId(qualifiedTypeId, nameof(qualifiedTypeId));
        return await db.Set<ComponentTypeRecord>().SingleOrDefaultAsync(
            value => value.QualifiedId == qualifiedTypeId, cancellationToken)
            ?? throw Error("COMPONENT_TYPE_UNKNOWN", "The component type does not exist.");
    }

    private async Task<ApplicationEcsEntityRecord> RequireEntityAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken)
    {
        ValidateEntityIds(stateSpaceId, entityId);
        return await db.Set<ApplicationEcsEntityRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.Id == entityId, cancellationToken)
            ?? throw Error("ENTITY_UNKNOWN", "The entity does not exist in this state space.");
    }

    private async Task RequireRelationshipKindAllowedAsync(
        string stateSpaceId,
        string qualifiedKind,
        CancellationToken cancellationToken)
    {
        var owner = QualifiedOwner(qualifiedKind);
        var stateSpace = await db.Set<ApplicationStateSpaceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == stateSpaceId, cancellationToken)
            ?? throw Error("STATE_SPACE_UNKNOWN", "The relationship state space does not exist.");
        var allowed = owner.IsSystem || owner.Value == stateSpace.ApplicationId
            || await db.Set<ApplicationRevisionBaseRecord>().AsNoTracking().AnyAsync(value =>
                value.ApplicationId == stateSpace.ApplicationId
                && value.BaseApplicationId == owner.Value, cancellationToken);
        if (!allowed)
            throw Error("RELATIONSHIP_KIND_OUTSIDE_APPLICATION",
                "The target relationship kind is not owned by this state space's application or any reviewed base revision.");
    }

    private static ApplicationIdentifier QualifiedOwner(string qualifiedId)
    {
        var separator = qualifiedId.IndexOf('.');
        if (separator <= 0)
            throw new ArgumentException("A relationship kind must be a qualified ID.", nameof(qualifiedId));
        var ownerText = qualifiedId[..separator];
        var owner = ownerText == ApplicationIdentifier.System.Value
            ? ApplicationIdentifier.System
            : ApplicationIdentifier.Parse(ownerText);
        ComponentTypeIdentifier.Validate(owner, qualifiedId);
        return owner;
    }

    private async Task<ComponentTypeLifecycleView> ComponentTypeViewAsync(
        ComponentTypeRecord row,
        CancellationToken cancellationToken)
    {
        var latest = await db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(value => value.QualifiedId == row.QualifiedId)
            .MaxAsync(value => value.Version, cancellationToken);
        return new(
            ApplicationIdentifier.Parse(row.ApplicationId),
            row.QualifiedId,
            latest,
            row.CreatedAtUtc,
            row.DisabledAtUtc,
            await ComponentTypeReferencesAsync(row.QualifiedId, cancellationToken));
    }

    private async Task<EntityLifecycleView> EntityViewAsync(
        ApplicationEcsEntityRecord row,
        CancellationToken cancellationToken) =>
        new(
            new(row.StateSpaceId, row.Id, row.Name, row.Revision, row.CreatedAtUtc, row.DeletedAtUtc),
            await EntityReferencesAsync(row.StateSpaceId, row.Id, cancellationToken));

    private async Task<IReadOnlyList<EcsReferenceCount>> ComponentTypeReferencesAsync(
        string qualifiedTypeId,
        CancellationToken cancellationToken) =>
        Counts(
            ("components", await db.Set<ApplicationEcsComponentRecord>().CountAsync(
                value => value.QualifiedTypeId == qualifiedTypeId, cancellationToken)),
            ("projection-inputs", await db.Set<ProjectionComponentInputRecord>().CountAsync(
                value => value.QualifiedTypeId == qualifiedTypeId, cancellationToken)),
            ("conditional-trigger-dependencies", await db.Set<ConditionalTriggerDependencyRecord>().CountAsync(
                value => value.QualifiedTypeId == qualifiedTypeId, cancellationToken)));

    private async Task<IReadOnlyList<EcsReferenceCount>> EntityReferencesAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken) =>
        Counts(
            ("components", await db.Set<ApplicationEcsComponentRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)),
            ("containments", await db.Set<ApplicationEcsContainmentRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && (value.ContainedEntityId == entityId || value.ContainerEntityId == entityId), cancellationToken)),
            ("relationships", await db.Set<ApplicationEcsRelationshipRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && (value.FromEntityId == entityId || value.ToEntityId == entityId), cancellationToken)),
            ("one-time-trigger-targets", await db.Set<OneTimeTriggerNotificationEntityRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)),
            ("recurring-trigger-targets", await db.Set<RecurringTriggerNotificationEntityRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)),
            ("conditional-trigger-dependencies", await db.Set<ConditionalTriggerDependencyRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)),
            ("conditional-trigger-targets", await db.Set<ConditionalTriggerNotificationEntityRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)),
            ("observation-trigger-targets", await db.Set<ObservationTriggerNotificationEntityRecord>().CountAsync(value =>
                value.StateSpaceId == stateSpaceId && value.EntityId == entityId, cancellationToken)));

    private static IReadOnlyList<EcsReferenceCount> Counts(params (string Kind, int Count)[] values) =>
        Array.AsReadOnly(values.Where(value => value.Count > 0)
            .Select(value => new EcsReferenceCount(value.Kind, value.Count)).ToArray());

    private static void RequireUnused(string code, string message, IReadOnlyList<EcsReferenceCount> references)
    {
        if (references.Count == 0) return;
        throw Error(code, $"{message} Blockers: {string.Join(", ", references.Select(value => $"{value.Kind}={value.Count}"))}.");
    }

    private static void RequireRevision(ApplicationEcsEntityRecord row, int expectedRevision)
    {
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (row.Revision != expectedRevision) throw Error("ENTITY_STALE", "The entity revision is stale.");
    }

    private static void ValidateEntityIds(string stateSpaceId, string entityId)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(entityId, nameof(entityId));
    }

    private static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("A bounded ID is required.", parameterName);
    }

    private static EcsLifecycleException Error(string code, string message) => new(code, message);
}
