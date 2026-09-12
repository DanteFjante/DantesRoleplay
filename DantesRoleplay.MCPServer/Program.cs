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
using DantesRoleplay.Knowledge;
using DantesRoleplay.Play;
using System.Diagnostics;

var startup = Stopwatch.StartNew();
var builder = WebApplication.CreateBuilder(args);
// Optional machine-local defaults stay outside version control. Deployment overrides retain priority.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables().AddCommandLine(args);

var developmentInformationScope = builder.Configuration["Information:DevelopmentScope"]
    ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_INFORMATION_SCOPE")
    ?? "local.*";
var runtimeStorage = RuntimeStoragePaths.Resolve(
    builder.Environment.ContentRootPath,
    builder.Configuration.GetConnectionString("Kernel"),
    builder.Configuration["BlobStorage:Root"],
    builder.Configuration["Retrieval:DerivedDataDirectory"]);
var databasePath = runtimeStorage.DatabasePath;
var blobStorageRoot = runtimeStorage.BlobStorageRoot;
var allowedSourceRoots = RuntimeStoragePaths.ResolveSourceRoots(
    builder.Environment.ContentRootPath,
    builder.Configuration.GetSection("Sources:AllowedRoots")
        .GetChildren()
        .Select(child => new KeyValuePair<string, string?>(child.Key, child.Value)));
var publishedApplicationCatalogs = builder.Configuration.GetSection("Catalogs:PublishedApplications")
    .GetChildren().Select(child => child.Value ?? string.Empty).ToArray();
var conversationCaptureValues = new[]
{
    builder.Configuration["ConversationMemoryCapture:ApplicationId"],
    builder.Configuration["ConversationMemoryCapture:StateSpaceId"],
    builder.Configuration["ConversationMemoryCapture:SessionContextId"],
    builder.Configuration["ConversationMemoryCapture:ProjectId"],
    builder.Configuration["ConversationMemoryCapture:RepositoryRoot"],
    builder.Configuration["ConversationMemoryCapture:ThreadId"]
};
if (conversationCaptureValues.Any(value => !string.IsNullOrWhiteSpace(value))
    && conversationCaptureValues.Any(string.IsNullOrWhiteSpace))
    throw new InvalidOperationException(
        "ConversationMemoryCapture requires ApplicationId, StateSpaceId, SessionContextId, ProjectId, RepositoryRoot, and ThreadId together.");

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
    builder.Services.AddInteractionRetrievalDerivedIndex(runtimeStorage.DerivedDataRoot);
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

// Register the shared kernel and MCP surface exercised by the end-to-end protocol tests. Host-only
// model providers, the Codex bridge, and web adapters are composed separately around this boundary.
builder.Services.AddDantesRoleplayMcpServer(
    databasePath,
    DatabaseProvider.Sqlite,
    developmentInformationScope,
    allowedSourceRoots,
    publishedApplicationCatalogs,
    builder.Configuration,
    blobStorageRoot);
if (conversationCaptureValues.All(value => !string.IsNullOrWhiteSpace(value)))
{
    var configuredApplication = conversationCaptureValues[0]!;
    var configuredState = conversationCaptureValues[1]!;
    var configuredSession = conversationCaptureValues[2]!;
    var configuredProject = conversationCaptureValues[3]!;
    var configuredRoot = ResolveRepositoryRoot(conversationCaptureValues[4], builder.Environment.ContentRootPath);
    var configuredThread = conversationCaptureValues[5]!;
    builder.Services.AddScoped<IConversationMemoryHostBinding>(services =>
    {
        var seat = services.GetRequiredService<ILocalKnowledgeSeatProvider>().Current();
        if (!seat.Enabled || string.IsNullOrWhiteSpace(seat.PrincipalId)
            || seat.ApplicationId != configuredApplication)
            throw new InvalidOperationException(
                "ConversationMemoryCapture does not match the active private application seat.");
        return new ConversationMemoryHostBinding(new(
            new(seat.PrincipalId, configuredApplication, configuredState, configuredSession),
            "codex", configuredProject, configuredRoot, configuredThread));
    });
}
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

app.Logger.LogInformation(
    "Runtime storage resolved to database {DatabasePath}, blobs {BlobStorageRoot}, and derived data {DerivedDataRoot}.",
    runtimeStorage.DatabasePath,
    runtimeStorage.BlobStorageRoot,
    runtimeStorage.DerivedDataRoot);

// Migrate, then seed the bootstrap contracts from the embedded markdown files. Seeding is
// idempotent by content hash, so a restart with no edits writes nothing.
await InitialiseStepAsync("Kernel database and bootstrap contracts", () =>
    app.Services.InitialiseDantesRoleplayAsync());
var settingsStarted = Stopwatch.GetTimestamp();
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
app.Logger.LogInformation("Startup: host settings ready in {ElapsedMs:F0} ms.",
    Stopwatch.GetElapsedTime(settingsStarted).TotalMilliseconds);
await InitialiseStepAsync("Website database", () => app.Services.InitialiseDantesRoleplayWebAsync());
await using (var assistantScope = app.Services.CreateAsyncScope())
{
    await InitialiseStepAsync("Interrupted conversation recovery", async () =>
        await assistantScope.ServiceProvider.GetRequiredService<IAssistantConversationService>()
            .RecoverInterruptedAsync());
}

app.UseDantesRoleplayRemoteWebBoundary();
app.UseRateLimiter();
app.MapMcp(ServerConfiguration.McpEndpoint);
app.MapDantesRoleplayHostWebAdapters();
app.MapDantesRoleplayWeb();

// Deliberately no HTTPS redirection. The MCP endpoint is reached over loopback by a local
// client, and a redirect there is a confusing failure rather than a security gain.
app.Lifetime.ApplicationStarted.Register(() =>
    ServerStartupDiagnostics.LogReady(app.Logger, app.Urls, startup.Elapsed));
app.Run();

async Task InitialiseStepAsync(string step, Func<Task> initialise)
{
    app.Logger.LogInformation("Startup: preparing {Step}...", step);
    var started = Stopwatch.GetTimestamp();
    await initialise();
    app.Logger.LogInformation("Startup: {Step} ready in {ElapsedMs:F0} ms.",
        step, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}

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
