using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
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
        JsonElement Changes = default,
        IReadOnlyList<RelationshipEditBody>? RelationshipEdits = null)
    {
        public string Mode { get; init; } = "changes";
        public JsonElement Object { get; init; }
    }

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
        IApplicationQueryRoleBindingResolver? roleResolver = null,
        IApplicationQueryAuthorizedContextProvider? authorizedContext = null,
        IEntityComponentStore? entities = null,
        IStateSpaceRegistry? stateSpaces = null)
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
        if (perspective == "dm" && (seat.Role != KnowledgeAudienceRole.GameMaster ||
            !SharedWebsiteContext.CanUseGameMaster(context)))
        {
            if (inputAware) return SafeError("READ_MODEL_FORBIDDEN");
            return Results.Json(new { code = "READ_MODEL_AUDIENCE_DENIED" },
                statusCode: StatusCodes.Status403Forbidden);
        }
        // A preview may narrow the host's grant, but can never elevate an actor seat.
        var audience = seat.Role == KnowledgeAudienceRole.GameMaster && perspective != "player" &&
            SharedWebsiteContext.CanUseGameMaster(context)
            ? MechanicAudienceContext.GameMaster
            : MechanicAudienceContext.Player;

        try
        {
            var crossEntity = seat.Role == KnowledgeAudienceRole.Actor && seat.ActorId != entityId;
            if (catalogs is null || !catalogs.TryGet(application!, out var catalog))
                return SafeError("READ_MODEL_UNAVAILABLE");
            var queryContract = ApplicationQueryContract.Parse(catalog.Inspect(new(application!, application!.Value,
                qualifiedQueryId)).ContentJson, application!);
            if (queryContract.CampaignSelection is not null)
                return SafeError("READ_MODEL_FORBIDDEN");
            if (crossEntity && queryContract.RoleBindings is null)
                return SafeError("READ_MODEL_FORBIDDEN");
            inputAware = true;
            var suppliedInput = context.Request.Query["input"];
            var suppliedCampaign = context.Request.Query["campaignId"];
            var suppliedCursor = context.Request.Query["cursor"];
            var suppliedLimit = context.Request.Query["limit"];
            var parsedLimit = 0;
            if (suppliedInput.Count > 1 || suppliedCampaign.Count > 0 || suppliedCursor.Count > 1
                || suppliedLimit.Count > 1 || suppliedCursor.Count == 1 && suppliedCursor[0]!.Length > 2_048
                || suppliedLimit.Count == 1 && (!int.TryParse(suppliedLimit[0], out parsedLimit)
                    || parsedLimit is < 1 or > 500))
                return SafeError("READ_MODEL_INPUT_INVALID");
            var input = ApplicationReadModelInput.Normalize(suppliedInput.Count == 0 ? "{}" : suppliedInput[0]!);
            IReadOnlyDictionary<string, string>? roleBindings;
            if (queryContract.RoleBindings is not null)
            {
                if (roleResolver is null || entities is null || stateSpaces is null)
                    return SafeError("READ_MODEL_UNAVAILABLE");
                var registeredSpace = stateSpaces.Get(stateSpaceId);
                if (registeredSpace is null || registeredSpace.ApplicationRevision.ApplicationId != application)
                    return SafeError("READ_MODEL_FORBIDDEN");
                var needsAuthorizedContext = queryContract.RoleBindings.Values.Any(
                    value => value.Source == "authorized-context");
                var needsEntityGrant = ApplicationObjectHostAccess.RequiresAuthorizedEntityGrant(seat);
                if ((needsAuthorizedContext || needsEntityGrant) && authorizedContext is null)
                    return SafeError("READ_MODEL_FORBIDDEN");
                var authorizedRoles = needsAuthorizedContext || needsEntityGrant
                    ? await authorizedContext!.ResolveAsync(application!, stateSpaceId, cancellationToken)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                if (authorizedRoles is null) return SafeError("READ_MODEL_FORBIDDEN");
                roleBindings = roleResolver.Resolve(queryContract, input,
                    new(entityId, authorizedRoles));
                if (roleBindings.Values.Any(roleEntityId =>
                        !ApplicationObjectHostAccess.CanReadRoleEntity(seat, roleEntityId, authorizedRoles)))
                    return SafeError("READ_MODEL_FORBIDDEN");
                foreach (var roleEntityId in roleBindings.Values.Distinct(StringComparer.Ordinal))
                    if (await entities.GetEntityAsync(stateSpaceId, roleEntityId, cancellationToken) is null)
                        return SafeError("READ_MODEL_FORBIDDEN");
            }
            else roleBindings = ResolveRouteEntityBinding(queryContract, entityId);
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
        CancellationToken cancellationToken,
        IApplicationQueryRoleBindingResolver? roleResolver = null,
        IApplicationQueryAuthorizedContextProvider? authorizedContext = null,
        IEntityComponentStore? entities = null,
        IStateSpaceRegistry? stateSpaces = null)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var seat = seats.Current();
        if (!Authorized(seat, applicationId, entityId, out var application) ||
            !ApplicationObjectHostAccess.CanWrite(seat))
            return SafeWriteError("OBJECT_WRITE_FORBIDDEN");

        try
        {
            if (!catalogs.TryGet(application!, out var catalog))
                return SafeWriteError("OBJECT_WRITE_UNAVAILABLE");
            var query = ApplicationQueryContract.Parse(catalog.Inspect(new(
                application!, application!.Value, qualifiedQueryId)).ContentJson, application!);
            if (query.Id != qualifiedQueryId || query.Status != "active" || !query.IsObjectProjection ||
                query.ObjectCollectionId is null)
                return SafeWriteError("OBJECT_WRITE_UNKNOWN");
            if (context.Request.Query.ContainsKey("campaign"))
                return SafeWriteError("OBJECT_WRITE_REQUEST_INVALID");
            if (stateSpaces is null) return SafeWriteError("OBJECT_WRITE_UNAVAILABLE");
            {
                var registeredSpace = stateSpaces.Get(stateSpaceId);
                if (registeredSpace is null || registeredSpace.ApplicationRevision.ApplicationId != application)
                    return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            }
            var suppliedInput = context.Request.Query["input"];
            if (suppliedInput.Count > 1) return SafeWriteError("OBJECT_WRITE_REQUEST_INVALID");
            var input = ApplicationReadModelInput.Normalize(
                suppliedInput.Count == 0 ? "{}" : suppliedInput[0]!);
            IReadOnlyDictionary<string, string>? roleBindings;
            if (query.RoleBindings is not null)
            {
                if (roleResolver is null || entities is null) return SafeWriteError("OBJECT_WRITE_UNAVAILABLE");
                var needsAuthorizedContext = query.RoleBindings.Values.Any(
                    value => value.Source == "authorized-context");
                if (needsAuthorizedContext && authorizedContext is null)
                    return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
                var authorizedRoles = needsAuthorizedContext
                    ? await authorizedContext!.ResolveAsync(application!, stateSpaceId, cancellationToken)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                if (authorizedRoles is null) return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
                roleBindings = roleResolver.Resolve(query, input, new(entityId, authorizedRoles));
                foreach (var roleEntityId in roleBindings.Values.Distinct(StringComparer.Ordinal))
                    if (await entities.GetEntityAsync(stateSpaceId, roleEntityId, cancellationToken) is null)
                        return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            }
            else roleBindings = ResolveRouteEntityBinding(query, entityId);
            if (roleBindings is null || !roleBindings.Values.Contains(entityId, StringComparer.Ordinal))
                return SafeWriteError("OBJECT_WRITE_FORBIDDEN");
            var validSubmission = body?.Mode switch
            {
                "changes" => body.Changes.ValueKind == JsonValueKind.Object
                    && body.Object.ValueKind == JsonValueKind.Undefined,
                "object" => body.Object.ValueKind == JsonValueKind.Object
                    && body.Changes.ValueKind == JsonValueKind.Undefined,
                _ => false
            };
            if (body is null || !validSubmission ||
                body.RelationshipEdits?.Any(value => value is null) == true)
                return SafeWriteError("OBJECT_WRITE_REQUEST_INVALID");

            var request = new ApplicationObjectWriteRequest(
                stateSpaceId,
                application!,
                new(query.ProjectionQualifiedId, query.ProjectionVersion, query.ProjectionContentHash),
                roleBindings,
                query.ObjectCollectionId,
                ApplicationObjectHostAccess.PrivilegedWritePerspective,
                body.IdempotencyKey,
                body.ExpectedSourceRevisionFingerprint,
                body.Mode == "changes" ? body.Changes.GetRawText() : "{}",
                body.RelationshipEdits?.Select(value => new ApplicationObjectRelationshipEdit(
                    value.Path, value.Operation, value.TargetEntityId, value.ExpectedRevision)).ToArray() ?? [])
            {
                SubmissionMode = body.Mode,
                SubmittedObjectJson = body.Mode == "object" ? body.Object.GetRawText() : "{}"
            };
            var result = await writes.WriteAsync(request, cancellationToken);
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
        catch (ApplicationReadModelException exception)
        {
            return SafeWriteError(exception.Code switch
            {
                "READ_MODEL_INPUT_INVALID" or "READ_MODEL_REQUEST_INVALID"
                    => "OBJECT_WRITE_REQUEST_INVALID",
                "READ_MODEL_ROLES_UNAVAILABLE" or "READ_MODEL_ROLES_INVALID"
                    or "READ_MODEL_FORBIDDEN" => "OBJECT_WRITE_FORBIDDEN",
                _ => "OBJECT_WRITE_UNAVAILABLE"
            });
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
            "OBJECT_WRITE_READ_ONLY_CHANGED" => (422, "The submitted object changes a read-only field."),
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

    private static IReadOnlyDictionary<string, string>? ResolveRouteEntityBinding(
        ApplicationQueryContract contract,
        string entityId)
    {
        // A transition-only generic compatibility path: only a single declared role may bind to
        // the route entity. It deliberately has no role-name or seat-field inference.
        if (contract.RoleBindings is not null || contract.Roles.Count != 1)
            return null;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [contract.Roles.Keys.Single()] = entityId
        };
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

}
