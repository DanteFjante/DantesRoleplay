# Control-center Slice 11 receipt — root entry route

Status: **accepted**  
Date: **2026-08-24**  
Scope: **ruleset-neutral web routing**

## Delivered boundary

- `GET /` now serves the active `control-center` page through the existing page store and the same
  web read security/rate-limit path as the direct page URL.
- The remote private-web allowlist includes `/`, while `/mcp` remains outside that surface.
- `/ui/control-center/index.html` and generic `/ui/{id}` page routes remain unchanged.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~WebInterfaceTests`
  — passed: 66/66.
- `dotnet test DantesRoleplay.slnx --no-restore` — passed: 667/667 shared tests and 20/20 local-AI
  tests.
- Focused route-map coverage proves one GET root route is owned by the web mapper and that it does
  not register `/mcp`; remote-boundary coverage proves root is allowed and MCP remains denied.

## Deliberate exclusions and operational note

No page bundle was uploaded or changed in SQLite. After the host restarts with this build, root
returns 404 until an active `control-center` revision exists; upload or publish that bundle using
the existing page workflow. No catalog, migration, MCP route, hosting, or remote deployment change
was made.
