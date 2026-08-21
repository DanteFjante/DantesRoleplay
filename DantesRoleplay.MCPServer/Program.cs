using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.MCPServer;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var vectorPath = builder.Configuration["Knowledge:Vector:ExtensionPath"]
    ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_SQLITE_VEC_EXTENSION");
var knowledgeRetrieval = new KnowledgeRetrievalOptions
{
    Embedding = new OllamaEmbeddingOptions
    {
        Enabled = Enabled(builder.Configuration["Knowledge:Embedding:Enabled"]) ||
                  Enabled(Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_INTEGRATION")),
        Endpoint = new Uri(builder.Configuration["Knowledge:Embedding:Endpoint"] ?? "http://localhost:11434"),
        Model = builder.Configuration["Knowledge:Embedding:Model"] ?? "qwen3-embedding:4b",
        ExpectedDimensions = Number(builder.Configuration["Knowledge:Embedding:Dimensions"], 2560)
    },
    Vector = new SqliteVecOptions
    {
        Enabled = Enabled(builder.Configuration["Knowledge:Vector:Enabled"]) ||
                  !string.IsNullOrWhiteSpace(vectorPath),
        ExtensionPath = vectorPath
    },
    Completion = new OllamaCompletionOptions
    {
        Enabled = Enabled(builder.Configuration["Knowledge:Completion:Enabled"]) ||
                  Enabled(Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION")),
        Endpoint = new Uri(builder.Configuration["Knowledge:Completion:Endpoint"] ?? "http://localhost:11434"),
        Model = builder.Configuration["Knowledge:Completion:Model"] ?? "qwen3:8b",
        Profile = builder.Configuration["Knowledge:Completion:Profile"] ?? "standard",
        MaxPromptCharacters = Number(builder.Configuration["Knowledge:Completion:MaxPromptCharacters"], 30_000),
        MaxOutputTokens = Number(builder.Configuration["Knowledge:Completion:MaxOutputTokens"], 1_024),
        MaxConcurrentRequests = Number(builder.Configuration["Knowledge:Completion:MaxConcurrentRequests"], 1)
    },
    Background = new KnowledgeBackgroundOptions
    {
        EmbeddingQueueCapacity = Number(builder.Configuration["Knowledge:Background:EmbeddingQueueCapacity"], 16),
        ProposalQueueCapacity = Number(builder.Configuration["Knowledge:Background:ProposalQueueCapacity"], 32),
        MaxRetainedJobs = Number(builder.Configuration["Knowledge:Background:MaxRetainedJobs"], 256),
        MaxAttempts = Number(builder.Configuration["Knowledge:Background:MaxAttempts"], 2)
    },
    BackfillBatchSize = Number(builder.Configuration["Knowledge:BackfillBatchSize"], 16),
    CandidateLimit = Number(builder.Configuration["Knowledge:CandidateLimit"], 60)
};
var developmentKnowledgeAudience = new DevelopmentKnowledgeAudienceOptions
{
    Enabled = Enabled(builder.Configuration["Knowledge:DevelopmentAudience:Enabled"]) ||
              Enabled(Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_KNOWLEDGE_AUDIENCE")),
    PrincipalId = builder.Configuration["Knowledge:DevelopmentAudience:PrincipalId"]
        ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_PRINCIPAL")
        ?? "development.local",
    CampaignId = builder.Configuration["Knowledge:DevelopmentAudience:CampaignId"]
        ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_CAMPAIGN")
        ?? "",
    Role = builder.Configuration["Knowledge:DevelopmentAudience:Role"]
        ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_ROLE")
        ?? "gm",
    ActorId = builder.Configuration["Knowledge:DevelopmentAudience:ActorId"]
        ?? Environment.GetEnvironmentVariable("DANTESROLEPLAY_DEVELOPMENT_ACTOR")
};
if (developmentKnowledgeAudience.Enabled) EnsureLoopbackOnly(builder.Configuration);

// Everything this application registers lives in one method, which the end-to-end test also
// calls — so the surface the test walks is the surface this host serves, by construction.
builder.Services.AddDantesRoleplayMcpServer(
    builder.Configuration.GetConnectionString("Kernel")
        ?? Path.Combine(builder.Environment.ContentRootPath, "data", "dantesroleplay.db"),
    DatabaseProvider.Sqlite,
    knowledgeRetrieval,
    developmentKnowledgeAudience);

var app = builder.Build();

if (developmentKnowledgeAudience.Enabled)
{
    // The development seat is deliberately a local convenience, never a LAN shortcut.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments(ServerConfiguration.McpEndpoint) &&
            (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next();
    });
}

// Migrate, then seed the bootstrap contracts from the embedded markdown files. Seeding is
// idempotent by content hash, so a restart with no edits writes nothing.
await app.Services.InitialiseDantesRoleplayAsync();

app.MapMcp(ServerConfiguration.McpEndpoint);

// Deliberately no HTTPS redirection. The MCP endpoint is reached over loopback by a local
// client, and a redirect there is a confusing failure rather than a security gain.
app.Run();

static bool Enabled(string? value) =>
    string.Equals(value, "1", StringComparison.Ordinal) ||
    bool.TryParse(value, out var enabled) && enabled;

static int Number(string? value, int fallback) =>
    int.TryParse(value, out var number) ? number : fallback;

static void EnsureLoopbackOnly(IConfiguration configuration)
{
    var raw = configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (string.IsNullOrWhiteSpace(raw)) return; // ASP.NET's default is localhost.
    foreach (var value in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
             (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))))
            throw new InvalidOperationException("Development knowledge audience may bind only to localhost or a loopback IP address.");
    }
}
