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
using DantesRoleplay.SystemCapabilities;
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
    private static void MapApplicationRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/applications/{applicationId}/catalog/browse", BrowseCatalog)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/applications/{applicationId}/catalog/records/{qualifiedId}", InspectCatalog)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/applications/{applicationId}/content", GetEffectiveApplicationContent)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/applications/{applicationId}/rules", GetReadableRules)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}",
                GetApplicationMechanicAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/prepare",
                PrepareApplicationMechanicAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/execute",
                ExecuteApplicationMechanicAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/recoveries/{idempotencyKey}",
                GetApplicationInterruptedRequestAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/applications/{applicationId}/state-spaces", GetApplicationStateSpaces)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/containments",
                GetApplicationContainmentsAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/relationships",
                GetApplicationRelationshipsAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities",
                GetApplicationEntitiesAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}",
                GetApplicationEntityAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/containment",
                GetApplicationContainmentAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components",
                GetApplicationComponentsAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}",
                GetApplicationComponentAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/play/sessions/{sessionContextId}",
                GetApplicationPlaySession)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/applications/{applicationId}/conversations/{conversationId}", GetApplicationConversation)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/conversations/{conversationId}/history",
                GetApplicationConversationHistory)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapPost("/api/applications/{applicationId}/conversations", CreateApplicationConversationAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/conversations/{conversationId}/turns",
                SendApplicationConversationTurnAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/conversations/{conversationId}/execute",
                ExecuteApplicationConversationAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/authoring/capabilities",
                GetApplicationAuthoringCapabilities)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/authoring/capabilities/{capabilityId}",
                InvokeApplicationAuthoringCapabilityAsync)
            .RequireDantesRoleplayUploadAccess();

        // Observation ingestion authenticates phone credentials and supplies its own principal.
        endpoints.MapPost("/api/applications/{applicationId}/observations", SubmitObservationAsync)
            .AddEndpointFilter<WebObservationRequestFilter>()
            .RequireRateLimiting(WebInterfaceSecurity.UploadRateLimitPolicy);
    }

    private static IResult GetApplicationAuthoringCapabilities(
        string applicationId,
        HttpContext context,
        [FromServices] WebPlatformAccessGuard access,
        [FromServices] IApplicationCandidateCapabilityGateway gateway)
    {
        var admitted = access.Evaluate(context);
        if (!admitted.Authenticated || admitted.TrustedPrincipal is null)
            return ApplicationAuthoringAccessError(admitted);
        ApplicationIdentifier app;
        try { app = ApplicationIdentifier.Parse(applicationId); }
        catch (ArgumentException)
        { return Results.Json(new { code = "APPLICATION_ID_INVALID", message = "The application identifier is invalid." }, statusCode: 400); }
        var result = gateway.Discover(admitted.TrustedPrincipal, app, context.TraceIdentifier);
        return result.Ok
            ? Results.Json(new { applicationId = app.Value, capabilities = result.Capabilities })
            : ApplicationAuthoringError(result.Error);
    }

    private static async Task<IResult> InvokeApplicationAuthoringCapabilityAsync(
        string applicationId,
        string capabilityId,
        HttpContext context,
        [FromServices] WebPlatformAccessGuard access,
        [FromServices] IApplicationCandidateCapabilityGateway gateway,
        CancellationToken cancellationToken)
    {
        var admitted = access.Evaluate(context);
        if (!admitted.Authenticated || admitted.TrustedPrincipal is null)
            return ApplicationAuthoringAccessError(admitted);
        ApplicationIdentifier app;
        try { app = ApplicationIdentifier.Parse(applicationId); }
        catch (ArgumentException)
        { return Results.Json(new { code = "APPLICATION_ID_INVALID", message = "The application identifier is invalid." }, statusCode: 400); }
        ApplicationAuthoringCapabilityWebRequest request;
        try { request = await ReadApplicationAuthoringBodyAsync<ApplicationAuthoringCapabilityWebRequest>(context, cancellationToken); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or InteractionContractException or DecoderFallbackException)
        { return Results.Json(new { code = "APPLICATION_AUTHORING_INPUT_INVALID", message = "The application authoring request is invalid." }, statusCode: 400); }
        var result = await gateway.InvokeAsync(admitted.TrustedPrincipal, app, capabilityId,
            request.Input.GetRawText(), request.IdempotencyKey, context.TraceIdentifier, cancellationToken);
        return result.Ok ? Results.Json(result) : ApplicationAuthoringError(result.Error);
    }

    private static IResult ApplicationAuthoringAccessError(WebPlatformAccessDecision decision) =>
        Results.Json(new
        {
            code = decision.ErrorCode ?? "PLATFORM_AUTHENTICATION_REQUIRED",
            message = decision.ErrorMessage ?? "Authenticated platform access is required."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult ApplicationAuthoringError(SystemCapabilityError? error)
    {
        var code = error?.Code ?? "APPLICATION_AUTHORING_UNAVAILABLE";
        var status = code.Contains("UNAUTHENTICATED", StringComparison.Ordinal)
            ? StatusCodes.Status401Unauthorized
            : code.Contains("DENIED", StringComparison.Ordinal)
                || code.Contains("NOT_AUTHORIZED", StringComparison.Ordinal)
                ? StatusCodes.Status403Forbidden
                : code.Contains("INVALID", StringComparison.Ordinal) || code.EndsWith("REQUIRED", StringComparison.Ordinal)
                    ? StatusCodes.Status400BadRequest
                    : code.Contains("CONFLICT", StringComparison.Ordinal) || code.Contains("STALE", StringComparison.Ordinal)
                        || code.Contains("MISMATCH", StringComparison.Ordinal)
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status503ServiceUnavailable;
        return Results.Json(new
        {
            code,
            message = error?.Message ?? "Application candidate authoring is unavailable.",
            recovery = error?.Recovery ?? "Retry after the current application and permission owners are available."
        }, statusCode: status);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ApplicationAuthoringCapabilityWebRequest(
        [property: JsonRequired] JsonElement Input,
        string? IdempotencyKey);

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

    private static IResult GetApplicationPlaySession(
        string applicationId,
        string stateSpaceId,
        string sessionContextId,
        HttpContext context,
        [FromServices] IApplicationPlayRecordStore records) => InteractionWeb(() =>
    {
        var parsed = ApplicationIdentifier.Parse(applicationId);
        return records.GetSession(new(
            InteractionPrincipal(context).PrincipalId,
            parsed.Value,
            stateSpaceId,
            sessionContextId));
    });

    private static IResult GetApplicationConversationHistory(
        string applicationId,
        string conversationId,
        HttpContext context,
        ApplicationConversationService conversations)
        => InteractionWeb(() =>
        {
            int? beforeOrdinal = null;
            if (context.Request.Query.TryGetValue("beforeOrdinal", out var beforeValue))
            {
                if (!int.TryParse(beforeValue, out var parsed) || parsed < 1)
                    throw new InteractionContractException(
                        "INVALID_HISTORY_CURSOR", "The history cursor must be a positive message ordinal.");
                beforeOrdinal = parsed;
            }
            var limit = 50;
            if (context.Request.Query.TryGetValue("limit", out var limitValue)
                && (!int.TryParse(limitValue, out limit) || limit is < 1 or > 100))
                throw new InteractionContractException(
                    "INVALID_HISTORY_LIMIT", "History pages contain between 1 and 100 messages.");
            return conversations.History(
                InteractionPrincipal(context), ApplicationIdentifier.Parse(applicationId),
                conversationId, beforeOrdinal, limit);
        });

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

    private static async Task<IResult> GetApplicationInterruptedRequestAsync(
        string applicationId, string stateSpaceId, string idempotencyKey,
        HttpContext context, [FromServices] IInteractionGateway interactions,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await interactions.FindReceiptByIdempotencyKeyAsync(
                InteractionPrincipal(context), ApplicationIdentifier.Parse(applicationId), stateSpaceId,
                idempotencyKey, context.Request.Query["resolutionReceiptId"].FirstOrDefault(), cancellationToken);
            return receipt is null ? Results.NotFound() : Results.Json(new
            {
                receipt.Id, receipt.Kind, receipt.Status, receipt.Code, receipt.SafeSummary,
                receipt.ProposalFingerprint, receipt.ResolutionReceiptId, receipt.IdempotencyKey,
                ApplicationId = receipt.ApplicationId.Value, receipt.StateSpaceId
            });
        }
        catch (InteractionContractException exception)
        {
            return Results.Json(new { error = exception.Code, message = exception.Message },
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException exception)
        {
            return Results.Json(new { error = "INTERACTION_REQUEST_INVALID", message = exception.Message },
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static DantesRoleplay.Authorization.TrustedPrincipalContext InteractionPrincipal(HttpContext context)
        => WebTrustedPrincipalContextFactory.FromPrincipal(context.User);

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

    private static async Task<T> ReadApplicationAuthoringBodyAsync<T>(
        HttpContext context, CancellationToken cancellationToken)
    {
        const int maximumBytes = 1_100_000;
        if (context.Request.ContentLength > maximumBytes)
            throw new InteractionContractException("APPLICATION_AUTHORING_REQUEST_TOO_LARGE",
                "The application authoring request is too large.");
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (stream.Length + read > maximumBytes)
                throw new InteractionContractException("APPLICATION_AUTHORING_REQUEST_TOO_LARGE",
                    "The application authoring request is too large.");
            stream.Write(buffer, 0, read);
        }
        if (stream.Length == 0)
            throw new InteractionContractException("APPLICATION_AUTHORING_REQUEST_INVALID",
                "The application authoring request body is required.");
        var json = StrictInteractionUtf8.GetString(stream.ToArray());
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        return JsonSerializer.Deserialize<T>(canonical, StrictInteractionJson)
            ?? throw new InteractionContractException("APPLICATION_AUTHORING_REQUEST_INVALID",
                "The application authoring request body is required.");
    }

    private static async Task<T> ReadInteractionBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        const int maximumBytes = 64 * 1024;
        if (context.Request.ContentLength > maximumBytes)
            throw new InteractionContractException("INTERACTION_REQUEST_TOO_LARGE", "The interaction request is too large.");
        return await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InteractionContractException("INTERACTION_REQUEST_INVALID", "The interaction request body is required.");
    }
}
