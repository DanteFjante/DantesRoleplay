# D&D 2024 web UI Slice 7D0 receipt — authorized knowledge core adoption

Status: **accepted**
Accepted: 2026-08-27
Implementation: [Slice 7D0 implementation](DND2024-WEB-UI-SLICE-7D0-IMPLEMENTATION.md)

## Delivered boundary

- Added the ruleset-neutral `src/system/knowledge/` component with provider-neutral ambient
  audience, exact application-binding, actor-participation, closed request/result, canonical
  projection, effective-state, lexical retrieval, candidate, and answer contracts.
- Added a read-only canonical projector over current application-scoped ECS/state-space edge owners.
  It requires an exact application/state-space/campaign binding, active campaign and world, one
  campaign-world edge, a valid world clock, one knowledge primary/classification/world/about shape,
  a live subject, and a valid optional interval.
- Added effective actor-state resolution with explicit-state precedence, deterministic applicable
  faction/containing-region baseline, world baseline, explicit-unknown override, familiar
  recognition, and derived unknown. Malformed scopes and graph records fail closed.
- Added deterministic lexical retrieval whose host allowlist and structural/time filters execute
  before text scoring and limiting. It creates no persistent or authoritative index.
- Added the policy-first candidate resolver. Actor participation is checked after campaign/world
  scope but before knowledge projection. Hydrated results are reauthorized before use.
- Added the bounded no-tools structured answer coordinator. Internal citations are validated then
  removed; mixed perspectives, invented IDs, ID echoes, malformed output, provider failure, and
  repeated input changes return bounded unknown/stale results.
- Added an opt-in `AddAuthorizedKnowledgeCore` composition seam. The generic server does not call
  it, and no permissive policy or application binding exists.

The implementation contains no D&D/catalog IDs. All component, relationship, JSON-field, temporal,
state, scope, and presentation vocabulary comes from a validated host/application binding.

## Acceptance evidence

- Focused command:
  `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --artifacts-path .codex-build/knowledge --no-restore --filter FullyQualifiedName~KnowledgeCore`
  — **11 passed, 0 failed**.
- Full command:
  `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --artifacts-path .codex-build/knowledge --no-restore`
  — **1,396 passed, 0 failed, 0 skipped**.
- Focused tests prove request closure, policy-first denial, wrong-campaign denial before data
  access, application-ECS projection, explicit/faction/region/world/unknown precedence, malformed
  scope rejection, pre-limit allowlisting, familiar-only recognition, ID-free safe answers, mixed
  perspective rejection, one-retry freshness, and explicit-owner dependency registration.
- A normal-output build first reached the server copy step and was blocked only because the running
  web application held its output assemblies. The isolated artifacts path compiled and tested the
  same worktree successfully without interrupting the app.
- `git diff --check` reported no whitespace errors; only the repository's existing LF-to-CRLF
  warning for the edited dependency plan.
- A source audit found no `dnd2024`, `game.core`, `old-dnd`, endpoint, query, tool, or server
  registration reference under `src/system/knowledge/`. The only caller of
  `AddAuthorizedKnowledgeCore` is the focused explicit-owner registration test.

Catalog validation was not required because no catalog artifact changed. A protocol walk was not
required because no MCP surface or current host dependency registration changed.

## Deliberately excluded

No fixed Orban seat, authentication implementation, D&D application vocabulary binding, live
campaign read/write, actor/baseline state import, MCP/HTTP/query dispatch, player notebook UI,
visibility-based grant, exact-record player lookup, vector retrieval, migration, catalog ID,
schema change, event, notification, operation, information record, or persistent index was added.

## Exit

Slice 7D0 stops here. Slice 7D1 may now bind one explicit loopback-only actor seat and the exact
D&D application vocabulary, while independently proving campaign participation, revocation,
cross-campaign denial, and that browser-selected actors never grant control.
