using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;

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
        CancellationToken cancellationToken)
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
            var result = await readModels.ReadAsync(new(
                stateSpaceId,
                application!,
                qualifiedQueryId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["subject"] = entityId
                }), cancellationToken);
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
                : exception.Code.Contains("STALE", StringComparison.Ordinal)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
            return Results.Json(new { code = exception.Code, message = exception.Message },
                statusCode: status);
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
