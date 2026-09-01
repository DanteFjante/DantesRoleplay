using System.Globalization;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

/// <summary>Read-only composition over application, ECS, schema, and public-catalog owners.</summary>
public sealed class ControlStructureExplorer(
    IApplicationRegistry applications,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IPublicApplicationCatalogProvider catalogs,
    ISystemCapabilityCatalog? systemCapabilities = null,
    IStateSpaceEdgeStore? edges = null,
    IApplicationActivationReader? activations = null)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 1024;

    private readonly IApplicationRegistry _applications = applications;
    private readonly IStateSpaceRegistry _stateSpaces = stateSpaces;
    private readonly IApplicationComponentTypeRegistry _componentTypes = componentTypes;
    private readonly IEntityComponentStore _entities = entities;
    private readonly IPublicApplicationCatalogProvider _catalogs = catalogs;
    private readonly ISystemCapabilityCatalog? _systemCapabilities = systemCapabilities;
    private readonly IStateSpaceEdgeStore? _edges = edges;
    private readonly IApplicationActivationReader? _activations = activations;

    public async Task<StructurePage<ApplicationSummary>> ListApplicationsThroughCapabilitiesAsync(
        AuthorizationAuditEvidence authorization,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        var catalog = RequireSystemCapabilities();
        var pageSize = PageSize(limit);
        var afterApplicationId = Decode(cursor, "applications", "all", pageSize);
        var result = await catalog.ReadAsync(
            SystemCapabilityIds.Applications,
            JsonSerializer.Serialize(new { afterApplicationId, limit = pageSize }),
            SystemCapabilityInvocationContext.FromAuthorization(authorization),
            cancellationToken);
        var data = CapabilityData(result);
        var items = data.GetProperty("applications").EnumerateArray().Select(ApplicationSummary).ToArray();
        var next = data.GetProperty("nextApplicationId").ValueKind == JsonValueKind.Null
            ? null
            : data.GetProperty("nextApplicationId").GetString();
        return Page(items, next, "applications", "all", pageSize);
    }

    public async Task<ApplicationDetail?> GetApplicationThroughCapabilitiesAsync(
        AuthorizationAuditEvidence authorization,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        var catalog = RequireSystemCapabilities();
        var result = await catalog.ReadAsync(
            SystemCapabilityIds.Applications,
            JsonSerializer.Serialize(new { applicationId, limit = DefaultPageSize }),
            SystemCapabilityInvocationContext.FromAuthorization(authorization),
            cancellationToken);
        if (!result.Ok && result.Error?.Code == "APPLICATION_UNKNOWN") return null;
        var application = CapabilityData(result).GetProperty("application");
        return application.ValueKind == JsonValueKind.Null ? null : ApplicationDetail(application);
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

    public StructurePage<StateSpaceSummary> ListApplicationStateSpaces(
        string applicationId, string? cursor, string? limit)
    {
        var id = Application(applicationId);
        RequireApplication(id);
        return ListStateSpaces(id.Value, cursor, limit);
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

    public async Task<StructurePage<EntitySummary>> ListApplicationEntitiesAsync(
        string applicationId, string stateSpaceId, string? cursor, string? limit,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        return await ListEntitiesAsync(stateSpaceId, cursor, limit, cancellationToken);
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

    public async Task<EntityDetail?> GetApplicationEntityAsync(
        string applicationId, string stateSpaceId, string entityId,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        return await GetEntityAsync(stateSpaceId, entityId, cancellationToken);
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

    public async Task<StructurePage<ComponentSummary>> ListApplicationComponentsAsync(
        string applicationId, string stateSpaceId, string entityId, string? cursor, string? limit,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        return await ListComponentsAsync(
            stateSpaceId, entityId, cursor, limit, cancellationToken);
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

    public async Task<ComponentDetail?> GetApplicationComponentAsync(
        string applicationId, string stateSpaceId, string entityId, string qualifiedTypeId,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        var exact = await GetComponentAsync(
            stateSpaceId, entityId, qualifiedTypeId, cancellationToken);
        if (exact is not null) return exact;

        var legacyTypeId = LegacyApplicationIdentity(applicationId, qualifiedTypeId);
        if (legacyTypeId == qualifiedTypeId) return null;
        var legacy = await GetComponentAsync(
            stateSpaceId, entityId, legacyTypeId, cancellationToken);
        return legacy is null ? null : legacy with { QualifiedTypeId = qualifiedTypeId };
    }

    public async Task<StructurePage<ContainmentSummary>> ListApplicationContainmentsAsync(
        string applicationId,
        string stateSpaceId,
        string containerEntityId,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        containerEntityId = BoundedId(containerEntityId, 200, "containerEntityId");
        await RequireEntityAsync(stateSpaceId, containerEntityId, cancellationToken);
        var pageSize = PageSize(limit);
        var scope = stateSpaceId + "\n" + containerEntityId;
        var page = await RequireEdges().ListContainmentsAsync(
            stateSpaceId,
            containerEntityId,
            Decode(cursor, "containments", scope, pageSize),
            pageSize,
            cancellationToken);
        return Page(
            page.Containments.Select(Summary).ToArray(),
            page.NextContainedEntityId,
            "containments",
            scope,
            pageSize);
    }

    public async Task<EntityContainmentDetail> GetApplicationContainmentAsync(
        string applicationId,
        string stateSpaceId,
        string containedEntityId,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        containedEntityId = BoundedId(containedEntityId, 200, "containedEntityId");
        await RequireEntityAsync(stateSpaceId, containedEntityId, cancellationToken);
        var value = await RequireEdges().GetContainmentAsync(
            stateSpaceId, containedEntityId, cancellationToken);
        return new(value is null ? null : Summary(value));
    }

    public async Task<StructurePage<RelationshipSummary>> ListApplicationRelationshipsAsync(
        string applicationId,
        string stateSpaceId,
        string fromEntityId,
        string qualifiedKind,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        RequireApplicationStateSpace(applicationId, stateSpaceId);
        fromEntityId = BoundedId(fromEntityId, 200, "fromEntityId");
        qualifiedKind = BoundedId(qualifiedKind, 200, "qualifiedKind");
        await RequireEntityAsync(stateSpaceId, fromEntityId, cancellationToken);
        var pageSize = PageSize(limit);
        var scope = stateSpaceId + "\n" + fromEntityId + "\n" + qualifiedKind;
        var afterToEntityId = Decode(cursor, "relationships", scope, pageSize);
        var relationships = await RequireEdges().ListRelationshipsAsync(stateSpaceId, cancellationToken);
        var matches = relationships
            .Where(value => value.FromEntityId == fromEntityId && value.QualifiedKind == qualifiedKind)
            .OrderBy(value => value.ToEntityId, StringComparer.Ordinal)
            .TakeWhile(value => afterToEntityId is null ||
                string.CompareOrdinal(value.ToEntityId, afterToEntityId) > 0)
            .Take(pageSize + 1)
            .ToArray();
        if (matches.Length == 0)
        {
            var legacyKind = LegacyApplicationIdentity(applicationId, qualifiedKind);
            if (legacyKind != qualifiedKind)
                matches = relationships
                    .Where(value => value.FromEntityId == fromEntityId && value.QualifiedKind == legacyKind)
                    .OrderBy(value => value.ToEntityId, StringComparer.Ordinal)
                    .TakeWhile(value => afterToEntityId is null ||
                        string.CompareOrdinal(value.ToEntityId, afterToEntityId) > 0)
                    .Take(pageSize + 1)
                    .ToArray();
        }
        var items = matches.Take(pageSize).Select(value =>
            Summary(value) with { QualifiedKind = qualifiedKind }).ToArray();
        var next = matches.Length > pageSize ? matches[pageSize - 1].ToEntityId : null;
        return Page(items, next, "relationships", scope, pageSize);
    }

    private static string LegacyApplicationIdentity(string applicationId, string qualifiedId) =>
        qualifiedId.StartsWith(applicationId + ".", StringComparison.Ordinal)
            ? qualifiedId
            : $"{applicationId}.{qualifiedId}";

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
        string? cursor, string? pageSize, string? namespaceId = null,
        bool includeShadowed = false)
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
            CatalogCursor(cursor),
            namespaceId,
            includeShadowed));
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

    public EffectiveApplicationContentResult GetEffectiveApplicationContent(
        string applicationId, string? cursor, string? pageSize)
    {
        var id = Application(applicationId);
        return Catalog(id).EffectiveContent(new(id, CatalogPageSize(pageSize), CatalogCursor(cursor)));
    }

    public ReadableRulesResult GetReadableRules(
        string applicationId, ReadableRuleAudience audience)
    {
        var id = Application(applicationId);
        return Catalog(id).ReadableRules(new(id, audience));
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

    private ISystemCapabilityCatalog RequireSystemCapabilities() =>
        _systemCapabilities ?? throw new ControlStructureException(
            "SYSTEM_CAPABILITY_UNAVAILABLE",
            "System capability dispatch is unavailable.",
            StatusCodes.Status503ServiceUnavailable);

    private IStateSpaceEdgeStore RequireEdges() =>
        _edges ?? throw new ControlStructureException(
            "APPLICATION_CONTAINMENT_UNAVAILABLE",
            "Application containment reads are unavailable.",
            StatusCodes.Status503ServiceUnavailable);

    private static JsonElement CapabilityData(SystemCapabilityReadResult result)
    {
        if (result.Ok && result.Data is not null) return result.Data.Value;
        var error = result.Error;
        var status = error?.Code switch
        {
            "PRIVATE_OPERATOR_UNAUTHENTICATED" or "PRIVATE_OPERATOR_WRONG_SCOPE" or
            "PRIVATE_OPERATOR_DENIED" => StatusCodes.Status403Forbidden,
            "SYSTEM_CAPABILITY_UNKNOWN" or "APPLICATION_UNKNOWN" => StatusCodes.Status404NotFound,
            "SYSTEM_CAPABILITY_UNAVAILABLE" or "SYSTEM_CAPABILITY_OUTPUT_INVALID" =>
                StatusCodes.Status503ServiceUnavailable,
            "CURSOR_STALE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        throw new ControlStructureException(
            error?.Code ?? "SYSTEM_CAPABILITY_UNAVAILABLE",
            error?.Message ?? "System capability dispatch is unavailable.",
            status);
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

    private void RequireApplicationStateSpace(string applicationId, string stateSpaceId)
    {
        var application = Application(applicationId);
        RequireApplication(application);
        stateSpaceId = BoundedId(stateSpaceId, 200, "stateSpaceId");
        var stateSpace = _stateSpaces.Get(stateSpaceId);
        if (stateSpace is null)
            throw new ControlStructureException(
                "STATE_SPACE_UNKNOWN", "The state space is unknown.", StatusCodes.Status404NotFound);
        if (stateSpace.ApplicationRevision.ApplicationId != application)
            throw new ControlStructureException(
                "STATE_SPACE_WRONG_APPLICATION",
                "The state space is unavailable for this application.",
                StatusCodes.Status404NotFound);
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

    private static ApplicationSummary ApplicationSummary(JsonElement value) =>
        new(
            value.GetProperty("id").GetString()!,
            value.GetProperty("displayName").GetString()!,
            value.GetProperty("description").GetString()!,
            value.GetProperty("baseApplications").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
    private static ApplicationDetail ApplicationDetail(JsonElement value) =>
        new(
            value.GetProperty("id").GetString()!,
            value.GetProperty("displayName").GetString()!,
            value.GetProperty("description").GetString()!,
            value.GetProperty("baseApplications").EnumerateArray()
                .Select(item => item.GetString()!).ToArray(),
            value.GetProperty("revision").GetInt32(),
            value.GetProperty("fingerprint").GetString()!);
    private StateSpaceSummary Summary(StateSpaceView value)
    {
        var active = _activations?.Current(value.ApplicationRevision.ApplicationId);
        var current = active is not null &&
            value.ApplicationRevision.Revision == active.ApplicationRevision &&
            string.Equals(value.ApplicationRevision.Fingerprint, active.ApplicationFingerprint,
                StringComparison.Ordinal) &&
            string.Equals(value.ManifestFingerprint, active.ActivationFingerprint, StringComparison.Ordinal) &&
            string.Equals(value.ResolutionFingerprint, active.ResolutionFingerprint, StringComparison.Ordinal);
        return new(
            value.StateSpaceId,
            value.ApplicationRevision.ApplicationId.Value,
            value.ApplicationRevision.Revision,
            value.ManifestFingerprint,
            value.ResolutionFingerprint,
            value.Scope == EcsStateSpaceScope.Runtime ? "runtime" : "application-publication",
            current,
            value.CreatedAtUtc);
    }
    private static ComponentTypeSummary Summary(RegisteredComponentTypeVersion value) =>
        new(value.Owner.Value, value.QualifiedId, value.Version, value.ProfileId,
            value.SchemaHash, value.CreatedAtUtc);
    private static EntitySummary Summary(EcsEntityView value) =>
        new(value.StateSpaceId, value.EntityId, value.Name, value.Revision, value.CreatedAtUtc);
    private static ComponentSummary Summary(EcsComponentView value) =>
        new(value.StateSpaceId, value.EntityId, value.Type.QualifiedTypeId,
            value.Type.TypeVersion, value.Type.SchemaHash, value.Revision, value.UpdatedAtUtc);
    private static ContainmentSummary Summary(EcsContainmentView value) =>
        new(value.StateSpaceId, value.ContainedEntityId, value.ContainerEntityId, value.Slot,
            value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);
    private static RelationshipSummary Summary(EcsRelationshipView value) =>
        new(value.StateSpaceId, value.FromEntityId, value.ToEntityId, value.QualifiedKind,
            value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc);

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
public sealed record StateSpaceSummary(
    string StateSpaceId,
    string ApplicationId,
    int ApplicationRevision,
    string ManifestFingerprint,
    string ResolutionFingerprint,
    string Scope,
    bool IsCurrent,
    DateTime CreatedAtUtc);
public sealed record ComponentTypeSummary(string Owner, string QualifiedId, int Version, string ProfileId, string SchemaHash, DateTime CreatedAtUtc);
public sealed record ComponentTypeDetail(string Owner, string QualifiedId, int Version, string ProfileId, string SchemaJson, string SchemaHash, DateTime CreatedAtUtc);
public sealed record EntitySummary(string StateSpaceId, string EntityId, string Name, int Revision, DateTime CreatedAtUtc);
public sealed record EntityDetail(string StateSpaceId, string EntityId, string Name, int Revision, DateTime CreatedAtUtc, DateTime? DeletedAtUtc);
public sealed record ComponentSummary(string StateSpaceId, string EntityId, string QualifiedTypeId, int TypeVersion, string SchemaHash, int Revision, DateTime UpdatedAtUtc);
public sealed record ComponentDetail(string StateSpaceId, string EntityId, string QualifiedTypeId, int TypeVersion, string SchemaHash, string ValueJson, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record ContainmentSummary(string StateSpaceId, string ContainedEntityId, string ContainerEntityId, string Slot, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record EntityContainmentDetail(ContainmentSummary? Containment);
public sealed record RelationshipSummary(string StateSpaceId, string FromEntityId, string ToEntityId, string QualifiedKind, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record CatalogOverview(string Status, IReadOnlyList<CatalogCollectionSummary> Collections);
