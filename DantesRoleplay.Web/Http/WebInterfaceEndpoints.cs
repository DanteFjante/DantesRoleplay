using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Live;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Play;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Ecs;
using DantesRoleplay.Web.Interactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Hosting;

public static partial class WebInterfaceEndpoints
{
    private const string HomePageId = "home";
    private static readonly UTF8Encoding StrictInteractionUtf8 = new(false, true);
    private static readonly JsonSerializerOptions StrictInteractionJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IEndpointRouteBuilder MapDantesRoleplayWeb(this IEndpointRouteBuilder endpoints)
    {
        MapPublicRoutes(endpoints);
        MapApplicationRoutes(endpoints);
        MapControlRoutes(endpoints);
        MapStructureRoutes(endpoints);
        MapPageAdministrationRoutes(endpoints);
        return endpoints;
    }

    private static void MapPublicRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", GetHomePageAsync).RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/ui/{id}", GetPageAsync).RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/ui/{id}/assets/{**path}", GetAssetAsync).RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/data/entity/{id}", GetEntityDataAsync).RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/data/{componentType}/{entityId}", GetComponentDataAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/changes", StreamChangesAsync).RequireDantesRoleplayStreamAccess();
        endpoints.MapGet("/api/session", GetSession).RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/web/applications", GetPublishedApplicationsAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/web/applications/{applicationId}", GetPublishedApplicationAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/web/applications/{applicationId}/pages/{slug}", GetPublishedPageAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/components/{name}.js", GetBrowserComponentAssetAsync)
            .RequireDantesRoleplayReadAccess();
    }

    private static IResult GetSession(HttpContext context)
    {
        var tailscale = string.Equals(
            context.User.Identity?.AuthenticationType,
            WebAccessPolicy.TailscaleAuthenticationType,
            StringComparison.Ordinal);
        return Results.Json(new
        {
            accessMode = tailscale ? "tailscale" : "local",
            login = tailscale ? context.User.Identity?.Name : null
        });
    }

    private static Task<IResult> GetPublishedApplicationsAsync(
        HttpContext context,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () => await discovery.ListApplicationsAsync(
            context.Request.Query["cursor"].FirstOrDefault(),
            PublicationLimit(context),
            diagnostics: false,
            cancellationToken));

    private static Task<IResult> GetPublishedApplicationAsync(
        string applicationId,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () => await discovery.GetApplicationAsync(
            ApplicationIdentifier.Parse(applicationId), diagnostics: false, cancellationToken));

    private static Task<IResult> GetPublishedPageAsync(
        string applicationId,
        string slug,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () => await discovery.GetPageAsync(
            ApplicationIdentifier.Parse(applicationId), slug, diagnostics: false, cancellationToken));

    private static Task<IResult> GetPublishedApplicationsDiagnosticAsync(
        HttpContext context,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () => await discovery.ListApplicationsAsync(
            context.Request.Query["cursor"].FirstOrDefault(),
            PublicationLimit(context),
            diagnostics: true,
            cancellationToken));

    private static Task<IResult> GetPublishedApplicationDiagnosticAsync(
        string applicationId,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () => await discovery.GetApplicationAsync(
            ApplicationIdentifier.Parse(applicationId), diagnostics: true, cancellationToken));

    private static Task<IResult> GetPublishedPageDiagnosticAsync(
        string applicationId,
        string slug,
        IWebPublicationDiscovery discovery,
        CancellationToken cancellationToken) =>
        PublicationAsync(async () =>
        {
            var id = ApplicationIdentifier.Parse(applicationId);
            var page = await discovery.GetPageAsync(id, slug, diagnostics: true, cancellationToken);
            if (page is null) return null;
            var application = await discovery.GetApplicationAsync(id, diagnostics: true, cancellationToken);
            var evidence = application?.Evidence?.Where(value =>
                value.EntityId == page.EntityId || value.Slug == page.Slug).ToArray() ?? [];
            return (object)new { page, evidence };
        });

    private static int PublicationLimit(HttpContext context) =>
        int.TryParse(context.Request.Query["limit"].FirstOrDefault(), out var limit) ? limit : 50;

    private static async Task<IResult> PublicationAsync<T>(Func<Task<T?>> read)
    {
        try
        {
            var value = await read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (exception is WebPublicationException or ArgumentException)
        {
            var code = exception is WebPublicationException publication
                ? publication.Code
                : "WEB_PUBLICATION_INVALID_REQUEST";
            var status = code switch
            {
                "WEB_PUBLICATION_CURSOR_STALE" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { error = code, message = exception.Message }, statusCode: status);
        }
    }

    private static async Task<IResult> GetBrowserComponentAssetAsync(
        HttpContext context,
        string name,
        CancellationToken cancellationToken)
    {
        var asset = await BrowserComponentAssets.GetAsync(name, cancellationToken);
        if (asset is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
        context.Response.Headers.ETag = asset.EntityTag;
        context.Response.Headers.LastModified = asset.LastModified.ToString("R");
        context.Response.Headers.Vary = "Accept-Encoding";
        if (IsNotModified(context.Request, asset)) return Results.StatusCode(StatusCodes.Status304NotModified);
        var encoding = PreferredEncoding(context.Request.Headers.AcceptEncoding);
        var content = encoding switch
        {
            "br" when asset.BrotliContent is not null => asset.BrotliContent,
            "gzip" when asset.GzipContent is not null => asset.GzipContent,
            _ => asset.Content
        };
        if (!ReferenceEquals(content, asset.Content)) context.Response.Headers.ContentEncoding = encoding;
        return Results.Bytes(content, "text/javascript; charset=utf-8");
    }

    private static bool IsNotModified(HttpRequest request, BrowserComponentAsset asset)
    {
        if (request.Headers.IfNoneMatch.Count > 0)
        {
            var expected = asset.EntityTag.StartsWith("W/", StringComparison.Ordinal)
                ? asset.EntityTag[2..] : asset.EntityTag;
            return request.Headers.IfNoneMatch.ToString().Split(',').Select(value => value.Trim())
                .Any(value => value == "*" || value == asset.EntityTag || value == expected
                    || value == "W/" + expected);
        }
        return DateTimeOffset.TryParse(request.Headers.IfModifiedSince,
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal, out var modifiedSince)
               && asset.LastModified <= modifiedSince.ToUniversalTime();
    }

    private static string? PreferredEncoding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var qualities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.Split(','))
        {
            var parts = item.Split(';', StringSplitOptions.TrimEntries);
            var quality = 1d;
            foreach (var parameter in parts.Skip(1))
                if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && (!double.TryParse(parameter[2..],
                        System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out quality)
                        || quality is < 0 or > 1))
                    quality = 0;
            qualities[parts[0]] = quality;
        }
        var wildcard = qualities.GetValueOrDefault("*");
        var brotli = qualities.TryGetValue("br", out var br) ? br : wildcard;
        var gzip = qualities.TryGetValue("gzip", out var gz) ? gz : wildcard;
        if (brotli <= 0 && gzip <= 0) return null;
        return brotli >= gzip ? "br" : "gzip";
    }

    private static async Task StreamChangesAsync(
        HttpContext context,
        SqliteWebChangeFeed changes,
        IWebChangeScopeAuthorizer changeScopes)
    {
        var pageId = context.Request.Query["page"].FirstOrDefault();
        if (pageId is not null && !WebPageId.IsValid(pageId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    error = "INVALID_PAGE_ID",
                    message = "Page IDs may contain letters, numbers, dots, underscores, and hyphens."
                },
                context.RequestAborted);
            return;
        }

        var applicationId = context.Request.Query["application"].FirstOrDefault();
        var stateSpaceId = context.Request.Query["stateSpace"].FirstOrDefault();
        var perspective = context.Request.Query["perspective"].FirstOrDefault();
        WebChangeSubscription? subscription = null;
        if (applicationId is not null || stateSpaceId is not null || perspective is not null)
        {
            var cursorText = context.Request.Headers["Last-Event-ID"].FirstOrDefault()
                ?? context.Request.Query["cursor"].FirstOrDefault();
            if (applicationId is null || stateSpaceId is null || perspective is null
                || (cursorText is not null && (!long.TryParse(cursorText,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedCursor)
                    || parsedCursor < 0)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "INVALID_CHANGE_SCOPE",
                    message = "Application change streams require bounded application, state-space, perspective, and cursor values."
                }, context.RequestAborted);
                return;
            }

            try
            {
                subscription = new WebChangeSubscription(applicationId, stateSpaceId, perspective,
                    cursorText is null ? null : long.Parse(cursorText,
                        System.Globalization.CultureInfo.InvariantCulture));
                subscription.Validate();
            }
            catch (ArgumentException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "INVALID_CHANGE_SCOPE",
                    message = "Application change streams require bounded application, state-space, perspective, and cursor values."
                }, context.RequestAborted);
                return;
            }

            if (!await changeScopes.AuthorizeAsync(subscription, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "CHANGE_SCOPE_FORBIDDEN",
                    message = "The current audience cannot subscribe to this application change scope."
                }, context.RequestAborted);
                return;
            }
        }

        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await context.Response.WriteAsync("retry: 2000\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            var stream = subscription is null
                ? changes.WatchAsync(pageId, cancellationToken: context.RequestAborted)
                : changes.WatchAsync(subscription, pageId, cancellationToken: context.RequestAborted);
            await foreach (var change in stream)
            {
                await context.Response.WriteAsync(
                    WebChangeSseFormatter.Format(change),
                    context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // A disconnected EventSource is the normal end of this request.
        }
    }

    private static async Task<IResult> GetPageAsync(
        string id,
        HttpContext context,
        IWebPageStore pages,
        [FromServices] IWebPublicationDiscovery publications,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var contentPageId = id;
        if (!SystemWebPageIds.IsSystemOwned(id))
        {
            var route = await publications.ResolvePageRouteAsync(id, cancellationToken);
            if (route.Status != "ready" || route.Page is null) return PublicationRouteError(route.Status);
            contentPageId = route.Page.ContentPageId;
        }

        var page = await pages.GetActiveAsync(contentPageId, cancellationToken);
        return page is null
            ? PublicationRouteError("content-missing")
            : Results.Text(page.Html, "text/html", Encoding.UTF8);
    }

    private static Task<IResult> GetHomePageAsync(
        HttpContext context,
        IWebPageStore pages,
        [FromServices] IWebPublicationDiscovery publications,
        CancellationToken cancellationToken) =>
        GetPageAsync(HomePageId, context, pages, publications, cancellationToken);

    private static async Task<IResult> GetAssetAsync(
        string id,
        string? path,
        HttpContext context,
        IWebPageStore pages,
        [FromServices] IWebPublicationDiscovery publications,
        CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return Results.NotFound();
        }

        var contentPageId = id;
        if (!SystemWebPageIds.IsSystemOwned(id))
        {
            var route = await publications.ResolvePageRouteAsync(id, cancellationToken);
            if (route.Status != "ready" || route.Page is null) return Results.NotFound();
            contentPageId = route.Page.ContentPageId;
        }
        var asset = await pages.GetActiveAssetAsync(contentPageId, $"assets/{path}", cancellationToken);
        context.Response.Headers.CacheControl = asset is not null && IsContentAddressedAsset(asset.Path)
            ? "private, max-age=31536000, immutable"
            : "private, no-store";
        return asset is null
            ? Results.NotFound()
            : Results.File(asset.Content, asset.ContentType);
    }

    private static bool IsContentAddressedAsset(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        for (var separator = fileName.IndexOf('-'); separator >= 0; separator = fileName.IndexOf('-', separator + 1))
        {
            var fingerprint = fileName[(separator + 1)..];
            if (fingerprint.Length is >= 8 and <= 64 &&
                fingerprint.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
                return true;
        }
        return false;
    }

    private static IResult PublicationRouteError(string status)
    {
        var (title, message, statusCode) = status switch
        {
            "page-hidden" => ("Page hidden", "This page is not available in public navigation.", StatusCodes.Status404NotFound),
            "page-disabled" => ("Page disabled", "This published page has been disabled.", StatusCodes.Status404NotFound),
            "content-missing" => ("Referenced content missing", "The page identity exists, but its active content is unavailable.", StatusCodes.Status424FailedDependency),
            "publication-invalid" => ("Publication configuration invalid", "The application publication must be repaired before this page can be opened.", StatusCodes.Status409Conflict),
            _ => ("Application unavailable", "No installed application publishes this page.", StatusCodes.Status404NotFound)
        };
        return Results.Content($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{title}</title></head>
            <body><main><h1>{title}</h1><p>{message}</p><p><a href="/">Return home</a></p></main></body></html>
            """, "text/html; charset=utf-8", Encoding.UTF8, statusCode);
    }

    private static Task<IResult> GetEntityDataAsync(
        string id,
        DynamicDataReader data,
        CancellationToken cancellationToken) =>
        GetDataAsync("entity", id, data, cancellationToken);

    private static Task<IResult> GetComponentDataAsync(
        string componentType,
        string entityId,
        DynamicDataReader data,
        CancellationToken cancellationToken) =>
        GetDataAsync(componentType, entityId, data, cancellationToken);

    private static async Task<IResult> GetDataAsync(
        string type,
        string entityId,
        DynamicDataReader data,
        CancellationToken cancellationToken)
    {
        var document = await data.ReadAsync(type, entityId, cancellationToken);
        return document is null
            ? Results.NotFound()
            : Results.Text(document.Json.ToJsonString(), "application/json", Encoding.UTF8);
    }

}
