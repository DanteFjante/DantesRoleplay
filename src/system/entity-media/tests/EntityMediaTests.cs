using DantesRoleplay.Applications;
using DantesRoleplay.AI;
using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Media;
using DantesRoleplay.SystemCapabilities;
using System.Text.Json;

namespace DantesRoleplay.Tests.Media;

public sealed class EntityMediaTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("fixture");
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly EcsComponentReference Type = new("game.core.media.visual", 1, new string('A', 64));

    [Fact]
    public async Task Discovery_is_owner_bound_ordered_and_audience_filtered()
    {
        var service = Service(Visual("""
          {"role":"portrait","visibility":["player","dm"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":100,"height":120,"alt":"Hero portrait","caption":"","order":2,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}},
          {"role":"scene","visibility":["dm"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":800,"height":600,"alt":"Hidden scene","caption":"Secret","order":1,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """));

        var player = await service.DiscoverAsync(Application, "space", "hero", EntityMediaAudience.Player);
        var gameMaster = await service.DiscoverAsync(Application, "space", "hero", EntityMediaAudience.GameMaster);

        Assert.Equal("visual-2", Assert.Single(player.Attachments).MediaId);
        Assert.Equal(["visual-1", "visual-2"], gameMaster.Attachments.Select(value => value.MediaId));
        Assert.Equal("resolution", player.ResolutionFingerprint);
        Assert.DoesNotContain(Hash, player.Attachments.Single().MediaId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_read_rechecks_visibility_before_opening_blob()
    {
        var service = Service(Visual("""
          {"role":"handout","visibility":["dm"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":1,"height":1,"alt":"Private handout","caption":"","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """));

        Assert.Null(await service.OpenReadAsync(Application, "space", "hero", "visual-0", EntityMediaAudience.Player));
        await using var allowed = await service.OpenReadAsync(
            Application, "space", "hero", "visual-0", EntityMediaAudience.GameMaster);

        Assert.NotNull(allowed);
        Assert.Equal(Hash, allowed!.Attachment.Sha256);
    }

    [Fact]
    public async Task Missing_blob_exposes_no_attachment_metadata_and_cannot_be_opened()
    {
        var service = Service(Visual("""
          {"role":"illustration","visibility":["player","dm"],"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","mimeType":"image/png","width":1,"height":1,"alt":"Missing art","caption":"Missing caption","order":0,"provenance":{"kind":"original","credit":"Artist","source":"private-source","reviewedOn":"2026-09-01","version":1}}
        """));
        var result = await service.DiscoverAsync(Application, "space", "hero", EntityMediaAudience.Player);
        Assert.Empty(result.Attachments);
        Assert.Empty(result.Diagnostics);
        Assert.Null(await service.OpenReadAsync(Application, "space", "hero", "visual-0", EntityMediaAudience.Player));
    }

    [Fact]
    public async Task Discovery_rejects_cross_application_state_space()
    {
        var service = Service(Visual("""
          {"role":"icon","visibility":["player"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":1,"height":1,"alt":"Icon","caption":"","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """));

        var failure = await Assert.ThrowsAsync<EntityMediaException>(() => service.DiscoverAsync(
            ApplicationIdentifier.Parse("another"), "space", "hero", EntityMediaAudience.Player));

        Assert.Equal("MEDIA_STATE_SPACE_WRONG_APPLICATION", failure.Code);
    }

    [Fact]
    public async Task Direct_ai_tool_fetches_verified_media_in_process()
    {
        var source = new EntityMediaAiToolSource(Service(Visual("""
          {"role":"portrait","visibility":["dm"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":100,"height":120,"alt":"Hero portrait","caption":"","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """)), new AudienceResolver(EntityMediaAudience.GameMaster));
        var invocation = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "local-loopback"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "entity-media-test")
        {
            ApplicationId = Application,
            StateSpaceId = "space",
            ResolutionFingerprint = "resolution"
        };
        var request = new AiRequest("fixture", "model", [new(AiMessageRole.User, "Show the portrait")]);
        var context = new SystemAiToolSourceContext(
            new("fixture", "Fixture", "Fixture agent"), request, invocation, null, null, () => []);
        var tool = Assert.Single(source.CreateTools(context));
        using var arguments = JsonDocument.Parse("""{"entityId":"hero","mediaId":"visual-0"}""");

        var result = await tool.InvokeAsync(new(
            "call-1", "system_entity_media", arguments.RootElement.Clone(), AiRequestKind.Message));

        Assert.True(result.Ok, result.ErrorMessage);
        var attached = Assert.Single(result.Media!);
        Assert.Equal(Hash, attached.Sha256);
        Assert.Equal(BlobMediaTypes.Png, attached.MediaType);
        Assert.Equal("Hero portrait", attached.Alt);
        Assert.Equal("hero", attached.EntityId);
        Assert.Equal("visual-0", attached.MediaId);
        Assert.Equal("portrait", attached.Role);
        Assert.Equal(100, attached.Width);
        Assert.Equal(120, attached.Height);
        Assert.NotEmpty(Convert.FromBase64String(attached.Base64Data));
    }

    [Fact]
    public void Direct_ai_media_tools_are_unavailable_without_a_host_authorized_audience()
    {
        var source = new EntityMediaAiToolSource(Service(Visual("""
          {"role":"portrait","visibility":["player"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":100,"height":120,"alt":"Hero portrait","caption":"","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """)));
        var invocation = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "local-loopback"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "entity-media-test")
        {
            ApplicationId = Application,
            StateSpaceId = "space",
            ResolutionFingerprint = "resolution"
        };
        var request = new AiRequest("fixture", "model", [new(AiMessageRole.User, "Show media")]);
        var context = new SystemAiToolSourceContext(
            new("fixture", "Fixture", "Fixture agent"), request, invocation, null, null, () => []);

        Assert.Empty(source.CreateTools(context));
    }

    [Fact]
    public async Task Current_location_map_is_resolved_from_the_host_bound_actor()
    {
        var source = new EntityMediaAiToolSource(Service(Visual("""
          {"role":"map","visibility":["player"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":800,"height":600,"alt":"Current location map","caption":"","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """)), new AudienceResolver(EntityMediaAudience.Player, "actor"), new Edges());
        var invocation = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "local-loopback"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "entity-media-test")
        {
            ApplicationId = Application,
            StateSpaceId = "space",
            ResolutionFingerprint = "resolution"
        };
        var request = new AiRequest("fixture", "model", [new(AiMessageRole.User, "Show my map")]);
        var context = new SystemAiToolSourceContext(
            new("fixture", "Fixture", "Fixture agent"), request, invocation, null, null, () => []);
        Assert.Contains(source.CreateTools(context), value =>
            value.Definition.Name == "system_current_location_media");
        var tool = Assert.Single(source.CreateTools(context), value =>
            value.Definition.Name == "system_current_location_map");
        using var arguments = JsonDocument.Parse("{}");

        var result = await tool.InvokeAsync(new(
            "call-1", tool.Definition.Name, arguments.RootElement.Clone(), AiRequestKind.Message));

        Assert.True(result.Ok, result.ErrorMessage);
        var attached = Assert.Single(result.Media!);
        Assert.Equal("location", attached.EntityId);
        Assert.Equal("map", attached.Role);
    }

    [Fact]
    public async Task Current_location_media_prefers_an_authorized_setting_card()
    {
        var source = new EntityMediaAiToolSource(Service(Visual("""
          {"role":"map","visibility":["player"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":800,"height":600,"alt":"Location map","caption":"","order":1,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}},
          {"role":"setting","visibility":["player"],"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mimeType":"image/png","width":1200,"height":675,"alt":"Location setting","caption":"Arrival view","order":0,"provenance":{"kind":"original","credit":"Artist","source":"fixture","reviewedOn":"2026-09-01","version":1}}
        """)), new AudienceResolver(EntityMediaAudience.Player, "actor"), new Edges());
        var invocation = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(
                "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "local-loopback"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "entity-media-test")
        {
            ApplicationId = Application,
            StateSpaceId = "space",
            ResolutionFingerprint = "resolution"
        };
        var request = new AiRequest("fixture", "model", [new(AiMessageRole.User, "Show this place")]);
        var context = new SystemAiToolSourceContext(
            new("fixture", "Fixture", "Fixture agent"), request, invocation, null, null, () => []);
        var tool = Assert.Single(source.CreateTools(context), value =>
            value.Definition.Name == "system_current_location_media");
        using var arguments = JsonDocument.Parse("{}");

        var result = await tool.InvokeAsync(new(
            "call-1", tool.Definition.Name, arguments.RootElement.Clone(), AiRequestKind.Message));

        Assert.True(result.Ok, result.ErrorMessage);
        var attached = Assert.Single(result.Media!);
        Assert.Equal("location", attached.EntityId);
        Assert.Equal("setting", attached.Role);
        Assert.Equal("Arrival view", attached.Caption);
    }

    private static EntityMediaService Service(string value) => new(
        new Spaces(), new Entities(value), new Blobs());

    private static string Visual(string attachments) =>
        $"{{\"status\":\"active\",\"attachments\":[{attachments}]}}";

    private sealed class Spaces : IStateSpaceRegistry
    {
        private readonly StateSpaceView _space = new("space",
            new(Application, 1, new string('B', 64), []), new string('C', 64), 1,
            DateTime.UtcNow, DateTime.UtcNow) { ResolutionFingerprint = "resolution" };
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == "space" ? _space : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) =>
            new([_space], null);
    }

    private sealed class Entities(string value) : IEntityComponentStore
    {
        private readonly EcsEntityView _entity = new("space", "hero", "Hero", 1, DateTime.UtcNow, null);
        private readonly EcsComponentView _component = new("space", "hero", Type, value, 1, DateTime.UtcNow, DateTime.UtcNow);
        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken = default) => Task.FromResult<EcsEntityView?>(stateSpaceId == "space" && entityId is "hero" or "location" ? _entity with { EntityId = entityId } : null);
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId, string qualifiedTypeId, CancellationToken cancellationToken = default) => Task.FromResult<EcsComponentView?>(qualifiedTypeId == Type.QualifiedTypeId ? _component : null);
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId, IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EcsComponentView>>(locators.Any(value => value.QualifiedTypeId == Type.QualifiedTypeId) ? [_component] : []);
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId, string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Blobs : IBlobTransferService
    {
        private readonly BlobAsset _asset = new(Hash, BlobMediaTypes.Png, 8, DateTimeOffset.UtcNow);
        public Task<BeginBlobUploadResult> BeginUploadAsync(BeginBlobUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadAsync(string uploadId, string uploadToken, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BlobAsset> FinalizeUploadAsync(string uploadId, string uploadToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BlobAsset?> FindAsync(string sha256, CancellationToken cancellationToken = default) => Task.FromResult<BlobAsset?>(sha256 == Hash ? _asset : null);
        public Task<BlobReadResult?> OpenReadAsync(string sha256, CancellationToken cancellationToken = default) => Task.FromResult<BlobReadResult?>(sha256 == Hash ? new(_asset, new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10])) : null);
    }

    private sealed class AudienceResolver(EntityMediaAudience audience, string? actorId = null) : IEntityMediaAudienceResolver
    {
        public EntityMediaAudienceContext? Resolve(ApplicationIdentifier applicationId) =>
            applicationId == Application ? new(audience, actorId) : null;
    }

    private sealed class Edges : IStateSpaceEdgeStore
    {
        public Task<EcsContainmentView?> GetContainmentAsync(string stateSpaceId, string containedEntityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EcsContainmentView?>(containedEntityId == "actor"
                ? new(stateSpaceId, "actor", "location", "presence", 1, DateTime.UtcNow, DateTime.UtcNow)
                : null);
        public Task<IReadOnlyList<EcsContainmentView>> ListContainmentsAsync(string stateSpaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsContainmentDiscoveryPage> ListContainmentsAsync(string stateSpaceId, string containerEntityId, string? afterContainedEntityId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsContainmentView> MoveContainmentAsync(string stateSpaceId, string containedEntityId, string containerEntityId, string slot, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveContainmentAsync(string stateSpaceId, string containedEntityId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsRelationshipView?> GetRelationshipAsync(string stateSpaceId, string fromEntityId, string toEntityId, string qualifiedKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EcsRelationshipView>> ListRelationshipsAsync(string stateSpaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsRelationshipView> SetRelationshipAsync(string stateSpaceId, string fromEntityId, string toEntityId, string qualifiedKind, string dataJson, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveRelationshipAsync(string stateSpaceId, string fromEntityId, string toEntityId, string qualifiedKind, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
