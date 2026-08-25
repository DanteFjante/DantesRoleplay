using System.Globalization;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

/// <summary>Read-only composition over application, ECS, schema, and public-catalog owners.</summary>
public sealed class ControlStructureExplorer(
    IApplicationRegistry applications,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IPublicApplicationCatalogProvider catalogs)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 1024;

    private readonly IApplicationRegistry _applications = applications;
    private readonly IStateSpaceRegistry _stateSpaces = stateSpaces;
    private readonly IApplicationComponentTypeRegistry _componentTypes = componentTypes;
    private readonly IEntityComponentStore _entities = entities;
    private readonly IPublicApplicationCatalogProvider _catalogs = catalogs;

    public StructurePage<ApplicationSummary> ListApplications(string? cursor, string? limit)
    {
        var pageSize = PageSize(limit);
        var page = _applications.ListPage(Decode(cursor, "applications", "all", pageSize), pageSize);
        return Page(page.Applications.Select(Summary).ToArray(), page.NextApplicationId,
            "applications", "all", pageSize);
    }

    public ApplicationDetail? GetApplication(string applicationId)
    {
        var id = Application(applicationId);
        var registration = _applications.Describe(id);
        var revision = _applications.Get(id);
        return registration is null || revision is null ? null : new(
            id.Value, registration.DisplayName, registration.Description,
            registration.BaseApplications.Select(value => value.Value).ToArray(),
            revision.Revision, revision.Fingerprint);
    }

    public StructurePage<StateSpaceSummary> ListStateSpaces(
        string applicationId, string? cursor, string? limit)
    {
        var id = Application(applicationId);
        var pageSize = PageSize(limit);
        var scope = id.Value;
        var page = _stateSpaces.ListPage(id, Decode(cursor, "state-spaces", scope, pageSize), pageSize);
        return Page(page.StateSpaces.Select(Summary).ToArray(), page.NextStateSpaceId,
            "state-spaces", scope, pageSize);
    }

    public StructurePage<ComponentTypeSummary> ListComponentTypes(
        string applicationId, string? cursor, string? limit)
    {
        var id = Application(applicationId);
        var pageSize = PageSize(limit);
        var scope = id.Value;
        var page = _componentTypes.ListLatestPage(
            id, Decode(cursor, "component-types", scope, pageSize), pageSize);
        return Page(page.ComponentTypes.Select(Summary).ToArray(), page.NextQualifiedId,
            "component-types", scope, pageSize);
    }

    public ComponentTypeDetail? GetComponentType(string qualifiedId, int version)
    {
        if (version < 1) throw Invalid("INVALID_VERSION", "Component type versions start at 1.");
        var value = _componentTypes.Get(BoundedId(qualifiedId, 200, "qualifiedId"), version);
        return value is null ? null : new(
            value.Owner.Value, value.QualifiedId, value.Version, value.ProfileId,
            value.SchemaJson, value.SchemaHash, value.CreatedAtUtc);
    }

    public async Task<StructurePage<EntitySummary>> ListEntitiesAsync(
        string stateSpaceId, string? cursor, string? limit,
        CancellationToken cancellationToken = default)
    {
        stateSpaceId = BoundedId(stateSpaceId, 200, "stateSpaceId");
        RequireStateSpace(stateSpaceId);
        var pageSize = PageSize(limit);
        var page = await _entities.ListEntitiesAsync(
            stateSpaceId, Decode(cursor, "entities", stateSpaceId, pageSize), pageSize,
            cancellationToken);
        return Page(page.Entities.Select(Summary).ToArray(), page.NextEntityId,
            "entities", stateSpaceId, pageSize);
    }

    public async Task<EntityDetail?> GetEntityAsync(
        string stateSpaceId, string entityId,
        CancellationToken cancellationToken = default)
    {
        stateSpaceId = BoundedId(stateSpaceId, 200, "stateSpaceId");
        entityId = BoundedId(entityId, 200, "entityId");
        RequireStateSpace(stateSpaceId);
        var value = await _entities.GetEntityAsync(stateSpaceId, entityId, cancellationToken);
        return value is null ? null : new(
            value.StateSpaceId, value.EntityId, value.Name, value.Revision,
            value.CreatedAtUtc, value.DeletedAtUtc);
    }

    public async Task<StructurePage<ComponentSummary>> ListComponentsAsync(
        string stateSpaceId, string entityId, string? cursor, string? limit,
        CancellationToken cancellationToken = default)
    {
        stateSpaceId = BoundedId(stateSpaceId, 200, "stateSpaceId");
        entityId = BoundedId(entityId, 200, "entityId");
        await RequireEntityAsync(stateSpaceId, entityId, cancellationToken);
        var pageSize = PageSize(limit);
        var scope = stateSpaceId + "\n" + entityId;
        var page = await _entities.ListComponentsAsync(
            stateSpaceId, entityId, Decode(cursor, "components", scope, pageSize), pageSize,
            cancellationToken);
        return Page(page.Components.Select(Summary).ToArray(), page.NextQualifiedTypeId,
            "components", scope, pageSize);
    }

    public async Task<ComponentDetail?> GetComponentAsync(
        string stateSpaceId, string entityId, string qualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        stateSpaceId = BoundedId(stateSpaceId, 200, "stateSpaceId");
        entityId = BoundedId(entityId, 200, "entityId");
        qualifiedTypeId = BoundedId(qualifiedTypeId, 200, "qualifiedTypeId");
        await RequireEntityAsync(stateSpaceId, entityId, cancellationToken);
        var value = await _entities.GetComponentAsync(
            stateSpaceId, entityId, qualifiedTypeId, cancellationToken);
        return value is null ? null : new(
            value.StateSpaceId, value.EntityId, value.Type.QualifiedTypeId,
            value.Type.TypeVersion, value.Type.SchemaHash, value.ValueJson,
            value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);
    }

    public CatalogOverview GetCatalog(string applicationId)
    {
        var id = Application(applicationId);
        RequireApplication(id);
        if (!_catalogs.TryGet(id, out var navigator)) return new("unavailable", []);
        var collections = navigator.ListCollections(id);
        return new(collections.Count == 0 ? "empty" : "available", collections);
    }

    public CatalogBrowseResult BrowseCatalog(
        string applicationId, string? collection, string? branch,
        string? cursor, string? pageSize)
    {
        var id = Application(applicationId);
        var navigator = Catalog(id);
        return navigator.Browse(new(
            id,
            BoundedId(collection, 63, "collection"),
            branch ?? string.Empty,
            CatalogPageSize(pageSize),
            CatalogCursor(cursor)));
    }

    public CatalogSearchResult SearchCatalog(
        string applicationId, string? query, string? collection, string? branch,
        IReadOnlyList<string>? kinds, IReadOnlyList<string>? statuses,
        string? cursor, string? pageSize)
    {
        var id = Application(applicationId);
        var navigator = Catalog(id);
        return navigator.Search(new(
            id,
            BoundedId(query, CatalogNavigationLimits.MaximumQueryLength, "query"),
            string.IsNullOrWhiteSpace(collection) ? null : collection,
            branch ?? string.Empty,
            kinds,
            statuses,
            CatalogPageSize(pageSize),
            CatalogCursor(cursor)));
    }

    public CatalogRecordView InspectCatalog(
        string applicationId, string? collection, string qualifiedId)
    {
        var id = Application(applicationId);
        var navigator = Catalog(id);
        return navigator.Inspect(new(
            id,
            BoundedId(collection, 63, "collection"),
            BoundedId(qualifiedId, 400, "qualifiedId")));
    }

    private ICatalogNavigator Catalog(ApplicationIdentifier applicationId)
    {
        RequireApplication(applicationId);
        if (!_catalogs.TryGet(applicationId, out var navigator))
            throw new ControlStructureException(
                "PUBLIC_CATALOG_UNAVAILABLE",
                "No public catalog is active for this application.",
                StatusCodes.Status404NotFound);
        return navigator;
    }

    private void RequireApplication(ApplicationIdentifier applicationId)
    {
        if (_applications.Get(applicationId) is null)
            throw new ControlStructureException(
                "APPLICATION_UNKNOWN", "The application is unknown.", StatusCodes.Status404NotFound);
    }

    private void RequireStateSpace(string stateSpaceId)
    {
        if (_stateSpaces.Get(stateSpaceId) is null)
            throw new ControlStructureException(
                "STATE_SPACE_UNKNOWN", "The state space is unknown.", StatusCodes.Status404NotFound);
    }

    private async Task RequireEntityAsync(
        string stateSpaceId, string entityId, CancellationToken cancellationToken)
    {
        RequireStateSpace(stateSpaceId);
        if (await _entities.GetEntityAsync(stateSpaceId, entityId, cancellationToken) is null)
            throw new ControlStructureException(
                "ENTITY_UNKNOWN", "The entity is unknown or deleted.", StatusCodes.Status404NotFound);
    }

    private static StructurePage<T> Page<T>(
        IReadOnlyList<T> items, string? nextKey, string kind, string scope, int pageSize) =>
        new(items, nextKey is null ? null : Encode(new(kind, scope, pageSize, nextKey)));

    private static string? Decode(string? cursor, string kind, string scope, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        if (cursor.Length > MaximumCursorLength)
            throw Invalid("CURSOR_INVALID", "The structure cursor is invalid.");
        StructureCursor token;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            token = JsonSerializer.Deserialize<StructureCursor>(Convert.FromBase64String(encoded))
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw Invalid("CURSOR_INVALID", "The structure cursor is invalid.");
        }
        if (token.Kind != kind || token.Scope != scope || token.PageSize != pageSize ||
            string.IsNullOrWhiteSpace(token.LastKey) || token.LastKey.Length > 400)
            throw new ControlStructureException(
                "CURSOR_STALE", "The structure cursor no longer matches this list. Restart it.",
                StatusCodes.Status409Conflict);
        return token.LastKey;
    }

    private static string Encode(StructureCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int PageSize(string? value) => ParsePageSize(value, MaximumPageSize);
    private static int CatalogPageSize(string? value) =>
        ParsePageSize(value, CatalogNavigationLimits.MaximumPageSize);

    private static string? CatalogCursor(string? cursor)
    {
        if (cursor is { Length: > MaximumCursorLength })
            throw Invalid("CURSOR_INVALID", "The catalog cursor is invalid.");
        return cursor;
    }

    private static int ParsePageSize(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultPageSize;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize) ||
            pageSize < 1 || pageSize > maximum)
            throw Invalid("INVALID_LIMIT", $"Page size must be an integer from 1 through {maximum}.");
        return pageSize;
    }

    private static ApplicationIdentifier Application(string value)
    {
        try { return ApplicationIdentifier.Parse(value); }
        catch (ArgumentException) { throw Invalid("INVALID_APPLICATION_ID", "The application ID is invalid."); }
    }

    private static string BoundedId(string? value, int maximum, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum || normalized.Any(char.IsControl))
            throw Invalid("INVALID_IDENTIFIER", $"{name} must contain 1 through {maximum} non-control characters.");
        return normalized;
    }

    private static ControlStructureException Invalid(string code, string message) =>
        new(code, message, StatusCodes.Status400BadRequest);

    private static ApplicationSummary Summary(ApplicationRegistration value) =>
        new(value.Id.Value, value.DisplayName, value.Description,
            value.BaseApplications.Select(baseApplication => baseApplication.Value).ToArray());
    private static StateSpaceSummary Summary(StateSpaceView value) =>
        new(value.StateSpaceId, value.ApplicationRevision.ApplicationId.Value,
            value.ApplicationRevision.Revision, value.ManifestFingerprint, value.CreatedAtUtc);
    private static ComponentTypeSummary Summary(RegisteredComponentTypeVersion value) =>
        new(value.Owner.Value, value.QualifiedId, value.Version, value.ProfileId,
            value.SchemaHash, value.CreatedAtUtc);
    private static EntitySummary Summary(EcsEntityView value) =>
        new(value.StateSpaceId, value.EntityId, value.Name, value.Revision, value.CreatedAtUtc);
    private static ComponentSummary Summary(EcsComponentView value) =>
        new(value.StateSpaceId, value.EntityId, value.Type.QualifiedTypeId,
            value.Type.TypeVersion, value.Type.SchemaHash, value.Revision, value.UpdatedAtUtc);

    private sealed record StructureCursor(string Kind, string Scope, int PageSize, string LastKey);
}

public sealed class ControlStructureException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record StructurePage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record ApplicationSummary(string Id, string DisplayName, string Description, IReadOnlyList<string> BaseApplications);
public sealed record ApplicationDetail(string Id, string DisplayName, string Description, IReadOnlyList<string> BaseApplications, int Revision, string Fingerprint);
public sealed record StateSpaceSummary(string StateSpaceId, string ApplicationId, int ApplicationRevision, string ManifestFingerprint, DateTime CreatedAtUtc);
public sealed record ComponentTypeSummary(string Owner, string QualifiedId, int Version, string ProfileId, string SchemaHash, DateTime CreatedAtUtc);
public sealed record ComponentTypeDetail(string Owner, string QualifiedId, int Version, string ProfileId, string SchemaJson, string SchemaHash, DateTime CreatedAtUtc);
public sealed record EntitySummary(string StateSpaceId, string EntityId, string Name, int Revision, DateTime CreatedAtUtc);
public sealed record EntityDetail(string StateSpaceId, string EntityId, string Name, int Revision, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
public sealed record ComponentSummary(string StateSpaceId, string EntityId, string QualifiedTypeId, int TypeVersion, string SchemaHash, int Revision, DateTime UpdatedAtUtc);
public sealed record ComponentDetail(string StateSpaceId, string EntityId, string QualifiedTypeId, int TypeVersion, string SchemaHash, string ValueJson, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record CatalogOverview(string Status, IReadOnlyList<CatalogCollectionSummary> Collections);
