# D&D 2024 MVP character-creation context receipt

Accepted: 2026-08-29

## Delivered

- `system.audience-context` now distinguishes a verified bound actor from a host-reserved actor
  that does not yet exist.
- A missing actor returns `status: "character-creation-required"`, the active application/campaign
  binding, and only the closed `characterCreation.characterId` input. It has no actor role hint.
- An existing actor with inactive, malformed, or ambiguous campaign participation remains denied.
- The D&D chat procedure first reads audience context, uses the reserved ID for the existing basic
  character-creation action, then rereads a bound context before normal play.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~KnowledgeCoreTests|FullyQualifiedName~GuardTests"`
  — 34 passed.
- `dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore`
  — 0 warnings and 0 errors.
- `./roleplay validate catalog` — 144 valid records, 21 existing advisory warnings, no live data
  touched.
- `git diff --check` passed for the delivered tracked files.

## Exclusions

No new player identity model, endpoint, MCP verb, D&D rule/component, web companion work, or
alternate character-creation transaction was added.
