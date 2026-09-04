using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.MCPServer;

public static class ApplicationReadModelWebEndpoint
{
    public static async Task<IResult> ReadAsync(
        string applicationId,
        string stateSpaceId,
        string entityId,
        string qualifiedQueryId,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IApplicationReadModelService readModels,
        CancellationToken cancellationToken,
        IPublicApplicationCatalogProvider? catalogs = null)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var seat = seats.Current();
        if (!Authorized(seat, applicationId, entityId, out var application))
            return Results.Json(new
            {
                code = "READ_MODEL_AUDIENCE_DENIED",
                message = "The current server-selected audience cannot inspect this entity."
            }, statusCode: StatusCodes.Status403Forbidden);

        try
        {
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
                seat.Role == KnowledgeAudienceRole.GameMaster
                    ? MechanicAudienceContext.GameMaster
                    : MechanicAudienceContext.Player), cancellationToken);
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
                    _ when contract.Roles.Count == 1 => entityId,
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
            || string.IsNullOrWhiteSpace(entityId) || entityId.Length > 200)
            return false;
        if (seat.Role == KnowledgeAudienceRole.Actor && seat.ActorId != entityId)
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
