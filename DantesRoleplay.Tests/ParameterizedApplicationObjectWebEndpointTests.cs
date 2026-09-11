using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ParameterizedApplicationObjectWebEndpointTests
{
    private const string Application = "sample-app";
    private const string StateSpace = "state.one";
    private const string Query = "sample-app.query.parameterized-object";
    private const string RouteEntity = "entity.route";

    [Fact]
    public async Task Full_object_submission_uses_only_declared_and_trusted_role_sources()
    {
        var writes = new Writes();
        var response = await WriteAsync(writes, "entity.target");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("object", writes.LastRequest!.SubmissionMode);
        Assert.Equal("After", JsonDocument.Parse(writes.LastRequest.SubmittedObjectJson)
            .RootElement.GetProperty("name").GetString());
        Assert.Equal(RouteEntity, writes.LastRequest.RoleEntityIds["anchor"]);
        Assert.Equal("entity.target", writes.LastRequest.RoleEntityIds["selected"]);
        Assert.Equal("entity.owner", writes.LastRequest.RoleEntityIds["trusted"]);
    }

    [Fact]
    public async Task Caller_input_cannot_override_the_authorized_context_or_cross_state_spaces()
    {
        var writes = new Writes();
        var injected = await WriteAsync(writes, "entity.target", "entity.attacker");
        Assert.Equal(StatusCodes.Status400BadRequest, injected.StatusCode);
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID", injected.Body.GetProperty("code").GetString());
        Assert.Equal(0, writes.Calls);

        var foreign = await WriteAsync(writes, "entity.target", stateSpaceId: "state.other");
        Assert.Equal(StatusCodes.Status403Forbidden, foreign.StatusCode);
        Assert.Equal("OBJECT_WRITE_FORBIDDEN", foreign.Body.GetProperty("code").GetString());
        Assert.Equal(0, writes.Calls);
    }

    [Fact]
    public async Task Actor_read_cannot_turn_declared_input_or_route_roles_into_cross_entity_authority()
    {
        var inputCrossEntity = await ReadAsync(RouteEntity: "entity.actor",
            selectedEntityId: "entity.attacker");
        Assert.Equal(StatusCodes.Status403Forbidden, inputCrossEntity.StatusCode);
        Assert.Equal("READ_MODEL_FORBIDDEN", inputCrossEntity.Body.GetProperty("code").GetString());
        Assert.Equal(0, inputCrossEntity.Calls);

        var routeCrossEntity = await ReadAsync(RouteEntity: "entity.attacker",
            selectedEntityId: "entity.actor");
        Assert.Equal(StatusCodes.Status403Forbidden, routeCrossEntity.StatusCode);
        Assert.Equal(0, routeCrossEntity.Calls);

        var crossState = await ReadAsync(RouteEntity: "entity.actor",
            selectedEntityId: "entity.actor", stateSpaceId: "state.other");
        Assert.Equal(StatusCodes.Status403Forbidden, crossState.StatusCode);
        Assert.Equal(0, crossState.Calls);
    }

    [Fact]
    public async Task Actor_read_may_use_its_original_entity_and_an_independently_trusted_role_entity()
    {
        var response = await ReadAsync(RouteEntity: "entity.actor", selectedEntityId: "entity.owner");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(1, response.Calls);
    }

    private static async Task<(int StatusCode, JsonElement Body)> WriteAsync(
        Writes writes,
        string selectedEntityId,
        string? attemptedTrustedOverride = null,
        string stateSpaceId = StateSpace)
    {
        var input = attemptedTrustedOverride is null
            ? JsonSerializer.Serialize(new { targetId = selectedEntityId })
            : JsonSerializer.Serialize(new
            {
                targetId = selectedEntityId,
                trusted = attemptedTrustedOverride
            });
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create("input", input);
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Services.AddLogging().BuildServiceProvider();
        var body = new ApplicationReadModelWebEndpoint.WriteBody(
            "object-submit.fixture.1", new string('C', 64))
        {
            Mode = "object",
            Object = JsonSerializer.SerializeToElement(new { name = "After" })
        };
        var application = ApplicationIdentifier.Parse(Application);
        var seat = new LocalKnowledgeSeatSnapshot(true, "principal", Application,
            "legacy-context", null, KnowledgeAudienceRole.GameMaster,
            AuthorizedRoleEntityIds: new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            });
        var entities = new Entities(stateSpaceId == StateSpace
            ? [RouteEntity, "entity.target", "entity.owner"] : []);
        var result = await ApplicationReadModelWebEndpoint.WriteAsync(
            Application, stateSpaceId, RouteEntity, Query, body, context,
            new Seats(seat), writes, new Catalog(), CancellationToken.None,
            new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator()),
            new AuthorizedContext(), entities, new StateSpaces(application));
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<(int StatusCode, JsonElement Body, int Calls)> ReadAsync(
        string RouteEntity,
        string selectedEntityId,
        string stateSpaceId = StateSpace)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create("input",
            JsonSerializer.Serialize(new { targetId = selectedEntityId }));
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Services.AddLogging().BuildServiceProvider();
        var reads = new Reads();
        var app = ApplicationIdentifier.Parse(Application);
        var seat = new LocalKnowledgeSeatSnapshot(true, "principal", Application,
            "legacy-context", "entity.actor", KnowledgeAudienceRole.Actor,
            AuthorizedRoleEntityIds: new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            });
        var result = await ApplicationReadModelWebEndpoint.ReadAsync(
            Application, stateSpaceId, RouteEntity, Query, context, new Seats(seat), reads,
            CancellationToken.None, new Catalog(), roleResolver:
            new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator()),
            authorizedContext: new AuthorizedContext(),
            entities: new Entities(["entity.actor", "entity.owner", "entity.attacker"]),
            stateSpaces: new StateSpaces(app));
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone(), reads.Calls);
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }

    private sealed class AuthorizedContext : IApplicationQueryAuthorizedContextProvider
    {
        public Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
            ApplicationIdentifier applicationId, string stateSpaceId,
            CancellationToken cancellationToken = default) => Task.FromResult<
                IReadOnlyDictionary<string, string>?>(applicationId.Value == Application
                    && stateSpaceId == StateSpace
                    ? new Dictionary<string, string> { ["session.owner"] = "entity.owner" }
                    : null);
    }

    private sealed class StateSpaces(ApplicationIdentifier application) : IStateSpaceRegistry
    {
        private readonly StateSpaceView state = new(StateSpace,
            new(application, 1, new string('A', 64), []), new string('B', 64), 1,
            DateTime.UnixEpoch, DateTime.UnixEpoch);
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == StateSpace ? state : null;
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) => throw new NotSupportedException();
    }

    private sealed class Entities(IReadOnlyCollection<string> ids) : IEntityComponentStore
    {
        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId,
            CancellationToken cancellationToken = default) => Task.FromResult<EcsEntityView?>(
            stateSpaceId == StateSpace && ids.Contains(entityId)
                ? new(stateSpaceId, entityId, entityId, 1, DateTime.UnixEpoch, null) : null);
        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId,
            int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId,
            string qualifiedTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId,
            IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId,
            string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId,
            EcsComponentReference type, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Writes : IApplicationObjectWriteService
    {
        public int Calls { get; private set; }
        public ApplicationObjectWriteRequest? LastRequest { get; private set; }
        public Task<ApplicationObjectWriteResult> WriteAsync(ApplicationObjectWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new ApplicationObjectWriteResult(true, false, false,
                "operation.fixture", "{\"name\":\"After\"}", new string('D', 64), []));
        }
    }

    private sealed class Reads : IApplicationReadModelService
    {
        public int Calls { get; private set; }
        public Task<ApplicationReadModelResult> ReadAsync(ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ApplicationReadModelResult(Application, StateSpace, Query,
                new string('A', 64), new string('B', 64), new string('C', 64),
                new string('D', 64), new string('E', 64), "{\"name\":\"Visible\"}"));
        }
    }

    private sealed class Catalog : IPublicApplicationCatalogProvider
    {
        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
        {
            navigator = new Navigator();
            return applicationId.Value == Application;
        }
    }

    private sealed class Navigator : ICatalogNavigator
    {
        public CatalogRecordView Inspect(CatalogRecordRequest request)
        {
            var content = JsonSerializer.Serialize(new
            {
                id = Query,
                category = "sample.read",
                name = "Parameterized object",
                description = "Reads one parameterized registered object.",
                matches = new[] { "parameterized object" },
                roles = new { anchor = "Route entity.", selected = "Selected entity.", trusted = "Trusted entity." },
                roleBindings = new
                {
                    anchor = new { source = "route-entity" },
                    selected = new { source = "input", pointer = "/targetId" },
                    trusted = new { source = "authorized-context", key = "session.owner" }
                },
                executor = "object-projection",
                @object = new
                {
                    qualifiedId = "sample-app.object.fixture",
                    version = 1,
                    contentFingerprint = new string('A', 64)
                },
                collection = "items",
                outputSchema = new { type = "object" },
                inputSchema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "targetId" },
                    properties = new { targetId = new { type = "string", minLength = 1, maxLength = 200 } }
                },
                exposure = "model-visible",
                status = "active"
            });
            return new(new(Application, "query", Query, "Parameterized object", "Object.", "",
                "active", 1, new string('B', 64), "fixture", "fixture.json"), content);
        }
        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) =>
            throw new NotSupportedException();
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) =>
            throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => throw new NotSupportedException();
    }

    private sealed class UnusedAudience : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId,
            CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException(
            "Explicit role bindings must not invoke the legacy campaign audience adapter.");
    }
    private sealed class UnusedBindings : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,
            CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException(
            "Explicit role bindings must not invoke the legacy campaign binding adapter.");
    }
    private sealed class UnusedParticipation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(KnowledgeApplicationBinding binding,
            string actorId, CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException(
            "Explicit role bindings must not invoke legacy participation.");
    }
}
