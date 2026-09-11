using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Web.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ApplicationReadModelWebEndpointTests
{
    [Fact]
    public async Task Change_stream_scope_cannot_elevate_or_cross_the_ambient_audience_binding()
    {
        var actorSeat = new LocalKnowledgeSeatSnapshot(
            true, "player", "dnd2024", "campaign.1", "actor.aric");
        var actor = new WebChangeScopeAuthorizer(new Seats(actorSeat), new Audience(actorSeat),
            new Bindings(), new Participation(true));

        Assert.True(await actor.AuthorizeAsync(new(
            "dnd2024", "dnd2024-main", "player")));
        Assert.False(await actor.AuthorizeAsync(new(
            "dnd2024", "dnd2024-main", "dm")));
        Assert.False(await actor.AuthorizeAsync(new(
            "other", "dnd2024-main", "player")));
        Assert.False(await actor.AuthorizeAsync(new(
            "dnd2024", "other-space", "player")));

        var gmSeat = actorSeat with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        var gameMaster = new WebChangeScopeAuthorizer(new Seats(gmSeat), new Audience(gmSeat),
            new Bindings(), new Participation(true));
        Assert.True(await gameMaster.AuthorizeAsync(new(
            "dnd2024", "dnd2024-main", "dm")));
        Assert.True(await gameMaster.AuthorizeAsync(new(
            "dnd2024", "dnd2024-main", "player")));
    }

    [Fact]
    public async Task Actor_may_read_only_their_own_registered_read_model()
    {
        var service = new ReadModels();

        var own = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service);
        var other = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.other", service);

        Assert.Equal(StatusCodes.Status200OK, own.StatusCode);
        Assert.Equal(MechanicAudienceContext.Player, service.LastRequest!.Audience);
        Assert.Equal("actor.aric", own.Body.GetProperty("data").GetProperty("subject")
            .GetProperty("id").GetString());
        Assert.Equal(StatusCodes.Status403Forbidden, other.StatusCode);
        Assert.Equal("READ_MODEL_FORBIDDEN", other.Body.GetProperty("code").GetString());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Game_master_may_read_an_application_character()
    {
        var service = new ReadModels();

        var response = await ReadAsync(new(true, "gm", "dnd2024", "campaign.1", null,
            KnowledgeAudienceRole.GameMaster), "actor.aric", service);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(MechanicAudienceContext.GameMaster, service.LastRequest!.Audience);
        Assert.Equal("resolution-fingerprint", response.Body
            .GetProperty("resolutionFingerprint").GetString());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Cross_application_and_disabled_seats_fail_before_projection()
    {
        var service = new ReadModels();

        var wrongApplication = await ReadAsync(new(true, "player", "other", "campaign.1", "actor.aric"),
            "actor.aric", service);
        var disabled = await ReadAsync(new(false, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service);

        Assert.Equal(StatusCodes.Status403Forbidden, wrongApplication.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, disabled.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Unbound_multi_role_catalog_never_infers_role_names_from_the_seat()
    {
        var service = new ReadModels();
        var response = await ReadAsync(
            new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, new QueryCatalog("dnd2024.query.actor-context", "campaign", "actor"));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        Assert.Equal("READ_MODEL_ROLES_UNAVAILABLE", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Single_unbound_route_entity_role_is_bound_without_role_name_inference()
    {
        var service = new ReadModels();
        var response = await ReadAsync(
            new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, new QueryCatalog("dnd2024.query.neutral-single", "opaque-route"));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("actor.aric", service.LastRequest!.RoleBindings["opaque-route"]);
        Assert.Single(service.LastRequest.RoleBindings);
    }

    [Fact]
    public async Task Unavailable_catalog_is_not_reported_as_missing_audience_roles()
    {
        var service = new ReadModels();
        var response = await ReadAsync(
            new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, new EmptyPublicApplicationCatalogProvider());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("READ_MODEL_UNAVAILABLE", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Game_master_player_preview_runs_with_player_audience()
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "gm", "dnd2024", "campaign.1", null,
            KnowledgeAudienceRole.GameMaster), "actor.aric", service, perspective: "player");
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(MechanicAudienceContext.Player, service.LastRequest!.Audience);
    }

    [Theory]
    [InlineData("dm", 403)]
    [InlineData("invalid", 400)]
    [InlineData("player&perspective=dm", 400)]
    public async Task Invalid_or_elevated_preview_fails_before_projection(string perspective, int status)
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, perspective: perspective);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("input=%7B%7D&input=%7B%7D")]
    [InlineData("input=%7B%22x%22%3A1%2C%22x%22%3A2%7D")]
    [InlineData("input=%5B%5D")]
    [InlineData("campaignId=one&campaignId=two")]
    public async Task Invalid_query_input_is_rejected_before_any_read_model(string query)
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, query: query);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("READ_MODEL_INPUT_INVALID", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Input_reaches_the_service_but_cannot_change_the_authorized_role()
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, query: "input=" + Uri.EscapeDataString("{\"itemId\":\"fixture.item\"}"));
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("actor.aric", service.LastRequest!.RoleBindings["subject"]);
        Assert.Equal("{\"itemId\":\"fixture.item\"}", service.LastRequest.InputJson);
    }

    [Fact]
    public async Task CampaignId_alias_is_rejected_before_projection()
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, query: "campaignId=foreign");
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("READ_MODEL_INPUT_INVALID", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("READ_MODEL_OUTPUT_INVALID", 503, "READ_MODEL_UNAVAILABLE")]
    [InlineData("READ_MODEL_SOURCE_STALE", 409, "READ_MODEL_SOURCE_STALE")]
    [InlineData("READ_MODEL_STATE_SPACE_UNKNOWN", 403, "READ_MODEL_FORBIDDEN")]
    public async Task New_input_aware_errors_are_sanitized(string failure, int status, string code)
    {
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", new ReadModels { FailureCode = failure }, query: "input=%7B%7D");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, response.Body.GetProperty("code").GetString());
        Assert.DoesNotContain("SECRET", response.Body.GetRawText());
        Assert.Equal(2, response.Body.EnumerateObject().Count());
    }

    [Fact]
    public async Task Inputless_registered_role_binding_sanitizes_unexpected_projection_failures()
    {
        var response = await ReadAsync(
            new(true, "gm", "dnd2024", "campaign.1", null, KnowledgeAudienceRole.GameMaster),
            "actor.aric", new ReadModels { ThrowUnexpected = true }, new RouteBoundCatalog(),
            qualifiedQueryId: "dnd2024.query.route-bound",
            roleResolver: new ApplicationQueryRoleBindingResolver(
                new DantesRoleplay.SchemaValidation.BoundedJsonSchemaValidator()),
            entities: new ExistingEntities("actor.aric"), stateSpaces: new StateSpaces());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("READ_MODEL_UNAVAILABLE", response.Body.GetProperty("code").GetString());
        Assert.DoesNotContain("SECRET", response.Body.GetRawText());
        Assert.Equal(2, response.Body.EnumerateObject().Count());
    }

    private static async Task<(int StatusCode, JsonElement Body)> ReadAsync(
        LocalKnowledgeSeatSnapshot seat,
        string entityId,
        ReadModels service,
        IPublicApplicationCatalogProvider? catalogs = null,
        string? perspective = null,
        string? query = null,
        string qualifiedQueryId = "dnd2024.query.character-sheet",
        IApplicationQueryRoleBindingResolver? roleResolver = null,
        IApplicationQueryAuthorizedContextProvider? authorizedContext = null,
        IEntityComponentStore? entities = null,
        IStateSpaceRegistry? stateSpaces = null)
    {
        var context = new DefaultHttpContext();
        if (perspective is not null) context.Request.QueryString = new QueryString("?perspective=" + perspective);
        if (query is not null) context.Request.QueryString = new QueryString("?" + query);
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Services
            .AddLogging()
            .BuildServiceProvider();
        var result = await ApplicationReadModelWebEndpoint.ReadAsync(
            "dnd2024", "dnd2024-main", entityId, qualifiedQueryId,
            context, new Seats(seat), service, CancellationToken.None,
            catalogs ?? new QueryCatalog(qualifiedQueryId, "subject"),
            roleResolver, authorizedContext,
            entities, stateSpaces);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }

    private sealed class ReadModels : IApplicationReadModelService
    {
        public int Calls { get; private set; }
        public ApplicationReadModelRequest? LastRequest { get; private set; }
        public List<ApplicationReadModelRequest> Requests { get; } = [];
        public string? FailureCode { get; init; }
        public bool ThrowUnexpected { get; init; }
        public string SelectedId { get; init; } = "encounter.1";
        public string ProofId { get; init; } = "entity.root";
        public bool ChangeSelection { get; init; }
        public bool ChangeSelectionEvidence { get; init; }
        public bool ChangeSelectedScope { get; init; }

        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            Requests.Add(request);
            if (ThrowUnexpected) throw new InvalidOperationException("SECRET unexpected projection detail");
            if (FailureCode is not null) throw new ApplicationReadModelException(FailureCode, "SECRET source detail");
            var data = request.QualifiedQueryId == "dnd2024.query.selection-proof"
                ? JsonSerializer.Serialize(new { proof = new { id = ProofId } })
                : request.QualifiedQueryId == "dnd2024.query.current-scene"
                ? JsonSerializer.Serialize(new { encounterId = ChangeSelection && Calls > 1 ? "encounter.changed" : SelectedId })
                : request.RoleBindings.TryGetValue("subject", out var subject)
                ? JsonSerializer.Serialize(new { subject = new { id = subject } })
                : JsonSerializer.Serialize(new { roles = request.RoleBindings });
            return Task.FromResult(new ApplicationReadModelResult(
                request.ApplicationId.Value,
                request.StateSpaceId,
                request.QualifiedQueryId,
                ChangeSelectedScope && request.QualifiedQueryId == "dnd2024.query.neutral-read"
                    ? "changed-state-fingerprint" : "state-fingerprint",
                "resolution-fingerprint",
                new string('A', 64),
                new string('B', 64),
                ChangeSelectionEvidence && request.QualifiedQueryId == "dnd2024.query.selection-proof" && Calls > 2
                    ? new string('D', 64) : new string('C', 64),
                data));
        }
    }

    private sealed class QueryCatalog(string queryId, params string[] roles) : IPublicApplicationCatalogProvider
    {
        private readonly ICatalogNavigator _navigator = new QueryNavigator(queryId, roles);

        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator)
        {
            navigator = _navigator;
            return true;
        }
    }

    private sealed class QueryNavigator(string queryId, IReadOnlyList<string> roles) : ICatalogNavigator
    {
        public CatalogRecordView Inspect(CatalogRecordRequest request)
        {
            var roleMap = roles.ToDictionary(value => value, value => $"Bind {value}.", StringComparer.Ordinal);
            var json = JsonSerializer.Serialize(new
            {
                id = queryId,
                category = "game.core.campaign.read-model",
                name = "Test query",
                description = "A focused query contract for endpoint role binding.",
                matches = new[] { "test query" },
                roles = roleMap,
                executor = "mechanic-projection",
                projection = new
                {
                    qualifiedId = "dnd2024.mechanic.test.project",
                    version = 1,
                    contentHash = new string('A', 64),
                    outputSchemaHash = new string('B', 64)
                },
                outputSchema = new { type = "object" },
                exposure = "model-visible",
                status = "active"
            });
            if (queryId == "dnd2024.query.selected")
            {
                var node = JsonNode.Parse(json)!;
                node["id"] = request.QualifiedId;
                if (request.QualifiedId == "dnd2024.query.current-scene")
                    node["roles"] = new JsonObject { ["campaign"] = "Trusted campaign." };
                else node["campaignSelection"] = new JsonObject
                {
                    ["queryId"] = "dnd2024.query.current-scene", ["entityIdField"] = "encounterId"
                };
                json = node.ToJsonString();
            }
            return new(new("dnd2024", "query", queryId, "Test query", "Test query.", "", "active", 1,
                new string('C', 64), "test", "test.json"), json);
        }

        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) =>
            throw new NotSupportedException();
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) =>
            throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Legacy_campaign_selection_contract_is_rejected_before_projection()
    {
        var reads = new ReadModels();
        var result = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "encounter.1", reads, new QueryCatalog("dnd2024.query.selected", "encounter"));
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("READ_MODEL_FORBIDDEN", result.Body.GetProperty("code").GetString());
        Assert.Equal(0, reads.Calls);
    }

    private sealed class RouteBoundCatalog : IPublicApplicationCatalogProvider
    {
        private readonly ICatalogNavigator navigator = new RouteBoundNavigator();

        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator value)
        {
            value = navigator;
            return applicationId.Value == "dnd2024";
        }
    }

    private sealed class RouteBoundNavigator : ICatalogNavigator
    {
        public CatalogRecordView Inspect(CatalogRecordRequest request)
        {
            var source = new QueryNavigator("dnd2024.query.route-bound", ["subject"]).Inspect(request);
            var node = JsonNode.Parse(source.ContentJson)!.AsObject();
            node["roleBindings"] = new JsonObject
            {
                ["subject"] = new JsonObject { ["source"] = "route-entity" }
            };
            return source with { ContentJson = node.ToJsonString() };
        }

        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) =>
            throw new NotSupportedException();
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) =>
            throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Neutral_selection_transport_passes_only_authorized_parent_roles_to_the_shared_service()
    {
        var reads = new ReadModels();
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seat.root"] = "entity.root",
            ["seat.witness"] = "entity.witness"
        };

        var result = await ReadAsync(new(true, "player", "dnd2024", "legacy.selection", "actor.aric"),
            "entity.root", reads, new NeutralSelectionCatalog(),
            qualifiedQueryId: "dnd2024.query.neutral-read",
            roleResolver: new ApplicationQueryRoleBindingResolver(new DantesRoleplay.SchemaValidation.BoundedJsonSchemaValidator()),
            authorizedContext: new AuthorizedContext(roles),
            entities: new ExistingEntities("entity.root", "entity.witness"),
            stateSpaces: new StateSpaces());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, reads.Calls);
        Assert.Equal("dnd2024.query.neutral-read", reads.LastRequest!.QualifiedQueryId);
        Assert.Equal("entity.root", reads.LastRequest.RoleBindings["target"]);
        Assert.Equal("entity.witness", reads.LastRequest.RoleBindings["witness"]);
    }

    [Fact]
    public async Task Neutral_selection_registration_never_grants_an_untrusted_route_entity()
    {
        var reads = new ReadModels();
        var result = await NeutralRead(reads,
            authorizedRoles: new Dictionary<string, string> { ["seat.witness"] = "entity.witness" });

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("READ_MODEL_FORBIDDEN", result.Body.GetProperty("code").GetString());
        Assert.Equal(0, reads.Calls);
    }

    [Fact]
    public async Task Neutral_selection_rejects_the_retired_campaignId_alias_before_the_shared_service()
    {
        var reads = new ReadModels();
        var alias = await NeutralRead(reads, query: "campaignId=entity.other");
        Assert.Equal(400, alias.StatusCode);
        Assert.Equal("READ_MODEL_INPUT_INVALID", alias.Body.GetProperty("code").GetString());
        Assert.Equal(0, reads.Calls);
    }

    private static Task<(int StatusCode, JsonElement Body)> NeutralRead(
        ReadModels reads,
        IReadOnlyDictionary<string, string>? authorizedRoles = null,
        IPublicApplicationCatalogProvider? catalog = null,
        string? query = null) => ReadAsync(
        new(true, "player", "dnd2024", "legacy.selection", "actor.aric"),
        "entity.root", reads, catalog ?? new NeutralSelectionCatalog(), query: query,
        qualifiedQueryId: "dnd2024.query.neutral-read",
        roleResolver: new ApplicationQueryRoleBindingResolver(new DantesRoleplay.SchemaValidation.BoundedJsonSchemaValidator()),
        authorizedContext: new AuthorizedContext(authorizedRoles ?? new Dictionary<string, string>
        {
            ["seat.root"] = "entity.root",
            ["seat.witness"] = "entity.witness"
        }),
        entities: new ExistingEntities("entity.root", "entity.witness"),
        stateSpaces: new StateSpaces());

    [Theory]
    [InlineData("dnd2024.query.character-sheet", "encounterId")]
    [InlineData("other.query.scene", "encounterId")]
    [InlineData("dnd2024.query.current-scene", "location.id")]
    [InlineData("dnd2024.query.current-scene", "")]
    public void Selection_metadata_rejects_self_cross_owner_and_nested_paths(string queryId, string field)
    {
        var app = ApplicationIdentifier.Parse("dnd2024");
        var node = JsonNode.Parse(new QueryNavigator("dnd2024.query.character-sheet", ["subject"])
            .Inspect(new(app, app.Value, "dnd2024.query.character-sheet")).ContentJson)!;
        node["campaignSelection"] = new JsonObject { ["queryId"] = queryId, ["entityIdField"] = field };
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(node.ToJsonString(), app));
    }

    private sealed class Audience(LocalKnowledgeSeatSnapshot seat) : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeAudienceResolution(new(seat.PrincipalId, campaignId, seat.Role,
                seat.ActorId, "policy.1")));
    }

    private sealed class NeutralSelectionCatalog(
        bool recursive = false,
        bool missing = false,
        bool targetWitness = false)
        : IPublicApplicationCatalogProvider
    {
        private readonly ICatalogNavigator navigator = new NeutralSelectionNavigator(recursive, missing, targetWitness);
        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator value)
        {
            value = navigator;
            return applicationId.Value == "dnd2024";
        }
    }

    private sealed class NeutralSelectionNavigator(bool recursive, bool missing, bool targetWitness) : ICatalogNavigator
    {
        public CatalogRecordView Inspect(CatalogRecordRequest request)
        {
            if (request.QualifiedId == "dnd2024.query.selection-missing")
                throw new KeyNotFoundException("Missing selector.");
            var selector = request.QualifiedId == "dnd2024.query.selection-proof";
            var roles = selector
                ? new Dictionary<string, string> { ["proof-root"] = "Opaque root.", ["proof-witness"] = "Opaque witness." }
                : new Dictionary<string, string> { ["target"] = "Opaque target.", ["witness"] = "Opaque witness." };
            var roleBindings = selector
                ? new Dictionary<string, object>
                {
                    ["proof-root"] = new { source = "route-entity" },
                    ["proof-witness"] = new { source = "authorized-context", key = "seat.witness" }
                }
                : new Dictionary<string, object>
                {
                    ["target"] = new { source = "route-entity" },
                    ["witness"] = new { source = "authorized-context", key = "seat.witness" }
                };
            var node = JsonSerializer.SerializeToNode(new
            {
                id = request.QualifiedId,
                category = "sample.read",
                name = "Neutral selection fixture",
                description = "Exercises application-neutral selection roles.",
                matches = new[] { "neutral selection fixture" },
                roles,
                roleBindings,
                executor = "mechanic-projection",
                projection = new
                {
                    qualifiedId = "dnd2024.mechanic.neutral.fixture",
                    version = 1,
                    contentHash = new string('A', 64),
                    outputSchemaHash = new string('B', 64)
                },
                outputSchema = new { type = "object" },
                exposure = "binding-only",
                status = "active"
            })!.AsObject();
            if (!selector)
                node["selection"] = new JsonObject
                {
                    ["queryId"] = missing ? "dnd2024.query.selection-missing" : "dnd2024.query.selection-proof",
                    ["targetRole"] = targetWitness ? "witness" : "target",
                    ["resultPointer"] = "/proof/id",
                    ["roleBindings"] = new JsonObject
                    {
                        ["proof-root"] = "target",
                        ["proof-witness"] = "witness"
                    }
                };
            else if (recursive)
                node["selection"] = new JsonObject
                {
                    ["queryId"] = "dnd2024.query.selection-proof-2",
                    ["targetRole"] = "proof-root",
                    ["resultPointer"] = "/proof/id",
                    ["roleBindings"] = new JsonObject { ["next-root"] = "proof-root" }
                };
            return new(new("dnd2024", "query", request.QualifiedId, "Neutral selection fixture",
                "Exercises application-neutral selection roles.", "", "active", 1,
                new string('C', 64), "test", "neutral-selection.json"), node.ToJsonString());
        }

        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) => throw new NotSupportedException();
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) => throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => throw new NotSupportedException();
    }

    private sealed class AuthorizedContext(IReadOnlyDictionary<string, string> values)
        : IApplicationQueryAuthorizedContextProvider
    {
        public Task<IReadOnlyDictionary<string, string>?> ResolveAsync(ApplicationIdentifier applicationId,
            string stateSpaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(
                applicationId.Value == "dnd2024" && stateSpaceId == "dnd2024-main" ? values : null);
    }

    private sealed class StateSpaces : IStateSpaceRegistry
    {
        private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("dnd2024");
        private static readonly StateSpaceView State = new("dnd2024-main",
            new(App, 1, new string('A', 64), []), new string('B', 64), 1,
            DateTime.UnixEpoch, DateTime.UnixEpoch);
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == State.StateSpaceId ? State : null;
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) => throw new NotSupportedException();
    }

    private sealed class ExistingEntities(params string[] ids) : IEntityComponentStore
    {
        private readonly HashSet<string> values = ids.ToHashSet(StringComparer.Ordinal);
        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId,
            CancellationToken cancellationToken = default) => Task.FromResult<EcsEntityView?>(
                stateSpaceId == "dnd2024-main" && values.Contains(entityId)
                    ? new(stateSpaceId, entityId, entityId, 1, DateTime.UnixEpoch, null) : null);
        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId, string qualifiedTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId, IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId, string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Participation(bool active) : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(KnowledgeApplicationBinding binding,
            string actorId, CancellationToken cancellationToken = default) => Task.FromResult(active
                ? new KnowledgeParticipationResolution(true, "participation.1")
                : KnowledgeParticipationResolution.MissingActor());
    }

    private sealed class Bindings : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId, CancellationToken cancellationToken = default)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            var json = File.ReadAllText(Path.Combine(directory!.FullName, "catalog", "applications", "dnd2024", "metadata", "authorized-knowledge.json"));
            Assert.True(KnowledgeApplicationBindingDocument.TryParse(json, "dnd2024", out var document));
            return Task.FromResult<KnowledgeApplicationBinding?>(document.Bind("dnd2024", "dnd2024-main", "campaign.1", "binding.1"));
        }
    }
}
