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
    private static void MapControlRoutes(IEndpointRouteBuilder endpoints)
    {
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
        endpoints.MapDantesRoleplayControlGet("/system/conversations", GetSystemConversationsAsync);
        endpoints.MapDantesRoleplayControlGet("/system/conversations/{conversationId}", GetSystemConversationAsync);
        endpoints.MapDantesRoleplayControlGet("/system/conversations/recoveries/{idempotencyKey}", RecoverSystemConversationAsync);
        endpoints.MapDantesRoleplayControlGet("/system/capabilities", GetSystemCapabilities);
        endpoints.MapDantesRoleplayControlGet("/ai/providers", GetAiProviders);
        endpoints.MapDantesRoleplayControlGet("/ai/providers/{providerId}/models", GetAiModelsAsync);
        endpoints.MapDantesRoleplayControlGet("/ai/conversations", GetAiConversationsAsync);
        endpoints.MapDantesRoleplayControlGet("/ai/conversations/{conversationId}", GetAiConversationAsync);
        endpoints.MapDantesRoleplayControlGet("/ai/recoveries/{idempotencyKey}", RecoverAiRequestAsync);
        endpoints.MapDantesRoleplayControlDelete(
            "/ai/conversations/{conversationId}", PrivateOperatorCapability.ControlAiMessage,
            DeleteAiConversationAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/ai/requests", PrivateOperatorCapability.ControlAiMessage, ExecuteAiRequestAsync);
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
        endpoints.MapDantesRoleplayControlGet("/system/tasks/{taskId}", GetSystemTaskAsync);
        endpoints.MapDantesRoleplayControlGet("/system/tasks/recoveries/{idempotencyKey}", RecoverSystemTaskAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/tasks/{taskId}/confirmations", PrivateOperatorCapability.Modify,
            ConfirmSystemTaskAsync);
        endpoints.MapDantesRoleplayControlPost(
            "/system/tasks/{taskId}/executions", PrivateOperatorCapability.Modify,
            ExecuteSystemTaskAsync);
        endpoints.MapDantesRoleplayControlGet("/system/capabilities/{capabilityId}", GetSystemCapability);
        endpoints.MapDantesRoleplayControlGet("/effects", GetCommittedEffectsAsync);
        endpoints.MapDantesRoleplayControlGet("/effects/{eventId}", GetCommittedEffectAsync);
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
        : context.User.Identity?.AuthenticationType == WebAccessPolicy.AnonymousPublicAuthenticationType
            ? "anonymous-public-operator" : "local-operator";

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

    private static Task<IResult> RecoverSystemConversationAsync(
        string idempotencyKey, HttpContext context, ControlSystemConversationExplorer explorer,
        CancellationToken cancellationToken) => AssistantAsync(context, async () =>
    {
        var recovered = await explorer.RecoverAsync(WebControlRequestFilter.GetAuthorizationEvidence(context),
            idempotencyKey, cancellationToken);
        return (object?)(recovered is null ? null : new
        {
            recovered.ConversationId, recovered.TurnId, recovered.Status, recovered.IdempotencyKey,
            recovered.Provider, recovered.Scope
        });
    });

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

    private static IResult GetAiProviders(HttpContext context, IWebAiGateway gateway)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        return Results.Json(new { providers = gateway.ListProviders() });
    }

    private static Task<IResult> GetAiModelsAsync(
        string providerId,
        HttpContext context,
        IWebAiGateway gateway,
        CancellationToken cancellationToken) => WebAiAsync(context, async () =>
            (object?)new { models = await gateway.ListModelsAsync(providerId, cancellationToken) });

    private static Task<IResult> GetAiConversationsAsync(
        HttpContext context,
        IWebAiGateway gateway,
        CancellationToken cancellationToken) => WebAiAsync(context, async () =>
            (object?)await gateway.ListConversationsAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context),
                context.Request.Query["provider"].FirstOrDefault() ?? "",
                context.Request.Query["surface"].FirstOrDefault() ?? "",
                cancellationToken));

    private static Task<IResult> GetAiConversationAsync(
        string conversationId,
        HttpContext context,
        IWebAiGateway gateway,
        CancellationToken cancellationToken) => WebAiAsync(context, async () =>
            (object?)await gateway.GetConversationAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context),
            conversationId,
            cancellationToken));

    private static Task<IResult> RecoverAiRequestAsync(
        string idempotencyKey, HttpContext context, IWebAiGateway gateway,
        CancellationToken cancellationToken) => WebAiAsync(context, async () =>
    {
        var request = new WebAiRecoveryRequest(
            context.Request.Query["surface"].FirstOrDefault() ?? "",
            context.Request.Query["provider"].FirstOrDefault() ?? "",
            idempotencyKey,
            context.Request.Query["applicationId"].FirstOrDefault(),
            context.Request.Query["resolutionFingerprint"].FirstOrDefault(),
            context.Request.Query["stateSpaceId"].FirstOrDefault());
        var recovered = await gateway.RecoverAsync(WebControlRequestFilter.GetAuthorizationEvidence(context),
            request, cancellationToken);
        return (object?)(recovered is null ? null : new
        {
            recovered.ConversationId, recovered.TurnId, recovered.Status, recovered.IdempotencyKey,
            recovered.Provider, recovered.Scope, recovered.Context!.Fingerprint, recovered.Context.SourceReferences
        });
    });

    private static async Task<IResult> DeleteAiConversationAsync(
        string conversationId,
        HttpContext context,
        IWebAiGateway gateway,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<AssistantConversationDelete>(
                context.Request, cancellationToken);
            return await WebAiAsync(context, async () =>
            {
                var deleted = await gateway.DeleteConversationAsync(
                    WebControlRequestFilter.GetAuthorizationEvidence(context),
                    conversationId,
                    request.ExpectedRevision,
                    cancellationToken);
                return deleted ? new { deleted = true, conversationId } : null;
            });
        }
        catch (ControlAssistantException exception) { return AssistantError(exception); }
    }

    private static async Task<IResult> ExecuteAiRequestAsync(
        HttpContext context,
        IWebAiGateway gateway,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await ControlAssistantExplorer.ReadBodyAsync<WebAiRequest>(
                context.Request, cancellationToken);
            return await WebAiAsync(context, async () => (object?)await gateway.ExecuteAsync(
                WebControlRequestFilter.GetAuthorizationEvidence(context), request, cancellationToken));
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

    private static Task<IResult> RecoverSystemTaskAsync(
        string idempotencyKey, HttpContext context, ControlSystemTaskExplorer explorer,
        CancellationToken cancellationToken) => AssistantAsync(context, async () =>
    {
        var recovered = await explorer.RecoverAsync(WebControlRequestFilter.GetAuthorizationEvidence(context),
            idempotencyKey, cancellationToken);
        return (object?)(recovered is null ? null : new
        {
            recovered.TaskId, recovered.ConfirmationId, recovered.ExecutionId, recovered.Status, IdempotencyKey = idempotencyKey
        });
    });

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

    private static Task<IResult> GetSystemCapabilities(
        HttpContext context,
        ControlSystemCapabilityExplorer explorer) =>
        AssistantAsync(context, () => Task.FromResult<object?>(explorer.List(
            WebControlRequestFilter.GetAuthorizationEvidence(context))));

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
        => WebTrustedPrincipalContextFactory.FromPrincipal(context.User).PrincipalId;

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

    private static async Task<IResult> WebAiAsync(HttpContext context, Func<Task<object?>> action)
    {
        ControlCenterStatus.ApplyCacheHeaders(context.Response);
        try
        {
            var value = await action();
            return value is null ? Results.NotFound() : Results.Json(value);
        }
        catch (WebAiException exception)
        {
            return Results.Json(new { error = exception.Code, message = exception.Message },
                statusCode: exception.StatusCode);
        }
        catch (AssistantConversationException exception)
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
            "ASSISTANT_CONVERSATION_IN_USE" or
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
}
