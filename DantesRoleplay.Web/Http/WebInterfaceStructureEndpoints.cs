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
    private static void MapStructureRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDantesRoleplayControlGet(
            "/triggers/applications",
            PrivateOperatorCapability.TriggerAdministrationRead,
            GetTriggerApplicationsAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/triggers/applications/{applicationId}",
            PrivateOperatorCapability.TriggerAdministrationRead,
            GetTriggerApplicationAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/triggers/applications/{applicationId}/phone-principal/{deviceId}",
            PrivateOperatorCapability.TriggerAdministrationRead,
            GetPhonePrincipalAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/triggers/commands/preview",
            PrivateOperatorCapability.TriggerAdministrationWrite,
            PreviewTriggerCommandAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/triggers/commands",
            PrivateOperatorCapability.TriggerAdministrationWrite,
            ApplyTriggerCommandAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/applications", GetApplications);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}", GetApplication);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/state-spaces", GetStateSpaces);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/component-types", GetComponentTypes);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/component-types/{qualifiedId}/versions/{version:int}", GetComponentType);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/state-spaces/{stateSpaceId}/entities", GetEntitiesAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/state-spaces/{stateSpaceId}/entities/{entityId}", GetEntityAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components", GetComponentsAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}",
            GetComponentAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/catalog", GetCatalog);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/catalog/browse", BrowseCatalog);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/catalog/search", SearchCatalog);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/catalog/records/{qualifiedId}", InspectCatalog);
        endpoints.MapDantesRoleplayControlGet(
            "/structure/applications/{applicationId}/content", GetEffectiveApplicationContent);
    }

    private static Task<IResult> GetApplications(
        HttpContext context,
        ControlStructureExplorer explorer,
        CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListApplicationsThroughCapabilitiesAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context),
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetApplication(
        string applicationId,
        HttpContext context,
        ControlStructureExplorer explorer,
        CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetApplicationThroughCapabilitiesAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context),
            applicationId,
            cancellationToken));

    private static IResult GetStateSpaces(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.ListStateSpaces(
            applicationId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault()));

    private static IResult GetApplicationStateSpaces(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.ListApplicationStateSpaces(
            applicationId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault()));

    private static async Task<IResult> GetAuthorizedKnowledgeAsync(
        string applicationId,
        string campaignId,
        HttpContext context,
        [FromServices] KnowledgeApplicationSelection application,
        [FromServices] IAuthorizedKnowledgeNotebookReader notebook,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!string.Equals(applicationId, application.ApplicationId, StringComparison.Ordinal))
            return Results.NotFound();

        var result = await notebook.ReadAsync(
            new AuthorizedKnowledgeNotebookRequest(campaignId), cancellationToken);
        return result.Status switch
        {
            "ready" or "empty" => Results.Json(new
            {
                status = result.Status,
                entries = result.Entries.Select(KnowledgeNotebookEntry).ToArray(),
                locations = result.Locations.Select(location => new
                {
                    name = location.Name,
                    entries = location.Entries.Select(KnowledgeNotebookEntry).ToArray()
                }).ToArray()
            }),
            "invalid" => Results.Json(new { error = "INVALID_KNOWLEDGE_REQUEST" },
                statusCode: StatusCodes.Status400BadRequest),
            "denied" => Results.Json(new { error = "KNOWLEDGE_UNAVAILABLE" },
                statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(new { error = "KNOWLEDGE_UNAVAILABLE" },
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static IReadOnlyDictionary<string, object> KnowledgeNotebookEntry(
        AuthorizedKnowledgeNotebookEntry value)
    {
        var entry = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["text"] = value.Text,
            ["stance"] = value.Stance,
            ["presentationKind"] = value.PresentationKind
        };
        if (value.MediaOwnerId is not null)
            entry["mediaOwnerId"] = value.MediaOwnerId;
        if (value.Subject is not null)
            entry["subject"] = new { id = value.Subject.Id, name = value.Subject.Name };
        return entry;
    }

    private static Task<IResult> GetApplicationEntitiesAsync(
        string applicationId, string stateSpaceId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListApplicationEntitiesAsync(
            applicationId,
            stateSpaceId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetApplicationContainmentsAsync(
        string applicationId, string stateSpaceId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListApplicationContainmentsAsync(
            applicationId,
            stateSpaceId,
            context.Request.Query["containerEntityId"].FirstOrDefault() ?? string.Empty,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetApplicationRelationshipsAsync(
        string applicationId, string stateSpaceId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken)
    {
        var fromEntityId = context.Request.Query["fromEntityId"].FirstOrDefault();
        var toEntityId = context.Request.Query["toEntityId"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromEntityId) && !string.IsNullOrWhiteSpace(toEntityId))
            return StructureAsync<StructurePage<RelationshipSummary>>(context, () =>
                Task.FromException<StructurePage<RelationshipSummary>>(
                    new ArgumentException("Supply either fromEntityId or toEntityId, not both.")));
        return !string.IsNullOrWhiteSpace(toEntityId)
            ? StructureAsync(context, () => explorer.ListApplicationIncomingRelationshipsAsync(
                applicationId,
                stateSpaceId,
                toEntityId,
                context.Request.Query["qualifiedKind"].FirstOrDefault() ?? string.Empty,
                context.Request.Query["cursor"].FirstOrDefault(),
                context.Request.Query["limit"].FirstOrDefault(),
                cancellationToken))
            : StructureAsync(context, () => explorer.ListApplicationRelationshipsAsync(
                applicationId,
                stateSpaceId,
                fromEntityId ?? string.Empty,
                context.Request.Query["qualifiedKind"].FirstOrDefault() ?? string.Empty,
                context.Request.Query["cursor"].FirstOrDefault(),
                context.Request.Query["limit"].FirstOrDefault(),
                cancellationToken));
    }

    private static Task<IResult> GetApplicationEntityAsync(
        string applicationId, string stateSpaceId, string entityId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetApplicationEntityAsync(
            applicationId, stateSpaceId, entityId, cancellationToken));

    private static Task<IResult> GetApplicationContainmentAsync(
        string applicationId, string stateSpaceId, string entityId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetApplicationContainmentAsync(
            applicationId, stateSpaceId, entityId, cancellationToken));

    private static Task<IResult> GetApplicationComponentsAsync(
        string applicationId, string stateSpaceId, string entityId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListApplicationComponentsAsync(
            applicationId,
            stateSpaceId,
            entityId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetApplicationComponentAsync(
        string applicationId, string stateSpaceId, string entityId, string qualifiedTypeId,
        HttpContext context, ControlStructureExplorer explorer,
        CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetApplicationComponentAsync(
            applicationId, stateSpaceId, entityId, qualifiedTypeId, cancellationToken));

    private static IResult GetComponentTypes(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.ListComponentTypes(
            applicationId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault()));

    private static IResult GetComponentType(
        string qualifiedId, int version, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.GetComponentType(qualifiedId, version));

    private static Task<IResult> GetEntitiesAsync(
        string stateSpaceId, HttpContext context, ControlStructureExplorer explorer,
        CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListEntitiesAsync(
            stateSpaceId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetEntityAsync(
        string stateSpaceId, string entityId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetEntityAsync(
            stateSpaceId, entityId, cancellationToken));

    private static Task<IResult> GetComponentsAsync(
        string stateSpaceId, string entityId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListComponentsAsync(
            stateSpaceId,
            entityId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetComponentAsync(
        string stateSpaceId, string entityId, string qualifiedTypeId, HttpContext context,
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.GetComponentAsync(
            stateSpaceId, entityId, qualifiedTypeId, cancellationToken));

    private static IResult GetCatalog(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.GetCatalog(applicationId));

    private static IResult BrowseCatalog(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.BrowseCatalog(
            applicationId,
            context.Request.Query["collection"].FirstOrDefault(),
            context.Request.Query["branch"].FirstOrDefault(),
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault()));

    private static IResult SearchCatalog(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.SearchCatalog(
            applicationId,
            context.Request.Query["q"].FirstOrDefault(),
            context.Request.Query["collection"].FirstOrDefault(),
            context.Request.Query["branch"].FirstOrDefault(),
            QueryValues(context, "kind"),
            QueryValues(context, "status"),
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            context.Request.Query["namespaceId"].FirstOrDefault(),
            bool.TryParse(context.Request.Query["includeShadowed"].FirstOrDefault(), out var includeShadowed)
                && includeShadowed));

    private static IResult InspectCatalog(
        string applicationId, string qualifiedId, HttpContext context,
        ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.InspectCatalog(
            applicationId,
            context.Request.Query["collection"].FirstOrDefault(),
            qualifiedId));

    private static IResult GetEffectiveApplicationContent(
        string applicationId, HttpContext context, ControlStructureExplorer explorer) =>
        Structure(context, () =>
        {
            var extensionsOnly = false;
            var extensionsOnlyValue = context.Request.Query["extensionsOnly"].FirstOrDefault();
            if (extensionsOnlyValue is not null && !bool.TryParse(extensionsOnlyValue, out extensionsOnly))
                throw new ArgumentException("extensionsOnly must be true or false.");
            return explorer.GetEffectiveApplicationContent(
                applicationId,
                context.Request.Query["cursor"].FirstOrDefault(),
                context.Request.Query["limit"].FirstOrDefault(),
                context.Request.Query["owner"].FirstOrDefault(),
                QueryValues(context, "kind"),
                context.Request.Query["query"].FirstOrDefault(),
                extensionsOnly,
                QueryValues(context, "component"),
                QueryValues(context, "componentAny"),
                QueryValues(context, "archetype"),
                QueryValues(context, "id"),
                QueryValues(context, "referenceAny"));
        });

    private static IResult GetReadableRules(
        string applicationId,
        HttpContext context,
        ControlStructureExplorer explorer,
        IWebReadableRulesAudienceProvider audience) =>
        Structure(context, () => explorer.GetReadableRules(applicationId, audience.Current()));

    private static IReadOnlyList<string> QueryValues(HttpContext context, string key) =>
        context.Request.Query[key]
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

    private static Task<IResult> GetTriggerApplicationsAsync(HttpContext context,
        ITriggerSchedulingAdministrationService administration, CancellationToken cancellationToken) =>
        TriggerAdministrationAsync(context, async () => await administration.QueryAsync(
            TriggerSchedulingAdministrationQuery.Create(null, limit: QueryLimit(context)), cancellationToken));

    private static Task<IResult> GetTriggerApplicationAsync(string applicationId, HttpContext context,
        ITriggerSchedulingAdministrationService administration, CancellationToken cancellationToken) =>
        TriggerAdministrationAsync(context, async () => await administration.QueryAsync(
            TriggerSchedulingAdministrationQuery.Create(ApplicationIdentifier.Parse(applicationId),
                context.Request.Query["resource"].FirstOrDefault(),
                context.Request.Query["id"].FirstOrDefault(), QueryLimit(context)), cancellationToken));

    private static Task<IResult> GetPhonePrincipalAsync(string applicationId, string deviceId,
        HttpContext context, ITriggerSchedulingAdministrationService administration,
        CancellationToken cancellationToken) => TriggerAdministrationAsync(context,
            async () => await administration.QueryAsync(TriggerSchedulingAdministrationQuery.Create(
                ApplicationIdentifier.Parse(applicationId), "phone-principal", deviceId, 1), cancellationToken));

    private static Task<IResult> PreviewTriggerCommandAsync(HttpContext context,
        ITriggerSchedulingAdministrationService administration, CancellationToken cancellationToken) =>
        TriggerCommandAsync(context, administration, preview: true, cancellationToken);

    private static Task<IResult> ApplyTriggerCommandAsync(HttpContext context,
        ITriggerSchedulingAdministrationService administration, CancellationToken cancellationToken) =>
        TriggerCommandAsync(context, administration, preview: false, cancellationToken);

    private static async Task<IResult> TriggerCommandAsync(HttpContext context,
        ITriggerSchedulingAdministrationService administration, bool preview,
        CancellationToken cancellationToken)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var command = await TriggerAdministrationHttpRequestReader.ReadAsync(context.Request, cancellationToken);
            var authorization = WebControlRequestFilter.GetAuthorizationEvidence(context);
            var operationContext = new TriggerSchedulingAdministrationContext(
                "Manage trigger scheduling from the private control center.",
                ["procedure.system.use"], authorization);
            var result = preview
                ? await administration.PreviewAsync(command, operationContext, cancellationToken)
                : await administration.CommitAsync(command, operationContext, cancellationToken);
            return Results.Json(result);
        }
        catch (Exception exception) when (IsTriggerAdministrationClientError(exception))
        { return TriggerAdministrationError(exception); }
    }

    private static async Task<IResult> TriggerAdministrationAsync(HttpContext context,
        Func<Task<TriggerSchedulingAdministrationView>> action)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try { return Results.Json(await action()); }
        catch (Exception exception) when (IsTriggerAdministrationClientError(exception))
        { return TriggerAdministrationError(exception); }
    }

    private static int QueryLimit(HttpContext context) =>
        int.TryParse(context.Request.Query["limit"].FirstOrDefault(), out var limit) ? limit : 50;

    private static bool IsTriggerAdministrationClientError(Exception exception) => exception is
        TriggerSchedulingAdministrationException or TriggerSchedulingContractException or
        ArgumentException or JsonException or InvalidOperationException;

    private static IResult TriggerAdministrationError(Exception exception)
    {
        var code = exception switch
        {
            TriggerSchedulingAdministrationException administration => administration.Code,
            TriggerSchedulingContractException contract => contract.Code,
            _ => "TRIGGER_ADMIN_INVALID_REQUEST"
        };
        var status = code switch
        {
            "APPLICATION_UNKNOWN" or "PHONE_DEVICE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "DRY_RUN_REQUIRED" or "REQUEST_TOKEN_CONFLICT" or "TRIGGER_ADMIN_INCONSISTENT" or
                "TRIGGER_SCHEDULING_IDEMPOTENCY_CONFLICT" => StatusCodes.Status409Conflict,
            "TRIGGER_ADMIN_PAYLOAD_TOO_LARGE" => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { error = code, message = exception.Message }, statusCode: status);
    }

    private static IResult Structure<T>(HttpContext context, Func<T?> read)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (IsStructureClientError(exception))
        {
            return StructureError(exception);
        }
    }

    private static async Task<IResult> StructureAsync<T>(HttpContext context, Func<Task<T>> read)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (IsStructureClientError(exception))
        {
            return StructureError(exception);
        }
    }

    private static bool IsStructureClientError(Exception exception) =>
        exception is ControlStructureException or CatalogNamespaceException or ArgumentException or KeyNotFoundException ||
        exception is InvalidOperationException { Message: "CURSOR_STALE" };

    private static IResult StructureError(Exception exception)
    {
        var (code, status) = exception switch
        {
            ControlStructureException control => (control.Code, control.StatusCode),
            CatalogNamespaceException catalogNamespace => (catalogNamespace.Code, StatusCodes.Status400BadRequest),
            InvalidOperationException => ("CURSOR_STALE", StatusCodes.Status409Conflict),
            KeyNotFoundException => ("STRUCTURE_RECORD_UNKNOWN", StatusCodes.Status404NotFound),
            ArgumentException argument when argument.Message.Contains("CATALOG_COLLECTION_UNKNOWN", StringComparison.Ordinal) =>
                ("CATALOG_COLLECTION_UNKNOWN", StatusCodes.Status404NotFound),
            ArgumentException argument when argument.Message.Contains("CURSOR_INVALID", StringComparison.Ordinal) =>
                ("CURSOR_INVALID", StatusCodes.Status400BadRequest),
            _ => ("INVALID_REQUEST", StatusCodes.Status400BadRequest)
        };
        return Results.Json(new { error = code, message = exception.Message }, statusCode: status);
    }
}
