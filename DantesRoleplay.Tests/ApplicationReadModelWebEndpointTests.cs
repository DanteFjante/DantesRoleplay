using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ApplicationReadModelWebEndpointTests
{
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
        Assert.Equal("READ_MODEL_AUDIENCE_DENIED", other.Body.GetProperty("code").GetString());
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
    public async Task Live_catalog_roles_bind_from_the_authorized_seat_context()
    {
        var service = new ReadModels();
        var response = await ReadAsync(
            new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, new QueryCatalog("dnd2024.query.actor-context", "campaign", "actor"));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("campaign.1", service.LastRequest!.RoleBindings["campaign"]);
        Assert.Equal("actor.aric", service.LastRequest.RoleBindings["actor"]);
        Assert.Equal(2, service.LastRequest.RoleBindings.Count);
    }

    [Fact]
    public async Task Unavailable_catalog_is_not_reported_as_missing_audience_roles()
    {
        var service = new ReadModels();
        var response = await ReadAsync(
            new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, new EmptyPublicApplicationCatalogProvider());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("READ_MODEL_CATALOG_UNAVAILABLE", response.Body.GetProperty("code").GetString());
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
    public async Task Explicit_campaign_requires_host_authorization_before_projection()
    {
        var service = new ReadModels();
        var response = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service, query: "campaignId=foreign");
        Assert.Equal(403, response.StatusCode);
        Assert.Equal("READ_MODEL_FORBIDDEN", response.Body.GetProperty("code").GetString());
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

    private static async Task<(int StatusCode, JsonElement Body)> ReadAsync(
        LocalKnowledgeSeatSnapshot seat,
        string entityId,
        ReadModels service,
        IPublicApplicationCatalogProvider? catalogs = null,
        string? perspective = null,
        string? query = null)
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
            "dnd2024", "dnd2024-main", entityId, "dnd2024.query.character-sheet",
            context, new Seats(seat), service, CancellationToken.None, catalogs);
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
        public string? FailureCode { get; init; }

        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (FailureCode is not null) throw new ApplicationReadModelException(FailureCode, "SECRET source detail");
            var data = request.RoleBindings.TryGetValue("subject", out var subject)
                ? JsonSerializer.Serialize(new { subject = new { id = subject } })
                : JsonSerializer.Serialize(new { roles = request.RoleBindings });
            return Task.FromResult(new ApplicationReadModelResult(
                request.ApplicationId.Value,
                request.StateSpaceId,
                request.QualifiedQueryId,
                "state-fingerprint",
                "resolution-fingerprint",
                new string('A', 64),
                new string('B', 64),
                new string('C', 64),
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
}
