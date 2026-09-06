using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Projections;

namespace DantesRoleplay.MCPServer;

public static class ApplicationReadModelWebEndpoint
{
    public sealed record RelationshipEditBody(
        string Path,
        string Operation,
        string TargetEntityId,
        int ExpectedRevision);

    public sealed record WriteBody(
        string IdempotencyKey,
        string ExpectedSourceRevisionFingerprint,
        JsonElement Changes,
        IReadOnlyList<RelationshipEditBody>? RelationshipEdits);

    public static async Task<IResult> ReadAsync(
        string applicationId,
        string stateSpaceId,
        string entityId,
        string qualifiedQueryId,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IApplicationReadModelService readModels,
        CancellationToken cancellationToken,
        IPublicApplicationCatalogProvider? catalogs = null,
        IAuthorizedKnowledgeAudiencePolicy? audiences = null,
        IKnowledgeApplicationBindingResolver? bindings = null,
        IKnowledgeActorParticipationVerifier? participation = null)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var inputAware = context.Request.Query.ContainsKey("input") || context.Request.Query.ContainsKey("campaignId")
            || context.Request.Query.ContainsKey("cursor") || context.Request.Query.ContainsKey("limit");
        var seat = seats.Current();
        if (!Authorized(seat, applicationId, entityId, out var application) ||
            catalogs is null && seat.Role == KnowledgeAudienceRole.Actor && seat.ActorId != entityId)
        {
            if (inputAware) return SafeError("READ_MODEL_FORBIDDEN");
            return Results.Json(new
            {
                code = "READ_MODEL_AUDIENCE_DENIED",
                message = "The current server-selected audience cannot inspect this entity."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var perspective = context.Request.Query["perspective"];
        if (perspective.Count > 1 || (perspective.Count == 1 && perspective[0] is not ("player" or "dm")))
        {
            if (inputAware) return SafeError("READ_MODEL_INPUT_INVALID");
            return Results.Json(new { code = "READ_MODEL_INVALID_PERSPECTIVE" },
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (perspective == "dm" && seat.Role != KnowledgeAudienceRole.GameMaster)
        {
            if (inputAware) return SafeError("READ_MODEL_FORBIDDEN");
            return Results.Json(new { code = "READ_MODEL_AUDIENCE_DENIED" },
                statusCode: StatusCodes.Status403Forbidden);
        }
        // A preview may narrow the host's grant, but can never elevate an actor seat.
        var audience = seat.Role == KnowledgeAudienceRole.GameMaster && perspective != "player"
            ? MechanicAudienceContext.GameMaster
            : MechanicAudienceContext.Player;

        try
        {
            var crossEntity = seat.Role == KnowledgeAudienceRole.Actor && seat.ActorId != entityId;
            ApplicationQueryContract? queryContract = null;
            if (catalogs is not null && catalogs.TryGet(application!, out var catalog))
                queryContract = ApplicationQueryContract.Parse(catalog.Inspect(new(application!, application!.Value,
                    qualifiedQueryId)).ContentJson, application!);
            if (crossEntity && queryContract?.CampaignSelection is null) return SafeError("READ_MODEL_FORBIDDEN");
            inputAware |= queryContract?.CampaignSelection is not null;
            var suppliedInput = context.Request.Query["input"];
            var suppliedCampaign = context.Request.Query["campaignId"];
            var suppliedCursor = context.Request.Query["cursor"];
            var suppliedLimit = context.Request.Query["limit"];
            var parsedLimit = 0;
            if (suppliedInput.Count > 1 || suppliedCampaign.Count > 1 || suppliedCursor.Count > 1
                || suppliedLimit.Count > 1 || suppliedCursor.Count == 1 && suppliedCursor[0]!.Length > 2_048
                || suppliedLimit.Count == 1 && (!int.TryParse(suppliedLimit[0], out parsedLimit)
                    || parsedLimit is < 1 or > 500))
                return SafeError("READ_MODEL_INPUT_INVALID");
            var input = ApplicationReadModelInput.Normalize(suppliedInput.Count == 0 ? "{}" : suppliedInput[0]!);
            if (suppliedCampaign.Count == 1 || queryContract?.CampaignSelection is not null)
            {
                var campaignId = suppliedCampaign.Count == 1 ? suppliedCampaign[0] : seat.CampaignId;
                var authorized = await SystemAudienceContextHandler.ResolveAsync(
                    seats, audiences, bindings, participation, campaignId, cancellationToken);
                var authorization = JsonSerializer.SerializeToElement(authorized.Data);
                if (authorized.Error is not null || bindings is null ||
                    authorization.ValueKind != JsonValueKind.Object ||
                    !authorization.TryGetProperty("status", out var status) || status.GetString() != "bound")
                    return SafeError("READ_MODEL_FORBIDDEN");
                var binding = await bindings.ResolveAsync(campaignId!, cancellationToken);
                if (binding is null || binding.ApplicationId != applicationId || binding.StateSpaceId != stateSpaceId ||
                    binding.CampaignEntityId != campaignId) return SafeError("READ_MODEL_FORBIDDEN");
                // This local value binds roles; it never changes the ambient host seat.
                seat = seat with { CampaignId = campaignId! };
            }
            ApplicationReadModelResult? selectionResult = null;
            if (queryContract?.CampaignSelection is { } selection)
            {
                selectionResult = await ReadSelectionAsync(selection, catalogs!, readModels, application!,
                    stateSpaceId, seat.CampaignId, audience, cancellationToken);
                if (!SelectionMatches(selectionResult, selection.EntityIdField, entityId)) return SafeError("READ_MODEL_FORBIDDEN");
            }
            var roleBindings = ResolveRoleBindings(
                catalogs, application!, qualifiedQueryId, entityId, seat);
            if (roleBindings is null)
                return Results.Json(new
                {
                    code = "READ_MODEL_ROLES_UNAVAILABLE",
                    message = "The current audience context cannot bind every declared read-model role."
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            var result = await readModels.ReadAsync(new(
                stateSpaceId,
                application!,
                qualifiedQueryId,
                roleBindings,
                audience, input,
                suppliedCursor.Count == 1 ? suppliedCursor[0] : null,
                suppliedLimit.Count == 1 ? parsedLimit : null), cancellationToken);
            if (selectionResult is not null && queryContract?.CampaignSelection is { } recheck)
            {
                var current = await ReadSelectionAsync(recheck, catalogs!, readModels, application!,
                    stateSpaceId, seat.CampaignId, audience, cancellationToken);
                if (current.SourceRevisionFingerprint != selectionResult.SourceRevisionFingerprint ||
                    !SelectionMatches(current, recheck.EntityIdField, entityId)) return SafeError("READ_MODEL_SOURCE_STALE");
            }
            using var data = JsonDocument.Parse(result.DataJson);
            return Results.Json(new
            {
                result.ApplicationId,
                result.StateSpaceId,
                result.QualifiedQueryId,
                result.StateSpaceFingerprint,
                result.ResolutionFingerprint,
                result.OutputSchemaHash,
                result.ResultFingerprint,
                result.SourceRevisionFingerprint,
                data = data.RootElement.Clone()
            });
        }
        catch (ApplicationReadModelException exception)
        {
            if (exception.Code is "READ_MODEL_INPUT_INVALID" or "READ_MODEL_FORBIDDEN" or
                "READ_MODEL_SELECTION_UNAVAILABLE" or "READ_MODEL_SOURCE_STALE" or "READ_MODEL_UNAVAILABLE")
                return SafeError(exception.Code);
            if (inputAware)
                return SafeError(exception.Code switch
                {
                    "READ_MODEL_REQUEST_INVALID" => "READ_MODEL_INPUT_INVALID",
                    "READ_MODEL_STATE_SPACE_UNKNOWN" => "READ_MODEL_FORBIDDEN",
                    _ when exception.Code.Contains("STALE", StringComparison.Ordinal) => "READ_MODEL_SOURCE_STALE",
                    _ => "READ_MODEL_UNAVAILABLE"
                });
            var status = exception.Code is "READ_MODEL_UNKNOWN" or "READ_MODEL_STATE_SPACE_UNKNOWN"
                ? StatusCodes.Status404NotFound
                : exception.Code is "READ_MODEL_CATALOG_UNAVAILABLE"
                    ? StatusCodes.Status503ServiceUnavailable
                : exception.Code.Contains("STALE", StringComparison.Ordinal)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
            return Results.Json(new { code = exception.Code, message = exception.Message },
                statusCode: status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or JsonException)
        { return SafeError("READ_MODEL_UNAVAILABLE"); }
        catch (Exception) when (inputAware) { return SafeError("READ_MODEL_UNAVAILABLE"); }
    }

    public static async Task<IResult> WriteAsync(
        string applicationId,
        string stateSpaceId,
        string entityId,
        string qualifiedQueryId,
        WriteBody body,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IApplicationObjectWriteService writes,
        IPublicApplicationCatalogProvider catalogs,
        IAuthorizedKnowledgeAudiencePolicy audiences,
        IKnowledgeApplicationBindingResolver bindings,
        IKnowledgeActorParticipationVerifier participation,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var seat = seats.Current();
        if (!Authorized(seat, applicationId, entityId, out var application) ||
            seat.Role != KnowledgeAudienceRole.GameMaster)
            return SafeWriteError("OBJECT_WRITE_FORBIDDEN");

        try
        {
            var authorization = await SystemAudienceContextHandler.ResolveAsync(
                seats, audiences, bindings, participation, seat.CampaignId, cancellationToken);
            if (authorization.Error is not null)
                return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            var binding = await bindings.ResolveAsync(seat.CampaignId, cancellationToken);
            if (binding is null || binding.ApplicationId != applicationId ||
                binding.StateSpaceId != stateSpaceId || binding.CampaignEntityId != seat.CampaignId)
                return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            if (!catalogs.TryGet(application!, out var catalog))
                return SafeWriteError("OBJECT_WRITE_UNAVAILABLE");
            var query = ApplicationQueryContract.Parse(catalog.Inspect(new(
                application!, application!.Value, qualifiedQueryId)).ContentJson, application!);
            if (query.Id != qualifiedQueryId || query.Status != "active" || !query.IsObjectProjection ||
                query.ObjectCollectionId is null)
                return SafeWriteError("OBJECT_WRITE_UNKNOWN");
            var roleBindings = ResolveRoleBindings(catalogs, application!, qualifiedQueryId, entityId, seat);
            if (roleBindings is null || !roleBindings.Values.Contains(entityId, StringComparer.Ordinal))
                return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            if (body is null || body.Changes.ValueKind != JsonValueKind.Object ||
                body.RelationshipEdits?.Any(value => value is null) == true)
                return SafeWriteError("OBJECT_WRITE_REQUEST_INVALID");

            var result = await writes.WriteAsync(new(
                stateSpaceId,
                application!,
                new(query.ProjectionQualifiedId, query.ProjectionVersion, query.ProjectionContentHash),
                roleBindings,
                query.ObjectCollectionId,
                "dm",
                body.IdempotencyKey,
                body.ExpectedSourceRevisionFingerprint,
                body.Changes.GetRawText(),
                body.RelationshipEdits?.Select(value => new ApplicationObjectRelationshipEdit(
                    value.Path, value.Operation, value.TargetEntityId, value.ExpectedRevision)).ToArray() ?? []),
                cancellationToken);
            using var data = JsonDocument.Parse(result.OutputJson);
            return Results.Json(new
            {
                applicationId = application.Value,
                stateSpaceId,
                qualifiedQueryId,
                result.Applied,
                result.Replayed,
                result.NoOp,
                result.OperationId,
                result.SourceRevisionFingerprint,
                data = data.RootElement.Clone()
            });
        }
        catch (ApplicationObjectWriteException exception)
        {
            return SafeWriteError(exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (KeyNotFoundException)
        {
            return SafeWriteError("OBJECT_WRITE_UNKNOWN");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return SafeWriteError("OBJECT_WRITE_UNAVAILABLE");
        }
    }

    private static IResult SafeWriteError(string code)
    {
        var (status, message) = code switch
        {
            "OBJECT_WRITE_REQUEST_INVALID" => (400, "The edit request is invalid."),
            "OBJECT_WRITE_FORBIDDEN" => (403, "This object is not writable by the current audience."),
            "OBJECT_WRITE_UNKNOWN" => (404, "The writable object is unavailable."),
            "OBJECT_WRITE_SOURCE_STALE" => (409, "The object changed. Refresh before saving."),
            "OBJECT_WRITE_IDEMPOTENCY_CONFLICT" => (409, "The edit key is already bound to another request."),
            "OBJECT_WRITE_REJECTED" => (422, "The declared object edit was rejected."),
            _ => (503, "The writable object is temporarily unavailable.")
        };
        return Results.Json(new { code, message }, statusCode: status);
    }

    private static IResult SafeError(string code)
    {
        var (status, message) = code switch
        {
            "READ_MODEL_INPUT_INVALID" => (400, "The request is invalid."),
            "READ_MODEL_FORBIDDEN" => (403, "This view is not available to the current audience."),
            "READ_MODEL_SELECTION_UNAVAILABLE" => (404, "This selection is unavailable."),
            "READ_MODEL_SOURCE_STALE" => (409, "The view changed. Refresh to continue."),
            _ => (503, "This view is temporarily unavailable.")
        };
        return Results.Json(new { code, message }, statusCode: status);
    }

    private static IReadOnlyDictionary<string, string>? ResolveRoleBindings(
        IPublicApplicationCatalogProvider? catalogs,
        ApplicationIdentifier application,
        string qualifiedQueryId,
        string entityId,
        LocalKnowledgeSeatSnapshot seat)
    {
        // Retain the original subject contract for isolated callers and older tests. The live host
        // supplies the catalog so role binding follows the query's own declaration rather than a
        // second handwritten list of D&D read models.
        if (catalogs is null)
            return new Dictionary<string, string>(StringComparer.Ordinal) { ["subject"] = entityId };
        try
        {
            if (!catalogs.TryGet(application, out var catalog))
                throw new ApplicationReadModelException("READ_MODEL_CATALOG_UNAVAILABLE",
                    "The active application catalog is unavailable. Inspect application readiness and restore or reactivate the reviewed catalog before retrying.");
            var record = catalog.Inspect(new(application, application.Value, qualifiedQueryId));
            var contract = ApplicationQueryContract.Parse(record.ContentJson, application);
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var role in contract.Roles.Keys)
            {
                var value = role switch
                {
                    "campaign" when !string.IsNullOrWhiteSpace(seat.CampaignId) => seat.CampaignId,
                    "actor" when !string.IsNullOrWhiteSpace(seat.ActorId) => seat.ActorId,
                    "actor" => entityId,
                    "subject" => entityId,
                    _ when contract.Roles.Count == 1 || contract.Roles.Count == 2 && contract.Roles.ContainsKey("campaign") => entityId,
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(value)) return null;
                bindings.Add(role, value);
            }
            return bindings;
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or JsonException)
        {
            return null;
        }
    }

    private static bool Authorized(
        LocalKnowledgeSeatSnapshot seat,
        string requestedApplicationId,
        string entityId,
        out ApplicationIdentifier? applicationId)
    {
        applicationId = null;
        if (!seat.Enabled || seat.ApplicationId != requestedApplicationId
            || seat.Role is not (KnowledgeAudienceRole.Actor or KnowledgeAudienceRole.GameMaster)
            || string.IsNullOrWhiteSpace(entityId) || entityId.Length > 200
            || seat.Role == KnowledgeAudienceRole.Actor && string.IsNullOrWhiteSpace(seat.ActorId))
            return false;
        try
        {
            applicationId = ApplicationIdentifier.Parse(requestedApplicationId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<ApplicationReadModelResult> ReadSelectionAsync(
        ApplicationQueryCampaignSelection selection, IPublicApplicationCatalogProvider catalogs,
        IApplicationReadModelService readModels, ApplicationIdentifier application, string stateSpaceId,
        string campaignId, MechanicAudienceContext audience, CancellationToken cancellationToken)
    {
        if (!catalogs.TryGet(application, out var catalog))
            throw new ApplicationReadModelException("READ_MODEL_FORBIDDEN", "Selection is unavailable.");
        var contract = ApplicationQueryContract.Parse(catalog.Inspect(new(application, application.Value,
            selection.QueryId)).ContentJson, application);
        if (contract.CampaignSelection is not null || contract.Roles.Count != 1 || !contract.Roles.ContainsKey("campaign"))
            throw new ApplicationReadModelException("READ_MODEL_FORBIDDEN", "Selection is unavailable.");
        return await readModels.ReadAsync(new(stateSpaceId, application, selection.QueryId,
            new Dictionary<string, string> { ["campaign"] = campaignId }, audience), cancellationToken);
    }

    private static bool SelectionMatches(ApplicationReadModelResult result, string field, string entityId)
    {
        using var data = JsonDocument.Parse(result.DataJson);
        return data.RootElement.ValueKind == JsonValueKind.Object &&
            data.RootElement.TryGetProperty(field, out var selected) && selected.ValueKind == JsonValueKind.String
            && selected.GetString() == entityId;
    }
}
