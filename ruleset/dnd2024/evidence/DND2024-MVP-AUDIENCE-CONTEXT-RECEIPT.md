# D&D 2024 MVP audience-context receipt

Accepted: 2026-08-28

## Delivered

- Added the read-only MCP query kind `system.audience-context` under the existing `query` tool.
- Reused the host-selected local seat, activated application binding, and active campaign
  participation verifier. No player-seat record, game-state schema, endpoint, or new MCP tool was
  added.
- The result contains only the verified application, state space, campaign, actor, actor role hint,
  and freshness revisions. It accepts no caller identity or scope.
- Denial happens before binding or participation reads when the configured audience is unavailable.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~GuardTests|FullyQualifiedName~LocalKnowledgeAudienceTests"`
  — 19 passed.
- `dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore`
  — succeeded with 0 warnings and 0 errors.
- `./roleplay validate catalog` — 144 valid records; 21 pre-existing advisory warnings; no live
  data touched.
- `git diff --check` passed for the delivered files.

## Exclusions

This does not create durable chat transcripts, alter the web companion, expose hidden knowledge,
allow caller-selected identity, or bypass the existing query-plan/confirmed-commit execution flow.
