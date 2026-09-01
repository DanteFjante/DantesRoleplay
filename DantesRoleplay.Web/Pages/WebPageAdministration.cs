using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Web.Pages;

public sealed record WebPageAdministrationView(
    string ApplicationId,
    string EntityId,
    string EntityName,
    int EntityRevision,
    int PageComponentRevision,
    bool Enabled,
    string Title,
    string NavigationLabel,
    string Slug,
    int Order,
    string Visibility,
    string ContentPageId,
    bool IsIndexPage,
    WebPageSummary? Content,
    IReadOnlyList<WebPublicationEvidence> Errors);

public sealed record WebPageCreateRequest(
    string EntityId,
    string Title,
    string NavigationLabel,
    string Slug,
    int Order,
    string Visibility,
    string Html,
    bool IsIndexPage = false);

public sealed record WebPageMetadataUpdateRequest(
    int ExpectedComponentRevision,
    string Title,
    string NavigationLabel,
    string Slug,
    int Order,
    string Visibility);

public sealed record WebPageIndexUpdateRequest(bool IsIndexPage);
public sealed record WebPageEnabledUpdateRequest(int ExpectedEntityRevision, bool Enabled);
public sealed record WebPageDraftAppendRequest(int ExpectedLatestRevision, int BaseRevision, string Html);
public sealed record WebPageRevisionActivationRequest(int ExpectedActiveRevision, int Revision);

/// <summary>
/// Operator-only application page administration. Public callers use publication slugs; this
/// surface resolves application publication space and content identity on the server.
/// </summary>
public sealed class WebPageAdministration(
    IApplicationRegistry applications,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IEcsLifecycleStore lifecycle,
    IEcsWriteTransactionFactory transactions,
    IWebPageStore content,
    IWebPageIdentityMigration migration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<IReadOnlyList<WebPageAdministrationView>> ListAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default)
    {
        var publication = Publication(applicationId);
        var result = new List<WebPageAdministrationView>();
        string? cursor = null;
        do
        {
            var batch = await lifecycle.ListEntitiesIncludingDisabledAsync(
                publication.StateSpaceId, cursor, 100, cancellationToken);
            foreach (var entity in batch.Entities)
            {
                var page = await lifecycle.GetComponentIncludingDisabledAsync(
                    publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.Page, cancellationToken);
                if (page is not null)
                    result.Add(await ViewAsync(applicationId, publication, entity, page, cancellationToken));
            }
            cursor = batch.NextEntityId;
        } while (cursor is not null);
        return result.OrderBy(value => value.Order).ThenBy(value => value.NavigationLabel, StringComparer.Ordinal)
            .ThenBy(value => value.EntityId, StringComparer.Ordinal).ToArray();
    }

    public async Task<WebPageAdministrationView?> GetAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var publication = Publication(applicationId);
        var entity = await lifecycle.GetEntityAsync(publication.StateSpaceId, entityId, cancellationToken);
        if (entity is null) return null;
        var page = await lifecycle.GetComponentIncludingDisabledAsync(
            publication.StateSpaceId, entityId, WebPageComponentTypes.Page, cancellationToken);
        return page is null ? null : await ViewAsync(
            applicationId, publication, entity.Entity, page, cancellationToken);
    }

    public async Task<WebPageAdministrationView> CreateAsync(
        ApplicationIdentifier applicationId,
        WebPageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCreate(request);
        var publication = Publication(applicationId);
        var pageType = componentTypes.GetLatest(WebPageComponentTypes.Page)
            ?? throw Error("WEB_PAGE_CONTRACT_MISSING", "The system.web.page contract is unavailable.");
        var indexType = componentTypes.GetLatest(WebPageComponentTypes.IndexPage)
            ?? throw Error("WEB_INDEX_PAGE_CONTRACT_MISSING", "The system.web.index-page contract is unavailable.");
        var contentPageId = ContentPageId(applicationId, request.EntityId);
        if (await content.GetSummaryAsync(contentPageId, cancellationToken) is not null)
            throw Error("WEB_CONTENT_IDENTITY_EXISTS", "The generated content identity is already in use.");

        await content.SaveAndActivateAsync(contentPageId, request.Html, cancellationToken);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        try
        {
            await entities.CreateEntityAsync(
                publication.StateSpaceId, request.EntityId, request.Title, cancellationToken);
            await entities.AddComponentAsync(new(
                publication.StateSpaceId,
                request.EntityId,
                Reference(pageType),
                Serialize(request.Title, request.NavigationLabel, request.Slug, request.Order,
                    request.Visibility, contentPageId),
                0), cancellationToken);
            if (request.IsIndexPage)
                await entities.AddComponentAsync(new(
                    publication.StateSpaceId, request.EntityId, Reference(indexType), "{}", 0), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        return await GetAsync(applicationId, request.EntityId, cancellationToken)
            ?? throw Error("WEB_PAGE_CREATE_FAILED", "The page identity was not readable after creation.");
    }

    public async Task<WebPageAdministrationView> UpdateMetadataAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageMetadataUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMetadata(request.Title, request.NavigationLabel, request.Slug, request.Order, request.Visibility);
        if (request.ExpectedComponentRevision < 1) throw Error("WEB_PAGE_REVISION_INVALID", "A positive component revision is required.");
        var publication = Publication(applicationId);
        var page = await entities.GetComponentAsync(
            publication.StateSpaceId, entityId, WebPageComponentTypes.Page, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The enabled page identity does not exist.");
        var value = Parse(page.ValueJson);
        await entities.SetComponentAsync(new(
            publication.StateSpaceId,
            entityId,
            page.Type,
            Serialize(request.Title, request.NavigationLabel, request.Slug, request.Order,
                request.Visibility, value.ActiveContentReference.PageId),
            request.ExpectedComponentRevision), cancellationToken);
        return await GetAsync(applicationId, entityId, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The page identity disappeared after update.");
    }

    public async Task<WebPageAdministrationView> SetIndexAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageIndexUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var publication = Publication(applicationId);
        var target = await entities.GetComponentAsync(
            publication.StateSpaceId, entityId, WebPageComponentTypes.Page, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The enabled page identity does not exist.");
        _ = target;
        var indexType = componentTypes.GetLatest(WebPageComponentTypes.IndexPage)
            ?? throw Error("WEB_INDEX_PAGE_CONTRACT_MISSING", "The system.web.index-page contract is unavailable.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        try
        {
            string? cursor = null;
            do
            {
                var batch = await entities.ListEntitiesAsync(publication.StateSpaceId, cursor, 100, cancellationToken);
                foreach (var entity in batch.Entities)
                {
                    var marker = await entities.GetComponentAsync(
                        publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.IndexPage, cancellationToken);
                    if (marker is null || request.IsIndexPage && entity.EntityId == entityId) continue;
                    await entities.RemoveComponentAsync(
                        publication.StateSpaceId, entity.EntityId, marker.Type, marker.Revision, cancellationToken);
                }
                cursor = batch.NextEntityId;
            } while (cursor is not null);
            var current = await entities.GetComponentAsync(
                publication.StateSpaceId, entityId, WebPageComponentTypes.IndexPage, cancellationToken);
            if (request.IsIndexPage && current is null)
                await entities.AddComponentAsync(new(
                    publication.StateSpaceId, entityId, Reference(indexType), "{}", 0), cancellationToken);
            if (!request.IsIndexPage && current is not null)
                await entities.RemoveComponentAsync(
                    publication.StateSpaceId, entityId, current.Type, current.Revision, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        return await GetAsync(applicationId, entityId, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The page identity disappeared after index update.");
    }

    public async Task<WebPageAdministrationView> SetEnabledAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageEnabledUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var publication = Publication(applicationId);
        await lifecycle.SetEntityEnabledAsync(
            publication.StateSpaceId, entityId, request.Enabled, request.ExpectedEntityRevision, cancellationToken);
        return await GetAsync(applicationId, entityId, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The page identity disappeared after lifecycle update.");
    }

    public Task<bool> DeleteAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        CancellationToken cancellationToken = default) =>
        lifecycle.DeleteEntityAndComponentsPermanentlyAsync(
            Publication(applicationId).StateSpaceId, entityId, cancellationToken);

    public async Task<WebPageRevisionDocument> AppendDraftAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageDraftAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageId = await ContentIdAsync(applicationId, entityId, cancellationToken);
        return await content.AppendDraftAsync(
            pageId, request.BaseRevision, request.ExpectedLatestRevision, request.Html, cancellationToken);
    }

    public async Task<WebPageDocument> PublishBundleAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageBundle bundle,
        CancellationToken cancellationToken = default) =>
        await content.SaveBundleAndActivateAsync(
            await ContentIdAsync(applicationId, entityId, cancellationToken),
            bundle,
            cancellationToken);

    public async Task<WebPageRevisionDiscoveryPage> ListRevisionsAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        int? beforeRevision,
        int limit,
        CancellationToken cancellationToken = default) =>
        await content.ListRevisionsAsync(
            await ContentIdAsync(applicationId, entityId, cancellationToken),
            beforeRevision, limit, cancellationToken);

    public async Task<WebPageRevisionDocument?> GetRevisionAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        int revision,
        CancellationToken cancellationToken = default) =>
        await content.GetRevisionAsync(
            await ContentIdAsync(applicationId, entityId, cancellationToken), revision, cancellationToken);

    public async Task<WebPageActivationResult> ActivateRevisionAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        WebPageRevisionActivationRequest request,
        CancellationToken cancellationToken = default) =>
        await content.ActivateRevisionAsync(
            await ContentIdAsync(applicationId, entityId, cancellationToken),
            request.Revision, request.ExpectedActiveRevision, cancellationToken);

    public Task<WebPageIdentityMigrationReport> InspectMigrationAsync(
        CancellationToken cancellationToken = default) => migration.InspectAsync(cancellationToken);

    public Task<WebPageIdentityMigrationReport> ApplyMigrationAsync(
        WebPageIdentityMigrationRequest request,
        CancellationToken cancellationToken = default) => migration.ApplyReviewedAsync(request, cancellationToken);

    public Task<WebPageIdentityMigrationReport?> GetMigrationReportAsync(
        CancellationToken cancellationToken = default) => migration.GetLastReportAsync(cancellationToken);

    private async Task<string> ContentIdAsync(
        ApplicationIdentifier applicationId,
        string entityId,
        CancellationToken cancellationToken)
    {
        var publication = Publication(applicationId);
        var page = await lifecycle.GetComponentIncludingDisabledAsync(
            publication.StateSpaceId, entityId, WebPageComponentTypes.Page, cancellationToken)
            ?? throw Error("WEB_PAGE_UNKNOWN", "The page identity does not exist.");
        return Parse(page.ValueJson).ActiveContentReference.PageId;
    }

    private async Task<WebPageAdministrationView> ViewAsync(
        ApplicationIdentifier applicationId,
        StateSpaceView publication,
        EcsEntityView entity,
        EcsComponentView component,
        CancellationToken cancellationToken)
    {
        var errors = new List<WebPublicationEvidence>();
        PageValue value;
        try { value = Parse(component.ValueJson); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new(applicationId.Value, entity.EntityId, entity.Name, entity.Revision, component.Revision,
                entity.DeletedAtUtc is null, "", "", "", 0, "", "", false, null,
                [new("page-component-invalid", exception.Message, entity.EntityId, Enabled: entity.DeletedAtUtc is null)]);
        }
        var contentPage = await content.GetSummaryAsync(value.ActiveContentReference.PageId, cancellationToken);
        if (contentPage is null)
            errors.Add(new("content-missing", "The referenced versioned web content does not exist.",
                entity.EntityId, value.Slug, entity.DeletedAtUtc is null));
        var index = await lifecycle.GetComponentIncludingDisabledAsync(
            publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.IndexPage, cancellationToken);
        return new(applicationId.Value, entity.EntityId, entity.Name, entity.Revision, component.Revision,
            entity.DeletedAtUtc is null, value.Title, value.NavigationLabel, value.Slug, value.Order,
            value.Visibility, value.ActiveContentReference.PageId, index is not null, contentPage,
            errors.AsReadOnly());
    }

    private StateSpaceView Publication(ApplicationIdentifier applicationId)
    {
        if (applications.Get(applicationId) is null)
            throw Error("WEB_APPLICATION_UNKNOWN", "The application is not registered.");
        var values = stateSpaces.ListPage(applicationId, null, 100).StateSpaces
            .Where(value => value.Scope == EcsStateSpaceScope.ApplicationPublication).ToArray();
        return values.Length switch
        {
            1 => values[0],
            0 => throw Error("WEB_PUBLICATION_MISSING", "The application has no publication state space."),
            _ => throw Error("WEB_PUBLICATION_INVALID", "The application has multiple publication state spaces.")
        };
    }

    private static void ValidateCreate(WebPageCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.EntityId) || request.EntityId.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Html) || Encoding.UTF8.GetByteCount(request.Html) > WebPageBundleLimits.MaximumHtmlBytes)
            throw Error("WEB_PAGE_CREATE_INVALID", "A bounded entity identity and initial HTML document are required.");
        ValidateMetadata(request.Title, request.NavigationLabel, request.Slug, request.Order, request.Visibility);
    }

    private static void ValidateMetadata(
        string title, string navigationLabel, string slug, int order, string visibility)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
            string.IsNullOrWhiteSpace(navigationLabel) || navigationLabel.Length > 100 ||
            !WebPageId.IsValid(slug) || order is < -1_000_000 or > 1_000_000 ||
            visibility is not ("public" or "hidden"))
            throw Error("WEB_PAGE_METADATA_INVALID", "Page metadata does not satisfy the system.web.page contract.");
    }

    private static string ContentPageId(ApplicationIdentifier applicationId, string entityId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(applicationId.Value + "\0" + entityId))).ToLowerInvariant();
        return "content-" + hash[..32];
    }

    private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private static string Serialize(
        string title, string navigationLabel, string slug, int order, string visibility, string pageId) =>
        JsonSerializer.Serialize(new PageValue(
            title, navigationLabel, slug, order, visibility, new(pageId)), Json);

    private static PageValue Parse(string json) => JsonSerializer.Deserialize<PageValue>(json, Json)
        ?? throw new InvalidOperationException("The page component is empty.");

    private static WebPageAdministrationException Error(string code, string message) => new(code, message);

    private sealed record ContentReference(string PageId);
    private sealed record PageValue(
        string Title,
        string NavigationLabel,
        string Slug,
        int Order,
        string Visibility,
        ContentReference ActiveContentReference);
}

public sealed class WebPageAdministrationException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
