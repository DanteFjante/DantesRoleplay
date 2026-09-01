using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.World;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Pages;

public static class WebPageComponentTypes
{
    public const string Page = "system.web.page";
    public const string IndexPage = "system.web.index-page";
}

public sealed record PublishedWebPage(
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string EntityId,
    string Title,
    string NavigationLabel,
    string Slug,
    int Order,
    string Visibility,
    string ContentPageId,
    bool IsIndexPage);

public interface IWebPagePublicationDirectory
{
    Task<PublishedWebPage?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<PublishedWebPage?> FindIndexAsync(ApplicationIdentifier applicationId, CancellationToken cancellationToken = default);
}

public sealed record WebPageIdentityMigrationItem(
    string PageId,
    string Classification,
    string Message,
    string? ApplicationId = null,
    string? StateSpaceId = null,
    string? EntityId = null,
    string ContentFingerprintBefore = "",
    string ContentFingerprintAfter = "",
    bool Reviewed = false);

public sealed record WebPageIdentityReview(
    string PageId,
    string Disposition,
    string? ApplicationId = null,
    string? EntityId = null,
    string? Title = null,
    string? NavigationLabel = null,
    string? Slug = null,
    int Order = 0,
    string Visibility = "public",
    bool IsIndexPage = false);

public sealed record WebPageIdentityMigrationRequest(IReadOnlyList<WebPageIdentityReview> Reviews);

public sealed record WebPageIdentityMigrationReport(
    int PublicationStateSpaces,
    int LinkedApplicationPages,
    int SystemOwnedPages,
    int UnclassifiablePages,
    IReadOnlyList<WebPageIdentityMigrationItem> Items,
    bool Applied = false,
    bool ContentVerified = false,
    DateTime? RecordedAtUtc = null);

public sealed class WebPageIdentityMigrationState
{
    public WebPageIdentityMigrationReport? LastReport { get; internal set; }
}

public interface IWebPageIdentityMigration
{
    Task<WebPageIdentityMigrationReport> InspectAsync(CancellationToken cancellationToken = default);
    Task<WebPageIdentityMigrationReport> ApplyReviewedAsync(
        WebPageIdentityMigrationRequest request,
        CancellationToken cancellationToken = default);
    Task<WebPageIdentityMigrationReport?> GetLastReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Bridges separately versioned web content to generic ECS publication identity. It interprets
/// only the two system.web contracts; HTML and assets remain owned by IWebPageStore.
/// </summary>
public sealed class WebPagePublicationService(
    IApplicationRegistry applications,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IWorldStore world,
    IWebPageStore pages,
    WebContentDbContext webDb,
    IEcsWriteTransactionFactory? transactions,
    WebPageIdentityMigrationState migrationState,
    ILogger<WebPagePublicationService> logger,
    IApplicationActivationReader? activations = null)
    : IWebPagePublicationDirectory, IWebPageIdentityMigration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public async Task<PublishedWebPage?> FindBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(slug)) return null;
        foreach (var application in AllApplications())
        {
            var found = await FindAsync(application.Id, value => value.Slug == slug, cancellationToken);
            if (found is not null) return found;
        }
        return null;
    }

    public Task<PublishedWebPage?> FindIndexAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default) =>
        FindAsync(applicationId, value => value.IsIndexPage, cancellationToken);

    public Task<WebPageIdentityMigrationReport> InspectAsync(
        CancellationToken cancellationToken = default) =>
        RunMigrationAsync(null, cancellationToken);

    public Task<WebPageIdentityMigrationReport> ApplyReviewedAsync(
        WebPageIdentityMigrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Reviews);
        if (request.Reviews.Count > 1_000 ||
            request.Reviews.Select(value => value.PageId).Distinct(StringComparer.Ordinal).Count() != request.Reviews.Count)
            throw new ArgumentException("Page migration reviews must be bounded and identify each page once.", nameof(request));
        return RunMigrationAsync(request.Reviews.ToDictionary(value => value.PageId, StringComparer.Ordinal), cancellationToken);
    }

    public async Task<WebPageIdentityMigrationReport?> GetLastReportAsync(
        CancellationToken cancellationToken = default)
    {
        if (migrationState.LastReport is not null) return migrationState.LastReport;
        var json = await webDb.PageMigrationReports.AsNoTracking()
            .Where(value => value.Id == "legacy-page-identity-v1")
            .Select(value => value.ReportJson)
            .SingleOrDefaultAsync(cancellationToken);
        migrationState.LastReport = json is null
            ? null
            : JsonSerializer.Deserialize<WebPageIdentityMigrationReport>(json, Json);
        return migrationState.LastReport;
    }

    private async Task<WebPageIdentityMigrationReport> RunMigrationAsync(
        IReadOnlyDictionary<string, WebPageIdentityReview>? reviews,
        CancellationToken cancellationToken)
    {
        var applicationsById = AllApplications().ToDictionary(value => value.Id.Value, StringComparer.Ordinal);
        var fingerprints = await ContentFingerprintsAsync(cancellationToken);
        var existing = await ExistingContentLinksAsync(applicationsById.Values, cancellationToken);
        var items = new List<WebPageIdentityMigrationItem>();
        var pending = new List<WebPageIdentityReview>();
        var systemOwned = 0;
        var linked = 0;
        var unresolved = 0;

        foreach (var page in fingerprints.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (SystemWebPageIds.IsSystemOwned(page.Key))
            {
                systemOwned++;
                items.Add(new(page.Key, "system-owned",
                    "The reviewed page remains owned by the system web surface.",
                    ContentFingerprintBefore: page.Value, ContentFingerprintAfter: page.Value, Reviewed: true));
                continue;
            }
            if (existing.TryGetValue(page.Key, out var publication))
            {
                linked++;
                items.Add(new(page.Key, "application-page",
                    "The page is already linked through explicit ECS publication identity.",
                    publication.ApplicationId.Value, publication.StateSpaceId, publication.EntityId,
                    page.Value, page.Value, true));
                continue;
            }
            if (reviews is null || !reviews.TryGetValue(page.Key, out var review))
            {
                unresolved++;
                items.Add(new(page.Key, "review-required",
                    "No application owner was inferred. An operator must explicitly link or retain this page as unclassified.",
                    ContentFingerprintBefore: page.Value, ContentFingerprintAfter: page.Value));
                continue;
            }
            ValidateReview(review, applicationsById);
            if (review.Disposition == "retain-unclassified")
            {
                unresolved++;
                items.Add(new(page.Key, "reviewed-unclassifiable",
                    "The operator explicitly retained this content without an application page identity.",
                    ContentFingerprintBefore: page.Value, ContentFingerprintAfter: page.Value, Reviewed: true));
                continue;
            }
            pending.Add(review);
        }

        if (reviews is not null && reviews.Keys.Any(pageId => !fingerprints.ContainsKey(pageId)))
            throw new ArgumentException("A page migration review names content that does not exist.", nameof(reviews));
        if (reviews is null || items.Any(value => !value.Reviewed))
            return await RecordReportAsync(new(
                ExistingPublicationCount(applicationsById.Values), linked, systemOwned, unresolved,
                items.AsReadOnly(), false, true, DateTime.UtcNow), cancellationToken);

        var definitions = await world.GetDefinitionsAsync(cancellationToken);
        var pageDefinition = definitions.SingleOrDefault(value => value.Id == WebPageComponentTypes.Page);
        var indexDefinition = definitions.SingleOrDefault(value => value.Id == WebPageComponentTypes.IndexPage);
        if (pageDefinition is null || indexDefinition is null)
            throw new InvalidOperationException(
                "Import system.web.page and system.web.index-page before applying reviewed page migration.");

        if (transactions is null)
            throw new InvalidOperationException("Transactional ECS page migration is unavailable.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        try
        {
            var publications = applicationsById.Values.ToDictionary(
                value => value.Id.Value, value => EnsurePublication(value.Id), StringComparer.Ordinal);
            var pageType = componentTypes.Define(new(
                ApplicationIdentifier.System, pageDefinition.Id, pageDefinition.Schema));
            var indexType = componentTypes.Define(new(
                ApplicationIdentifier.System, indexDefinition.Id, indexDefinition.Schema));
            foreach (var review in pending)
            {
                var publication = publications[review.ApplicationId!];
                await LinkReviewedPageAsync(review, publication, pageType, indexType, cancellationToken);
                linked++;
                var fingerprint = fingerprints[review.PageId];
                items.Add(new(review.PageId, "application-page",
                    "The reviewed page was linked without changing content revisions, activation, or assets.",
                    review.ApplicationId, publication.StateSpaceId, review.EntityId,
                    fingerprint, fingerprint, true));
            }

            var after = await ContentFingerprintsAsync(cancellationToken);
            if (fingerprints.Count != after.Count || fingerprints.Any(value =>
                    !after.TryGetValue(value.Key, out var hash) || hash != value.Value))
                throw new InvalidOperationException(
                    "Web content changed during page identity migration; all ECS changes were rolled back.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await RecordReportAsync(new(
            ExistingPublicationCount(applicationsById.Values), linked, systemOwned, unresolved,
            items.OrderBy(value => value.PageId, StringComparer.Ordinal).ToArray(),
            true, true, DateTime.UtcNow), cancellationToken);
    }

    private async Task<PublishedWebPage?> FindAsync(
        ApplicationIdentifier applicationId,
        Func<PublishedWebPage, bool> predicate,
        CancellationToken cancellationToken)
    {
        var publication = Publication(applicationId);
        if (publication is null || componentTypes.GetLatest(WebPageComponentTypes.Page) is null) return null;
        string? cursor = null;
        do
        {
            var batch = await entities.ListEntitiesAsync(publication.StateSpaceId, cursor, 100, cancellationToken);
            foreach (var entity in batch.Entities)
            {
                var component = await entities.GetComponentAsync(
                    publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.Page, cancellationToken);
                if (component is null) continue;
                PageValue? value;
                try { value = JsonSerializer.Deserialize<PageValue>(component.ValueJson, Json); }
                catch (JsonException) { continue; }
                if (value?.ActiveContentReference?.PageId is null) continue;
                var marker = await entities.GetComponentAsync(
                    publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.IndexPage, cancellationToken);
                var page = new PublishedWebPage(applicationId, publication.StateSpaceId, entity.EntityId,
                    value.Title, value.NavigationLabel, value.Slug, value.Order, value.Visibility,
                    value.ActiveContentReference.PageId, marker is not null);
                if (predicate(page)) return page;
            }
            cursor = batch.NextEntityId;
        } while (cursor is not null);
        return null;
    }

    private async Task LinkReviewedPageAsync(
        WebPageIdentityReview review,
        StateSpaceView publication,
        RegisteredComponentTypeVersion pageType,
        RegisteredComponentTypeVersion indexType,
        CancellationToken cancellationToken)
    {
        var entityId = review.EntityId!;
        var existing = await entities.GetEntityAsync(publication.StateSpaceId, entityId, cancellationToken);
        if (existing is null)
            await entities.CreateEntityAsync(publication.StateSpaceId, entityId, review.Title!, cancellationToken);
        var page = await entities.GetComponentAsync(
            publication.StateSpaceId, entityId, WebPageComponentTypes.Page, cancellationToken);
        var value = JsonSerializer.Serialize(new PageValue(
            review.Title!, review.NavigationLabel!, review.Slug!, review.Order, review.Visibility,
            new ContentReference(review.PageId)), Json);
        if (page is null)
            await entities.AddComponentAsync(new(
                publication.StateSpaceId, entityId, Reference(pageType), value, 0), cancellationToken);
        else
        {
            var stored = JsonSerializer.Deserialize<PageValue>(page.ValueJson, Json);
            if (stored?.ActiveContentReference.PageId != review.PageId)
                throw new InvalidOperationException(
                    $"Reviewed entity '{entityId}' already refers to different web content.");
        }
        if (review.IsIndexPage && await entities.GetComponentAsync(
                publication.StateSpaceId, entityId, WebPageComponentTypes.IndexPage, cancellationToken) is null)
            await entities.AddComponentAsync(new(
                publication.StateSpaceId, entityId, Reference(indexType), "{}", 0), cancellationToken);
    }

    private async Task<Dictionary<string, PublishedWebPage>> ExistingContentLinksAsync(
        IEnumerable<ApplicationRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PublishedWebPage>(StringComparer.Ordinal);
        if (componentTypes.GetLatest(WebPageComponentTypes.Page) is null) return result;
        foreach (var registration in registrations)
        {
            var publication = Publication(registration.Id);
            if (publication is null) continue;
            string? cursor = null;
            do
            {
                var batch = await entities.ListEntitiesAsync(publication.StateSpaceId, cursor, 100, cancellationToken);
                foreach (var entity in batch.Entities)
                {
                    var component = await entities.GetComponentAsync(
                        publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.Page, cancellationToken);
                    if (component is null) continue;
                    PageValue? value;
                    try { value = JsonSerializer.Deserialize<PageValue>(component.ValueJson, Json); }
                    catch (JsonException) { continue; }
                    if (value?.ActiveContentReference?.PageId is not { } pageId) continue;
                    var index = await entities.GetComponentAsync(
                        publication.StateSpaceId, entity.EntityId, WebPageComponentTypes.IndexPage, cancellationToken);
                    var linked = new PublishedWebPage(registration.Id, publication.StateSpaceId, entity.EntityId,
                        value.Title, value.NavigationLabel, value.Slug, value.Order, value.Visibility,
                        pageId, index is not null);
                    if (!result.TryAdd(pageId, linked))
                        throw new InvalidOperationException(
                            $"Web content '{pageId}' is linked from more than one publication entity.");
                }
                cursor = batch.NextEntityId;
            } while (cursor is not null);
        }
        return result;
    }

    private async Task<Dictionary<string, string>> ContentFingerprintsAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pageCursor = null;
        do
        {
            var pageBatch = await pages.ListPageAsync(pageCursor, 100, cancellationToken);
            foreach (var page in pageBatch.Pages)
            {
                var evidence = new StringBuilder()
                    .Append(page.Id).Append('|').Append(page.ActiveRevision).Append('|').Append(page.LatestRevision);
                int? revisionCursor = null;
                var revisions = new List<WebPageRevisionSummary>();
                do
                {
                    var revisionBatch = await pages.ListRevisionsAsync(
                        page.Id, revisionCursor, 100, cancellationToken);
                    revisions.AddRange(revisionBatch.Revisions);
                    revisionCursor = revisionBatch.NextRevision;
                } while (revisionCursor is not null);
                foreach (var revision in revisions.OrderBy(value => value.Revision))
                {
                    var document = await pages.GetRevisionAsync(page.Id, revision.Revision, cancellationToken)
                        ?? throw new InvalidOperationException("A web revision disappeared during migration inspection.");
                    evidence.Append('\n').Append(revision.Revision).Append('|').Append(revision.ContentHash);
                    foreach (var asset in document.Assets.OrderBy(value => value.Path, StringComparer.Ordinal))
                        evidence.Append('\n').Append(asset.Path).Append('|').Append(asset.ContentHash)
                            .Append('|').Append(asset.Content.Length);
                }
                result.Add(page.Id, Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(evidence.ToString()))));
            }
            pageCursor = pageBatch.NextPageId;
        } while (pageCursor is not null);
        return result;
    }

    private async Task<WebPageIdentityMigrationReport> RecordReportAsync(
        WebPageIdentityMigrationReport report,
        CancellationToken cancellationToken)
    {
        var row = await webDb.PageMigrationReports.SingleOrDefaultAsync(
            value => value.Id == "legacy-page-identity-v1", cancellationToken);
        if (row is null)
        {
            row = new()
            {
                Id = "legacy-page-identity-v1",
                ReportJson = JsonSerializer.Serialize(report, Json),
                UpdatedAtUtc = report.RecordedAtUtc ?? DateTime.UtcNow
            };
            webDb.PageMigrationReports.Add(row);
        }
        else
        {
            row.ReportJson = JsonSerializer.Serialize(report, Json);
            row.UpdatedAtUtc = report.RecordedAtUtc ?? DateTime.UtcNow;
        }
        await webDb.SaveChangesAsync(cancellationToken);
        migrationState.LastReport = report;
        foreach (var item in report.Items.Where(value => !value.Reviewed))
            logger.LogWarning("Web page {PageId} requires explicit migration review. {Message}",
                item.PageId, item.Message);
        return report;
    }

    private int ExistingPublicationCount(IEnumerable<ApplicationRegistration> registrations) =>
        registrations.Count(value => Publication(value.Id) is not null);

    private static void ValidateReview(
        WebPageIdentityReview review,
        IReadOnlyDictionary<string, ApplicationRegistration> applications)
    {
        if (!WebPageId.IsValid(review.PageId))
            throw new ArgumentException("A migration review contains an invalid page ID.");
        if (review.Disposition == "retain-unclassified")
        {
            if (review.ApplicationId is not null || review.EntityId is not null)
                throw new ArgumentException("A retained unclassified page cannot claim an application identity.");
            return;
        }
        if (review.Disposition != "application-page" || review.ApplicationId is null ||
            !applications.ContainsKey(review.ApplicationId) ||
            string.IsNullOrWhiteSpace(review.EntityId) || review.EntityId.Length > 200 ||
            string.IsNullOrWhiteSpace(review.Title) || review.Title.Length > 200 ||
            string.IsNullOrWhiteSpace(review.NavigationLabel) || review.NavigationLabel.Length > 100 ||
            !WebPageId.IsValid(review.Slug) || review.Visibility is not ("public" or "hidden"))
            throw new ArgumentException("An application-page review requires exact bounded application, entity, metadata, and slug values.");
    }

    private StateSpaceView EnsurePublication(ApplicationIdentifier applicationId)
    {
        var existing = Publication(applicationId);
        if (existing is not null) return existing;
        var revision = applications.Get(applicationId)
            ?? throw new InvalidOperationException($"Registered application '{applicationId}' has no immutable revision.");
        var active = activations?.Current(applicationId);
        var manifestFingerprint = active?.ActivationFingerprint ?? revision.Fingerprint;
        var resolutionFingerprint = active?.ResolutionFingerprint ?? manifestFingerprint;
        return stateSpaces.Create(new StateSpaceBinding(
            $"application-publication:{applicationId.Value}",
            revision,
            manifestFingerprint,
            resolutionFingerprint,
            EcsStateSpaceScope.ApplicationPublication));
    }

    private StateSpaceView? Publication(ApplicationIdentifier applicationId) =>
        stateSpaces.ListPage(applicationId, null, 100).StateSpaces
            .SingleOrDefault(value => value.Scope == EcsStateSpaceScope.ApplicationPublication);

    private IReadOnlyList<ApplicationRegistration> AllApplications()
    {
        var result = new List<ApplicationRegistration>();
        string? cursor = null;
        do
        {
            var page = applications.ListPage(cursor, 100);
            result.AddRange(page.Applications);
            cursor = page.NextApplicationId;
        } while (cursor is not null);
        return result;
    }

    private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private sealed record ContentReference(string PageId);

    private sealed record PageValue(
        string Title,
        string NavigationLabel,
        string Slug,
        int Order,
        string Visibility,
        ContentReference ActiveContentReference);
}

public static class SystemWebPageIds
{
    private static readonly string[] Values =
    {
        "home",
        "control-center"
    };

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Values);

    public static bool IsSystemOwned(string pageId) => Values.Contains(pageId, StringComparer.Ordinal);
}
