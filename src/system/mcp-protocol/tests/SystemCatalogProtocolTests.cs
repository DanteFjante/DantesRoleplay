using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Sources;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Ecs;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;

namespace DantesRoleplay.McpProtocol.Tests;

public sealed class SystemCatalogProtocolTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Empty_provider_discloses_nothing_and_capabilities_publish_only_confirmed_system_writes()
    {
        await using var db = _fixture.CreateContext();
        var result = await QueryAsync(db, new EmptyPublicApplicationCatalogProvider(), "system.catalogs", applicationId: "fixture");
        var capabilities = await QueryAsync(db, new EmptyPublicApplicationCatalogProvider(), "capabilities");
        var data = Json(capabilities.Data);

        Assert.False(result.Ok);
        Assert.Equal("PUBLIC_CATALOG_UNAVAILABLE", result.Error?.Code);
        Assert.Equal("query(kind: \"capabilities\")", result.Error?.Fix);
        Assert.Contains(data.GetProperty("Query").EnumerateArray(), item => item.GetProperty("Name").GetString() == "system.catalog.browse");
        Assert.Equal(
            ["system.application.activate", "system.application.register", "system.component-type.register", "system.interaction-execute", "system.interaction-recipe-review", "system.knowledge-state.sync", "system.source.register", "system.state-space.adopt-legacy", "system.state-space.create", "system.state-space.upgrade", "system.trigger-scheduling", "system.world-state.sync"],
            data.GetProperty("Commit").EnumerateArray()
                .Select(item => item.GetProperty("Name").GetString())
                .Where(name => name!.StartsWith("system.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
        Assert.Equal(2, await db.Operations.AsNoTracking().CountAsync(operation => operation.Tool == "query"));
    }

    [Fact]
    public async Task Direct_dispatch_lists_browses_searches_inspects_and_recovers_from_cursor_errors()
    {
        await using var db = _fixture.CreateContext();
        var provider = PublicFixture();
        var listed = await QueryAsync(db, provider, "system.catalogs", applicationId: "fixture");
        var first = await QueryAsync(db, provider, "system.catalog.browse", applicationId: "fixture", collection: "actions", pageSize: 1);
        var firstData = Json(first.Data).GetProperty("Result");
        var cursor = firstData.GetProperty("NextCursor").GetString();
        var second = await QueryAsync(db, provider, "system.catalog.browse", applicationId: "fixture", collection: "actions", pageSize: 1, cursor: cursor);
        var searched = await QueryAsync(db, provider, "system.catalog.search", applicationId: "fixture", query: "strike");
        var record = await QueryAsync(db, provider, "system.catalog.record", applicationId: "fixture", collection: "actions", id: "fixture.attack");
        var tampered = cursor![..^1] + (cursor[^1] == 'A' ? "B" : "A");
        var invalid = await QueryAsync(db, provider, "system.catalog.browse", applicationId: "fixture", collection: "actions", pageSize: 1, cursor: tampered);
        var stale = await QueryAsync(db, provider, "system.catalog.browse", applicationId: "fixture", collection: "actions", pageSize: 2, cursor: cursor);

        Assert.True(listed.Ok); Assert.True(first.Ok); Assert.True(second.Ok); Assert.True(searched.Ok); Assert.True(record.Ok);
        Assert.NotNull(cursor);
        Assert.NotEqual(
            firstData.GetProperty("Entries")[0].GetProperty("StableKey").GetString(),
            Json(second.Data).GetProperty("Result").GetProperty("Entries")[0].GetProperty("StableKey").GetString());
        Assert.Equal("fixture.attack", Json(searched.Data).GetProperty("Result").GetProperty("Records")[0].GetProperty("Record").GetProperty("QualifiedId").GetString());
        Assert.Equal("{\"id\":\"fixture.attack\"}", Json(record.Data).GetProperty("Record").GetProperty("ContentJson").GetString());
        Assert.Equal("CURSOR_INVALID", invalid.Error?.Code);
        Assert.Equal("CURSOR_STALE", stale.Error?.Code);
        Assert.All(await db.Operations.AsNoTracking().ToListAsync(), operation => Assert.Equal("query", operation.Tool));
    }

    public void Dispose() => _fixture.Dispose();

    private static Task<ToolEnvelope> QueryAsync(
        DantesRoleplayDbContext db,
        IPublicApplicationCatalogProvider catalogs,
        string kind,
        string? applicationId = null,
        string? collection = null,
        string? query = null,
        string? id = null,
        int? pageSize = null,
        string? cursor = null) => new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: kind, id: id, query: query, applicationId: applicationId, collection: collection,
            pageSize: pageSize, cursor: cursor, publicCatalogs: catalogs);

    private static JsonElement Json(object? value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    internal static IPublicApplicationCatalogProvider PublicFixture()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var records = new[]
        {
            Record("document", "fixture.guide", "Guide", "", [], []),
            Record("action", "fixture.attack", "Attack", "combat", ["strike"], ["attack target"])
        };
        var manifest = CatalogNavigationManifest.Create(app, new string('A', 64), "catalog-lexical-v1",
            [new("actions", "Actions", "Public application actions.")],
            [
                new("actions", "", "Actions", "Public application actions.", CatalogDescriptionStatus.Authored),
                new("actions", "combat", "Combat", "Public combat actions.", CatalogDescriptionStatus.Authored)
            ], records);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("public-protocol-fixture-cursor-signing-key")));
        var ledger = ApplicationIdentifier.Parse("ledger");
        var ledgerManifest = CatalogNavigationManifest.Create(
            ledger, new string('B', 64), "catalog-lexical-v1",
            [new("actions", "Ledger actions", "Public ledger application actions.")],
            [new("actions", "", "Ledger actions", "Public ledger application actions.", CatalogDescriptionStatus.Authored)],
            [Record("action", "ledger.reconcile", "Reconcile ledger", "", ["balance"], ["reconcile entries"])]);
        var ledgerNavigator = new InMemoryCatalogNavigator(ledgerManifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("public-protocol-fixture-cursor-signing-key")));
        return new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = navigator,
            [ledger] = ledgerNavigator
        });
    }

    private static CatalogRecordDefinition Record(string kind, string id, string name, string path, IReadOnlyList<string> aliases, IReadOnlyList<string> phrases)
    {
        var content = $$"""{"id":"{{id}}"}""";
        return new("actions", kind, id, name, "A public protocol fixture.", aliases, phrases, path,
            "active", 1, content, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            "public", "catalog/actions.json");
    }
}

public sealed class SystemCatalogMcpWalkTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _databasePath = null!;
    private string _sourceRoot = null!;
    private int _nextId = 1;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"catalog-walk-{Guid.NewGuid():N}.db");
        _sourceRoot = Path.Combine(Path.GetTempPath(), $"catalog-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_sourceRoot, "catalog"));
        Directory.CreateDirectory(Path.Combine(_sourceRoot, "extension"));
        await File.WriteAllTextAsync(Path.Combine(_sourceRoot, "catalog", "preview.json"), "{\"preview\":true}");
        await File.WriteAllTextAsync(Path.Combine(_sourceRoot, "extension", "optional.json"), "{\"optional\":true}");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDantesRoleplayMcpServer(_databasePath,
            allowedSourceRoots: new Dictionary<string, string>
            {
                ["workspace"] = _sourceRoot,
                ["repository"] = RepositoryRoot()
            },
            publishedApplicationCatalogs: ["dnd2024"]);
        var fixtureCatalog = SystemCatalogProtocolTests.PublicFixture();
        builder.Services.AddScoped<IPublicApplicationCatalogProvider>(services =>
            new CombinedPublicApplicationCatalogProvider(
                fixtureCatalog,
                services.GetRequiredService<ActivatedApplicationCatalogProvider>()));
        _app = builder.Build();
        await _app.Services.InitialiseDantesRoleplayAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var application = ApplicationIdentifier.Parse("fixture");
            scope.ServiceProvider.GetRequiredService<IApplicationRegistry>()
                .Register(new(application, "Fixture", "Protocol fixture application.", []));
            var type = scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>()
                .Define(new(application, "fixture.stats",
                    "{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\"}}}"));
            scope.ServiceProvider.GetRequiredService<IProjectionDefinitionRegistry>().Define(new(
                application, "fixture.score-view", "{\"type\":\"integer\"}",
                [new("stats", "subject", new(type.QualifiedId, type.Version, type.SchemaHash))], [],
                [new("stats", "/score", "")]));
            scope.ServiceProvider.GetRequiredService<ISourceRegistry>().Register(new(
                application, "catalog", "workspace", "catalog/**/*.json", SourceTrust.Trusted, 10, "catalog"));
            scope.ServiceProvider.GetRequiredService<ISourceRegistry>().Register(new(
                application, "extension.optional", "workspace", "extension/**/*.json",
                SourceTrust.Trusted, 20, "extension.optional"));
            scope.ServiceProvider.GetRequiredService<ISourceScanReceiptStore>().Record(new(
                application, "catalog", 1, SourceScanStatus.Succeeded, new string('B', 64), DateTime.UtcNow));
        }
        _app.MapMcp(ServerConfiguration.McpEndpoint);
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        await CallAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "system-catalog-walk", version = "1.0" }
        });
    }

    [Fact]
    public async Task Public_catalog_walk_uses_three_verbs_and_callable_cursor_recovery()
    {
        var tools = await CallAsync("tools/list", new { });
        Assert.Equal(["commit", "orient", "query"], tools.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).Order(StringComparer.Ordinal));

        var capabilities = await ToolAsync("query", new { kind = "capabilities" });
        var applications = await ToolAsync("query", new { kind = "system.applications" });
        var application = await ToolAsync("query", new { kind = "system.applications", applicationId = "fixture" });
        var sources = await ToolAsync("query", new { kind = "system.sources", applicationId = "fixture" });
        var source = await ToolAsync("query", new { kind = "system.sources", applicationId = "fixture", id = "catalog" });
        var preview = await ToolAsync("query", new
        {
            kind = "system.application-preview", applicationId = "fixture",
            sourceIds = new[] { "catalog" }, limit = 1
        });
        var activationToken = "7123456789abcdef0123456789abcdef";
        var activationPayload = JsonSerializer.Serialize(new
        {
            requestToken = activationToken,
            applicationId = "fixture",
            previewFingerprint = preview.Data.GetProperty("previewFingerprint").GetString(),
            expectedActiveFingerprint = (string?)null,
            sourceIds = new[] { "catalog" }
        });
        var activationWithoutDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Activate the exact neutral fixture overlay.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload, dryRun = true,
            intent = "Validate the exact neutral fixture overlay activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Activate the exact neutral fixture overlay.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationReplay = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Activate the exact neutral fixture overlay.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceToken = "8123456789abcdef0123456789abcdef";
        var stateSpacePayload = JsonSerializer.Serialize(new
        {
            requestToken = stateSpaceToken,
            stateSpaceId = "fixture-space",
            applicationId = "fixture",
            activeFingerprint = activationCommit.Data.GetProperty("activation").GetProperty("activationFingerprint").GetString(),
            expectedFingerprint = (string?)null
        });
        var stateSpaceWithoutDryRun = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Create an empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceDryRun = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload, dryRun = true,
            intent = "Validate empty neutral fixture state-space creation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceCommit = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Create an empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceReplay = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Create an empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        await File.WriteAllTextAsync(Path.Combine(_sourceRoot, "catalog", "preview.json"),
            "{\"preview\":\"second\"}");
        var secondPreview = await ToolAsync("query", new
        {
            kind = "system.application-preview", applicationId = "fixture",
            sourceIds = new[] { "catalog" }, limit = 1
        });
        var secondActivationToken = "9123456789abcdef0123456789abcdef";
        var secondActivationPayload = JsonSerializer.Serialize(new
        {
            requestToken = secondActivationToken,
            applicationId = "fixture",
            previewFingerprint = secondPreview.Data.GetProperty("previewFingerprint").GetString(),
            expectedActiveFingerprint = activationCommit.Data.GetProperty("activation").GetProperty("activationFingerprint").GetString(),
            sourceIds = new[] { "catalog" }
        });
        var secondActivationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = secondActivationPayload, dryRun = true,
            intent = "Validate a second exact neutral fixture activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var secondActivationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = secondActivationPayload,
            intent = "Activate the second exact neutral fixture overlay.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var upgradeToken = "a123456789abcdef0123456789abcdef";
        var upgradePayload = JsonSerializer.Serialize(new
        {
            requestToken = upgradeToken,
            stateSpaceId = "fixture-space",
            applicationId = "fixture",
            activeFingerprint = secondActivationCommit.Data.GetProperty("activation").GetProperty("activationFingerprint").GetString(),
            expectedBindingFingerprint = stateSpaceCommit.Data.GetProperty("binding").GetProperty("bindingFingerprint").GetString()
        });
        var upgradeWithoutDryRun = await ToolAsync("commit", new
        {
            kind = "system.state-space.upgrade", payload = upgradePayload,
            intent = "Upgrade the empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var upgradeDryRun = await ToolAsync("commit", new
        {
            kind = "system.state-space.upgrade", payload = upgradePayload, dryRun = true,
            intent = "Validate the empty neutral fixture state-space upgrade.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var upgradeCommit = await ToolAsync("commit", new
        {
            kind = "system.state-space.upgrade", payload = upgradePayload,
            intent = "Upgrade the empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var upgradeReplay = await ToolAsync("commit", new
        {
            kind = "system.state-space.upgrade", payload = upgradePayload,
            intent = "Upgrade the empty neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceHistoricalReplay = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Replay creation of the neutral fixture state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activatedApplication = await ToolAsync("query", new
        {
            kind = "system.applications", applicationId = "fixture"
        });
        var dependencies = await ToolAsync("query", new { kind = "system.dependencies", applicationId = "fixture", limit = 1 });
        var fieldDependents = await ToolAsync("query", new
        {
            kind = "system.dependencies", applicationId = "fixture",
            id = "component:fixture.stats@1#/score", transitive = true, limit = 1
        });
        var listed = await ToolAsync("query", new { kind = "system.catalogs", applicationId = "fixture" });
        var first = await ToolAsync("query", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "actions", pageSize = 1 });
        var cursor = first.Data.GetProperty("result").GetProperty("nextCursor").GetString();
        var second = await ToolAsync("query", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "actions", pageSize = 1, cursor });
        var searched = await ToolAsync("query", new { kind = "system.catalog.search", applicationId = "fixture", query = "strike" });
        var record = await ToolAsync("query", new { kind = "system.catalog.record", applicationId = "fixture", collection = "actions", id = "fixture.attack" });
        var ledgerListed = await ToolAsync("query", new { kind = "system.catalogs", applicationId = "ledger" });
        var ledgerSearch = await ToolAsync("query", new
        {
            kind = "system.catalog.search", applicationId = "ledger", collection = "actions",
            query = "reconcile entries"
        });
        var fixtureIsolationSearch = await ToolAsync("query", new
        {
            kind = "system.catalog.search", applicationId = "fixture", collection = "actions",
            query = "reconcile entries"
        });
        var ledgerRecord = await ToolAsync("query", new
        {
            kind = "system.catalog.record", applicationId = "ledger", collection = "actions",
            id = "ledger.reconcile"
        });

        var applicationToken = "5123456789abcdef0123456789abcdef";
        var applicationPayload = JsonSerializer.Serialize(new
        {
            requestToken = applicationToken,
            applicationId = "registered",
            displayName = "Registered",
            description = "Protocol registration fixture.",
            baseApplications = Array.Empty<string>(),
            expectedFingerprint = (string?)null
        });
        var applicationWithoutDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.register",
            payload = applicationPayload,
            intent = "Register a neutral application.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var applicationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.register",
            payload = applicationPayload,
            dryRun = true,
            intent = "Validate a neutral application registration.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        await AssertFailureAsync("APPLICATION_UNKNOWN", new { kind = "system.applications", applicationId = "registered" });
        var applicationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.register",
            payload = applicationPayload,
            intent = "Register a neutral application.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var applicationReplay = await ToolAsync("commit", new
        {
            kind = "system.application.register",
            payload = applicationPayload,
            intent = "Register a neutral application.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var componentTypeToken = "c123456789abcdef0123456789abcdef";
        var componentTypePayload = JsonSerializer.Serialize(new
        {
            requestToken = componentTypeToken,
            applicationId = "registered",
            qualifiedTypeId = "registered.note",
            schemaJson = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"note\":{\"type\":\"string\",\"maxLength\":80}}}",
            expectedSchemaHash = (string?)null
        });
        var componentTypeWithoutDryRun = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = componentTypePayload,
            intent = "Register a neutral fixture component type.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var componentTypeDryRun = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = componentTypePayload, dryRun = true,
            intent = "Validate a neutral fixture component type.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var componentTypeCommit = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = componentTypePayload,
            intent = "Register a neutral fixture component type.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var componentTypeReplay = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = componentTypePayload,
            intent = "Register a neutral fixture component type.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var sourceToken = "6123456789abcdef0123456789abcdef";
        var sourcePayload = JsonSerializer.Serialize(new
        {
            requestToken = sourceToken,
            applicationId = "registered",
            sourceId = "documents",
            allowedRootId = "workspace",
            relativePathOrGlob = "documents/**/*.md",
            trust = "untrusted",
            precedence = 5,
            logicalIdentity = "documents",
            expectedFingerprint = (string?)null
        });
        var sourceDryRun = await ToolAsync("commit", new
        {
            kind = "system.source.register", payload = sourcePayload, dryRun = true,
            intent = "Validate a neutral document source.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var sourceCommit = await ToolAsync("commit", new
        {
            kind = "system.source.register", payload = sourcePayload,
            intent = "Register a neutral document source.", proceduresUsed = new[] { "procedure.system.use" }
        });
        var registeredApplication = await ToolAsync("query", new { kind = "system.applications", applicationId = "registered" });
        var registeredSource = await ToolAsync("query", new { kind = "system.sources", applicationId = "registered", id = "documents" });

        Assert.True(capabilities.Ok); Assert.True(applications.Ok); Assert.True(application.Ok); Assert.True(sources.Ok); Assert.True(source.Ok); Assert.True(preview.Ok); Assert.True(activationDryRun.Ok); Assert.True(activationCommit.Ok); Assert.True(activationReplay.Ok); Assert.True(stateSpaceDryRun.Ok); Assert.True(stateSpaceCommit.Ok); Assert.True(stateSpaceReplay.Ok); Assert.True(secondPreview.Ok); Assert.True(secondActivationDryRun.Ok); Assert.True(secondActivationCommit.Ok); Assert.True(upgradeDryRun.Ok); Assert.True(upgradeCommit.Ok); Assert.True(upgradeReplay.Ok); Assert.True(stateSpaceHistoricalReplay.Ok); Assert.True(activatedApplication.Ok); Assert.True(dependencies.Ok); Assert.True(fieldDependents.Ok); Assert.True(listed.Ok); Assert.True(first.Ok); Assert.True(second.Ok); Assert.True(searched.Ok); Assert.True(record.Ok); Assert.True(ledgerListed.Ok); Assert.True(ledgerSearch.Ok); Assert.True(fixtureIsolationSearch.Ok); Assert.True(ledgerRecord.Ok);
        Assert.False(activationWithoutDryRun.Ok);
        Assert.Equal("DRY_RUN_REQUIRED", activationWithoutDryRun.Error.GetProperty("code").GetString());
        Assert.True(activationDryRun.Data.GetProperty("dryRun").GetBoolean());
        Assert.False(activationCommit.Data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(activationToken, activationCommit.OperationId);
        Assert.Equal(activationCommit.OperationId, activationReplay.OperationId);
        Assert.Equal(activationCommit.Data.GetRawText(), activationReplay.Data.GetRawText());
        Assert.False(activationCommit.Data.GetProperty("activation").GetProperty("dependencyCoverageComplete").GetBoolean());
        Assert.Equal(["catalog"], activationCommit.Data.GetProperty("activation").GetProperty("sourceIds")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(secondActivationCommit.Data.GetProperty("activation").GetProperty("activationFingerprint").GetString(),
            activatedApplication.Data.GetProperty("application").GetProperty("active").GetProperty("activationFingerprint").GetString());
        Assert.False(stateSpaceWithoutDryRun.Ok);
        Assert.Equal("DRY_RUN_REQUIRED", stateSpaceWithoutDryRun.Error.GetProperty("code").GetString());
        Assert.True(stateSpaceDryRun.Data.GetProperty("dryRun").GetBoolean());
        Assert.False(stateSpaceCommit.Data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(stateSpaceToken, stateSpaceCommit.OperationId);
        Assert.Equal(stateSpaceCommit.OperationId, stateSpaceReplay.OperationId);
        Assert.Equal(stateSpaceCommit.Data.GetRawText(), stateSpaceReplay.Data.GetRawText());
        Assert.Equal("fixture-space", activatedApplication.Data.GetProperty("stateSpaces")[0].GetProperty("stateSpaceId").GetString());
        Assert.Equal(stateSpaceCommit.Data.GetProperty("binding").GetProperty("bindingFingerprint").GetString(),
            stateSpaceHistoricalReplay.Data.GetProperty("binding").GetProperty("bindingFingerprint").GetString());
        Assert.False(upgradeWithoutDryRun.Ok);
        Assert.Equal("DRY_RUN_REQUIRED", upgradeWithoutDryRun.Error.GetProperty("code").GetString());
        Assert.True(upgradeDryRun.Data.GetProperty("dryRun").GetBoolean());
        Assert.False(upgradeCommit.Data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(upgradeToken, upgradeCommit.OperationId);
        Assert.Equal(upgradeCommit.Data.GetRawText(), upgradeReplay.Data.GetRawText());
        Assert.Equal("empty-state-compatible", upgradeCommit.Data.GetProperty("compatibility").GetProperty("code").GetString());
        Assert.Equal(0, upgradeCommit.Data.GetProperty("compatibility").GetProperty("entityCount").GetInt32());
        Assert.Equal(2, activatedApplication.Data.GetProperty("stateSpaces")[0].GetProperty("bindingRevision").GetInt32());
        Assert.Equal(upgradeCommit.Data.GetProperty("binding").GetProperty("bindingFingerprint").GetString(),
            activatedApplication.Data.GetProperty("stateSpaces")[0].GetProperty("bindingFingerprint").GetString());
        Assert.False(applicationWithoutDryRun.Ok);
        Assert.Equal("DRY_RUN_REQUIRED", applicationWithoutDryRun.Error.GetProperty("code").GetString());
        Assert.Contains(applicationToken, applicationWithoutDryRun.Error.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.True(applicationDryRun.Ok); Assert.True(applicationCommit.Ok); Assert.True(applicationReplay.Ok);
        Assert.False(componentTypeWithoutDryRun.Ok);
        Assert.Equal("DRY_RUN_REQUIRED", componentTypeWithoutDryRun.Error.GetProperty("code").GetString());
        Assert.True(componentTypeDryRun.Ok); Assert.True(componentTypeCommit.Ok); Assert.True(componentTypeReplay.Ok);
        Assert.True(componentTypeDryRun.Data.GetProperty("dryRun").GetBoolean());
        Assert.False(componentTypeCommit.Data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(componentTypeToken, componentTypeCommit.OperationId);
        Assert.Equal(componentTypeCommit.OperationId, componentTypeReplay.OperationId);
        Assert.Equal(componentTypeCommit.Data.GetRawText(), componentTypeReplay.Data.GetRawText());
        Assert.Equal("registered.note", componentTypeCommit.Data.GetProperty("componentType").GetProperty("qualifiedId").GetString());
        Assert.Equal(1, componentTypeCommit.Data.GetProperty("componentType").GetProperty("version").GetInt32());
        Assert.True(sourceDryRun.Ok); Assert.True(sourceCommit.Ok); Assert.True(registeredApplication.Ok); Assert.True(registeredSource.Ok);
        Assert.True(applicationDryRun.Data.GetProperty("dryRun").GetBoolean());
        Assert.False(applicationCommit.Data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(applicationToken, applicationCommit.OperationId);
        Assert.Equal(applicationCommit.OperationId, applicationReplay.OperationId);
        Assert.Equal(applicationCommit.Data.GetRawText(), applicationReplay.Data.GetRawText());
        Assert.Equal(sourceToken, sourceCommit.OperationId);
        Assert.Equal("documents/**/*.md", registeredSource.Data.GetProperty("source").GetProperty("relativePathOrGlob").GetString());
        Assert.Contains(capabilities.Data.GetProperty("query").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.catalog.search");
        Assert.Contains(capabilities.Data.GetProperty("query").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.dependencies");
        Assert.Contains(capabilities.Data.GetProperty("commit").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.application.activate");
        Assert.Contains(capabilities.Data.GetProperty("commit").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.component-type.register");
        Assert.Contains(capabilities.Data.GetProperty("commit").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.state-space.create");
        Assert.Contains(capabilities.Data.GetProperty("commit").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "system.state-space.upgrade");
        Assert.Equal("fixture.attack", record.Data.GetProperty("record").GetProperty("summary").GetProperty("qualifiedId").GetString());
        Assert.Equal("ledger", ledgerListed.Data.GetProperty("applicationId").GetString());
        Assert.Equal("ledger.reconcile", ledgerSearch.Data.GetProperty("result").GetProperty("records")[0]
            .GetProperty("record").GetProperty("qualifiedId").GetString());
        Assert.Empty(fixtureIsolationSearch.Data.GetProperty("result").GetProperty("records").EnumerateArray());
        Assert.Equal("ledger.reconcile", ledgerRecord.Data.GetProperty("record").GetProperty("summary")
            .GetProperty("qualifiedId").GetString());
        Assert.Equal("fixture", applications.Data.GetProperty("applications")[0].GetProperty("id").GetString());
        var listedCoreSource = Assert.Single(sources.Data.GetProperty("sources").EnumerateArray(),
            value => value.GetProperty("sourceId").GetString() == "catalog");
        Assert.Equal("catalog/**/*.json", listedCoreSource.GetProperty("relativePathOrGlob").GetString());
        Assert.Equal(1, listedCoreSource.GetProperty("latestScan").GetProperty("generation").GetInt32());
        Assert.Equal("Fixture", application.Data.GetProperty("application").GetProperty("displayName").GetString());
        Assert.Equal("catalog", source.Data.GetProperty("source").GetProperty("sourceId").GetString());
        Assert.True(preview.Data.GetProperty("isValid").GetBoolean());
        Assert.Equal(1, preview.Data.GetProperty("counts").GetProperty("winners").GetInt32());
        Assert.Equal("catalog/preview.json", preview.Data.GetProperty("winners")[0].GetProperty("relativePath").GetString());
        Assert.DoesNotContain(_sourceRoot, preview.Data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, dependencies.Data.GetProperty("counts").GetProperty("nodes").GetInt32());
        Assert.Single(dependencies.Data.GetProperty("nodes").EnumerateArray());
        Assert.True(dependencies.Data.GetProperty("truncated").GetBoolean());
        Assert.False(dependencies.Data.GetProperty("coverage").GetProperty("complete").GetBoolean());
        Assert.Equal("fixture.score-view",
            fieldDependents.Data.GetProperty("dependents")[0].GetProperty("node").GetProperty("qualifiedId").GetString());
        Assert.DoesNotContain(Path.GetTempPath(), sources.Data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.All(first.NextSteps, step => Assert.StartsWith("query(kind:", step, StringComparison.Ordinal));

        var stale = await ToolAsync("query", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "actions", pageSize = 2, cursor });
        Assert.False(stale.Ok);
        Assert.Equal("CURSOR_STALE", stale.Error.GetProperty("code").GetString());
        Assert.StartsWith("query(kind:", stale.Error.GetProperty("fix").GetString(), StringComparison.Ordinal);

        var denied = await ToolAsync("query", new { kind = "system.applications" }, remoteCandidate: true);
        Assert.False(denied.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", denied.Error.GetProperty("code").GetString());
        var deniedCommit = await ToolAsync("commit", new
        {
            kind = "system.application.register", payload = "not-json", dryRun = true
        }, remoteCandidate: true);
        Assert.False(deniedCommit.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedCommit.Error.GetProperty("code").GetString());
        var deniedActivation = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = "not-json", dryRun = true
        }, remoteCandidate: true);
        Assert.False(deniedActivation.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedActivation.Error.GetProperty("code").GetString());
        var deniedComponentType = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = "not-json", dryRun = true
        }, remoteCandidate: true);
        Assert.False(deniedComponentType.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedComponentType.Error.GetProperty("code").GetString());
        var deniedStateSpace = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = "not-json", dryRun = true
        }, remoteCandidate: true);
        Assert.False(deniedStateSpace.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedStateSpace.Error.GetProperty("code").GetString());
        var deniedUpgrade = await ToolAsync("commit", new
        {
            kind = "system.state-space.upgrade", payload = "not-json", dryRun = true
        }, remoteCandidate: true);
        Assert.False(deniedUpgrade.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedUpgrade.Error.GetProperty("code").GetString());
        await AssertFailureAsync("APPLICATION_UNKNOWN", new { kind = "system.applications", applicationId = "missing" });
        await AssertFailureAsync("SOURCE_UNKNOWN", new { kind = "system.sources", applicationId = "fixture", id = "missing" });
        await AssertFailureAsync("INVALID_PAYLOAD", new { kind = "system.sources", applicationId = "fixture", limit = 101 });
        await AssertFailureAsync("INVALID_PAYLOAD", new { kind = "system.application-preview", applicationId = "fixture", limit = 251 });
        await AssertFailureAsync("INVALID_PAYLOAD", new { kind = "system.dependencies", applicationId = "fixture", limit = 251 });
        var deniedPreview = await ToolAsync("query", new
        {
            kind = "system.application-preview", applicationId = "system"
        }, remoteCandidate: true);
        Assert.False(deniedPreview.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedPreview.Error.GetProperty("code").GetString());
        var deniedDependencies = await ToolAsync("query", new
        {
            kind = "system.dependencies", applicationId = "system", id = "invalid"
        }, remoteCandidate: true);
        Assert.False(deniedDependencies.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", deniedDependencies.Error.GetProperty("code").GetString());

        var tamperedCursor = cursor![..^1] + (cursor[^1] == 'A' ? "B" : "A");
        await AssertFailureAsync("INVALID_APPLICATION", new { kind = "system.catalogs", applicationId = "system" });
        await AssertFailureAsync("PUBLIC_CATALOG_UNAVAILABLE", new { kind = "system.catalogs", applicationId = "missing" });
        await AssertFailureAsync("INVALID_PAYLOAD", new { kind = "system.catalog.browse", applicationId = "fixture" });
        await AssertFailureAsync("CATALOG_COLLECTION_UNKNOWN", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "missing" });
        await AssertFailureAsync("CATALOG_NODE_UNKNOWN", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "actions", branch = "missing" });
        await AssertFailureAsync("CATALOG_RECORD_UNKNOWN", new { kind = "system.catalog.record", applicationId = "fixture", collection = "actions", id = "fixture.missing" });
        await AssertFailureAsync("CURSOR_INVALID", new { kind = "system.catalog.browse", applicationId = "fixture", collection = "actions", pageSize = 1, cursor = tamperedCursor });

        var history = await ToolAsync("query", new { kind = "history", subject = "query:system.catalog.browse", limit = 20 });
        Assert.True(history.Ok);
        Assert.NotEmpty(history.Data.GetProperty("operations").EnumerateArray());

        using var auditScope = _app.Services.CreateScope();
        var audits = await auditScope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>()
            .Operations.AsNoTracking().Where(operation => operation.Subject == "query:system.applications" && operation.Success).ToListAsync();
        Assert.NotEmpty(audits);
        Assert.All(audits, audit =>
        {
            Assert.Contains("principal.", audit.GuardEvidenceJson, StringComparison.Ordinal);
            Assert.DoesNotContain("local-operator", audit.GuardEvidenceJson, StringComparison.OrdinalIgnoreCase);
        });
        var database = auditScope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        var directCatalogs = auditScope.ServiceProvider.GetRequiredService<IPublicApplicationCatalogProvider>();
        var ledgerApplication = ApplicationIdentifier.Parse("ledger");
        Assert.True(directCatalogs.TryGet(ledgerApplication, out var directLedger));
        var directLedgerRecord = directLedger.Inspect(new(ledgerApplication, "actions", "ledger.reconcile"));
        Assert.Equal(
            ledgerRecord.Data.GetProperty("record").GetProperty("contentJson").GetString(),
            directLedgerRecord.ContentJson);
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == applicationToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == componentTypeToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == sourceToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == activationToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == stateSpaceToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == secondActivationToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == upgradeToken));
        Assert.Single(auditScope.ServiceProvider.GetRequiredService<ISourceScanReceiptStore>()
            .For(ApplicationIdentifier.Parse("fixture"), "catalog"));
        Assert.Single(auditScope.ServiceProvider.GetRequiredService<IStateSpaceRegistry>()
            .ListPage(ApplicationIdentifier.Parse("fixture"), null, 100).StateSpaces);
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_entity"));
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_component"));
        Assert.Equal(2, await ScalarAsync(database,
            "SELECT COUNT(*) FROM system_state_space_binding_revision WHERE StateSpaceId = 'fixture-space'"));
    }

    [Fact]
    public async Task Trail_survival_operator_onboarding_uses_only_existing_system_protocol()
    {
        var tools = await CallAsync("tools/list", new { });
        Assert.Equal(["commit", "orient", "query"], tools.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).Order(StringComparer.Ordinal));

        var applicationToken = "a123456789abcdef0123456789abcdef";
        var applicationPayload = JsonSerializer.Serialize(new
        {
            requestToken = applicationToken,
            applicationId = "trail-survival",
            displayName = "Trail Survival",
            description = "Original customizable single-player trail-survival application.",
            baseApplications = Array.Empty<string>(),
            expectedFingerprint = (string?)null
        });
        var applicationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.register", payload = applicationPayload, dryRun = true,
            intent = "Validate the Trail Survival application registration.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var applicationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.register", payload = applicationPayload,
            intent = "Register the Trail Survival application.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        Assert.True(applicationDryRun.Ok, applicationDryRun.Ok ? "" : applicationDryRun.Error.GetRawText());
        Assert.True(applicationCommit.Ok, applicationCommit.Ok ? "" : applicationCommit.Error.GetRawText());

        var sourceToken = "b123456789abcdef0123456789abcdef";
        var sourcePayload = JsonSerializer.Serialize(new
        {
            requestToken = sourceToken,
            applicationId = "trail-survival",
            sourceId = "trail-survival-core",
            allowedRootId = "repository",
            relativePathOrGlob = "catalog/applications/trail-survival/**/*",
            trust = "trusted",
            precedence = 0,
            logicalIdentity = "trail-survival-core-catalog",
            expectedFingerprint = (string?)null
        });
        var sourceDryRun = await ToolAsync("commit", new
        {
            kind = "system.source.register", payload = sourcePayload, dryRun = true,
            intent = "Validate the Trail Survival authored source registration.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var sourceCommit = await ToolAsync("commit", new
        {
            kind = "system.source.register", payload = sourcePayload,
            intent = "Register the Trail Survival authored source.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        Assert.True(sourceDryRun.Ok, sourceDryRun.Ok ? "" : sourceDryRun.Error.GetRawText());
        Assert.True(sourceCommit.Ok, sourceCommit.Ok ? "" : sourceCommit.Error.GetRawText());

        var preview = await ToolAsync("query", new
        {
            kind = "system.application-preview", applicationId = "trail-survival", limit = 100
        });
        Assert.True(preview.Ok, preview.Ok ? "" : preview.Error.GetRawText());
        Assert.True(preview.Data.GetProperty("isValid").GetBoolean());
        Assert.Equal(0, preview.Data.GetProperty("counts").GetProperty("problems").GetInt32());
        var winner = Assert.Single(preview.Data.GetProperty("winners").EnumerateArray(), value =>
            value.GetProperty("relativePath").GetString() ==
            "catalog/applications/trail-survival/procedures/application/procedure.trail-survival.about.md");
        Assert.Equal(
            "catalog/applications/trail-survival/procedures/application/procedure.trail-survival.about.md",
            winner.GetProperty("relativePath").GetString());

        var activationToken = "c123456789abcdef0123456789abcdef";
        var activationPayload = JsonSerializer.Serialize(new
        {
            requestToken = activationToken,
            applicationId = "trail-survival",
            previewFingerprint = preview.Data.GetProperty("previewFingerprint").GetString(),
            expectedActiveFingerprint = (string?)null
        });
        var activationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload, dryRun = true,
            intent = "Validate the exact Trail Survival source activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Activate the exact Trail Survival source.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationReplay = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Replay the exact Trail Survival source activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        Assert.True(activationDryRun.Ok, activationDryRun.Ok ? "" : activationDryRun.Error.GetRawText());
        Assert.True(activationCommit.Ok, activationCommit.Ok ? "" : activationCommit.Error.GetRawText());
        Assert.True(activationReplay.Ok, activationReplay.Ok ? "" : activationReplay.Error.GetRawText());
        Assert.Equal(activationCommit.OperationId, activationReplay.OperationId);
        Assert.Equal(activationCommit.Data.GetRawText(), activationReplay.Data.GetRawText());

        using (var scope = _app.Services.CreateScope())
        {
            var materializer = scope.ServiceProvider
                .GetRequiredService<ActivatedApplicationCatalogMaterializer>();
            var record = Assert.Single(materializer.Build(
                ApplicationIdentifier.Parse("trail-survival")).Records, value =>
                value.QualifiedId == "trail-survival.procedure.trail-survival.about");
            Assert.Equal("trail-survival.procedure.trail-survival.about", record.QualifiedId);
        }

        var stateSpaceToken = "d123456789abcdef0123456789abcdef";
        var stateSpacePayload = JsonSerializer.Serialize(new
        {
            requestToken = stateSpaceToken,
            stateSpaceId = "trail-survival-onboarding",
            applicationId = "trail-survival",
            activeFingerprint = activationCommit.Data.GetProperty("activation")
                .GetProperty("activationFingerprint").GetString(),
            expectedFingerprint = (string?)null
        });
        var stateSpaceDryRun = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload, dryRun = true,
            intent = "Validate an empty Trail Survival state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceCommit = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Create an empty Trail Survival state space.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var stateSpaceReplay = await ToolAsync("commit", new
        {
            kind = "system.state-space.create", payload = stateSpacePayload,
            intent = "Replay the empty Trail Survival state-space creation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        Assert.True(stateSpaceDryRun.Ok, stateSpaceDryRun.Ok ? "" : stateSpaceDryRun.Error.GetRawText());
        Assert.True(stateSpaceCommit.Ok, stateSpaceCommit.Ok ? "" : stateSpaceCommit.Error.GetRawText());
        Assert.True(stateSpaceReplay.Ok, stateSpaceReplay.Ok ? "" : stateSpaceReplay.Error.GetRawText());
        Assert.Equal(stateSpaceCommit.OperationId, stateSpaceReplay.OperationId);
        Assert.Equal(stateSpaceCommit.Data.GetRawText(), stateSpaceReplay.Data.GetRawText());

        var application = await ToolAsync("query", new
        {
            kind = "system.applications", applicationId = "trail-survival"
        });
        var source = await ToolAsync("query", new
        {
            kind = "system.sources", applicationId = "trail-survival", id = "trail-survival-core"
        });
        Assert.True(application.Ok, application.Ok ? "" : application.Error.GetRawText());
        Assert.True(source.Ok, source.Ok ? "" : source.Error.GetRawText());
        Assert.DoesNotContain(RepositoryRoot(), source.Data.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using (var scope = _app.Services.CreateScope())
        {
            var stateSpaces = scope.ServiceProvider.GetRequiredService<IStateSpaceRegistry>();
            Assert.Single(stateSpaces.ListPage(
                ApplicationIdentifier.Parse("trail-survival"), null, 100).StateSpaces);
            var database = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_entity"));
            Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_component"));
            Assert.Equal(1, await ScalarAsync(database,
                "SELECT COUNT(*) FROM system_application_activation_revision WHERE ApplicationId = 'trail-survival'"));
            Assert.Equal(1, await ScalarAsync(database,
                "SELECT COUNT(*) FROM system_state_space_binding_revision WHERE StateSpaceId = 'trail-survival-onboarding'"));
        }
    }

    [Fact]
    public async Task Dnd2024_legacy_sources_register_preview_and_activate_without_claiming_system_or_fixture_files()
    {
        var applicationToken = "b123456789abcdef0123456789abcdef";
        var applicationPayload = JsonSerializer.Serialize(new
        {
            requestToken = applicationToken,
            applicationId = "dnd2024",
            displayName = "D&D 2024",
            description = "Legacy gameplay catalog adoption proof.",
            baseApplications = Array.Empty<string>(),
            expectedFingerprint = (string?)null
        });
        var applicationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.register", payload = applicationPayload, dryRun = true,
            intent = "Validate the dnd2024 application registration.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var applicationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.register", payload = applicationPayload,
            intent = "Register the dnd2024 application.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var componentDirectory = Path.Combine(RepositoryRoot(), "catalog", "components");
        var compatibleComponentSchemas = Directory.GetFiles(componentDirectory, "*.schema.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new
            {
                LegacyId = Path.GetFileName(path)[..^".schema.json".Length],
                SchemaJson = File.ReadAllText(path)
            })
            .Where(schema => new BoundedJsonSchemaValidator().Compile(schema.SchemaJson).IsAccepted)
            .ToArray();
        Assert.Equal(39, compatibleComponentSchemas.Length);
        var componentOperations = new List<(string Token, JsonElement Data, string OperationId, string Payload)>();
        foreach (var (schema, index) in compatibleComponentSchemas.Select((value, index) => (value, index)))
        {
            var token = $"c{index + 1:x31}";
            var payload = JsonSerializer.Serialize(new
            {
                requestToken = token,
                applicationId = "dnd2024",
                qualifiedTypeId = "dnd2024." + schema.LegacyId,
                schemaJson = schema.SchemaJson,
                expectedSchemaHash = (string?)null
            });
            var dryRun = await ToolAsync("commit", new
            {
                kind = "system.component-type.register", payload, dryRun = true,
                intent = $"Validate legacy component type {schema.LegacyId}.",
                proceduresUsed = new[] { "procedure.system.use" }
            });
            var commit = await ToolAsync("commit", new
            {
                kind = "system.component-type.register", payload,
                intent = $"Register legacy component type {schema.LegacyId}.",
                proceduresUsed = new[] { "procedure.system.use" }
            });
            Assert.True(dryRun.Ok, dryRun.Ok ? "" : dryRun.Error.GetRawText());
            Assert.True(commit.Ok, commit.Ok ? "" : commit.Error.GetRawText());
            Assert.Equal(token, commit.OperationId);
            Assert.Equal(1, commit.Data.GetProperty("componentType").GetProperty("version").GetInt32());
            componentOperations.Add((token, commit.Data, commit.OperationId, payload));
        }
        var representativeComponentReplay = await ToolAsync("commit", new
        {
            kind = "system.component-type.register", payload = componentOperations[0].Payload,
            intent = "Replay one immutable legacy component-type registration.",
            proceduresUsed = new[] { "procedure.system.use" }
        });

        var sourceSpecifications = new (string Id, string Specification)[]
        {
            ("components-game-core", "catalog/components/game.core.*"),
            ("component-stats", "catalog/components/stats*"),
            ("mechanics-game-core", "catalog/mechanics/game/core/**/*"),
            ("mechanics-check", "catalog/mechanics/check/*"),
            ("mechanics-change", "catalog/mechanics/change/*"),
            ("procedures-game-core", "catalog/procedures/game/core/**/*.md"),
            ("procedures-campaign", "catalog/procedures/campaign/*.md"),
            ("procedures-quest", "catalog/procedures/quest/*.md"),
            ("procedures-play", "catalog/procedures/play/*.md"),
            ("event-types-game-core", "catalog/event-types/game.core.*"),
            ("subscriptions-game-core", "catalog/subscriptions/subscription.game.core.*.json")
        };
        var sourceOperationIds = new List<string>();
        foreach (var (source, index) in sourceSpecifications.Select((value, index) => (value, index)))
        {
            var token = $"e{index + 1:x31}";
            var payload = JsonSerializer.Serialize(new
            {
                requestToken = token,
                applicationId = "dnd2024",
                sourceId = source.Id,
                allowedRootId = "repository",
                relativePathOrGlob = source.Specification,
                trust = "trusted",
                precedence = 0,
                logicalIdentity = $"legacy-{source.Id}",
                expectedFingerprint = (string?)null
            });
            var dryRun = await ToolAsync("commit", new
            {
                kind = "system.source.register", payload, dryRun = true,
                intent = $"Validate the {source.Id} dnd2024 legacy source.",
                proceduresUsed = new[] { "procedure.system.use" }
            });
            var commit = await ToolAsync("commit", new
            {
                kind = "system.source.register", payload,
                intent = $"Register the {source.Id} dnd2024 legacy source.",
                proceduresUsed = new[] { "procedure.system.use" }
            });
            Assert.True(dryRun.Ok, dryRun.Ok ? "" : dryRun.Error.GetRawText());
            Assert.True(commit.Ok, commit.Ok ? "" : commit.Error.GetRawText());
            Assert.Equal(token, commit.OperationId);
            sourceOperationIds.Add(token);
        }

        var sources = await ToolAsync("query", new
        {
            kind = "system.sources", applicationId = "dnd2024", limit = 100
        });
        var preview = await ToolAsync("query", new
        {
            kind = "system.application-preview", applicationId = "dnd2024", limit = 250
        });
        Assert.True(applicationDryRun.Ok, applicationDryRun.Ok ? "" : applicationDryRun.Error.GetRawText());
        Assert.True(applicationCommit.Ok, applicationCommit.Ok ? "" : applicationCommit.Error.GetRawText());
        Assert.True(representativeComponentReplay.Ok, representativeComponentReplay.Ok ? "" : representativeComponentReplay.Error.GetRawText());
        Assert.Equal(componentOperations[0].OperationId, representativeComponentReplay.OperationId);
        Assert.Equal(componentOperations[0].Data.GetRawText(), representativeComponentReplay.Data.GetRawText());
        Assert.True(sources.Ok, sources.Ok ? "" : sources.Error.GetRawText());
        Assert.True(preview.Ok, preview.Ok ? "" : preview.Error.GetRawText());
        Assert.True(preview.Data.GetProperty("isValid").GetBoolean());
        Assert.Equal(sourceSpecifications.Length,
            preview.Data.GetProperty("counts").GetProperty("sources").GetInt32());
        Assert.Equal(0, preview.Data.GetProperty("counts").GetProperty("problems").GetInt32());
        Assert.False(preview.Data.GetProperty("truncated").GetBoolean());
        Assert.Equal(sourceSpecifications.Length, sources.Data.GetProperty("sources").GetArrayLength());
        Assert.All(preview.Data.GetProperty("sources").EnumerateArray(), source =>
        {
            Assert.True(source.GetProperty("documentCount").GetInt32() > 0, source.GetRawText());
            Assert.Equal(0, source.GetProperty("problemCount").GetInt32());
        });

        var previewPaths = preview.Data.GetProperty("winners").EnumerateArray()
            .Select(winner => winner.GetProperty("relativePath").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(134, previewPaths.Length);
        Assert.All(previewPaths, path => Assert.True(IsRatifiedLegacyGameSource(path), path));
        Assert.Contains("catalog/components/game.core.world.clock.json", previewPaths);
        Assert.Contains("catalog/components/stats.json", previewPaths);
        Assert.Contains("catalog/components/stats.schema.json", previewPaths);
        Assert.Contains("catalog/mechanics/game/core/world/time/mechanic.game.core.world.clock.advance.js", previewPaths);
        Assert.Contains("catalog/procedures/game/core/world/procedure.game.core.world.time.md", previewPaths);
        Assert.Contains("catalog/event-types/game.core.world.clock.advanced.json", previewPaths);
        Assert.Contains("catalog/subscriptions/subscription.game.core.world.condition.sync-route-closure.json", previewPaths);
        Assert.DoesNotContain("catalog/procedures/system/procedure.system.use.md", previewPaths);
        Assert.DoesNotContain("catalog/event-types/world.component.added.json", previewPaths);
        Assert.DoesNotContain("catalog/manifest.json", previewPaths);
        Assert.DoesNotContain("catalog/world/entities/orban.json", previewPaths);
        Assert.DoesNotContain("catalog/world/relationships.json", previewPaths);

        var activationToken = "f123456789abcdef0123456789abcdef";
        var activationPayload = JsonSerializer.Serialize(new
        {
            requestToken = activationToken,
            applicationId = "dnd2024",
            previewFingerprint = preview.Data.GetProperty("previewFingerprint").GetString(),
            expectedActiveFingerprint = (string?)null
        });
        var activationDryRun = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload, dryRun = true,
            intent = "Validate the exact dnd2024 legacy source activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationCommit = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Activate the exact dnd2024 legacy source manifest.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var activationReplay = await ToolAsync("commit", new
        {
            kind = "system.application.activate", payload = activationPayload,
            intent = "Replay the exact dnd2024 legacy source activation.",
            proceduresUsed = new[] { "procedure.system.use" }
        });
        var application = await ToolAsync("query", new
        {
            kind = "system.applications", applicationId = "dnd2024"
        });
        using (var materializationScope = _app.Services.CreateScope())
        {
            var materializer = materializationScope.ServiceProvider
                .GetRequiredService<ActivatedApplicationCatalogMaterializer>();
            Assert.Equal(38, materializer.Build(ApplicationIdentifier.Parse("dnd2024")).Records.Count);
        }
        var catalogs = await ToolAsync("query", new
        {
            kind = "system.catalogs", applicationId = "dnd2024"
        });
        var catalogRoot = await ToolAsync("query", new
        {
            kind = "system.catalog.browse", applicationId = "dnd2024", collection = "dnd2024",
            branch = "", pageSize = 1
        });
        var timeCatalog = await ToolAsync("query", new
        {
            kind = "system.catalog.browse", applicationId = "dnd2024", collection = "dnd2024",
            branch = "mechanics/game/core/world/time", pageSize = 100
        });
        var catalogSearch = await ToolAsync("query", new
        {
            kind = "system.catalog.search", applicationId = "dnd2024", collection = "dnd2024",
            query = "advance the clock", pageSize = 25
        });
        var catalogRecord = await ToolAsync("query", new
        {
            kind = "system.catalog.record", applicationId = "dnd2024", collection = "dnd2024",
            id = "dnd2024.mechanic.game.core.world.clock.advance"
        });
        var hiddenSystemSearch = await ToolAsync("query", new
        {
            kind = "system.catalog.search", applicationId = "dnd2024", collection = "dnd2024",
            query = "procedure.system.use", pageSize = 25
        });

        Assert.True(activationDryRun.Ok, activationDryRun.Ok ? "" : activationDryRun.Error.GetRawText());
        Assert.True(activationCommit.Ok, activationCommit.Ok ? "" : activationCommit.Error.GetRawText());
        Assert.True(activationReplay.Ok, activationReplay.Ok ? "" : activationReplay.Error.GetRawText());
        Assert.True(application.Ok, application.Ok ? "" : application.Error.GetRawText());
        Assert.True(catalogs.Ok, catalogs.Ok ? "" : catalogs.Error.GetRawText());
        Assert.True(catalogRoot.Ok, catalogRoot.Ok ? "" : catalogRoot.Error.GetRawText());
        Assert.True(timeCatalog.Ok, timeCatalog.Ok ? "" : timeCatalog.Error.GetRawText());
        Assert.True(catalogSearch.Ok, catalogSearch.Ok ? "" : catalogSearch.Error.GetRawText());
        Assert.True(catalogRecord.Ok, catalogRecord.Ok ? "" : catalogRecord.Error.GetRawText());
        Assert.True(hiddenSystemSearch.Ok, hiddenSystemSearch.Ok ? "" : hiddenSystemSearch.Error.GetRawText());
        Assert.Equal(activationCommit.Data.GetRawText(), activationReplay.Data.GetRawText());
        Assert.Equal(134, activationCommit.Data.GetProperty("activation").GetProperty("winnerCount").GetInt32());
        Assert.Equal(activationCommit.Data.GetProperty("activation").GetProperty("activationFingerprint").GetString(),
            application.Data.GetProperty("application").GetProperty("active").GetProperty("activationFingerprint").GetString());
        var collection = Assert.Single(catalogs.Data.GetProperty("collections").EnumerateArray());
        Assert.Equal("dnd2024", collection.GetProperty("id").GetString());
        Assert.Equal(38, collection.GetProperty("recordCount").GetInt32());
        Assert.NotNull(catalogRoot.Data.GetProperty("result").GetProperty("nextCursor").GetString());
        Assert.Equal("mechanics", catalogRoot.Data.GetProperty("result").GetProperty("entries")[0]
            .GetProperty("node").GetProperty("path").GetString());
        Assert.Contains(timeCatalog.Data.GetProperty("result").GetProperty("entries").EnumerateArray(), entry =>
            entry.GetProperty("record").GetProperty("qualifiedId").GetString()
                == "dnd2024.mechanic.game.core.world.clock.advance");
        Assert.Equal("dnd2024.mechanic.game.core.world.clock.advance",
            catalogSearch.Data.GetProperty("result").GetProperty("records")[0]
                .GetProperty("record").GetProperty("qualifiedId").GetString());
        Assert.Empty(hiddenSystemSearch.Data.GetProperty("result").GetProperty("records").EnumerateArray());
        Assert.Equal("dnd2024.mechanic.game.core.world.clock.advance",
            catalogRecord.Data.GetProperty("record").GetProperty("summary").GetProperty("qualifiedId").GetString());
        using (var recordContent = JsonDocument.Parse(
                   catalogRecord.Data.GetProperty("record").GetProperty("contentJson").GetString()!))
        {
            Assert.Equal("mechanic.game.core.world.clock.advance",
                recordContent.RootElement.GetProperty("id").GetString());
            Assert.Equal("advance the clock",
                recordContent.RootElement.GetProperty("matches").GetString()!.Split('\n')[1].Trim());
        }

        using var scope = _app.Services.CreateScope();
        var active = scope.ServiceProvider.GetRequiredService<IApplicationActivationReader>()
            .Current(ApplicationIdentifier.Parse("dnd2024"));
        Assert.NotNull(active);
        Assert.Equal(previewPaths, active.Winners.Select(winner => winner.RelativePath).Order(StringComparer.Ordinal));
        var publicCatalogs = scope.ServiceProvider.GetRequiredService<IPublicApplicationCatalogProvider>();
        Assert.True(publicCatalogs.TryGet(ApplicationIdentifier.Parse("dnd2024"), out var navigator));
        Assert.Equal(38, Assert.Single(navigator.ListCollections(ApplicationIdentifier.Parse("dnd2024"))).RecordCount);
        var rootNode = navigator.Browse(new(ApplicationIdentifier.Parse("dnd2024"), "dnd2024"));
        Assert.Equal(CatalogDescriptionStatus.Authored, rootNode.Node.DescriptionStatus);
        Assert.All(rootNode.Entries.Where(entry => entry.Node is not null),
            entry => Assert.Equal(CatalogDescriptionStatus.Missing, entry.Node!.DescriptionStatus));
        var database = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == applicationToken));
        Assert.Equal(1, await database.Operations.AsNoTracking().CountAsync(operation => operation.Id == activationToken));
        Assert.All(componentOperations, operation =>
            Assert.Equal(1, database.Operations.AsNoTracking().Count(value => value.Id == operation.Token)));
        var registeredTypes = scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>()
            .ListLatestPage(ApplicationIdentifier.Parse("dnd2024"), null, 100).ComponentTypes;
        Assert.Equal(39, registeredTypes.Count);
        Assert.Equal(compatibleComponentSchemas.Select(schema => "dnd2024." + schema.LegacyId).Order(StringComparer.Ordinal),
            registeredTypes.Select(type => type.QualifiedId).Order(StringComparer.Ordinal));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>()
            .GetLatest("dnd2024.game.core.campaign.session-checkpoint"));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>()
            .GetLatest("dnd2024.game.core.campaign.session-recap"));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>()
            .GetLatest("dnd2024.stats"));
        Assert.All(sourceOperationIds, token =>
            Assert.Equal(1, database.Operations.AsNoTracking().Count(operation => operation.Id == token)));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<IStateSpaceRegistry>()
            .ListPage(ApplicationIdentifier.Parse("dnd2024"), null, 100).StateSpaces);
        var projectionGraph = scope.ServiceProvider.GetRequiredService<IProjectionDefinitionRegistry>()
            .GetImpactGraph(ApplicationIdentifier.Parse("dnd2024"));
        Assert.Empty(projectionGraph.Forward);
        Assert.Empty(projectionGraph.Reverse);
        Assert.Equal(0, await ScalarAsync(database,
            "SELECT COUNT(*) FROM system_projection_definition WHERE ApplicationId = 'dnd2024'"));
        foreach (var table in new[]
                 {
                     "system_projection_definition_version",
                     "system_projection_component_input",
                     "system_projection_dependency_input",
                     "system_projection_mapping"
                 })
        {
            Assert.Equal(0, await ScalarAsync(database,
                $"SELECT COUNT(*) FROM {table} WHERE QualifiedId LIKE 'dnd2024.%'"));
        }
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_entity"));
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM system_ecs_component"));
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM entity"));
        Assert.Equal(0, await ScalarAsync(database, "SELECT COUNT(*) FROM component"));
    }

    private static bool IsRatifiedLegacyGameSource(string path) =>
        path.StartsWith("catalog/components/game.core.", StringComparison.Ordinal)
        || path is "catalog/components/stats.json" or "catalog/components/stats.schema.json"
        || path.StartsWith("catalog/mechanics/game/core/", StringComparison.Ordinal)
        || path.StartsWith("catalog/mechanics/check/", StringComparison.Ordinal)
        || path.StartsWith("catalog/mechanics/change/", StringComparison.Ordinal)
        || path.StartsWith("catalog/procedures/game/core/", StringComparison.Ordinal)
        || path.StartsWith("catalog/procedures/campaign/", StringComparison.Ordinal)
        || path.StartsWith("catalog/procedures/quest/", StringComparison.Ordinal)
        || path.StartsWith("catalog/procedures/play/", StringComparison.Ordinal)
        || path.StartsWith("catalog/event-types/game.core.", StringComparison.Ordinal)
        || path.StartsWith("catalog/subscriptions/subscription.game.core.", StringComparison.Ordinal);

    private sealed class CombinedPublicApplicationCatalogProvider(
        IPublicApplicationCatalogProvider fixtures,
        IPublicApplicationCatalogProvider activated) : IPublicApplicationCatalogProvider
    {
        public bool TryGet(ApplicationIdentifier applicationId, out ICatalogNavigator navigator) =>
            fixtures.TryGet(applicationId, out navigator!) || activated.TryGet(applicationId, out navigator!);
    }

    private async Task AssertFailureAsync(string code, object arguments)
    {
        var result = await ToolAsync("query", arguments);
        Assert.False(result.Ok);
        Assert.Equal(code, result.Error.GetProperty("code").GetString());
        Assert.StartsWith("query(kind:", result.Error.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    private static async Task<long> ScalarAsync(DantesRoleplayDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("The repository root could not be located.");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { if (Directory.Exists(_sourceRoot)) Directory.Delete(_sourceRoot, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task<(bool Ok, JsonElement Data, JsonElement Error, IReadOnlyList<string> NextSteps, string OperationId)> ToolAsync(string name, object arguments, bool remoteCandidate = false)
    {
        var result = await CallAsync("tools/call", new { name, arguments }, remoteCandidate);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.True(text.TrimStart().StartsWith('{'), text);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return (root.GetProperty("ok").GetBoolean(),
            root.TryGetProperty("data", out var data) ? data.Clone() : default,
            root.TryGetProperty("error", out var error) ? error.Clone() : default,
            root.TryGetProperty("nextSteps", out var steps) ? steps.EnumerateArray().Select(value => value.GetString() ?? "").ToArray() : [],
            root.TryGetProperty("operationId", out var operationId) ? operationId.GetString() ?? "" : "");
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, bool remoteCandidate = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerConfiguration.McpEndpoint)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = _nextId++, method, @params = parameters })
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept", "text/event-stream");
        if (remoteCandidate)
        {
            request.Headers.Host = "roleplay.example.ts.net";
            request.Headers.Add("Tailscale-User-Login", "operator@example.com");
        }
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        if (body.StartsWith("event:", StringComparison.Ordinal) || body.StartsWith("data:", StringComparison.Ordinal))
            body = string.Concat(body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("result").Clone();
    }
}
