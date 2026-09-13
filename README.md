# DantesRoleplay

DantesRoleplay is a data-authored roleplaying engine. The C# projects provide a generic, auditable runtime; rulesets and campaign behavior live in catalog data and sandboxed JavaScript.

For contributors and coding agents, start with [AGENTS.md](AGENTS.md) and [docs/current/README.md](docs/current/README.md). Those pages are the maintained entry point; older implementation plans are not part of the working documentation.

## Getting started

On Windows, install the .NET 10 SDK and Node.js 22.13 or newer with `npm`, and make each
command available on `PATH`. Then start the local server from the repository root:

```powershell
.\run-mcp-server.cmd
```

On a new checkout with no saved runtime profile or existing runtime data, the launcher validates
those prerequisites, prepares one frozen local host, catalog source, and browser bundle, then
installs the application, starter data, and website pages without AI-provider calls. It verifies
readiness and saves the resulting runtime selection. Package restore may use the network on the
first run. Later starts use that byte-verified selection unchanged. The first run refuses to
initialize over an existing database or data directory; recover or select that runtime explicitly
instead of importing it implicitly.

Open `http://127.0.0.1:6217/` for the local website or connect an MCP client to
`http://127.0.0.1:6217/mcp`. See [Operations](docs/current/OPERATIONS.md) for profile checks,
restarts, explicit recovery profiles, and public access.

## Updating an installation

Download or pull the new repository files into the existing checkout, keeping its
`DantesRoleplay.MCPServer/data` directory. Then run:

```powershell
.\update-mcp-server.cmd
# Equivalent:
.\run-mcp-server.ps1 -Update
```

The update builds the new release, briefly stops the selected server, and upgrades a copy of its
database and files. It preserves the existing game, messages, settings, and locally authored content;
it does not reinstall the starter world. Conflicting catalog edits or incompatible contracts stop the
update before selection. The previous installation remains available for recovery. Ordinary launches
continue to use the selected release; they do not rebuild or update it automatically.

## Repository map

- `DantesRoleplay/` — generic domain and ECS kernel
- `DantesRoleplay.DataAccess/` — persistence, retrieval, hosting, and the JavaScript sandbox
- `DantesRoleplay.MCPServer/` — MCP protocol surface
- `DantesRoleplay.Tools/` — catalog import, export, verification, and validation
- `DantesRoleplay.Web/` — web client
- `DantesRoleplay.LocalAI/` — local model integration
- `catalog/` — authoritative authored procedures, schemas, fixtures, applications, and mechanics
- `docs/current/` — current human/LLM guidance

## Essential checks

```powershell
dotnet build DantesRoleplay.slnx
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj
.\roleplay.cmd validate catalog
```
