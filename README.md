# DantesRoleplay

DantesRoleplay is a data-authored roleplaying engine. The C# projects provide a generic, auditable runtime; rulesets and campaign behavior live in catalog data and sandboxed JavaScript.

For contributors and coding agents, start with [AGENTS.md](AGENTS.md) and [docs/current/README.md](docs/current/README.md). Those pages are the maintained entry point; older implementation plans are not part of the working documentation.

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
