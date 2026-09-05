using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Tests;

public sealed class ApplicationReadinessTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Report_separates_and_evidences_every_ready_owner()
    {
        await using var db = fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("dnd2024");
        var registry = new InMemoryApplicationRegistry();
        registry.Register(new(application, "D&D 2024", "D&D 2024 application.", []));
        var service = new ApplicationReadinessService(
            db,
            registry,
            new Activation(Manifest(application)),
            new Catalogs(available: true),
            new Publications(),
            new Pages(activeRevision: 2, latestRevision: 2),
            new Seats(),
            new Audience(allowed: true),
            new Bindings(Binding()),
            new Participation());

        var report = await service.ReadAsync("dnd2024");

        Assert.Equal("ready", report.Status);
        var database = report.Checks.Single(value => value.Name == "database");
        Assert.Matches("^[0-9A-F]{64}$", database.Evidence!.Fingerprint);
        Assert.DoesNotContain("Data Source=", JsonSerializer.Serialize(database), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(database.Evidence.Fingerprint, (await service.ReadAsync("dnd2024")).Checks
            .Single(value => value.Name == "database").Evidence!.Fingerprint);
        Assert.Equal([
            "database",
            "application-registration",
            "active-catalog-snapshot",
            "catalog-materialization",
            "extension-resolution",
            "query-callability",
            "web-page-release",
            "audience-binding"
        ], report.Checks.Select(value => value.Name));
        Assert.All(report.Checks, value =>
        {
            Assert.Equal("ready", value.Status);
            Assert.Null(value.Recovery);
        });
        Assert.Equal("APPLICATION_QUERIES_CALLABLE",
            report.Checks.Single(value => value.Name == "query-callability").Code);
        var page = report.Checks.Single(value => value.Name == "web-page-release");
        Assert.Equal("WEB_INDEX_PAGE_CURRENT", page.Code);
        Assert.Equal("2", page.Evidence!.Revision);
        Assert.Matches("^[0-9A-F]{64}$", page.Evidence.Fingerprint);
        Assert.Equal("AUDIENCE_CONTEXT_BOUND",
            report.Checks.Single(value => value.Name == "audience-binding").Code);
    }

    [Fact]
    public async Task Broken_catalog_stale_page_and_denied_audience_have_distinct_recoveries()
    {
        await using var db = fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("dnd2024");
        var registry = new InMemoryApplicationRegistry();
        registry.Register(new(application, "D&D 2024", "D&D 2024 application.", []));
        var service = new ApplicationReadinessService(
            db,
            registry,
            new Activation(Manifest(application)),
            new Catalogs(available: false),
            new Publications(),
            new Pages(activeRevision: 1, latestRevision: 2),
            new Seats(),
            new Audience(allowed: false),
            new Bindings(Binding()),
            new Participation());

        var report = await service.ReadAsync("dnd2024");

        Assert.Equal("failed", report.Status);
        AssertFailure(report, "catalog-materialization", "CATALOG_FINGERPRINT_MISMATCH", "repair-catalog");
        AssertFailure(report, "query-callability", "APPLICATION_QUERY_CATALOG_UNAVAILABLE", "repair-catalog");
        AssertFailure(report, "web-page-release", "WEB_INDEX_PAGE_STALE", "review-page-release");
        AssertFailure(report, "audience-binding", "AUDIENCE_CONTEXT_DENIED", "bind-audience");
    }

    [Fact]
    public void Mcp_and_direct_ai_catalogs_publish_the_readiness_contract()
    {
        var descriptor = Assert.Single(McpVerbCatalog.QueryKinds,
            value => value.Name == "system.application-readiness").Descriptor;

        Assert.Equal("mcp.query.system.application-readiness", descriptor.Id);
        Assert.False(descriptor.RequiresConfirmation);
        Assert.Equal("read-system-state", descriptor.Authorization.RequiredCapability);
        Assert.NotEmpty(descriptor.Examples);
        Assert.NotEmpty(descriptor.Errors);
        Assert.NotEmpty(descriptor.RecoveryActions);
        using var schema = JsonDocument.Parse(descriptor.Input.SchemaJson);
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("applicationId", out _));
    }

    private static void AssertFailure(
        ApplicationReadinessReport report,
        string name,
        string code,
        string action)
    {
        var check = report.Checks.Single(value => value.Name == name);
        Assert.Equal("failed", check.Status);
        Assert.Equal(code, check.Code);
        Assert.Equal(action, check.Recovery!.Action);
        Assert.False(string.IsNullOrWhiteSpace(check.Recovery.Description));
    }

    private static ActiveApplicationManifest Manifest(ApplicationIdentifier application) => new(
        application,
        4,
        1,
        Hash('A'),
        Hash('B'),
        Hash('C'),
        Hash('D'),
        Hash('E'),
        Hash('F'),
        "application-dependency-coverage/v1",
        true,
        [],
        [],
        "operation.fixture",
        DateTime.UtcNow)
    {
        ResolutionFingerprint = Hash('9')
    };

    private static KnowledgeApplicationBinding Binding()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json");
        Assert.True(KnowledgeApplicationBindingDocument.TryParse(
            File.ReadAllText(path), "dnd2024", out var document));
        return document.Bind("dnd2024", "dnd2024-main", "campaign.fixture", "binding.fixture");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Hash(char value) => new(value, 64);

    private sealed class Activation(ActiveApplicationManifest manifest) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) => manifest;
    }

    private sealed class Catalogs(bool available) : IPublicApplicationCatalogProvider, IPublicApplicationCatalogDiagnostics
    {
        private readonly ICatalogNavigator navigator = new QueryNavigator();

        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator value)
        {
            value = navigator;
            return available;
        }

        public PublicApplicationCatalogFailure? LastFailure(ApplicationIdentifier applicationId) =>
            available ? null : new("CATALOG_FINGERPRINT_MISMATCH", "A retained catalog file changed after activation.");
    }

    private sealed class QueryNavigator : ICatalogNavigator
    {
        private static readonly CatalogRecordSummary Query = new(
            "dnd2024", "query", "dnd2024.query.fixture", "Fixture query", "Fixture query.", "queries",
            "active", 1, Hash('1'), "fixture", "queries/fixture.json");
        private static readonly CatalogRecordSummary Projection = new(
            "dnd2024", "mechanic", "dnd2024.mechanic.fixture.project", "Fixture projection",
            "Fixture projection.", "mechanics", "active", 1, Hash('2'), "fixture", "mechanics/fixture.json");

        public CatalogSearchResult Search(CatalogSearchRequest request) =>
            new([new(Query, 1)], null);

        public CatalogRecordView Inspect(CatalogRecordRequest request) => request.QualifiedId switch
        {
            "dnd2024.query.fixture" => new(Query, JsonSerializer.Serialize(new
            {
                id = Query.QualifiedId,
                category = "dnd2024.presentation.fixture",
                name = Query.Name,
                description = Query.Description,
                matches = new[] { "show fixture" },
                roles = new { subject = "The authorized subject." },
                executor = "mechanic-projection",
                projection = new
                {
                    qualifiedId = Projection.QualifiedId,
                    version = Projection.Version,
                    contentHash = Projection.ContentFingerprint,
                    outputSchemaHash = Hash('3')
                },
                outputSchema = new { type = "object", additionalProperties = false },
                exposure = "model-visible",
                status = "active"
            })),
            "dnd2024.mechanic.fixture.project" => new(Projection, "{}"),
            _ => throw new KeyNotFoundException()
        };

        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) => [];
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) =>
            throw new NotSupportedException();
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => throw new NotSupportedException();
    }

    private sealed class Publications : IWebPagePublicationDirectory
    {
        public Task<PublishedWebPage?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<PublishedWebPage?>(null);

        public Task<PublishedWebPage?> FindIndexAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default) => Task.FromResult<PublishedWebPage?>(new(
                applicationId, "publication.fixture", "page.fixture", "Play", "Play", "dnd2024-play",
                0, "public", "dnd2024-play", true));
    }

    private sealed class Pages(int activeRevision, int latestRevision) : IWebPageStore
    {
        public Task<WebPageSummary?> GetSummaryAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WebPageSummary?>(new(id, activeRevision, latestRevision, DateTime.UtcNow));

        public Task<WebPageRevisionDocument?> GetRevisionAsync(
            string id, int revision, CancellationToken cancellationToken = default) =>
            Task.FromResult<WebPageRevisionDocument?>(new(
                new(id, revision, revision == activeRevision, DateTime.UtcNow, Hash('4'), 2, 128),
                "<main></main>", []));

        public Task<WebPageDiscoveryPage> ListPageAsync(string? afterPageId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageRevisionDiscoveryPage> ListRevisionsAsync(string id, int? beforeRevision, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageRevisionDocument> AppendDraftAsync(string id, int baseRevision, int expectedLatestRevision, string html, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageRevisionDocument> AppendBundleDraftAsync(string id, int expectedLatestRevision, WebPageBundle bundle, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageActivationResult> ActivateRevisionAsync(string id, int revision, int expectedActiveRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageDocument> SaveAndActivateAsync(string id, string html, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageDocument> SaveBundleAndActivateAsync(string id, WebPageBundle bundle, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageDocument?> GetActiveAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WebPageAssetDocument?> GetActiveAssetAsync(string id, string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Seats : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => new(
            true, "principal.fixture", "dnd2024", "campaign.fixture", null,
            KnowledgeAudienceRole.GameMaster, ["dnd2024-core"]);
    }

    private sealed class Audience(bool allowed) : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default) => Task.FromResult(allowed
                ? new KnowledgeAudienceResolution(new(
                    "principal.fixture", campaignId, KnowledgeAudienceRole.GameMaster, null, "policy.fixture"))
                : KnowledgeAudienceResolution.Denied());
    }

    private sealed class Bindings(KnowledgeApplicationBinding binding) : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<KnowledgeApplicationBinding?>(binding);
    }

    private sealed class Participation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(
            KnowledgeApplicationBinding binding,
            string actorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeParticipationResolution(true, "participation.fixture"));
    }
}
