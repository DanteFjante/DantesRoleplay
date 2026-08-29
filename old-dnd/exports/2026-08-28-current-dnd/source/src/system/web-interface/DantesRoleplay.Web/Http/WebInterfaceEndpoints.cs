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
using DantesRoleplay.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.Knowledge;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Web.Interactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Hosting;

public static class WebInterfaceEndpoints
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
        Secure(endpoints.MapPut("/api/pages/{id}", UploadPageAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapPut("/api/pages/{id}/bundle", UploadBundleAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapGet("/", GetHomePageAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/ui/{id}", GetPageAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/ui/{id}/index.html", GetPageAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/ui/{id}/assets/{**path}", GetAssetAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/data/entity/{id}", GetEntityDataAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/data/{componentType}/{entityId}", GetComponentDataAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/changes", StreamChangesAsync), WebInterfaceSecurity.StreamRateLimitPolicy);
        Secure(endpoints.MapGet("/api/session", GetSession), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/components/application-conversation.js", GetApplicationConversationElement), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/components/system-workspace.js", GetSystemWorkspaceElement), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/components/{name}.js", GetBrowserComponentAssetAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/catalog/records/{qualifiedId}", InspectCatalog), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}", GetApplicationMechanicAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapPost("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/prepare", PrepareApplicationMechanicAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapPost("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/execute", ExecuteApplicationMechanicAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces", GetApplicationStateSpaces), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/campaigns/{campaignId}/knowledge", GetAuthorizedKnowledgeAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/containments", GetApplicationContainmentsAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/relationships", GetApplicationRelationshipsAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities", GetApplicationEntitiesAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}", GetApplicationEntityAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/containment", GetApplicationContainmentAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components", GetApplicationComponentsAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}", GetApplicationComponentAsync), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapGet("/api/applications/{applicationId}/conversations/{conversationId}", GetApplicationConversation), WebInterfaceSecurity.ReadRateLimitPolicy);
        Secure(endpoints.MapPost("/api/applications/{applicationId}/conversations", CreateApplicationConversationAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapPost("/api/applications/{applicationId}/conversations/{conversationId}/turns", SendApplicationConversationTurnAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        Secure(endpoints.MapPost("/api/applications/{applicationId}/conversations/{conversationId}/execute", ExecuteApplicationConversationAsync), WebInterfaceSecurity.UploadRateLimitPolicy);
        endpoints.MapPost("/api/applications/{applicationId}/observations", SubmitObservationAsync)
            .AddEndpointFilter<WebObservationRequestFilter>()
            .RequireRateLimiting(WebInterfaceSecurity.UploadRateLimitPolicy);
        endpoints.MapDantesRoleplayControlGet("/status", GetControlCenterStatus);
        endpoints.MapDantesRoleplayControlGet("/settings", GetControlSettings);
        endpoints.MapDantesRoleplayControlGet("/settings/{key}", GetControlSetting);
        endpoints.MapDantesRoleplayControlGet("/settings/{key}/versions", GetControlSettingVersionsAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/settings/{key}", PrivateOperatorCapability.ControlSettingsWrite, UpdateControlSettingAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/settings/{key}/reset", PrivateOperatorCapability.ControlSettingsWrite, ResetControlSettingAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/settings/{key}/rollback", PrivateOperatorCapability.ControlSettingsWrite, RollbackControlSettingAsync);
        endpoints.MapDantesRoleplayControlGet("/assistants/local/status", GetLocalAssistantStatusAsync);
        endpoints.MapDantesRoleplayControlGet("/assistants/codex/status", GetCodexAssistantStatusAsync);
        endpoints.MapDantesRoleplayControlGet("/conversations", GetAssistantConversationsAsync);
        endpoints.MapDantesRoleplayControlGet("/conversations/{conversationId}", GetAssistantConversationAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/conversations", PrivateOperatorCapability.ControlAiMessage, CreateAssistantConversationAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/conversations/{conversationId}/turns", PrivateOperatorCapability.ControlAiMessage, SendAssistantTurnAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/conversations/{conversationId}/turns/{turnId}/cancel",
            PrivateOperatorCapability.ControlAiMessage, CancelCodexTurnAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/conversations/{conversationId}/turns/{turnId}/approvals/{approvalId}",
            PrivateOperatorCapability.ControlCodexApprove, DecideCodexApprovalAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/system/conversations", GetSystemConversationsAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/system/conversations/{conversationId}", GetSystemConversationAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/conversations", PrivateOperatorCapability.ControlAiMessage,
            CreateSystemConversationAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/conversations/{conversationId}/turns", PrivateOperatorCapability.ControlAiMessage,
            SendSystemConversationTurnAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/system/conversations/{conversationId}/tasks", GetSystemTasksAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/conversations/{conversationId}/tasks", PrivateOperatorCapability.ControlAiMessage,
            PrepareSystemTaskAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/system/tasks/{taskId}", GetSystemTaskAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/tasks/{taskId}/confirmations", PrivateOperatorCapability.Modify,
            ConfirmSystemTaskAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/tasks/{taskId}/executions", PrivateOperatorCapability.Modify,
            ExecuteSystemTaskAsync);
        endpoints.MapDantesRoleplayControlGet(
            "/system/capabilities/{capabilityId}", GetSystemCapability);
        endpoints.MapDantesRoleplayControlGet("/effects", GetCommittedEffectsAsync);
        endpoints.MapDantesRoleplayControlGet("/effects/{eventId}", GetCommittedEffectAsync);
        endpoints.MapDantesRoleplayControlGet("/triggers/applications",
            PrivateOperatorCapability.TriggerAdministrationRead, GetTriggerApplicationsAsync);
        endpoints.MapDantesRoleplayControlGet("/triggers/applications/{applicationId}",
            PrivateOperatorCapability.TriggerAdministrationRead, GetTriggerApplicationAsync);
        endpoints.MapDantesRoleplayControlGet("/triggers/applications/{applicationId}/phone-principal/{deviceId}",
            PrivateOperatorCapability.TriggerAdministrationRead, GetPhonePrincipalAsync);
        endpoints.MapDantesRoleplayControlPost("/triggers/commands/preview",
            PrivateOperatorCapability.TriggerAdministrationWrite, PreviewTriggerCommandAsync);
        endpoints.MapDantesRoleplayControlPost("/triggers/commands",
            PrivateOperatorCapability.TriggerAdministrationWrite, ApplyTriggerCommandAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/applications", GetApplications);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}", GetApplication);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/state-spaces", GetStateSpaces);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/component-types", GetComponentTypes);
        endpoints.MapDantesRoleplayControlGet("/structure/component-types/{qualifiedId}/versions/{version:int}", GetComponentType);
        endpoints.MapDantesRoleplayControlGet("/structure/state-spaces/{stateSpaceId}/entities", GetEntitiesAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/state-spaces/{stateSpaceId}/entities/{entityId}", GetEntityAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components", GetComponentsAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}", GetComponentAsync);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/catalog", GetCatalog);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/catalog/browse", BrowseCatalog);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/catalog/search", SearchCatalog);
        endpoints.MapDantesRoleplayControlGet("/structure/applications/{applicationId}/catalog/records/{qualifiedId}", InspectCatalog);
        endpoints.MapDantesRoleplayControlGet("/pages", GetControlPagesAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}", GetControlPageAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}/revisions", GetControlPageRevisionsAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}/revisions/{revision:int}", GetControlPageRevisionAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}/revisions/{revision:int}/bundle", ExportControlPageRevisionAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}/revisions/{revision:int}/preview/index.html", PreviewControlPageRevisionAsync);
        endpoints.MapDantesRoleplayControlGet("/pages/{pageId}/revisions/{revision:int}/preview/assets/{**path}", PreviewControlPageAssetAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/pages/{pageId}/drafts", PrivateOperatorCapability.ControlPagesWrite, AppendControlPageDraftAsync);
        endpoints.MapDantesRoleplayControlPut(
            "/pages/{pageId}/active", PrivateOperatorCapability.ControlPagesWrite, ActivateControlPageRevisionAsync);
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route, string rateLimitPolicy) =>
        route
            .AddEndpointFilter<WebInterfaceSecurityFilter>()
            .RequireRateLimiting(rateLimitPolicy);

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

    private static IResult GetApplicationConversationElement() => Results.Text(
        ApplicationConversationElement.Script, "text/javascript; charset=utf-8", Encoding.UTF8);

    private static IResult GetSystemWorkspaceElement() => Results.Text(
        SystemWorkspaceElement.Script, "text/javascript; charset=utf-8", Encoding.UTF8);

    private static async Task<IResult> GetBrowserComponentAssetAsync(
        string name, CancellationToken cancellationToken)
    {
        var script = await BrowserComponentAssets.ReadAsync(name, cancellationToken);
        return script is null
            ? Results.NotFound()
            : Results.Text(script, "text/javascript; charset=utf-8", Encoding.UTF8);
    }

    private static async Task<IResult> SubmitObservationAsync(
        string applicationId,
        HttpContext context,
        ObservationHttpRequestReader reader,
        IObservationIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var submission = await reader.ReadAsync(context.Request, cancellationToken);
            var result = await ingestion.SubmitAsync(
                WebObservationRequestFilter.GetPrincipal(context),
                ApplicationIdentifier.Parse(applicationId),
                submission,
                cancellationToken);
            if (result.Disposition == TriggerSchedulingWriteDisposition.Conflict)
                return ObservationError("OBSERVATION_IDENTITY_CONFLICT",
                    "The request or occurrence identity was already used for different observation data.",
                    StatusCodes.Status409Conflict);
            return Results.Json(new
            {
                observationId = result.Value!.Id,
                accepted = true,
                duplicate = result.Disposition == TriggerSchedulingWriteDisposition.Replay,
                status = "recorded"
            }, statusCode: StatusCodes.Status202Accepted);
        }
        catch (ObservationHttpRequestException exception)
        {
            return ObservationError(exception.Code, exception.Message, exception.StatusCode);
        }
        catch (ObservationIngestionException exception)
        {
            return ObservationError(exception.Code, exception.Message, IngestionStatus(exception.Code));
        }
        catch (TriggerSchedulingContractException exception)
        {
            return ObservationError(exception.Code, exception.Message, ContractStatus(exception.Code));
        }
        catch (ArgumentException exception)
        {
            return ObservationError("OBSERVATION_REQUEST_INVALID", exception.Message,
                StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ObservationError("OBSERVATION_RECORDING_UNAVAILABLE",
                "The observation could not be durably recorded. Try again shortly.",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static int IngestionStatus(string code) => code switch
    {
        "OBSERVATION_RATE_LIMITED" => StatusCodes.Status429TooManyRequests,
        "OBSERVATION_SCHEMA_INVALID" => StatusCodes.Status422UnprocessableEntity,
        "OBSERVATION_SCHEMA_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
        "TRIGGER_SCHEDULING_APPLICATION_NOT_FOUND" or
        "TRIGGER_SCHEDULING_SOURCE_NOT_FOUND" or
        "TRIGGER_SCHEDULING_STRUCTURE_NOT_FOUND" or
        "TRIGGER_SCHEDULING_OBSERVATION_STALE" => StatusCodes.Status404NotFound,
        "OBSERVATION_PRINCIPAL_REQUIRED" or
        "OBSERVATION_PRINCIPAL_FORBIDDEN" or
        "PHONE_SUBMISSION_DENIED" or
        "OBSERVATION_SOURCE_DISABLED" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    private static int ContractStatus(string code) => code switch
    {
        "TRIGGER_SCHEDULING_APPLICATION_NOT_FOUND" or
        "TRIGGER_SCHEDULING_SOURCE_NOT_FOUND" or
        "TRIGGER_SCHEDULING_STRUCTURE_NOT_FOUND" or
        "TRIGGER_SCHEDULING_OBSERVATION_STALE" => StatusCodes.Status404NotFound,
        "OBSERVATION_PRINCIPAL_REQUIRED" or
        "OBSERVATION_PRINCIPAL_FORBIDDEN" or
        "OBSERVATION_SOURCE_DISABLED" or
        "OBSERVATION_STRUCTURE_FORBIDDEN" => StatusCodes.Status403Forbidden,
        "TRIGGER_CLOCK_NOT_UTC" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };

    private static IResult ObservationError(string code, string message, int statusCode) =>
        Results.Json(new { error = code, message }, statusCode: statusCode);

    private static IResult GetApplicationConversation(
        string applicationId, string conversationId, HttpContext context,
        ApplicationConversationService conversations) =>
        InteractionWeb(() => conversations.Get(InteractionPrincipal(context),
            ApplicationIdentifier.Parse(applicationId), conversationId));

    private static async Task<IResult> CreateApplicationConversationAsync(
        string applicationId, HttpContext context, ApplicationConversationService conversations,
        CancellationToken cancellationToken) =>
        await InteractionWebAsync(async () => conversations.Create(InteractionPrincipal(context),
            ApplicationIdentifier.Parse(applicationId),
            await ReadInteractionBodyAsync<ApplicationConversationCreateRequest>(context, cancellationToken)));

    private static async Task<IResult> SendApplicationConversationTurnAsync(
        string applicationId, string conversationId, HttpContext context,
        ApplicationConversationService conversations, CancellationToken cancellationToken) =>
        await InteractionWebAsync(async () => await conversations.TurnAsync(InteractionPrincipal(context),
            ApplicationIdentifier.Parse(applicationId), conversationId,
            await ReadInteractionBodyAsync<ApplicationConversationTurnRequest>(context, cancellationToken),
            cancellationToken));

    private static async Task<IResult> ExecuteApplicationConversationAsync(
        string applicationId, string conversationId, HttpContext context,
        ApplicationConversationService conversations, CancellationToken cancellationToken) =>
        await InteractionWebAsync(async () => await conversations.ExecuteAsync(InteractionPrincipal(context),
            ApplicationIdentifier.Parse(applicationId), conversationId,
            await ReadInteractionBodyAsync<ApplicationConversationExecuteRequest>(context, cancellationToken),
            cancellationToken));

    private static Task<IResult> GetApplicationMechanicAsync(
        string applicationId, string stateSpaceId, string qualifiedMechanicId,
        HttpContext context, ApplicationMechanicWebService mechanics,
        CancellationToken cancellationToken) =>
        ApplicationMechanicWebAsync(context, async () => await mechanics.DescribeAsync(
            ApplicationIdentifier.Parse(applicationId), stateSpaceId, qualifiedMechanicId,
            cancellationToken));

    private static Task<IResult> PrepareApplicationMechanicAsync(
        string applicationId, string stateSpaceId, string qualifiedMechanicId,
        HttpContext context, ApplicationMechanicWebService mechanics,
        CancellationToken cancellationToken) =>
        ApplicationMechanicWebAsync(context, async () => await mechanics.PrepareAsync(
            InteractionPrincipal(context), ApplicationIdentifier.Parse(applicationId),
            stateSpaceId, qualifiedMechanicId,
            await ReadApplicationMechanicBodyAsync<ApplicationMechanicPrepareRequest>(context, cancellationToken),
            cancellationToken));

    private static Task<IResult> ExecuteApplicationMechanicAsync(
        string applicationId, string stateSpaceId, string qualifiedMechanicId,
        HttpContext context, ApplicationMechanicWebService mechanics,
        CancellationToken cancellationToken) =>
        ApplicationMechanicWebAsync(context, async () => await mechanics.ExecuteAsync(
            InteractionPrincipal(context), ApplicationIdentifier.Parse(applicationId),
            stateSpaceId, qualifiedMechanicId,
            await ReadApplicationMechanicBodyAsync<ApplicationMechanicExecuteRequest>(context, cancellationToken),
            cancellationToken));

    private static DantesRoleplay.Authorization.TrustedPrincipalContext InteractionPrincipal(HttpContext context)
    {
        var tailscale = string.Equals(context.User.Identity?.AuthenticationType,
            WebAccessPolicy.TailscaleAuthenticationType, StringComparison.Ordinal);
        return PrivateOperatorPrincipal.Create(tailscale ? "tailscale-serve" : "local-loopback",
            tailscale ? context.User.Identity?.Name ?? "tailscale-operator" : "local-operator");
    }

    private static IResult InteractionWeb<T>(Func<T?> read)
    {
        try
        {
            var value = read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException)
        {
            var code = exception is InteractionContractException contract ? contract.Code : "INTERACTION_REQUEST_INVALID";
            return Results.Json(new { error = code, message = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> InteractionWebAsync<T>(Func<Task<T?>> read)
    {
        try
        {
            var value = await read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or JsonException)
        {
            var code = exception is InteractionContractException contract ? contract.Code : "INTERACTION_REQUEST_INVALID";
            return Results.Json(new { error = code, message = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ApplicationMechanicWebAsync<T>(
        HttpContext context, Func<Task<T>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Json(await action());
        }
        catch (ApplicationMechanicWebException exception)
        {
            return Results.Json(new { error = exception.Code, message = exception.Message },
                statusCode: exception.StatusCode);
        }
        catch (InteractionContractException exception)
        {
            var status = exception.Code.Contains("CONFLICT", StringComparison.Ordinal)
                || exception.Code.Contains("STALE", StringComparison.Ordinal)
                ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return Results.Json(new { error = exception.Code, message = exception.Message },
                statusCode: status);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            return Results.Json(new { error = "APPLICATION_ACTION_REQUEST_INVALID", message = exception.Message },
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<T> ReadApplicationMechanicBodyAsync<T>(
        HttpContext context, CancellationToken cancellationToken)
    {
        const int maximumBytes = 64 * 1024;
        if (context.Request.ContentLength > maximumBytes)
            throw new InteractionContractException("INTERACTION_REQUEST_TOO_LARGE",
                "The application action request is too large.");
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (stream.Length + read > maximumBytes)
                throw new InteractionContractException("INTERACTION_REQUEST_TOO_LARGE",
                    "The application action request is too large.");
            stream.Write(buffer, 0, read);
        }
        if (stream.Length == 0)
            throw new InteractionContractException("INTERACTION_REQUEST_INVALID",
                "The application action request body is required.");
        var json = StrictInteractionUtf8.GetString(stream.ToArray());
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        return JsonSerializer.Deserialize<T>(canonical, StrictInteractionJson)
            ?? throw new InteractionContractException("INTERACTION_REQUEST_INVALID",
                "The application action request body is required.");
    }

    private static async Task<T> ReadInteractionBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        const int maximumBytes = 64 * 1024;
        if (context.Request.ContentLength > maximumBytes)
            throw new InteractionContractException("INTERACTION_REQUEST_TOO_LARGE", "The interaction request is too large.");
        return await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InteractionContractException("INTERACTION_REQUEST_INVALID", "The interaction request body is required.");
    }

    private static IResult GetControlCenterStatus(HttpContext context)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        return Results.Json(ControlCenterStatus.Create(context.User));
    }

    private static Task<IResult> GetControlSettings(
        HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, async () => (ControlSettingPage?)await explorer.ListAsync(cancellationToken));

    private static Task<IResult> GetControlSetting(
        string key, HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, () => explorer.GetAsync(key, cancellationToken));

    private static Task<IResult> GetControlSettingVersionsAsync(
        string key, HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, async () => (ControlSettingVersionPage?)await explorer.ListVersionsAsync(
            key, context.Request.Query["beforeVersion"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(), cancellationToken));

    private static Task<IResult> UpdateControlSettingAsync(
        string key, HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, async () => await explorer.UpdateAsync(
            key, await ControlSettingsExplorer.ReadBodyAsync<ControlSettingUpdateRequest>(context.Request, cancellationToken),
            SettingActor(context), cancellationToken));

    private static Task<IResult> ResetControlSettingAsync(
        string key, HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, async () => await explorer.ResetAsync(
            key, await ControlSettingsExplorer.ReadBodyAsync<ControlSettingResetRequest>(context.Request, cancellationToken),
            SettingActor(context), cancellationToken));

    private static Task<IResult> RollbackControlSettingAsync(
        string key, HttpContext context, ControlSettingsExplorer explorer, CancellationToken cancellationToken) =>
        SettingsAsync(context, async () => await explorer.RollbackAsync(
            key, await ControlSettingsExplorer.ReadBodyAsync<ControlSettingRollbackRequest>(context.Request, cancellationToken),
            SettingActor(context), cancellationToken));

    private static string SettingActor(HttpContext context) => string.Equals(
        context.User.Identity?.AuthenticationType, WebAccessPolicy.TailscaleAuthenticationType, StringComparison.Ordinal)
        ? context.User.Identity?.Name ?? "tailscale-operator"
        : "local-operator";

    private static async Task<IResult> SettingsAsync<T>(HttpContext context, Func<Task<T?>> read)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await read();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (ControlSettingsException exception)
        {
            return Results.Json(
                new { error = exception.Code, message = exception.Message },
                statusCode: exception.StatusCode);
        }
    }

    private static Task<IResult> GetLocalAssistantStatusAsync(
        HttpContext context, ControlAssistantExplorer explorer, CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await explorer.StatusAsync(cancellationToken));

    private static Task<IResult> GetCodexAssistantStatusAsync(
        HttpContext context, ICodexConversationService codex, CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await codex.GetStatusAsync(cancellationToken));

    private static Task<IResult> GetAssistantConversationsAsync(
        HttpContext context, ControlAssistantExplorer explorer, CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await explorer.ListAsync(
            AssistantOperatorId(context), context.Request.Query["provider"].FirstOrDefault(),
            context.Request.Query["cursor"].FirstOrDefault(), context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetAssistantConversationAsync(
        string conversationId, HttpContext context, ControlAssistantExplorer explorer,
        CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await explorer.GetAsync(
            AssistantOperatorId(context), conversationId, cancellationToken));

    private static async Task<IResult> CreateAssistantConversationAsync(
        HttpContext context, ControlAssistantExplorer explorer, ICodexConversationService codex,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<AssistantConversationCreate>(
                context.Request, cancellationToken);
            if (request.Provider == "codex") return await StreamCodexAsync(
                context, codex.CreateAsync(AssistantOperatorId(context), request, cancellationToken), cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.CreateAsync(
                AssistantOperatorId(context), request, cancellationToken));
        }
        catch (Exception exception) when (exception is ControlAssistantException or CodexBridgeException)
        { return AssistantError(exception); }
    }

    private static async Task<IResult> SendAssistantTurnAsync(
        string conversationId, HttpContext context, ControlAssistantExplorer explorer,
        ICodexConversationService codex, CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<AssistantConversationTurnCreate>(
                context.Request, cancellationToken);
            var current = await explorer.GetAsync(AssistantOperatorId(context), conversationId, cancellationToken);
            if (current is null) return Results.NotFound();
            if (current.Summary.Provider == "codex") return await StreamCodexAsync(
                context, codex.SendAsync(AssistantOperatorId(context), conversationId, request, cancellationToken),
                cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.SendAsync(
                AssistantOperatorId(context), conversationId, request, cancellationToken));
        }
        catch (Exception exception) when (exception is ControlAssistantException or CodexBridgeException)
        { return AssistantError(exception); }
    }

    private static Task<IResult> CancelCodexTurnAsync(
        string conversationId, string turnId, HttpContext context, ICodexConversationService codex,
        CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await codex.CancelAsync(
            AssistantOperatorId(context), conversationId, turnId, cancellationToken));

    private static Task<IResult> DecideCodexApprovalAsync(
        string conversationId, string turnId, string approvalId,
        HttpContext context, ICodexConversationService codex, CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await codex.ApproveAsync(
            AssistantOperatorId(context), conversationId, turnId, approvalId,
            await ControlAssistantExplorer.ReadBodyAsync<CodexApprovalDecisionInput>(
                context.Request, cancellationToken), cancellationToken));

    private static Task<IResult> GetSystemConversationsAsync(
        HttpContext context,
        ControlSystemConversationExplorer explorer,
        CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await explorer.ListAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context),
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetSystemConversationAsync(
        string conversationId,
        HttpContext context,
        ControlSystemConversationExplorer explorer,
        CancellationToken cancellationToken) =>
        AssistantAsync(context, async () => (object?)await explorer.GetAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context),
            conversationId,
            cancellationToken));

    private static async Task<IResult> CreateSystemConversationAsync(
        HttpContext context,
        ControlSystemConversationExplorer explorer,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<SystemConversationCreate>(
                context.Request, cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.CreateAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context),
                request,
                cancellationToken));
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static async Task<IResult> SendSystemConversationTurnAsync(
        string conversationId,
        HttpContext context,
        ControlSystemConversationExplorer explorer,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<AssistantConversationTurnCreate>(
                context.Request, cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.SendAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context),
                conversationId,
                request,
                cancellationToken));
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static Task<IResult> GetSystemTasksAsync(
        string conversationId, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken) => AssistantAsync(context, async () => (object?)await explorer.ListAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context), conversationId,
            context.Request.Query["cursor"].FirstOrDefault(), context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetSystemTaskAsync(
        string taskId, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken) => AssistantAsync(context, async () => (object?)await explorer.GetAsync(
            WebControlRequestFilter.GetAuthorizationEvidence(context), taskId, cancellationToken));

    private static async Task<IResult> PrepareSystemTaskAsync(
        string conversationId, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlSystemTaskExplorer.ReadBodyAsync<SystemTaskPrepareRequest>(context.Request, cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.PrepareAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context), conversationId, request, cancellationToken));
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static async Task<IResult> ConfirmSystemTaskAsync(
        string taskId, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlSystemTaskExplorer.ReadBodyAsync<SystemTaskConfirmationRequest>(context.Request, cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.ConfirmAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context), taskId, request, cancellationToken));
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static async Task<IResult> ExecuteSystemTaskAsync(
        string taskId, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlSystemTaskExplorer.ReadBodyAsync<SystemTaskExecutionRequest>(context.Request, cancellationToken);
            return await AssistantAsync(context, async () => (object?)await explorer.ExecuteAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context), taskId, request, cancellationToken));
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static Task<IResult> GetSystemCapability(
        string capabilityId,
        HttpContext context,
        ControlSystemCapabilityExplorer explorer) =>
        AssistantAsync(context, () => Task.FromResult<object?>(explorer.Get(
            WebControlRequestFilter.GetAuthorizationEvidence(context), capabilityId)));

    private static async Task<IResult> StreamCodexAsync(
        HttpContext context, IAsyncEnumerable<CodexConversationEvent> events,
        CancellationToken cancellationToken)
    {
        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);
        CodexConversationEvent first;
        try
        {
            if (!await enumerator.MoveNextAsync())
                return Results.Json(new { error = "CODEX_STREAM_EMPTY", message = "Codex produced no stream result." },
                    statusCode: StatusCodes.Status502BadGateway);
            first = enumerator.Current;
        }
        catch (Exception exception) when (exception is ControlAssistantException or CodexBridgeException or AssistantConversationException)
        { return AssistantError(exception); }

        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/x-ndjson; charset=utf-8";
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        try
        {
            await WriteCodexEventAsync(context, first, cancellationToken);
            while (await enumerator.MoveNextAsync())
                await WriteCodexEventAsync(context, enumerator.Current, cancellationToken);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        return Results.Empty;
    }

    private static async Task WriteCodexEventAsync(
        HttpContext context, CodexConversationEvent item, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(line) > 256 * 1024)
            throw new InvalidOperationException("The normalized Codex stream event exceeded 256 KiB.");
        await context.Response.WriteAsync(line + "\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static string AssistantOperatorId(HttpContext context)
    {
        var tailscale = string.Equals(context.User.Identity?.AuthenticationType,
            WebAccessPolicy.TailscaleAuthenticationType, StringComparison.Ordinal);
        return PrivateOperatorPrincipal.Create(
            tailscale ? "tailscale-serve" : "local-loopback",
            tailscale ? context.User.Identity?.Name ?? "invalid" : "local-operator").PrincipalId;
    }

    private static async Task<IResult> AssistantAsync(HttpContext context, Func<Task<object?>> action)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await action();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (ControlAssistantException exception)
        {
            return Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (Exception exception) when (exception is CodexBridgeException or AssistantConversationException)
        {
            return AssistantError(exception);
        }
    }

    private static IResult AssistantError(Exception exception)
    {
        var (code, message) = exception switch
        {
            ControlAssistantException value => (value.Code, value.Message),
            CodexBridgeException value => (value.Code, value.Message),
            AssistantConversationException value => (value.Code, value.Message),
            _ => ("ASSISTANT_FAILURE", "The assistant request failed.")
        };
        var status = code switch
        {
            "ASSISTANT_CONVERSATION_UNKNOWN" or "ASSISTANT_TURN_UNKNOWN" or
            "CODEX_APPROVAL_UNKNOWN" => StatusCodes.Status404NotFound,
            "ASSISTANT_IDEMPOTENCY_CONFLICT" or "ASSISTANT_REVISION_STALE" or
            "ASSISTANT_TURN_ACTIVE" or "ASSISTANT_TURN_NOT_ACTIVE" or
            "CODEX_THREAD_MISMATCH" or "CODEX_TURN_MISMATCH" or
            "CODEX_APPROVAL_NOT_PENDING" or "CODEX_APPROVAL_REVISION_STALE" or
            "CODEX_APPROVAL_EXPIRED" or "CODEX_APPROVAL_NOT_ACCEPTABLE" or
            "CODEX_APPROVAL_TURN_INACTIVE" or "CODEX_APPROVAL_SESSION_UNKNOWN" or
            "CODEX_APPROVAL_ALREADY_DISPATCHED" => StatusCodes.Status409Conflict,
            "CODEX_SERVICE_UNAVAILABLE" or "CODEX_PROCESS_UNAVAILABLE" or
            "CODEX_VERSION_UNSUPPORTED" or "ASSISTANT_SERVICE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { error = code, message }, statusCode: status);
    }

    private static async Task<IResult> GetCommittedEffectsAsync(
        HttpContext context,
        CommittedEffectHistory history,
        CancellationToken cancellationToken)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var query = context.Request.Query;
            var page = await history.ListAsync(
                query["type"].FirstOrDefault(),
                query["entityId"].FirstOrDefault(),
                query["rootOperationId"].FirstOrDefault(),
                query["cursor"].FirstOrDefault(),
                query["limit"].FirstOrDefault(),
                cancellationToken);
            return Results.Json(page);
        }
        catch (CommittedEffectHistoryException exception)
        {
            return ControlHistoryError(exception);
        }
    }

    private static async Task<IResult> GetCommittedEffectAsync(
        string eventId,
        HttpContext context,
        CommittedEffectHistory history,
        CancellationToken cancellationToken)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var detail = await history.GetAsync(eventId, cancellationToken);
            if (detail is null) return Results.NotFound();
            return Results.Json(detail);
        }
        catch (CommittedEffectHistoryException exception)
        {
            return ControlHistoryError(exception);
        }
    }

    private static IResult ControlHistoryError(CommittedEffectHistoryException exception) =>
        Results.Json(
            new { error = exception.Code, message = exception.Message },
            statusCode: exception.StatusCode);

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
                entries = result.Entries.Select(value => new
                {
                    text = value.Text,
                    stance = value.Stance,
                    presentationKind = value.PresentationKind
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
        ControlStructureExplorer explorer, CancellationToken cancellationToken) =>
        StructureAsync(context, () => explorer.ListApplicationRelationshipsAsync(
            applicationId,
            stateSpaceId,
            context.Request.Query["fromEntityId"].FirstOrDefault() ?? string.Empty,
            context.Request.Query["qualifiedKind"].FirstOrDefault() ?? string.Empty,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

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
            context.Request.Query["limit"].FirstOrDefault()));

    private static IResult InspectCatalog(
        string applicationId, string qualifiedId, HttpContext context,
        ControlStructureExplorer explorer) =>
        Structure(context, () => explorer.InspectCatalog(
            applicationId,
            context.Request.Query["collection"].FirstOrDefault(),
            qualifiedId));

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
        exception is ControlStructureException or ArgumentException or KeyNotFoundException ||
        exception is InvalidOperationException { Message: "CURSOR_STALE" };

    private static IResult StructureError(Exception exception)
    {
        var (code, status) = exception switch
        {
            ControlStructureException control => (control.Code, control.StatusCode),
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

    private static Task<IResult> GetControlPagesAsync(
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () => (object?)await editor.ListPagesAsync(
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetControlPageAsync(
        string pageId,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () => (object?)await editor.GetPageAsync(pageId, cancellationToken));

    private static Task<IResult> GetControlPageRevisionsAsync(
        string pageId,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () => (object?)await editor.ListRevisionsAsync(
            pageId,
            context.Request.Query["cursor"].FirstOrDefault(),
            context.Request.Query["limit"].FirstOrDefault(),
            cancellationToken));

    private static Task<IResult> GetControlPageRevisionAsync(
        string pageId,
        int revision,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () => (object?)await editor.GetRevisionAsync(
            pageId, revision, cancellationToken));

    private static Task<IResult> ExportControlPageRevisionAsync(
        string pageId,
        int revision,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () =>
        {
            var bundle = await editor.ExportAsync(pageId, revision, cancellationToken);
            return bundle is null
                ? null
                : Results.File(bundle.Content, "application/zip", bundle.FileName);
        }, resultIsResult: true);

    private static Task<IResult> PreviewControlPageRevisionAsync(
        string pageId,
        int revision,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () =>
        {
            var html = await editor.PreviewHtmlAsync(pageId, revision, cancellationToken);
            if (html is null) return null;
            ControlPageEditor.ApplyPreviewHeaders(context.Response, asset: false);
            return Results.Text(html, "text/html", Encoding.UTF8);
        }, resultIsResult: true);

    private static Task<IResult> PreviewControlPageAssetAsync(
        string pageId,
        int revision,
        string? path,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () =>
        {
            var asset = await editor.PreviewAssetAsync(pageId, revision, path, cancellationToken);
            if (asset is null) return null;
            ControlPageEditor.ApplyPreviewHeaders(context.Response, asset: true);
            return Results.File(asset.Content, asset.ContentType);
        }, resultIsResult: true);

    private static Task<IResult> AppendControlPageDraftAsync(
        string pageId,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () =>
        {
            var request = await ControlPageEditor.ReadBodyAsync<ControlPageDraftRequest>(
                context.Request, cancellationToken);
            return (object?)await editor.AppendDraftAsync(pageId, request, cancellationToken);
        });

    private static Task<IResult> ActivateControlPageRevisionAsync(
        string pageId,
        HttpContext context,
        ControlPageEditor editor,
        CancellationToken cancellationToken) =>
        PageEditorAsync(context, async () =>
        {
            var request = await ControlPageEditor.ReadBodyAsync<ControlPageActivationRequest>(
                context.Request, cancellationToken);
            return (object?)await editor.ActivateAsync(pageId, request, cancellationToken);
        });

    private static async Task<IResult> PageEditorAsync(
        HttpContext context,
        Func<Task<object?>> action,
        bool resultIsResult = false)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await action();
            if (value is null) return Results.NotFound();
            return resultIsResult ? (IResult)value : Results.Json(value);
        }
        catch (ControlPageEditorException exception)
        {
            return PageEditorError(exception.Code, exception.Message, exception.StatusCode);
        }
        catch (WebPageStoreException exception)
        {
            var status = exception.Code switch
            {
                "PAGE_UNKNOWN" or "REVISION_UNKNOWN" => StatusCodes.Status404NotFound,
                "PAGE_LATEST_STALE" or "PAGE_ACTIVE_STALE" or "PAGE_ALREADY_ACTIVE" or "CURSOR_STALE" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return PageEditorError(exception.Code, exception.Message, status);
        }
        catch (ArgumentException exception)
        {
            return PageEditorError("INVALID_REQUEST", exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static IResult PageEditorError(string code, string message, int status) =>
        Results.Json(new { error = code, message }, statusCode: status);

    private static async Task StreamChangesAsync(
        HttpContext context,
        SqliteWebChangeFeed changes)
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

        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await context.Response.WriteAsync("retry: 2000\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            await foreach (var change in changes.WatchAsync(
                pageId,
                cancellationToken: context.RequestAborted))
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

    private static async Task<IResult> UploadBundleAsync(
        string id,
        HttpRequest request,
        IWebPageStore pages,
        WebPageBundleReader bundles,
        CancellationToken cancellationToken)
    {
        if (!WebPageId.IsValid(id))
        {
            return InvalidPageId();
        }

        var mediaType = request.ContentType?.Split(';', 2)[0].Trim();
        if (!string.Equals(mediaType, "application/zip", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new
                {
                    error = "ZIP_REQUIRED",
                    message = "Upload a ZIP bundle using the application/zip content type."
                },
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        WebPageBundle bundle;
        try
        {
            bundle = await bundles.ReadAsync(
                request.Body,
                request.ContentLength,
                cancellationToken);
        }
        catch (WebPageBundleException exception)
        {
            return Results.Json(
                new { error = exception.Code, message = exception.Message },
                statusCode: exception.StatusCode);
        }

        var saved = await pages.SaveBundleAndActivateAsync(id, bundle, cancellationToken);
        return Results.Json(new
        {
            saved.Id,
            saved.Revision,
            AssetCount = bundle.Assets.Count,
            Url = $"/ui/{saved.Id}/index.html"
        });
    }

    private static async Task<IResult> UploadPageAsync(
        string id,
        HttpRequest request,
        IWebPageStore pages,
        [FromServices] WebHtmlReader htmlReader,
        CancellationToken cancellationToken)
    {
        if (!WebPageId.IsValid(id))
        {
            return InvalidPageId();
        }

        if (request.ContentType is null ||
            !request.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new
                {
                    error = "HTML_REQUIRED",
                    message = "Upload a complete HTML document using the text/html content type."
                },
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        string html;
        try
        {
            html = await htmlReader.ReadAsync(request.Body, request.ContentLength, cancellationToken);
        }
        catch (WebHtmlUploadException exception)
        {
            return Results.Json(
                new { error = exception.Code, message = exception.Message },
                statusCode: exception.StatusCode);
        }

        var saved = await pages.SaveAndActivateAsync(id, html, cancellationToken);
        return Results.Json(new
        {
            saved.Id,
            saved.Revision,
            Url = $"/ui/{saved.Id}"
        });
    }

    private static async Task<IResult> GetPageAsync(
        string id,
        IWebPageStore pages,
        [FromServices] IApplicationRegistry applications,
        CancellationToken cancellationToken)
    {
        var page = await pages.GetActiveAsync(id, cancellationToken);
        if (page is not null) return Results.Text(page.Html, "text/html", Encoding.UTF8);
        if (!ApplicationPageId.TryGetApplicationId(id, out var applicationId)) return Results.NotFound();
        var application = applications.Describe(applicationId!);
        return application is null
            ? Results.NotFound()
            : Results.Text(ApplicationPageTemplate.Render(application), "text/html", Encoding.UTF8);
    }

    private static Task<IResult> GetHomePageAsync(
        IWebPageStore pages,
        [FromServices] IApplicationRegistry applications,
        CancellationToken cancellationToken) =>
        GetPageAsync(HomePageId, pages, applications, cancellationToken);

    private static async Task<IResult> GetAssetAsync(
        string id,
        string? path,
        IWebPageStore pages,
        CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return Results.NotFound();
        }

        var asset = await pages.GetActiveAssetAsync(id, $"assets/{path}", cancellationToken);
        return asset is null
            ? Results.NotFound()
            : Results.File(asset.Content, asset.ContentType);
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

    private static IResult InvalidPageId() =>
        Results.BadRequest(new
        {
            error = "INVALID_PAGE_ID",
            message = "Page IDs may contain letters, numbers, dots, underscores, and hyphens."
        });
}
