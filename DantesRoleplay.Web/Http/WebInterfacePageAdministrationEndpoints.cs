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
    private static void MapPageAdministrationRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDantesRoleplayControlGet("/web/applications", GetPublishedApplicationsDiagnosticAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}", GetPublishedApplicationDiagnosticAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}/pages/{slug}", GetPublishedPageDiagnosticAsync);
        endpoints.MapDantesRoleplayControlGet("/web/page-migration", GetPageMigrationReportAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/web/page-migration/reviews",
            PrivateOperatorCapability.ControlPagesWrite,
            ApplyPageMigrationReviewsAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}/pages", GetAdminPagesAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/web/applications/{applicationId}/pages",
            PrivateOperatorCapability.ControlPagesWrite,
            CreateAdminPageAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}/pages/{entityId:regex(^web-page:.+$)}", GetAdminPageAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/web/applications/{applicationId}/pages/{entityId}/metadata",
            PrivateOperatorCapability.ControlPagesWrite,
            UpdateAdminPageMetadataAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/web/applications/{applicationId}/pages/{entityId}/index",
            PrivateOperatorCapability.ControlPagesWrite,
            UpdateAdminPageIndexAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/web/applications/{applicationId}/pages/{entityId}/enabled",
            PrivateOperatorCapability.ControlPagesWrite,
            UpdateAdminPageEnabledAsync);
        endpoints.MapDantesRoleplayControlDelete(
            "/web/applications/{applicationId}/pages/{entityId}",
            PrivateOperatorCapability.ControlPagesWrite,
            DeleteAdminPageAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}/pages/{entityId}/revisions", GetAdminPageRevisionsAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/web/applications/{applicationId}/pages/{entityId}/revisions/{revision:int}",
            GetAdminPageRevisionAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/web/applications/{applicationId}/pages/{entityId}/drafts",
            PrivateOperatorCapability.ControlPagesWrite,
            AppendAdminPageDraftAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/web/applications/{applicationId}/pages/{entityId}/bundle-drafts",
            PrivateOperatorCapability.ControlPagesWrite,
            AppendAdminPageBundleDraftAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/web/applications/{applicationId}/pages/{entityId}/bundle",
            PrivateOperatorCapability.ControlPagesWrite,
            PublishAdminPageBundleAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/web/applications/{applicationId}/pages/{entityId}/active",
            PrivateOperatorCapability.ControlPagesWrite,
            ActivateAdminPageRevisionAsync);
    }

    private static Task<IResult> GetPageMigrationReportAsync(
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)(await pages.GetMigrationReportAsync(cancellationToken)
                ?? await pages.InspectMigrationAsync(cancellationToken)));

    private static Task<IResult> ApplyPageMigrationReviewsAsync(
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.ApplyMigrationAsync(
                await WebPageAdministrationRequestReader.ReadAsync<WebPageIdentityMigrationRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> GetAdminPagesAsync(
        string applicationId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.ListAsync(ApplicationIdentifier.Parse(applicationId), cancellationToken));

    private static Task<IResult> CreateAdminPageAsync(
        string applicationId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.CreateAsync(
                ApplicationIdentifier.Parse(applicationId),
                await WebPageAdministrationRequestReader.ReadAsync<WebPageCreateRequest>(context.Request, cancellationToken),
                cancellationToken));

    private static Task<IResult> GetAdminPageAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.GetAsync(
                ApplicationIdentifier.Parse(applicationId), entityId, cancellationToken));

    private static Task<IResult> UpdateAdminPageMetadataAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.UpdateMetadataAsync(
                ApplicationIdentifier.Parse(applicationId), entityId,
                await WebPageAdministrationRequestReader.ReadAsync<WebPageMetadataUpdateRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> UpdateAdminPageIndexAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.SetIndexAsync(
                ApplicationIdentifier.Parse(applicationId), entityId,
                await WebPageAdministrationRequestReader.ReadAsync<WebPageIndexUpdateRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> UpdateAdminPageEnabledAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.SetEnabledAsync(
                ApplicationIdentifier.Parse(applicationId), entityId,
                await WebPageAdministrationRequestReader.ReadAsync<WebPageEnabledUpdateRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> DeleteAdminPageAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
        {
            var deleted = await pages.DeleteAsync(
                ApplicationIdentifier.Parse(applicationId), entityId, cancellationToken);
            return deleted ? new { deleted = true, entityId } : null;
        });

    private static Task<IResult> GetAdminPageRevisionsAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
        {
            var before = ParseOptionalPositiveInt(context.Request.Query["beforeRevision"].FirstOrDefault(), "beforeRevision");
            var limit = ParseBoundedPositiveInt(context.Request.Query["limit"].FirstOrDefault(), 25, 100, "limit");
            return (object?)await pages.ListRevisionsAsync(
                ApplicationIdentifier.Parse(applicationId), entityId, before, limit, cancellationToken);
        });

    private static Task<IResult> GetAdminPageRevisionAsync(
        string applicationId,
        string entityId,
        int revision,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            revision < 1
                ? throw new ArgumentException("revision must be positive.")
                : (object?)await pages.GetRevisionAsync(
                    ApplicationIdentifier.Parse(applicationId), entityId, revision, cancellationToken));

    private static Task<IResult> AppendAdminPageDraftAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.AppendDraftAsync(
                ApplicationIdentifier.Parse(applicationId), entityId,
                await WebPageAdministrationRequestReader.ReadAsync<WebPageDraftAppendRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> PublishAdminPageBundleAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.PublishBundleAsync(
                ApplicationIdentifier.Parse(applicationId),
                entityId,
                await new WebPageBundleReader().ReadAsync(
                    context.Request.Body, context.Request.ContentLength, cancellationToken),
                cancellationToken));

    private static Task<IResult> AppendAdminPageBundleDraftAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.AppendBundleDraftAsync(
                ApplicationIdentifier.Parse(applicationId),
                entityId,
                ParseOptionalPositiveInt(
                    context.Request.Query["expectedLatestRevision"].FirstOrDefault(),
                    "expectedLatestRevision")
                    ?? throw new ArgumentException("expectedLatestRevision is required."),
                await new WebPageBundleReader().ReadAsync(
                    context.Request.Body, context.Request.ContentLength, cancellationToken),
                cancellationToken));

    private static Task<IResult> ActivateAdminPageRevisionAsync(
        string applicationId,
        string entityId,
        HttpContext context,
        WebPageAdministration pages,
        CancellationToken cancellationToken) =>
        PageAdministrationAsync(context, async () =>
            (object?)await pages.ActivateRevisionAsync(
                ApplicationIdentifier.Parse(applicationId), entityId,
                await WebPageAdministrationRequestReader.ReadAsync<WebPageRevisionActivationRequest>(
                    context.Request, cancellationToken), cancellationToken));

    private static async Task<IResult> PageAdministrationAsync(
        HttpContext context,
        Func<Task<object?>> action)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await action();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (IsPageAdministrationClientError(exception))
        {
            var (code, status) = exception switch
            {
                WebPageAdministrationException admin => (admin.Code, AdminStatus(admin.Code)),
                WebPageStoreException store => (store.Code, StoreStatus(store.Code)),
                WebPageBundleException bundle => (bundle.Code, bundle.StatusCode),
                EcsRoleConstraintException constraint => (constraint.Code, StatusCodes.Status409Conflict),
                EcsLifecycleException lifecycle => (lifecycle.Code, LifecycleStatus(lifecycle.Code)),
                WebPageAdministrationRequestException request => (request.Code, request.StatusCode),
                _ => ("INVALID_REQUEST", StatusCodes.Status400BadRequest)
            };
            return PageEditorError(code, exception.Message, status);
        }
    }

    private static bool IsPageAdministrationClientError(Exception exception) =>
        exception is WebPageAdministrationException or WebPageStoreException or WebPageBundleException or EcsRoleConstraintException or
            EcsLifecycleException or WebPageAdministrationRequestException or ArgumentException or JsonException;

    private static int AdminStatus(string code) => code.EndsWith("_UNKNOWN", StringComparison.Ordinal)
        ? StatusCodes.Status404NotFound
        : code.EndsWith("_EXISTS", StringComparison.Ordinal)
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

    private static int StoreStatus(string code) => code switch
    {
        "PAGE_UNKNOWN" or "REVISION_UNKNOWN" => StatusCodes.Status404NotFound,
        "PAGE_LATEST_STALE" or "PAGE_ACTIVE_STALE" or "PAGE_ALREADY_ACTIVE" or "CURSOR_STALE" =>
            StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    private static int LifecycleStatus(string code) => code.Contains("UNKNOWN", StringComparison.Ordinal)
        ? StatusCodes.Status404NotFound
        : StatusCodes.Status409Conflict;

    private static int? ParseOptionalPositiveInt(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value, out var parsed) || parsed < 1)
            throw new ArgumentException($"{name} must be a positive integer.");
        return parsed;
    }

    private static int ParseBoundedPositiveInt(string? value, int fallback, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, out var parsed) || parsed < 1 || parsed > maximum)
            throw new ArgumentException($"{name} must be an integer from 1 through {maximum}.");
        return parsed;
    }

    private static IResult PageEditorError(string code, string message, int status) =>
        Results.Json(new { error = code, message }, statusCode: status);
}
