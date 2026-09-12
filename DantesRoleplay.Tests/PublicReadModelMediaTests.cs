using System.Net;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Media;
using DantesRoleplay.MCPServer;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class PublicReadModelMediaTests
{
    private const string Owner = "realm-root-7";
    private const string Campaign = "campaign.1";
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("dnd2024");

    [Theory]
    [InlineData(ProjectionMode.ReadyOwner, true)]
    [InlineData(ProjectionMode.UnavailableOwner, false)]
    [InlineData(ProjectionMode.EchoedOwner, false)]
    public async Task Public_player_group_issues_media_only_for_an_authorized_owner_reference(
        ProjectionMode mode, bool expectedMedia)
    {
        var seat = PlayerGroupSeat();
        var views = new Views { Mode = mode };
        var media = new MediaSource();
        var links = new ReadModelMediaLinkStore();
        var binding = new BindingResolver();
        var response = await ReadOwner(seat, views, media, links, binding);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(MechanicAudienceContext.Player, views.LastRequest!.Audience);
        Assert.Equal(expectedMedia, response.Body.TryGetProperty("media", out var projectedMedia));
        Assert.Equal(expectedMedia ? 1 : 0, media.DiscoverCalls);
        if (expectedMedia)
        {
            Assert.Equal(Owner, projectedMedia.GetProperty("entityId").GetString());
            var url = projectedMedia.GetProperty("attachments")[0]
                .GetProperty("contentUrl").GetString();
            Assert.Matches("^/api/read-model-media/[a-f0-9]{64}/content$", url);
            Assert.NotNull(links.Find(url!.Split('/')[3]));
        }
    }

    [Fact]
    public async Task Public_media_ticket_replays_the_player_projection_and_revokes_on_changes()
    {
        var seat = PlayerGroupSeat();
        var views = new Views();
        var media = new MediaSource();
        var links = new ReadModelMediaLinkStore();
        var binding = new BindingResolver();
        var issued = await ReadOwner(seat, views, media, links, binding);
        var url = issued.Body.GetProperty("media").GetProperty("attachments")[0]
            .GetProperty("contentUrl").GetString()!;

        Assert.Equal(StatusCodes.Status200OK,
            await Open(url, seat, views, media, links, binding));

        views.Mode = ProjectionMode.UnavailableOwner;
        Assert.Equal(StatusCodes.Status404NotFound,
            await Open(url, seat, views, media, links, binding));

        views.Mode = ProjectionMode.ReadyOwner;
        views.SourceRevisionFingerprint = new string('D', 64);
        Assert.Equal(StatusCodes.Status404NotFound,
            await Open(url, seat, views, media, links, binding));

        views.SourceRevisionFingerprint = new string('C', 64);
        media.Caption = "Changed map metadata";
        Assert.Equal(StatusCodes.Status404NotFound,
            await Open(url, seat, views, media, links, binding));

        media.Caption = "Map";
        binding.Revision = "binding.2";
        Assert.Equal(StatusCodes.Status404NotFound,
            await Open(url, seat, views, media, links, binding));
    }

    private static async Task<(int StatusCode, JsonElement Body)> ReadOwner(
        LocalKnowledgeSeatSnapshot seat,
        Views views,
        MediaSource media,
        ReadModelMediaLinkStore links,
        BindingResolver binding)
    {
        var context = Context("/api/applications/dnd2024/state-spaces/dnd2024-main/entities/" +
            Owner + "/read-models/dnd2024.query.world-scope");
        var result = await ApplicationReadModelWebEndpoint.ReadAsync(
            Application.Value, "dnd2024-main", Owner, "dnd2024.query.world-scope",
            context, new Seats(seat), views, CancellationToken.None,
            new QueryCatalog(),
            new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator()),
            new AuthorizedContext(),
            new ExistingEntities(),
            new StateSpaces(),
            media,
            links,
            binding);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<int> Open(
        string url,
        LocalKnowledgeSeatSnapshot seat,
        Views views,
        MediaSource media,
        ReadModelMediaLinkStore links,
        BindingResolver binding)
    {
        var context = Context(url);
        var token = url.Split('/')[3];
        var result = await ReadModelMediaWebEndpoint.ReadAsync(
            token, context, new Seats(seat), links, views, new Audience(seat),
            binding, media, CancellationToken.None);
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private static DefaultHttpContext Context(string path)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.4");
        context.Request.Path = path;
        context.User = WebAccessPolicy.CreatePrincipal(new(true, WebAccessMode.AnonymousPublic));
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase)
            .Services
            .BuildServiceProvider();
        return context;
    }

    private static LocalKnowledgeSeatSnapshot PlayerGroupSeat() => new(
        true, "public-website", Application.Value, Campaign, null,
        KnowledgeAudienceRole.PlayerGroup,
        AuthorizedRoleEntityIds: new Dictionary<string, string>());

    public enum ProjectionMode
    {
        ReadyOwner,
        UnavailableOwner,
        EchoedOwner
    }

    private sealed class Views : IApplicationReadModelService
    {
        public ProjectionMode Mode { get; set; } = ProjectionMode.ReadyOwner;
        public string SourceRevisionFingerprint { get; set; } = new('C', 64);
        public ApplicationReadModelRequest? LastRequest { get; private set; }

        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var data = Mode switch
            {
                ProjectionMode.ReadyOwner => JsonSerializer.Serialize(new
                {
                    state = "ready",
                    scope = new { id = Owner, name = "The Seventh Realm", status = "active" }
                }),
                ProjectionMode.UnavailableOwner => JsonSerializer.Serialize(new
                {
                    state = "unavailable",
                    target = new { id = Owner, name = "The Seventh Realm" }
                }),
                _ => JsonSerializer.Serialize(new { requested = new { id = Owner } })
            };
            return Task.FromResult(new ApplicationReadModelResult(
                Application.Value, "dnd2024-main", request.QualifiedQueryId,
                new string('1', 64), new string('2', 64), new string('A', 64),
                new string('B', 64), SourceRevisionFingerprint, data));
        }
    }

    private sealed class MediaSource : IEntityMediaService
    {
        public int DiscoverCalls { get; private set; }
        public string Caption { get; set; } = "Map";

        private EntityMediaAttachment Attachment => new(
            "map.realm", "map",
            [EntityMediaAudience.Player, EntityMediaAudience.GameMaster],
            new string('a', 64), "image/webp", 1000, 1000,
            "Map of the Seventh Realm", Caption, 0,
            new("original", "Fixture credit", "fixture/source", "2026-09-12", 1));

        public Task<EntityMediaDiscoveryResult> DiscoverAsync(
            ApplicationIdentifier applicationId, string stateSpaceId, string entityId,
            EntityMediaAudience audience, bool diagnostics = false,
            CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            return Task.FromResult(new EntityMediaDiscoveryResult(
                applicationId.Value, stateSpaceId, entityId, "resolution",
                audience == EntityMediaAudience.Player ? [Attachment] : [], []));
        }

        public Task<EntityMediaReadResult?> OpenReadAsync(
            ApplicationIdentifier applicationId, string stateSpaceId, string entityId,
            string mediaId, EntityMediaAudience audience,
            CancellationToken cancellationToken = default)
        {
            var attachment = mediaId == Attachment.MediaId &&
                audience == EntityMediaAudience.Player ? Attachment : null;
            return Task.FromResult<EntityMediaReadResult?>(attachment is null ? null : new(
                attachment,
                new(new(attachment.Sha256, attachment.MediaType, 8, DateTimeOffset.UtcNow),
                    new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10]))));
        }
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }

    private sealed class Audience(LocalKnowledgeSeatSnapshot seat)
        : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(
            string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(campaignId == seat.CampaignId
                ? new KnowledgeAudienceResolution(new(
                    seat.PrincipalId, campaignId, seat.Role, seat.ActorId, "policy.1"))
                : KnowledgeAudienceResolution.Denied());
    }

    private sealed class BindingResolver : IKnowledgeApplicationBindingResolver
    {
        public string Revision { get; set; } = "binding.1";

        public Task<KnowledgeApplicationBinding?> ResolveAsync(
            string campaignId, CancellationToken cancellationToken = default)
        {
            if (campaignId != Campaign)
                return Task.FromResult<KnowledgeApplicationBinding?>(null);
            var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "metadata", "authorized-knowledge.json");
            Assert.True(KnowledgeApplicationBindingDocument.TryParse(
                File.ReadAllText(path), Application.Value, out var document));
            return Task.FromResult<KnowledgeApplicationBinding?>(
                document.Bind(Application.Value, "dnd2024-main", Campaign, Revision));
        }
    }

    private sealed class AuthorizedContext : IApplicationQueryAuthorizedContextProvider
    {
        public Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
            ApplicationIdentifier applicationId, string stateSpaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(
                new Dictionary<string, string>());
    }

    private sealed class QueryCatalog : IPublicApplicationCatalogProvider
    {
        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
        {
            navigator = new QueryNavigator();
            return applicationId == Application;
        }
    }

    private sealed class QueryNavigator : ICatalogNavigator
    {
        public CatalogRecordView Inspect(CatalogRecordRequest request)
        {
            var json = JsonSerializer.Serialize(new
            {
                id = "dnd2024.query.world-scope",
                category = "sample.read",
                name = "World scope",
                description = "Projects one route-bound owner.",
                matches = new[] { "world scope" },
                roles = new Dictionary<string, string> { ["scope"] = "Exact scope." },
                roleBindings = new Dictionary<string, object>
                {
                    ["scope"] = new { source = "route-entity" }
                },
                executor = "mechanic-projection",
                projection = new
                {
                    qualifiedId = "dnd2024.mechanic.world-scope",
                    version = 1,
                    contentHash = new string('A', 64),
                    outputSchemaHash = new string('B', 64)
                },
                outputSchema = new { type = "object" },
                exposure = "binding-only",
                status = "active"
            });
            return new(new(Application.Value, "query", "dnd2024.query.world-scope",
                "World scope", "Projects one route-bound owner.", "", "active", 1,
                new string('C', 64), "test", "test.json"), json);
        }

        public IReadOnlyList<CatalogCollectionSummary> ListCollections(
            ApplicationIdentifier applicationId) => throw new NotSupportedException();
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) =>
            throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) =>
            throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(
            EffectiveApplicationContentRequest request) => throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class StateSpaces : IStateSpaceRegistry
    {
        private static readonly StateSpaceView State = new(
            "dnd2024-main",
            new(Application, 1, new string('A', 64), []),
            new string('B', 64), 1, DateTime.UnixEpoch, DateTime.UnixEpoch);

        public StateSpaceView? Get(string stateSpaceId) =>
            stateSpaceId == State.StateSpaceId ? State : null;
        public StateSpaceView Create(StateSpaceBinding binding) =>
            throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(
            ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) =>
            throw new NotSupportedException();
    }

    private sealed class ExistingEntities : IEntityComponentStore
    {
        public Task<EcsEntityView?> GetEntityAsync(
            string stateSpaceId, string entityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EcsEntityView?>(stateSpaceId == "dnd2024-main" && entityId == Owner
                ? new(stateSpaceId, entityId, "The Seventh Realm", 1, DateTime.UnixEpoch, null)
                : null);

        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId,
            string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId,
            string? afterEntityId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId,
            int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId,
            string qualifiedTypeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId,
            IReadOnlyList<EcsComponentLocator> locators,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId,
            string entityId, string? afterQualifiedTypeId, int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId,
            EcsComponentReference type, int expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}