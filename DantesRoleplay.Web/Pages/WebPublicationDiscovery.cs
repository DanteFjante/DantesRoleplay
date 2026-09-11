using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Web.Pages;

public sealed record WebPublishedPageView(
    string EntityId,
    string Slug,
    string Title,
    string NavigationLabel,
    int Order,
    string Visibility,
    string Url,
    string ContentPageId,
    bool IsIndexPage,
    bool Enabled);

public sealed record WebPublicationEvidence(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EntityId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Slug = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Enabled = null);

public sealed record WebApplicationPublicationView(
    string ApplicationId,
    string DisplayName,
    string PublicationStatus,
    bool IsPublishable,
    bool IsClickable,
    bool HasAdditionalPages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolutionFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebPublishedPageView? IndexPage,
    IReadOnlyList<WebPublishedPageView> Pages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PublicationStateSpaceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WebPublicationEvidence>? Evidence = null);

public sealed record WebSystemPageView(string PageId, string Title, string Url);

public sealed record WebApplicationPublicationPage(
    IReadOnlyList<WebApplicationPublicationView> Applications,
    IReadOnlyList<WebSystemPageView> SystemPages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

public sealed record WebPageRouteResolution(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApplicationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WebPublishedPageView? Page = null);

/// <summary>Host routing input only. It carries no content, permission or readiness evidence.</summary>
internal sealed record WebPageHostRouteCandidate(ApplicationIdentifier ApplicationId,
    string PublicationStateSpaceId, string EntityId, string Slug,
    WebPageContentReference ActiveContentReference, bool IsIndexPage);

internal sealed record WebPageHostRouteResolution(string Status, WebPageHostRouteCandidate? Candidate = null);

public sealed class WebPublicationException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public interface IWebPublicationDiscovery
{
    Task<WebApplicationPublicationPage> ListApplicationsAsync(
        string? cursor,
        int limit,
        bool diagnostics = false,
        CancellationToken cancellationToken = default);

    Task<WebApplicationPublicationView?> GetApplicationAsync(
        ApplicationIdentifier applicationId,
        bool diagnostics = false,
        CancellationToken cancellationToken = default);

    Task<WebPublishedPageView?> GetPageAsync(
        ApplicationIdentifier applicationId,
        string slug,
        bool diagnostics = false,
        CancellationToken cancellationToken = default);

    Task<WebPageRouteResolution> ResolvePageRouteAsync(
        string slug,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts generic application-publication ECS state into the bounded model consumed by web and AI clients.
/// It knows only the system web component contracts; page content remains in IWebPageStore.
/// </summary>
public sealed class WebPublicationDiscovery(
    IApplicationRegistry applications,
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore entities,
    IWebPageStore content,
    IEcsLifecycleStore? lifecycle = null,
    IApplicationActivationReader? activations = null) : IWebPublicationDiscovery
{
    private const int MaximumApplications = 10_000;
    private const int MaximumPublicationEntities = 1_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<WebApplicationPublicationPage> ListApplicationsAsync(
        string? cursor,
        int limit,
        bool diagnostics = false,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw Error("WEB_PUBLICATION_LIMIT_INVALID", "The limit must be between 1 and 100.");
        var registrations = AllApplications();
        var snapshot = SnapshotFingerprint(registrations);
        var after = DecodeCursor(cursor, snapshot);
        if (after is not null && registrations.All(value => value.Id.Value != after))
            throw Error("WEB_PUBLICATION_CURSOR_STALE", "The application publication cursor is stale.");

        var candidates = registrations
            .Where(value => after is null || string.CompareOrdinal(value.Id.Value, after) > 0)
            .Take(limit + 1)
            .ToArray();
        var hasMore = candidates.Length > limit;
        var selected = hasMore ? candidates[..limit] : candidates;
        var views = new List<WebApplicationPublicationView>(selected.Length);
        foreach (var registration in selected)
            views.Add(await BuildApplicationAsync(registration, diagnostics, cancellationToken));

        return new(
            views.AsReadOnly(),
            await SystemPagesAsync(cancellationToken),
            hasMore ? EncodeCursor(selected[^1].Id.Value, snapshot) : null);
    }

    public async Task<WebApplicationPublicationView?> GetApplicationAsync(
        ApplicationIdentifier applicationId,
        bool diagnostics = false,
        CancellationToken cancellationToken = default)
    {
        var registration = applications.Describe(applicationId);
        return registration is null ? null : await BuildApplicationAsync(registration, diagnostics, cancellationToken);
    }

    public async Task<WebPublishedPageView?> GetPageAsync(
        ApplicationIdentifier applicationId,
        string slug,
        bool diagnostics = false,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(slug)) throw Error("WEB_PAGE_SLUG_INVALID", "A valid bounded page slug is required.");
        var registration = applications.Describe(applicationId);
        if (registration is null) return null;
        var model = await ReadPublicationAsync(registration, diagnostics, cancellationToken);
        var exactMatches = model.PagesForInspection.Where(value => value.Slug == slug
            && (diagnostics || value.Enabled)).ToArray();
        if (exactMatches.Length > 1)
            throw Error("WEB_PAGE_SLUG_AMBIGUOUS", "More than one publication page has this slug; no page was selected.");
        if (exactMatches.Length == 0) return null;
        var page = exactMatches[0];
        return diagnostics || page.Enabled && page.Visibility == "public" && page.IsUsable
            ? ToView(page)
            : null;
    }

    public async Task<WebPageRouteResolution> ResolvePageRouteAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var match = await ResolveRouteMatchAsync(slug, inspectContent: true, cancellationToken);
        if (match.Page is null) return new(match.Status, match.ApplicationId?.Value);
        if (!match.Page.IsUsable) return new("content-missing", match.ApplicationId!.Value);
        return new("ready", match.ApplicationId!.Value, ToView(match.Page));
    }

    /// <summary>
    /// Selects a unique enabled visible route, preserving the exact authored content reference.
    /// The host must construct current authority and reselect/authorize before serving anything.
    /// This lookup does not inspect content or turn a routing candidate into a usable publication.
    /// </summary>
    internal async Task<WebPageHostRouteResolution> ResolveHostRouteAsync(string slug,
        CancellationToken cancellationToken = default)
    {
        var match = await ResolveRouteMatchAsync(slug, inspectContent: false, cancellationToken);
        return match.Page is null ? new(match.Status) : new("candidate", new(match.ApplicationId!,
            match.Publication!.Publication!.StateSpaceId, match.Page.EntityId, match.Page.Slug,
            match.Page.ActiveContentReference, match.Page.IsIndexPage));
    }

    private async Task<RouteMatch> ResolveRouteMatchAsync(string slug, bool inspectContent,
        CancellationToken cancellationToken)
    {
        if (!WebPageId.IsValid(slug)) return new("application-unavailable");
        RouteMatch? selected = null;
        var matches = 0;
        ApplicationIdentifier? firstApplication = null;
        foreach (var registration in AllApplications())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publication = await ReadPublicationAsync(registration, diagnostics: true, cancellationToken, inspectContent);
            if (!inspectContent && publication.StateSpaceListTruncated) return new("publication-invalid");
            foreach (var page in publication.PagesForInspection.Where(value => value.Slug == slug))
            {
                if (matches++ == 0) firstApplication = registration.Id;
                if (!page.Enabled) continue;
                if (selected is not null) return new("publication-invalid");
                selected = new("candidate", registration.Id, publication, page);
            }
        }
        if (matches == 0) return new("application-unavailable");
        if (selected is null) return new("page-disabled", matches == 1 ? firstApplication : null);
        var enabled = selected.Publication!.PagesForInspection.Where(value => value.Enabled).ToArray();
        if (selected.Publication.PublicationCount != 1
            || enabled.Count(value => value.Slug == slug) > 1
            || enabled.Count(value => value.IsIndexPage) > 1)
            return new("publication-invalid", selected.ApplicationId);
        if (selected.Page!.Visibility == "hidden") return new("page-hidden", selected.ApplicationId);
        return selected;
    }

    private async Task<WebApplicationPublicationView> BuildApplicationAsync(
        ApplicationRegistration registration,
        bool diagnostics,
        CancellationToken cancellationToken)
    {
        var model = await ReadPublicationAsync(registration, diagnostics, cancellationToken);
        if (model.Publication is null)
            return new(registration.Id.Value, registration.DisplayName, "missing-publication", false, false, false,
                null, null, [], null,
                diagnostics ? [new("PUBLICATION_SPACE_MISSING", "The installed application has no publication state space.")] : null);

        var enabled = model.PagesForInspection.Where(value => value.Enabled).ToArray();
        var duplicateSlugs = enabled.GroupBy(value => value.Slug, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var indexCandidates = enabled.Where(value => value.IsIndexPage).ToArray();
        var corrupt = duplicateSlugs.Count > 0 || indexCandidates.Length > 1 || model.PublicationCount > 1;
        var usable = enabled.Where(value => value.IsUsable && value.Visibility == "public"
            && !duplicateSlugs.Contains(value.Slug)).ToArray();
        var index = !corrupt && indexCandidates.Length == 1
            ? usable.SingleOrDefault(value => value.EntityId == indexCandidates[0].EntityId)
            : null;
        var navigationCandidates = diagnostics
            ? model.PagesForInspection.Where(value => value.IsUsable && !duplicateSlugs.Contains(value.Slug))
            : usable;
        var additional = navigationCandidates.Where(value => !value.IsIndexPage)
            .OrderBy(value => value.Order)
            .ThenBy(value => value.NavigationLabel, StringComparer.Ordinal)
            .ThenBy(value => value.EntityId, StringComparer.Ordinal)
            .Select(ToView).ToArray();

        var evidence = diagnostics ? model.Evidence.ToList() : null;
        if (diagnostics)
        {
            foreach (var slug in duplicateSlugs)
                evidence!.Add(new("DUPLICATE_PAGE_SLUG", "More than one enabled page has this slug.", Slug: slug));
            if (indexCandidates.Length > 1)
                evidence!.Add(new("MULTIPLE_INDEX_PAGES", "More than one enabled index page exists; none was selected."));
        }

        var allIndexCandidates = model.PagesForInspection.Where(value => value.IsIndexPage).ToArray();
        var status = corrupt ? "invalid"
            : index is not null ? "ready"
            : indexCandidates.Length == 1 && indexCandidates[0].Visibility == "hidden" ? "index-page-hidden"
            : indexCandidates.Length == 1 && !indexCandidates[0].IsUsable ? "index-content-missing"
            : indexCandidates.Length == 0 && allIndexCandidates.Any(value => !value.Enabled) ? "index-page-disabled"
            : "missing-index-page";
        return new(
            registration.Id.Value,
            registration.DisplayName,
            status,
            !corrupt,
            index is not null,
            additional.Length > 0,
            EffectiveResolutionFingerprint(registration, model.Publication),
            index is null ? null : ToView(index),
            Array.AsReadOnly(additional),
            diagnostics ? model.Publication.StateSpaceId : null,
            diagnostics ? evidence!.AsReadOnly() : null);
    }

    private async Task<PublicationRead> ReadPublicationAsync(
        ApplicationRegistration registration,
        bool diagnostics,
        CancellationToken cancellationToken,
        bool inspectContent = true)
    {
        var stateSpacePage = stateSpaces.ListPage(registration.Id, null, 100);
        var publicationSpaces = stateSpacePage.StateSpaces
            .Where(value => value.Scope == EcsStateSpaceScope.ApplicationPublication).ToArray();
        if (publicationSpaces.Length == 0) return new(null, 0, [], [], stateSpacePage.NextStateSpaceId is not null);
        var evidence = new List<WebPublicationEvidence>();
        if (publicationSpaces.Length > 1)
            evidence.Add(new("MULTIPLE_PUBLICATION_SPACES", "The application has more than one publication state space."));
        var publication = publicationSpaces.OrderBy(value => value.StateSpaceId, StringComparer.Ordinal).First();
        var values = new List<InspectedPage>();
        string? cursor = null;
        var count = 0;
        do
        {
            var batch = lifecycle is not null
                ? await lifecycle.ListEntitiesIncludingDisabledAsync(publication.StateSpaceId, cursor, 100, cancellationToken)
                : await entities.ListEntitiesAsync(publication.StateSpaceId, cursor, 100, cancellationToken);
            foreach (var entity in batch.Entities)
            {
                count++;
                if (count > MaximumPublicationEntities)
                    throw Error("WEB_PUBLICATION_TOO_LARGE", $"Publication '{publication.StateSpaceId}' exceeds {MaximumPublicationEntities} entities.");
                var pageComponent = await ComponentAsync(publication.StateSpaceId, entity.EntityId,
                    WebPageComponentTypes.Page, lifecycle is not null, cancellationToken);
                var marker = await ComponentAsync(publication.StateSpaceId, entity.EntityId,
                    WebPageComponentTypes.IndexPage, lifecycle is not null, cancellationToken);
                if (pageComponent is null)
                {
                    if (marker is not null)
                        evidence.Add(new("INDEX_PAGE_REQUIRES_PAGE", "An index-page marker exists without a page component.",
                            entity.EntityId, Enabled: entity.DeletedAtUtc is null));
                    continue;
                }

                PageValue? parsed;
                try { parsed = JsonSerializer.Deserialize<PageValue>(pageComponent.ValueJson, Json); }
                catch (Exception exception) when (exception is JsonException or WebPageStoreException)
                {
                    evidence.Add(new("PAGE_COMPONENT_MALFORMED", exception.Message, entity.EntityId,
                        Enabled: entity.DeletedAtUtc is null));
                    continue;
                }
                if (!Valid(parsed))
                {
                    evidence.Add(new("PAGE_COMPONENT_MALFORMED", "The page component does not satisfy the system.web.page contract.",
                        entity.EntityId, parsed?.Slug, entity.DeletedAtUtc is null));
                    continue;
                }

                var pageContent = inspectContent
                    ? await content.GetSummaryAsync(parsed!.ActiveContentReference.PageId, cancellationToken) : null;
                // Permissioned pinned content must be selected and authorized by the new owner
                // adapter. The legacy HTML route must never substitute its mutable active pointer.
                var contentAvailable = !parsed!.ActiveContentReference.IsPinned && pageContent is { ActiveRevision: > 0 };
                if (inspectContent && !contentAvailable)
                    evidence.Add(new(parsed.ActiveContentReference.IsPinned ? "PAGE_PERMISSIONED_CONTENT_UNAVAILABLE" : "PAGE_CONTENT_MISSING",
                        parsed.ActiveContentReference.IsPinned
                            ? "Pinned content requires the permissioned composition publication adapter."
                            : "The active content reference does not resolve to active web content.",
                        entity.EntityId, parsed.Slug, entity.DeletedAtUtc is null));
                if (diagnostics && entity.DeletedAtUtc is not null)
                    evidence.Add(new("PAGE_ENTITY_DISABLED", "The page entity is disabled and excluded from public discovery.",
                        entity.EntityId, parsed.Slug, false));
                if (diagnostics && parsed.Visibility == "hidden")
                    evidence.Add(new("PAGE_HIDDEN", "The page is hidden and excluded from public navigation.",
                        entity.EntityId, parsed.Slug, entity.DeletedAtUtc is null));

                values.Add(new(entity.EntityId, parsed.Title, parsed.NavigationLabel, parsed.Slug,
                    parsed.Order, parsed.Visibility, parsed.ActiveContentReference,
                    marker is not null, entity.DeletedAtUtc is null, contentAvailable));
            }
            cursor = batch.NextEntityId;
        } while (cursor is not null);
        return new(publication, publicationSpaces.Length, values.AsReadOnly(), evidence.AsReadOnly(), stateSpacePage.NextStateSpaceId is not null);
    }

    private Task<EcsComponentView?> ComponentAsync(
        string stateSpaceId,
        string entityId,
        string typeId,
        bool diagnostics,
        CancellationToken cancellationToken) =>
        diagnostics && lifecycle is not null
            ? lifecycle.GetComponentIncludingDisabledAsync(stateSpaceId, entityId, typeId, cancellationToken)
            : entities.GetComponentAsync(stateSpaceId, entityId, typeId, cancellationToken);

    private IReadOnlyList<ApplicationRegistration> AllApplications()
    {
        var result = new List<ApplicationRegistration>();
        string? cursor = null;
        do
        {
            var page = applications.ListPage(cursor, 100);
            result.AddRange(page.Applications);
            if (result.Count > MaximumApplications)
                throw Error("WEB_APPLICATIONS_TOO_LARGE", $"Application discovery exceeds {MaximumApplications} registrations.");
            cursor = page.NextApplicationId;
        } while (cursor is not null);
        return result.AsReadOnly();
    }

    private string SnapshotFingerprint(IEnumerable<ApplicationRegistration> registrations)
    {
        var canonical = string.Join('\n', registrations.Select(registration =>
        {
            var publications = stateSpaces.ListPage(registration.Id, null, 100).StateSpaces
                .Where(value => value.Scope == EcsStateSpaceScope.ApplicationPublication)
                .OrderBy(value => value.StateSpaceId, StringComparer.Ordinal)
                .Select(value => $"{value.StateSpaceId}:{value.ResolutionFingerprint}");
            var activeResolution = activations?.Current(registration.Id)?.ResolutionFingerprint ?? "";
            return $"{registration.Id.Value}|{activeResolution}|{string.Join(',', publications)}";
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private string EffectiveResolutionFingerprint(
        ApplicationRegistration registration,
        StateSpaceView publication) =>
        activations?.Current(registration.Id)?.ResolutionFingerprint ?? publication.ResolutionFingerprint;

    private async Task<IReadOnlyList<WebSystemPageView>> SystemPagesAsync(CancellationToken cancellationToken)
    {
        var result = new List<WebSystemPageView>();
        foreach (var pageId in SystemWebPageIds.All)
        {
            var page = await content.GetSummaryAsync(pageId, cancellationToken);
            if (page is not { ActiveRevision: > 0 }) continue;
            result.Add(new(pageId, pageId == "home" ? "Home" : "Control Center",
                pageId == "home" ? "/" : $"/ui/{pageId}"));
        }
        return result.AsReadOnly();
    }

    private static string? DecodeCursor(string? cursor, string expectedFingerprint)
    {
        if (cursor is null) return null;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var value = JsonSerializer.Deserialize<CursorValue>(Convert.FromBase64String(padded), Json);
            if (value is null || value.Version != 1 || string.IsNullOrWhiteSpace(value.AfterApplicationId)
                || value.AfterApplicationId.Length > 100 || value.SnapshotFingerprint.Length != 64)
                throw Error("WEB_PUBLICATION_CURSOR_INVALID", "The application publication cursor is invalid.");
            if (!string.Equals(value.SnapshotFingerprint, expectedFingerprint, StringComparison.Ordinal))
                throw Error("WEB_PUBLICATION_CURSOR_STALE", "The application resolution fingerprint changed.");
            return value.AfterApplicationId;
        }
        catch (WebPublicationException) { throw; }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw Error("WEB_PUBLICATION_CURSOR_INVALID", "The application publication cursor is invalid.");
        }
    }

    private static string EncodeCursor(string afterApplicationId, string fingerprint)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new CursorValue(1, afterApplicationId, fingerprint), Json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool Valid(PageValue? value) => value is not null
        && !string.IsNullOrWhiteSpace(value.Title)
        && !string.IsNullOrWhiteSpace(value.NavigationLabel)
        && WebPageId.IsValid(value.Slug)
        && value.Visibility is "public" or "hidden"
        && value.ActiveContentReference is not null
        && WebPageId.IsValid(value.ActiveContentReference.PageId);

    private static WebPublishedPageView ToView(InspectedPage page) => new(
        page.EntityId, page.Slug, page.Title, page.NavigationLabel, page.Order, page.Visibility,
        $"/ui/{page.Slug}", page.ContentPageId, page.IsIndexPage, page.Enabled);

    private static WebPublicationException Error(string code, string message) => new(code, message);

    private sealed record CursorValue(int Version, string AfterApplicationId, string SnapshotFingerprint);
    private sealed record PageValue(string Title, string NavigationLabel, string Slug, int Order,
        string Visibility, WebPageContentReference ActiveContentReference);
    private sealed record InspectedPage(string EntityId, string Title, string NavigationLabel, string Slug,
        int Order, string Visibility, WebPageContentReference ActiveContentReference, bool IsIndexPage, bool Enabled, bool IsUsable)
    {
        public string ContentPageId => ActiveContentReference.PageId;
    }
    private sealed record PublicationRead(StateSpaceView? Publication, int PublicationCount,
        IReadOnlyList<InspectedPage> PagesForInspection, IReadOnlyList<WebPublicationEvidence> Evidence, bool StateSpaceListTruncated);
    private sealed record RouteMatch(string Status, ApplicationIdentifier? ApplicationId = null,
        PublicationRead? Publication = null, InspectedPage? Page = null);
}
