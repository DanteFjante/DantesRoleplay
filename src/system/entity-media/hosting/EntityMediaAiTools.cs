using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Ecs;
using DantesRoleplay.Media;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

public sealed class EntityMediaAiToolSource(
    IEntityMediaService media,
    IEntityMediaAudienceResolver? audiences = null,
    IStateSpaceEdgeStore? edges = null) : ISystemAiToolSource
{
    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context)
    {
        var application = context.Invocation.ApplicationId;
        if (application is null || string.IsNullOrWhiteSpace(context.Invocation.StateSpaceId)) return [];
        var audience = audiences?.Resolve(application);
        if (audience is null) return [];
        var tools = new List<IAiTool>
        {
            new ReadEntityMediaTool(media, context.Invocation, audience.Audience)
        };
        if (edges is not null && !string.IsNullOrWhiteSpace(audience.ActorId))
        {
            tools.Add(new ReadCurrentLocationMediaTool(
                media, edges, context.Invocation, audience.Audience, audience.ActorId));
            tools.Add(new ReadCurrentLocationMapTool(
                media, edges, context.Invocation, audience.Audience, audience.ActorId));
        }
        return tools;
    }

    private sealed class ReadEntityMediaTool(
        IEntityMediaService media,
        SystemCapabilityInvocationContext context,
        EntityMediaAudience audience) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            "system_entity_media",
            "List authorized visual attachments for one entity, or fetch one exact attachment and attach its verified image bytes to the next model turn. Application and state-space context are supplied by the host.",
            """{"type":"object","additionalProperties":false,"required":["entityId"],"properties":{"entityId":{"type":"string","minLength":1,"maxLength":200},"mediaId":{"type":"string","minLength":1,"maxLength":200}}}""");

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var applicationId = context.ApplicationId!;
            var entityId = invocation.Arguments.GetProperty("entityId").GetString()!;
            if (!invocation.Arguments.TryGetProperty("mediaId", out var mediaIdElement))
            {
                var result = await media.DiscoverAsync(applicationId, context.StateSpaceId, entityId,
                    audience, diagnostics: false, cancellationToken);
                return AiToolResult.Success(JsonSerializer.Serialize(new
                {
                    result.ApplicationId, result.StateSpaceId, result.EntityId, result.ResolutionFingerprint,
                    attachments = result.Attachments.Select(Describe)
                }));
            }

            var mediaId = mediaIdElement.GetString()!;
            await using var read = await media.OpenReadAsync(applicationId, context.StateSpaceId, entityId,
                mediaId, audience, cancellationToken);
            if (read is null)
                return AiToolResult.Failure("ENTITY_MEDIA_NOT_FOUND",
                    "The attachment is missing, inactive, or unavailable to this trusted context.");
            using var memory = new MemoryStream();
            await read.Blob.Content.CopyToAsync(memory, cancellationToken);
            var content = Content(entityId, read.Attachment, memory.ToArray());
            return AiToolResult.Success(JsonSerializer.Serialize(Describe(read.Attachment)), [content]);
        }
    }

    private sealed class ReadCurrentLocationMediaTool(
        IEntityMediaService media,
        IStateSpaceEdgeStore edges,
        SystemCapabilityInvocationContext context,
        EntityMediaAudience audience,
        string actorId) : IAiTool
    {
        private static readonly string[] PresentationRoles =
            ["setting", "scene", "illustration", "portrait", "handout", "map", "icon"];

        public AiToolDefinition Definition { get; } = new(
            "system_current_location_media",
            "Fetch an authorized visual card for the current actor location, including after the actor enters a new location. The host resolves the actor, location, application, state space, and Player/DM audience; the model supplies no identity or path.",
            """{"type":"object","additionalProperties":false,"properties":{}}""");

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var applicationId = context.ApplicationId!;
            var presence = await edges.GetContainmentAsync(context.StateSpaceId, actorId, cancellationToken);
            if (presence is null || presence.Slot != "presence")
                return AiToolResult.Failure(
                    "CURRENT_LOCATION_UNAVAILABLE",
                    "The host-authorized actor does not have an exact current-location containment.");
            var discovered = await media.DiscoverAsync(
                applicationId, context.StateSpaceId, presence.ContainerEntityId, audience,
                diagnostics: false, cancellationToken);
            var attachment = PresentationRoles
                .Select(role => discovered.Attachments.FirstOrDefault(value => value.Role == role))
                .FirstOrDefault(value => value is not null);
            if (attachment is null)
                return AiToolResult.Failure(
                    "CURRENT_LOCATION_MEDIA_UNAVAILABLE",
                    "No authorized visual attachment is available for the current location.");
            await using var read = await media.OpenReadAsync(
                applicationId, context.StateSpaceId, presence.ContainerEntityId,
                attachment.MediaId, audience, cancellationToken);
            if (read is null)
                return AiToolResult.Failure(
                    "CURRENT_LOCATION_MEDIA_UNAVAILABLE",
                    "The current location attachment is no longer available to this trusted context.");
            using var memory = new MemoryStream();
            await read.Blob.Content.CopyToAsync(memory, cancellationToken);
            var data = new
            {
                currentLocationEntityId = presence.ContainerEntityId,
                discovered.ResolutionFingerprint,
                attachment = Describe(read.Attachment)
            };
            return AiToolResult.Success(
                JsonSerializer.Serialize(data),
                [Content(presence.ContainerEntityId, read.Attachment, memory.ToArray())]);
        }
    }

    private sealed class ReadCurrentLocationMapTool(
        IEntityMediaService media,
        IStateSpaceEdgeStore edges,
        SystemCapabilityInvocationContext context,
        EntityMediaAudience audience,
        string actorId) : IAiTool
    {
        private const int MaximumAncestryDepth = 8;

        public AiToolDefinition Definition { get; } = new(
            "system_current_location_map",
            "Fetch the authorized map for the current actor location. The host resolves the actor, location ancestry, application, state space, and Player/DM audience; the model supplies no identity or path.",
            """{"type":"object","additionalProperties":false,"properties":{}}""");

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var applicationId = context.ApplicationId!;
            var presence = await edges.GetContainmentAsync(context.StateSpaceId, actorId, cancellationToken);
            if (presence is null || presence.Slot != "presence")
                return AiToolResult.Failure(
                    "CURRENT_LOCATION_UNAVAILABLE",
                    "The host-authorized actor does not have an exact current-location containment.");

            var ownerId = presence.ContainerEntityId;
            var visited = new HashSet<string>(StringComparer.Ordinal) { actorId };
            for (var depth = 0; depth < MaximumAncestryDepth && visited.Add(ownerId); depth++)
            {
                var discovered = await media.DiscoverAsync(
                    applicationId, context.StateSpaceId, ownerId, audience,
                    diagnostics: false, cancellationToken);
                var map = discovered.Attachments.FirstOrDefault(value => value.Role == "map");
                if (map is not null)
                {
                    await using var read = await media.OpenReadAsync(
                        applicationId, context.StateSpaceId, ownerId, map.MediaId,
                        audience, cancellationToken);
                    if (read is null) break;
                    using var memory = new MemoryStream();
                    await read.Blob.Content.CopyToAsync(memory, cancellationToken);
                    var data = new
                    {
                        currentLocationEntityId = presence.ContainerEntityId,
                        mapOwnerEntityId = ownerId,
                        discovered.ResolutionFingerprint,
                        attachment = Describe(read.Attachment)
                    };
                    return AiToolResult.Success(
                        JsonSerializer.Serialize(data),
                        [Content(ownerId, read.Attachment, memory.ToArray())]);
                }

                var parent = await edges.GetContainmentAsync(
                    context.StateSpaceId, ownerId, cancellationToken);
                if (parent is null) break;
                ownerId = parent.ContainerEntityId;
            }

            return AiToolResult.Failure(
                "CURRENT_LOCATION_MAP_UNAVAILABLE",
                "No authorized map attachment is available for the current location or its containing map scopes.");
        }
    }

    private static AiMediaContent Content(
        string entityId,
        EntityMediaAttachment attachment,
        byte[] bytes) => new(
        attachment.MediaType,
        Convert.ToBase64String(bytes),
        attachment.Sha256,
        attachment.Alt,
        entityId,
        attachment.MediaId,
        attachment.Role,
        attachment.Width,
        attachment.Height,
        attachment.Caption);

    private static object Describe(EntityMediaAttachment value) => new
    {
        value.MediaId, value.Role,
        visibility = value.Visibility.Select(item => item == EntityMediaAudience.Player ? "player" : "dm"),
        value.Sha256, value.MediaType, value.Width, value.Height, value.Alt, value.Caption, value.Order,
        mcpResourceUri = $"media://blob/sha256/{value.Sha256}"
    };
}
