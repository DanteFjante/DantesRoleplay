using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Media;

namespace DantesRoleplay.MCPServer;

public static class ReadModelMediaWebEndpoint
{
    private static readonly HashSet<string> UnavailableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "archived", "denied", "forbidden", "hidden", "redacted", "unavailable", "withheld"
    };

    public static async Task<IResult> ReadAsync(string token, HttpContext context,
        ILocalKnowledgeSeatProvider seats, IReadModelMediaLinkStore links,
        IApplicationReadModelService views, IAuthorizedKnowledgeAudiencePolicy audiences,
        IKnowledgeApplicationBindingResolver bindings, IEntityMediaService media,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (context.Request.Query.Count != 0 || links.Find(token) is not { } ticket) return Unavailable();
        var seat = seats.Current();
        var request = ticket.Request;
        var perspective = request.Audience?.Perspective;
        if (!ValidSeat(seat, ticket, perspective, context)) return Unavailable();

        EntityMediaReadResult? opened = null;
        try
        {
            var binding = await bindings.ResolveAsync(ticket.CampaignId, cancellationToken);
            if (!ValidBinding(binding, ticket)) return Unavailable();
            try { binding!.Validate(); }
            catch (ArgumentException) { return Unavailable(); }

            var before = await audiences.ResolveAsync(ticket.CampaignId, cancellationToken);
            if (!GrantMatchesSeat(before, seat, ticket.CampaignId)) return Unavailable();

            // The current registered projection rechecks participation, knowledge, activation,
            // audience and source revision. A cached link grants nothing by itself.
            var view = await views.ReadAsync(request, cancellationToken);
            using var data = JsonDocument.Parse(view.DataJson);
            var url = ReadModelMediaLinkStore.Url(token);
            var linkedByExactUrl = ProjectionContainsContentUrl(data.RootElement, url);
            var linkedByOwner = ProjectionContainsAuthorizedOwnerReference(data.RootElement, ticket.OwnerId);
            if (!linkedByExactUrl && !linkedByOwner) return Unavailable();

            var audience = perspective == "dm"
                ? EntityMediaAudience.GameMaster
                : EntityMediaAudience.Player;
            opened = await media.OpenReadAsync(request.ApplicationId, request.StateSpaceId, ticket.OwnerId,
                ticket.MediaId, audience, cancellationToken);
            if (opened is null) return Unavailable();

            var expectedFingerprint = linkedByExactUrl
                ? ReadModelMediaLinkStore.Fingerprint(opened.Attachment)
                : ReadModelMediaLinkStore.Fingerprint(opened.Attachment,
                    view.SourceRevisionFingerprint, binding!.BindingRevision);
            var after = await audiences.ResolveAsync(ticket.CampaignId, cancellationToken);
            var currentBinding = await bindings.ResolveAsync(ticket.CampaignId, cancellationToken);
            if (!GrantMatchesSeat(after, seat, ticket.CampaignId) ||
                before.Grant != after.Grant ||
                !SameSeat(seat, seats.Current()) ||
                !SameBinding(binding!, currentBinding) ||
                opened.Attachment.MediaType is not ("image/png" or "image/jpeg" or "image/webp") ||
                expectedFingerprint != ticket.AttachmentFingerprint)
            {
                return Unavailable();
            }

            var result = Results.Stream(opened.Blob.Content, opened.Attachment.MediaType,
                enableRangeProcessing: false);
            opened = null; // The HTTP stream result now owns disposal.
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Unavailable(); }
        finally { if (opened is not null) await opened.DisposeAsync(); }
    }

    internal static bool ProjectionContainsAuthorizedOwnerReference(JsonElement value, string ownerId) =>
        Token(ownerId) && ContainsAuthorizedOwnerReference(value, ownerId, ancestorUnavailable: false, depth: 0);

    private static bool ContainsAuthorizedOwnerReference(JsonElement value, string ownerId,
        bool ancestorUnavailable, int depth)
    {
        if (depth > 64) return false;
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(item =>
                ContainsAuthorizedOwnerReference(item, ownerId, ancestorUnavailable, depth + 1));
        if (value.ValueKind != JsonValueKind.Object) return false;

        var unavailable = ancestorUnavailable || HasUnavailableState(value);
        if (!unavailable && HasOwnerId(value, ownerId) &&
            value.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String && Token(name.GetString()))
            return true;
        return !unavailable && value.EnumerateObject().Any(property =>
            ContainsAuthorizedOwnerReference(property.Value, ownerId, unavailable, depth + 1));
    }

    private static bool HasOwnerId(JsonElement value, string ownerId) =>
        value.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String && id.GetString() == ownerId ||
        value.TryGetProperty("entityId", out var entityId) &&
            entityId.ValueKind == JsonValueKind.String && entityId.GetString() == ownerId;

    private static bool HasUnavailableState(JsonElement value)
    {
        foreach (var propertyName in new[] { "state", "status", "availability" })
            if (value.TryGetProperty(propertyName, out var state) &&
                state.ValueKind == JsonValueKind.String &&
                UnavailableStates.Contains(state.GetString() ?? ""))
                return true;
        return false;
    }

    private static bool ProjectionContainsContentUrl(JsonElement value, string url) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("media", out var gallery) &&
        gallery.ValueKind == JsonValueKind.Array &&
        gallery.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("contentUrl", out var content) &&
            content.ValueKind == JsonValueKind.String && content.GetString() == url);

    private static bool ValidSeat(LocalKnowledgeSeatSnapshot seat, ReadModelMediaTicket ticket,
        string? perspective, HttpContext context) =>
        seat.Enabled && seat.ApplicationId == ticket.Request.ApplicationId.Value &&
        perspective is "player" or "dm" &&
        seat.Role switch
        {
            KnowledgeAudienceRole.Actor =>
                Token(seat.ActorId) && seat.ActorId == ticket.ObserverId && perspective == "player",
            KnowledgeAudienceRole.GameMaster =>
                seat.ActorId is null &&
                (perspective == "player" || SharedWebsiteContext.CanUseGameMaster(context)),
            KnowledgeAudienceRole.PlayerGroup =>
                seat.ActorId is null && seat.PrincipalId == ticket.ObserverId && perspective == "player",
            _ => false
        };

    private static bool GrantMatchesSeat(KnowledgeAudienceResolution resolution,
        LocalKnowledgeSeatSnapshot seat, string campaignId) =>
        resolution.Granted && resolution.Grant is { } grant &&
        grant.PrincipalId == seat.PrincipalId &&
        grant.CampaignId == campaignId &&
        grant.Role == seat.Role &&
        grant.ActorId == seat.ActorId;

    private static bool ValidBinding(KnowledgeApplicationBinding? binding, ReadModelMediaTicket ticket) =>
        binding is not null &&
        binding.ApplicationId == ticket.Request.ApplicationId.Value &&
        binding.StateSpaceId == ticket.Request.StateSpaceId &&
        binding.CampaignEntityId == ticket.CampaignId;

    private static bool SameBinding(KnowledgeApplicationBinding left, KnowledgeApplicationBinding? right) =>
        right is not null &&
        left.ApplicationId == right.ApplicationId &&
        left.StateSpaceId == right.StateSpaceId &&
        left.CampaignEntityId == right.CampaignEntityId &&
        left.BindingRevision == right.BindingRevision;

    private static bool SameSeat(LocalKnowledgeSeatSnapshot left, LocalKnowledgeSeatSnapshot right) =>
        left.Enabled == right.Enabled && left.PrincipalId == right.PrincipalId &&
        left.ApplicationId == right.ApplicationId && left.CampaignId == right.CampaignId &&
        left.ActorId == right.ActorId && left.Role == right.Role &&
        (left.SourceIds ?? []).SequenceEqual(right.SourceIds ?? [], StringComparer.Ordinal);

    private static bool Token(string? value) => value is { Length: > 0 and <= 400 } &&
        value == value.Trim() && !value.Any(char.IsControl);

    private static IResult Unavailable() => Results.NotFound(new
    {
        code = "ENTITY_MEDIA_NOT_FOUND",
        message = "The attachment is unavailable."
    });
}
