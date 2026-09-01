using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

public sealed class SqliteStateSpaceEdgeStore(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces) : IStateSpaceEdgeStore
{
    public async Task<EcsContainmentView?> GetContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(containedEntityId, nameof(containedEntityId));
        var row = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.ContainedEntityId == containedEntityId)
            .Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainedEntityId && entity.DeletedAtUtc == null)
                && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainerEntityId && entity.DeletedAtUtc == null))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : View(row);
    }

    public async Task<IReadOnlyList<EcsContainmentView>> ListContainmentsAsync(
        string stateSpaceId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        RequireStateSpace(stateSpaceId);
        var rows = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId)
            .Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainedEntityId && entity.DeletedAtUtc == null)
                && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainerEntityId && entity.DeletedAtUtc == null))
            .OrderBy(value => value.ContainedEntityId)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows.Select(View).ToArray());
    }

    public async Task<EcsContainmentDiscoveryPage> ListContainmentsAsync(
        string stateSpaceId,
        string containerEntityId,
        string? afterContainedEntityId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(containerEntityId, nameof(containerEntityId));
        ValidateLimit(limit);
        await RequireEntityAsync(stateSpaceId, containerEntityId, cancellationToken);

        string? after = null;
        if (!string.IsNullOrWhiteSpace(afterContainedEntityId))
        {
            ValidateId(afterContainedEntityId, nameof(afterContainedEntityId));
            var cursorExists = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking().AnyAsync(
                value => value.StateSpaceId == stateSpaceId
                    && value.ContainerEntityId == containerEntityId
                    && value.ContainedEntityId == afterContainedEntityId
                    && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                        entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainedEntityId
                        && entity.DeletedAtUtc == null),
                cancellationToken);
            if (!cursorExists) throw new InvalidOperationException("CURSOR_STALE");
            after = afterContainedEntityId;
        }

        var rows = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId
                && value.ContainerEntityId == containerEntityId
                && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ContainedEntityId && entity.DeletedAtUtc == null)
                && (after == null || string.Compare(value.ContainedEntityId, after) > 0))
            .OrderBy(value => value.ContainedEntityId)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var page = hasMore ? rows[..limit] : rows;
        return new(
            Array.AsReadOnly(page.Select(View).ToArray()),
            hasMore ? page[^1].ContainedEntityId : null);
    }

    public async Task<EcsContainmentView> MoveContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        string containerEntityId,
        string slot,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(containedEntityId, nameof(containedEntityId));
        ValidateId(containerEntityId, nameof(containerEntityId));
        if (containedEntityId == containerEntityId)
            throw new ArgumentException("An entity cannot contain itself.");
        if (slot is null || slot.Length > 100)
            throw new ArgumentException("A containment slot may not exceed 100 characters.", nameof(slot));
        await RequireEntityAsync(stateSpaceId, containedEntityId, cancellationToken);
        await RequireEntityAsync(stateSpaceId, containerEntityId, cancellationToken);
        await RejectContainmentCycleAsync(stateSpaceId, containedEntityId, containerEntityId, cancellationToken);

        var row = await db.Set<ApplicationEcsContainmentRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.ContainedEntityId == containedEntityId,
            cancellationToken);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            if (expectedRevision != 0)
                throw new InvalidOperationException("The containment revision is stale.");
            row = new ApplicationEcsContainmentRecord
            {
                StateSpaceId = stateSpaceId,
                ContainedEntityId = containedEntityId,
                ContainerEntityId = containerEntityId,
                Slot = slot,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(row);
        }
        else
        {
            if (expectedRevision != row.Revision)
                throw new InvalidOperationException("The containment revision is stale.");
            row.ContainerEntityId = containerEntityId;
            row.Slot = slot;
            row.Revision++;
            row.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<bool> RemoveContainmentAsync(
        string stateSpaceId,
        string containedEntityId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(containedEntityId, nameof(containedEntityId));
        var row = await db.Set<ApplicationEcsContainmentRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.ContainedEntityId == containedEntityId,
            cancellationToken);
        if (row is null) return false;
        if (expectedRevision != row.Revision)
            throw new InvalidOperationException("The containment revision is stale.");
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EcsRelationshipView?> GetRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(fromEntityId, nameof(fromEntityId));
        ValidateId(toEntityId, nameof(toEntityId));
        ValidateKind(stateSpaceId, qualifiedKind);
        var row = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.FromEntityId == fromEntityId
                && value.ToEntityId == toEntityId && value.QualifiedKind == qualifiedKind)
            .Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.FromEntityId && entity.DeletedAtUtc == null)
                && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ToEntityId && entity.DeletedAtUtc == null))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : View(row);
    }

    public async Task<IReadOnlyList<EcsRelationshipView>> ListRelationshipsAsync(
        string stateSpaceId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        RequireStateSpace(stateSpaceId);
        var rows = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId)
            .Where(value => db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.FromEntityId && entity.DeletedAtUtc == null)
                && db.Set<ApplicationEcsEntityRecord>().Any(entity =>
                    entity.StateSpaceId == stateSpaceId && entity.Id == value.ToEntityId && entity.DeletedAtUtc == null))
            .OrderBy(value => value.FromEntityId).ThenBy(value => value.ToEntityId)
            .ThenBy(value => value.QualifiedKind)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows.Select(View).ToArray());
    }

    public async Task<EcsRelationshipView> SetRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        string dataJson,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(fromEntityId, nameof(fromEntityId));
        ValidateId(toEntityId, nameof(toEntityId));
        ValidateKind(stateSpaceId, qualifiedKind);
        ValidateJson(dataJson);
        await RequireEntityAsync(stateSpaceId, fromEntityId, cancellationToken);
        await RequireEntityAsync(stateSpaceId, toEntityId, cancellationToken);
        var row = await db.Set<ApplicationEcsRelationshipRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.FromEntityId == fromEntityId
                && value.ToEntityId == toEntityId && value.QualifiedKind == qualifiedKind,
            cancellationToken);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            if (expectedRevision != 0)
                throw new InvalidOperationException("The relationship revision is stale.");
            row = new ApplicationEcsRelationshipRecord
            {
                StateSpaceId = stateSpaceId,
                FromEntityId = fromEntityId,
                ToEntityId = toEntityId,
                QualifiedKind = qualifiedKind,
                Data = dataJson,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(row);
        }
        else
        {
            if (expectedRevision != row.Revision)
                throw new InvalidOperationException("The relationship revision is stale.");
            row.Data = dataJson;
            row.Revision++;
            row.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<bool> RemoveRelationshipAsync(
        string stateSpaceId,
        string fromEntityId,
        string toEntityId,
        string qualifiedKind,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(fromEntityId, nameof(fromEntityId));
        ValidateId(toEntityId, nameof(toEntityId));
        ValidateKind(stateSpaceId, qualifiedKind);
        var row = await db.Set<ApplicationEcsRelationshipRecord>().SingleOrDefaultAsync(
            value => value.StateSpaceId == stateSpaceId && value.FromEntityId == fromEntityId
                && value.ToEntityId == toEntityId && value.QualifiedKind == qualifiedKind,
            cancellationToken);
        if (row is null) return false;
        if (expectedRevision != row.Revision)
            throw new InvalidOperationException("The relationship revision is stale.");
        db.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ValidateKind(string stateSpaceId, string qualifiedKind)
    {
        var stateSpace = RequireStateSpace(stateSpaceId);
        var separator = qualifiedKind.IndexOf('.');
        if (separator <= 0)
            throw new ArgumentException("A relationship kind must be a qualified ID.", nameof(qualifiedKind));
        var ownerText = qualifiedKind[..separator];
        var owner = ownerText == ApplicationIdentifier.System.Value
            ? ApplicationIdentifier.System
            : ApplicationIdentifier.Parse(ownerText);
        ComponentTypeIdentifier.Validate(owner, qualifiedKind);
        if (!owner.IsSystem && owner != stateSpace.ApplicationRevision.ApplicationId
            && !stateSpace.ApplicationRevision.BaseApplications.Contains(owner))
            throw new ArgumentException(
                "The relationship kind is outside this state space's exact application revision or bases.",
                nameof(qualifiedKind));
    }

    private StateSpaceView RequireStateSpace(string stateSpaceId) =>
        stateSpaces.Get(stateSpaceId) ?? throw new InvalidOperationException("The state space is unknown.");

    private async Task RequireEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken)
    {
        RequireStateSpace(stateSpaceId);
        if (!await db.Set<ApplicationEcsEntityRecord>().AsNoTracking().AnyAsync(value =>
                value.StateSpaceId == stateSpaceId && value.Id == entityId && value.DeletedAtUtc == null,
                cancellationToken))
            throw new InvalidOperationException("The edge entity is unknown or deleted.");
    }

    private async Task RejectContainmentCycleAsync(
        string stateSpaceId,
        string containedEntityId,
        string candidateContainerId,
        CancellationToken cancellationToken)
    {
        var parents = await db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId)
            .ToDictionaryAsync(value => value.ContainedEntityId, value => value.ContainerEntityId,
                StringComparer.Ordinal, cancellationToken);
        var current = candidateContainerId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current) && parents.TryGetValue(current, out var parent))
        {
            if (parent == containedEntityId)
                throw new InvalidOperationException("Containment must remain acyclic.");
            current = parent;
        }
        if (current == containedEntityId)
            throw new InvalidOperationException("Containment must remain acyclic.");
    }

    private static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("A bounded ID is required.", parameterName);
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "A containment page may contain 1 through 100 rows.");
    }

    private static void ValidateJson(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)
            || Encoding.UTF8.GetByteCount(dataJson) > SystemJsonSchemaProfile.MaximumValueBytes)
            throw new ArgumentException("Relationship data must be bounded JSON.", nameof(dataJson));
        try
        {
            using var _ = JsonDocument.Parse(dataJson, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Relationship data must be valid bounded JSON.", nameof(dataJson), exception);
        }
    }

    private static EcsContainmentView View(ApplicationEcsContainmentRecord value) =>
        new(value.StateSpaceId, value.ContainedEntityId, value.ContainerEntityId, value.Slot,
            value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);

    private static EcsRelationshipView View(ApplicationEcsRelationshipRecord value) =>
        new(value.StateSpaceId, value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.Data,
            value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);
}

internal sealed class ApplicationEcsContainmentRecord
{
    public required string StateSpaceId { get; set; }
    public required string ContainedEntityId { get; set; }
    public required string ContainerEntityId { get; set; }
    public string Slot { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class ApplicationEcsRelationshipRecord
{
    public required string StateSpaceId { get; set; }
    public required string FromEntityId { get; set; }
    public required string ToEntityId { get; set; }
    public required string QualifiedKind { get; set; }
    public string Data { get; set; } = "{}";
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
