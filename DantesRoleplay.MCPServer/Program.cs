using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;

var builder = WebApplication.CreateBuilder(args);

// Everything this application registers lives in one method, which the end-to-end test also
// calls — so the surface the test walks is the surface this host serves, by construction.
builder.Services.AddDantesRoleplayMcpServer(
    builder.Configuration.GetConnectionString("Kernel")
        ?? Path.Combine(builder.Environment.ContentRootPath, "data", "dantesroleplay.db"),
    DatabaseProvider.Sqlite);

var app = builder.Build();

// Migrate, then seed the bootstrap contracts from the embedded markdown files. Seeding is
// idempotent by content hash, so a restart with no edits writes nothing.
await app.Services.InitialiseDantesRoleplayAsync();

app.MapMcp(ServerConfiguration.McpEndpoint);

// Deliberately no HTTPS redirection. The MCP endpoint is reached over loopback by a local
// client, and a redirect there is a confusing failure rather than a security gain.
app.Run();
