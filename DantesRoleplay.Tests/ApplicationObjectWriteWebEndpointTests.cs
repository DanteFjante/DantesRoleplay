using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Projections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ApplicationObjectWriteWebEndpointTests
{
    private const string Application = "fixture-app";
    private const string Query = "fixture-app.query.summary";
    private const string Entity = "entity.fixture";

    [Fact]
    public async Task Game_master_write_uses_the_exact_active_object_query_and_bound_route_entity()
    {
        var writes = new Writes();

        var response = await WriteAsync(new(true, "gm", Application, "scope.fixture", null,
            KnowledgeAudienceRole.GameMaster), writes, Body("edit-1"));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(Application, writes.LastRequest!.ApplicationId.Value);
        Assert.Equal("fixture-app.object.summary", writes.LastRequest.Object.QualifiedId);
        Assert.Equal(3, writes.LastRequest.Object.Version);
        Assert.Equal("items", writes.LastRequest.CollectionId);
        Assert.Equal("dm", writes.LastRequest.Perspective);
        Assert.Equal(Entity, writes.LastRequest.RoleEntityIds["subject"]);
        Assert.Equal("changed", response.Body.GetProperty("data").GetProperty("value").GetString());
        Assert.Equal("private, no-store", response.CacheControl);
    }

    [Fact]
    public async Task Non_game_master_is_rejected_before_the_write_service()
    {
        var writes = new Writes();

        var response = await WriteAsync(new(true, "player", Application, "scope.fixture", Entity),
            writes, Body("edit-2"));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.Equal("OBJECT_WRITE_FORBIDDEN", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, writes.Calls);
    }

    [Fact]
    public async Task Write_failures_are_status_mapped_without_internal_details()
    {
        var writes = new Writes { FailureCode = "OBJECT_WRITE_SOURCE_STALE" };

        var response = await WriteAsync(new(true, "gm", Application, "scope.fixture", null,
            KnowledgeAudienceRole.GameMaster), writes, Body("edit-3"));

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("OBJECT_WRITE_SOURCE_STALE", response.Body.GetProperty("code").GetString());
        Assert.DoesNotContain("SECRET", response.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_rejects_a_state_space_outside_the_current_authorized_binding()
    {
        var writes = new Writes();

        var response = await WriteAsync(new(true, "gm", Application, "scope.fixture", null,
            KnowledgeAudienceRole.GameMaster), writes, Body("edit-4"), "space.other");

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.Equal("OBJECT_WRITE_FORBIDDEN", response.Body.GetProperty("code").GetString());
        Assert.Equal(0, writes.Calls);
    }

    private static ApplicationReadModelWebEndpoint.WriteBody Body(string key) => new(
        key,
        new string('C', 64),
        JsonSerializer.SerializeToElement(new { value = "changed" }),
        [new("/items", "relationship.add", "entity.target", 0)]);

    private static async Task<(int StatusCode, JsonElement Body, string? CacheControl)> WriteAsync(
        LocalKnowledgeSeatSnapshot seat,
        Writes writes,
        ApplicationReadModelWebEndpoint.WriteBody body,
        string stateSpaceId = "space.fixture")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Services.AddLogging().BuildServiceProvider();
        var result = await ApplicationReadModelWebEndpoint.WriteAsync(
            Application, stateSpaceId, Entity, Query, body, context,
            new Seats(seat), writes, new Catalog(), new Audience(seat), new Bindings(), new Participation(),
            CancellationToken.None);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone(), context.Response.Headers.CacheControl);
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }

    private sealed class Audience(LocalKnowledgeSeatSnapshot seat) : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default) => Task.FromResult(new KnowledgeAudienceResolution(new(
                seat.PrincipalId, campaignId, seat.Role, seat.ActorId, "policy.fixture")));
    }

    private sealed class Bindings : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
                "authorized-knowledge.json");
            Assert.True(KnowledgeApplicationBindingDocument.TryParse(
                File.ReadAllText(path), "dnd2024", out var document));
            return Task.FromResult<KnowledgeApplicationBinding?>(document.Bind(
                Application, "space.fixture", campaignId, "binding.fixture"));
        }
    }

    private sealed class Participation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(
            KnowledgeApplicationBinding binding,
            string actorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeParticipationResolution(true, "participation.fixture"));
    }

    private sealed class Writes : IApplicationObjectWriteService
    {
        public int Calls { get; private set; }
        public ApplicationObjectWriteRequest? LastRequest { get; private set; }
        public string? FailureCode { get; init; }

        public Task<ApplicationObjectWriteResult> WriteAsync(
            ApplicationObjectWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (FailureCode is not null)
                throw new ApplicationObjectWriteException(FailureCode, "SECRET implementation detail");
            return Task.FromResult(new ApplicationObjectWriteResult(
                true, false, false, "operation.fixture", "{\"value\":\"changed\"}",
                new string('D', 64), []));
        }
    }

    private sealed class Catalog : IPublicApplicationCatalogProvider
    {
        private readonly ICatalogNavigator navigator = new Navigator();

        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator value)
        {
            value = navigator;
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
                category = "fixture.summary",
                name = "Fixture summary",
                description = "A writable fixture object.",
                matches = new[] { "fixture summary" },
                roles = new { subject = "The route-bound entity." },
                executor = "object-projection",
                @object = new
                {
                    qualifiedId = "fixture-app.object.summary",
                    version = 3,
                    contentFingerprint = new string('A', 64)
                },
                collection = "items",
                outputSchema = new { type = "object" },
                exposure = "model-visible",
                status = "active"
            });
            return new(new(Application, "query", Query, "Fixture summary", "Fixture summary.", "",
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

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
