using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Web.Settings;
using DantesRoleplay.HostSettings;
using System.Text.Json;
using DantesRoleplay.Retrieval;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.AI;
using DantesRoleplay.AI.Ollama;

var builder = WebApplication.CreateBuilder(args);

var developmentInformationScope = builder.Configuration["Information:DevelopmentScope"]
    ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_INFORMATION_SCOPE")
    ?? "local.*";
var databasePath = builder.Configuration.GetConnectionString("Kernel")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "dantesroleplay.db");
var blobStorageRoot = builder.Configuration["BlobStorage:Root"]
    ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "blobs");
var allowedSourceRoots = builder.Configuration.GetSection("Sources:AllowedRoots")
    .GetChildren()
    .ToDictionary(child => child.Key, child => child.Value ?? string.Empty, StringComparer.Ordinal);
var publishedApplicationCatalogs = builder.Configuration.GetSection("Catalogs:PublishedApplications")
    .GetChildren().Select(child => child.Value ?? string.Empty).ToArray();

var hostSettings = new ConfiguredHostSettingDefinitionProvider(builder.Configuration);
var outerHostOptions = new InteractionOuterHostOptions(builder.Configuration);
builder.Services.AddSingleton<IHostSettingDefinitionProvider>(hostSettings);
builder.Services.AddSingleton(outerHostOptions.Selection);
builder.Services.AddHttpClient("local-assistant", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<OllamaAiProvider>(services =>
    new OllamaAiProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("local-assistant"),
        hostSettings.CreateCompletionOptions()));
builder.Services.AddSingleton<ILocalStructuredCompletionProvider>(services =>
    services.GetRequiredService<OllamaAiProvider>());
builder.Services.AddSingleton<IAiProvider>(services =>
    services.GetRequiredService<OllamaAiProvider>());

// Say at startup whether each published catalog materializes. A drifted file otherwise degrades
// the whole application silently until someone tries to run a mechanic.
builder.Services.AddHostedService(services => new CatalogHealthCheck(
    services.GetRequiredService<IServiceScopeFactory>(),
    publishedApplicationCatalogs,
    services.GetRequiredService<ILoggerFactory>().CreateLogger<CatalogHealthCheck>()));

// Intent retrieval over the active catalog. Without an embedding provider and a derived index,
// system.feature-search runs lexically and answers a phrased question ("what does a location need
// to be playable") with nothing, while the same record is found by a keyword ("furnish place").
// Both halves are opt-in and fail soft: an unreachable model or a missing index degrades to the
// lexical path rather than failing a search.
var embeddingOptions = new OllamaEmbeddingOptions
{
    Enabled = builder.Configuration.GetValue("Retrieval:Embedding:Enabled", false),
    Endpoint = new Uri(
        builder.Configuration["Retrieval:Embedding:Endpoint"] ?? "http://localhost:11434",
        UriKind.Absolute),
    Model = builder.Configuration["Retrieval:Embedding:Model"] ?? "qwen3-embedding:4b",
    ExpectedDimensions = builder.Configuration.GetValue("Retrieval:Embedding:ExpectedDimensions", 2560)
};
if (embeddingOptions.Enabled)
{
    builder.Services.AddSingleton<ITextEmbeddingProvider>(services =>
        new OllamaEmbeddingProvider(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("local-assistant"),
            embeddingOptions));
    builder.Services.AddInteractionRetrievalDerivedIndex(
        builder.Configuration["Retrieval:DerivedDataDirectory"]
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "derived"));
    builder.Services.AddHostedService(services => new InteractionRetrievalWarmup(
        services.GetRequiredService<IServiceScopeFactory>(),
        publishedApplicationCatalogs,
        services.GetRequiredService<ILoggerFactory>().CreateLogger<InteractionRetrievalWarmup>()));
}
var remotePlannerOptions = new OpenAiInteractionPlanningOptions
{
    Enabled = builder.Configuration.GetValue<bool>("InteractionPlanning:Remote:Enabled"),
    ApiKey = builder.Configuration["InteractionPlanning:Remote:ApiKey"]
        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? string.Empty
};
builder.Services.AddSingleton(remotePlannerOptions);
builder.Services.AddHttpClient<OpenAiResponsesInteractionPlanningProvider>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<OpenAiResponsesOuterInteractionProvider>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("local-interaction-outer", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<IInteractionOuterLocalCompletionProvider>(services =>
    new InteractionOuterLocalCompletionProvider(
        new OllamaAiProvider(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("local-interaction-outer"),
            outerHostOptions.LocalCompletion),
        outerHostOptions.LocalAdapter));
builder.Services.AddSingleton<LocalInteractionOuterProvider>(services => new(
    services.GetRequiredService<IInteractionOuterLocalCompletionProvider>()));
builder.Services.AddSingleton<IInteractionOuterProviderAdapter>(services =>
    services.GetRequiredService<LocalInteractionOuterProvider>());
builder.Services.AddSingleton<IInteractionOuterProviderAdapter>(services =>
    services.GetRequiredService<OpenAiResponsesOuterInteractionProvider>());
builder.Services.AddSingleton<SelectedInteractionOuterProvider>();
builder.Services.AddSingleton<IInteractionOuterTurnProvider>(services =>
    services.GetRequiredService<SelectedInteractionOuterProvider>());
builder.Services.AddSingleton<IInteractionNarrationProvider>(services =>
    services.GetRequiredService<SelectedInteractionOuterProvider>());
builder.Services.AddSingleton<IInteractionTaskAgendaProvider>(services =>
    services.GetRequiredService<SelectedInteractionOuterProvider>());

// Everything this application registers lives in one method, which the end-to-end test also
// calls — so the surface the test walks is the surface this host serves, by construction.
builder.Services.AddDantesRoleplayMcpServer(
    databasePath,
    DatabaseProvider.Sqlite,
    developmentInformationScope,
    allowedSourceRoots,
    publishedApplicationCatalogs,
    builder.Configuration,
    blobStorageRoot);
builder.Services.AddCodexBridgeComponent(new CodexBridgeOptions(
    builder.Configuration["Codex:ExecutablePath"] ?? "codex",
    ResolveRepositoryRoot(
        builder.Configuration["Codex:RepositoryRoot"],
        builder.Environment.ContentRootPath),
    builder.Configuration["Codex:PinnedVersion"] ?? CodexBridgeVersions.CurrentPinnedVersion,
    Model: builder.Configuration["Codex:Model"] ?? CodexBridgeModels.Luna));
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddDantesRoleplayWeb(databasePath, builder.Configuration);
builder.Services.AddScoped<ApplicationReadinessService>();

var app = builder.Build();

// Migrate, then seed the bootstrap contracts from the embedded markdown files. Seeding is
// idempotent by content hash, so a restart with no edits writes nothing.
await app.Services.InitialiseDantesRoleplayAsync();
await using (var settingsScope = app.Services.CreateAsyncScope())
{
    var overrides = settingsScope.ServiceProvider.GetRequiredService<IHostSettingOverrideStore>();
    var heads = await overrides.GetHeadsAsync();
    var values = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
    foreach (var head in heads.Values)
    {
        if (head.ValueJson is null)
        {
            values.Add(head.Key, null);
            continue;
        }
        using var document = JsonDocument.Parse(head.ValueJson);
        values.Add(head.Key, document.RootElement.Clone());
    }
    hostSettings.ApplyStartupOverrides(values);
    await overrides.MarkPendingAppliedAsync();
}
hostSettings.MarkProviderRegistered();
await app.Services.InitialiseDantesRoleplayWebAsync();
await using (var assistantScope = app.Services.CreateAsyncScope())
{
    await assistantScope.ServiceProvider.GetRequiredService<IAssistantConversationService>()
        .RecoverInterruptedAsync();
}

app.UseDantesRoleplayRemoteWebBoundary();
app.UseRateLimiter();
app.MapMcp(ServerConfiguration.McpEndpoint);
app.MapPut("/api/blob-uploads/{uploadId}", BlobTransferWebEndpoints.UploadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.UploadRateLimitPolicy);
app.MapGet("/api/blobs/sha256/{sha256}", BlobTransferWebEndpoints.DownloadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet("/api/audience-context", AudienceContextWebEndpoint.CurrentAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet("/api/readiness/applications/{applicationId}", ApplicationReadinessWebEndpoint.ReadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet(
        "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media",
        EntityMediaWebEndpoints.DiscoverAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet(
        "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media/{mediaId}/content",
        EntityMediaWebEndpoints.ReadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet(
        "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/read-models/{qualifiedQueryId}",
        ApplicationReadModelWebEndpoint.ReadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapGet(
        "/api/applications/{applicationId}/campaigns/{campaignId}/chronology",
        WorldChronologyWebEndpoint.ReadAsync)
    .AddEndpointFilter<WebInterfaceSecurityFilter>()
    .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
app.MapDantesRoleplayWeb();

// Deliberately no HTTPS redirection. The MCP endpoint is reached over loopback by a local
// client, and a redirect there is a confusing failure rather than a security gain.
app.Run();

static string ResolveRepositoryRoot(string? configured, string contentRoot)
{
    if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());
    foreach (var start in new[] { contentRoot, Environment.CurrentDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
    }
    return Path.GetFullPath(contentRoot);
}
