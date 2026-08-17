using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;

var builder = WebApplication.CreateBuilder(args);

// The kernel. One call registers the DbContext and every store.
//
// SQLite by default: one file you can copy to snapshot a campaign and delete to reset.
// ARCHITECTURE.md §8.3 explains why there is no Postgres and no vector store yet, and names the
// conditions that would change that.
builder.Services.AddDantesRoleplayDataAccess(
    builder.Configuration.GetConnectionString("Kernel")
        ?? Path.Combine(builder.Environment.ContentRootPath, "data", "dantesroleplay.db"),
    DatabaseProvider.Sqlite);

// The sandbox that runs game rules. A singleton because it holds no state between runs: every
// call builds a fresh Jint engine, which is what stops one mechanic seeing what another left.
//
// Registered here rather than behind an AddDantesRoleplayRules() helper, so that the one component
// in this system that executes code an LLM wrote appears by name in the host's startup.
builder.Services.AddSingleton<IMechanicEngine, JintMechanicEngine>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless: no server-to-client requests (sampling, elicitation) are needed.
        options.Stateless = true;
    })
    .WithTools<OrientTool>()
    .WithTools<ProcedureTools>()
    .WithTools<WorldTools>()
    .WithTools<HistoryTool>();

var app = builder.Build();

// Migrate, then seed the bootstrap contracts from the embedded markdown files. Seeding is
// idempotent by content hash, so a restart with no edits writes nothing.
await app.Services.InitialiseDantesRoleplayAsync();

// Explicit path. MapMcp() with no argument serves at the root, which makes the endpoint
// ambiguous with any future page route and gives clients a URL with no visible protocol in it.
app.MapMcp("/mcp");

// Deliberately no HTTPS redirection. The MCP endpoint is reached over loopback by a local
// client, and a redirect there is a confusing failure rather than a security gain.
app.Run();
