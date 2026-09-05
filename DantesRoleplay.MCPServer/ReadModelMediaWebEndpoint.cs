using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Media;

namespace DantesRoleplay.MCPServer;

public static class ReadModelMediaWebEndpoint
{
    public static async Task<IResult> ReadAsync(string token, HttpContext context,
        ILocalKnowledgeSeatProvider seats, IReadModelMediaLinkStore links,
        IApplicationReadModelService views, IAuthorizedKnowledgeAudiencePolicy audiences,
        IEntityMediaService media, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (context.Request.Query.Count != 0 || links.Find(token) is not { } ticket) return Unavailable();
        var seat = seats.Current();
        var request = ticket.Request;
        var perspective = request.Audience?.Perspective;
        if (!seat.Enabled || seat.ApplicationId != request.ApplicationId.Value ||
            seat.Role is not (KnowledgeAudienceRole.Actor or KnowledgeAudienceRole.GameMaster) ||
            seat.Role == KnowledgeAudienceRole.GameMaster && seat.ActorId is not null ||
            perspective is not ("player" or "dm") ||
            seat.Role == KnowledgeAudienceRole.Actor && (seat.ActorId != ticket.ObserverId || perspective != "player")) return Unavailable();
        EntityMediaReadResult? opened = null;
        try
        {
            var before = await audiences.ResolveAsync(ticket.CampaignId, cancellationToken);
            if (!before.Granted || before.Grant!.PrincipalId != seat.PrincipalId) return Unavailable();
            // The current registered projection rechecks possession, participation, knowledge,
            // activation and the caller's real grant. A cached link grants nothing by itself.
            var view = await views.ReadAsync(request, cancellationToken);
            using var data = JsonDocument.Parse(view.DataJson);
            var url = ReadModelMediaLinkStore.Url(token);
            if (!data.RootElement.TryGetProperty("media", out var gallery) || gallery.ValueKind != JsonValueKind.Array ||
                !gallery.EnumerateArray().Any(value => value.TryGetProperty("contentUrl", out var content) &&
                    content.ValueKind == JsonValueKind.String && content.GetString() == url)) return Unavailable();
            var audience = perspective == "dm" ? EntityMediaAudience.GameMaster : EntityMediaAudience.Player;
            opened = await media.OpenReadAsync(request.ApplicationId, request.StateSpaceId, ticket.OwnerId,
                ticket.MediaId, audience, cancellationToken);
            if (opened is null) return Unavailable();
            var after = await audiences.ResolveAsync(ticket.CampaignId, cancellationToken);
            if (!after.Granted || before.Grant != after.Grant || !SameSeat(seat, seats.Current()) ||
                opened.Attachment.MediaType is not ("image/png" or "image/jpeg" or "image/webp") ||
                ReadModelMediaLinkStore.Fingerprint(opened.Attachment) != ticket.AttachmentFingerprint)
            {
                return Unavailable();
            }
            var result = Results.Stream(opened.Blob.Content, opened.Attachment.MediaType, enableRangeProcessing: false);
            opened = null; // The HTTP stream result now owns disposal.
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Unavailable(); }
        finally { if (opened is not null) await opened.DisposeAsync(); }
    }

    private static bool SameSeat(LocalKnowledgeSeatSnapshot left, LocalKnowledgeSeatSnapshot right) =>
        left.Enabled == right.Enabled && left.PrincipalId == right.PrincipalId && left.ApplicationId == right.ApplicationId &&
        left.CampaignId == right.CampaignId && left.ActorId == right.ActorId && left.Role == right.Role &&
        (left.SourceIds ?? []).SequenceEqual(right.SourceIds ?? [], StringComparer.Ordinal);

    private static IResult Unavailable() => Results.NotFound(new { code = "ENTITY_MEDIA_NOT_FOUND", message = "The attachment is unavailable." });
}
