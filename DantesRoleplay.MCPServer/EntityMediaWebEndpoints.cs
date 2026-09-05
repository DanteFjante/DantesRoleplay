using DantesRoleplay.Applications;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Media;

namespace DantesRoleplay.MCPServer;

public static class EntityMediaWebEndpoints
{
    public sealed record BatchRequest(string[] EntityIds, string? Perspective);

    // Read-only batching retains the ordinary owner authorization and never exposes raw blob IDs.
    public static async Task<IResult> DiscoverBatchAsync(string applicationId, string stateSpaceId,
        BatchRequest request, HttpContext context, ILocalKnowledgeSeatProvider seats,
        IEntityMediaService media, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!TryAudience(seats.Current(), applicationId, out var application, out var audience)) return Denied();
        if (request.EntityIds is null || request.EntityIds.Length is < 1 or > 256 ||
            request.EntityIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(char.IsControl)) ||
            request.EntityIds.Distinct(StringComparer.Ordinal).Count() != request.EntityIds.Length ||
            request.Perspective is not (null or "player" or "dm"))
            return Results.BadRequest(new { code = "MEDIA_BATCH_INVALID" });
        if (request.Perspective == "dm" && audience != EntityMediaAudience.GameMaster) return Denied();
        if (request.Perspective == "player") audience = EntityMediaAudience.Player;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var items = new List<object>();
        try
        {
            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    var result = await media.DiscoverAsync(application!, stateSpaceId, entityId, audience,
                        diagnostics: false, deadline.Token);
                    items.Add(new { entityId, attachments = result.Attachments.Select(value => new {
                        value.MediaId, value.Role, value.MediaType, value.Width, value.Height, value.Alt,
                        value.Caption, value.Order, contentUrl = ContentPath(applicationId, stateSpaceId, entityId, value.MediaId)
                    }).ToArray() });
                }
                catch (EntityMediaException) { items.Add(new { entityId, attachments = Array.Empty<object>() }); }
            }
            var resultBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { applicationId, stateSpaceId, items },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return resultBytes.Length > 2 * 1024 * 1024
                ? Results.Json(new { code = "MEDIA_BATCH_TOO_LARGE" }, statusCode: 413)
                : Results.Bytes(resultBytes, "application/json");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Results.Json(new { code = "MEDIA_BATCH_TIMEOUT" }, statusCode: 504); }
    }

    public static async Task<IResult> DiscoverAsync(
        string applicationId,
        string stateSpaceId,
        string entityId,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IEntityMediaService media,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!TryAudience(seats.Current(), applicationId, out var application, out var audience))
            return Denied();
        var perspective = context.Request.Query["perspective"];
        if (perspective.Count > 1 || perspective.Count == 1 && perspective[0] is not ("player" or "dm"))
            return Results.BadRequest(new { code = "MEDIA_PERSPECTIVE_INVALID" });
        if (perspective == "dm" && audience != EntityMediaAudience.GameMaster) return Denied();
        if (perspective == "player") audience = EntityMediaAudience.Player;
        try
        {
            var result = await media.DiscoverAsync(
                application!, stateSpaceId, entityId, audience, diagnostics: false, cancellationToken);
            return Results.Json(new
            {
                result.ApplicationId,
                result.StateSpaceId,
                result.EntityId,
                result.ResolutionFingerprint,
                attachments = result.Attachments.Select(value => new
                {
                    value.MediaId,
                    value.Role,
                    value.MediaType,
                    value.Width,
                    value.Height,
                    value.Alt,
                    value.Caption,
                    value.Order,
                    contentUrl = ContentPath(applicationId, stateSpaceId, entityId, value.MediaId)
                })
            });
        }
        catch (EntityMediaException exception)
        {
            return Failure(exception);
        }
    }

    public static async Task<IResult> ReadAsync(
        string applicationId,
        string stateSpaceId,
        string entityId,
        string mediaId,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IEntityMediaService media,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!TryAudience(seats.Current(), applicationId, out var application, out var audience))
            return Denied();
        try
        {
            var result = await media.OpenReadAsync(
                application!, stateSpaceId, entityId, mediaId, audience, cancellationToken);
            return result is null
                ? Results.NotFound(new { code = "ENTITY_MEDIA_NOT_FOUND", message = "The attachment is unavailable." })
                : Results.Stream(result.Blob.Content, result.Attachment.MediaType, enableRangeProcessing: true);
        }
        catch (EntityMediaException exception)
        {
            return Failure(exception);
        }
    }

    private static bool TryAudience(
        LocalKnowledgeSeatSnapshot seat,
        string requestedApplicationId,
        out ApplicationIdentifier? applicationId,
        out EntityMediaAudience audience)
    {
        applicationId = null;
        audience = EntityMediaAudience.Player;
        if (!seat.Enabled || !string.Equals(seat.ApplicationId, requestedApplicationId, StringComparison.Ordinal) ||
            seat.Role is not (KnowledgeAudienceRole.Actor or KnowledgeAudienceRole.GameMaster)) return false;
        try { applicationId = ApplicationIdentifier.Parse(requestedApplicationId); }
        catch (ArgumentException) { return false; }
        audience = seat.Role == KnowledgeAudienceRole.GameMaster
            ? EntityMediaAudience.GameMaster
            : EntityMediaAudience.Player;
        return true;
    }

    private static string ContentPath(string applicationId, string stateSpaceId, string entityId, string mediaId) =>
        $"/api/applications/{Uri.EscapeDataString(applicationId)}/state-spaces/{Uri.EscapeDataString(stateSpaceId)}" +
        $"/entities/{Uri.EscapeDataString(entityId)}/media/{Uri.EscapeDataString(mediaId)}/content";

    private static IResult Denied() => Results.Json(new
    {
        code = "ENTITY_MEDIA_AUDIENCE_DENIED",
        message = "The current server-selected audience cannot access this media owner."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult Failure(EntityMediaException exception) => Results.Json(new
    {
        code = exception.Code,
        message = exception.Message
    }, statusCode: exception.Code is "MEDIA_ENTITY_UNKNOWN" or "MEDIA_STATE_SPACE_UNKNOWN"
        ? StatusCodes.Status404NotFound
        : exception.Code == "MEDIA_STATE_SPACE_WRONG_APPLICATION"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest);
}
