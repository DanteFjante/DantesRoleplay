# D&D 2024 Current View Slice 2 completion receipt

Status: **implementation complete; feature acceptance pending 2026-08-30**

## Delivered boundary

- Added the authored `game.core.campaign.current-scene` catalog component and
  `procedure.campaign.current-scene`. Registration inside the D&D application produces the existing
  runtime namespace `dnd2024.game.core.campaign.current-scene`; no doubled namespace was introduced.
- Added a closed campaign selector with exact location and optional conversation/encounter
  references. The server resolves Combat over Conversation over Exploration and never scans for a
  likely focus.
- Preserved exact actor-containment Exploration compatibility for older campaigns. A Game Master
  without the new campaign selector receives an explicit unavailable state.
- Added bounded Conversation projection from accepted interaction/participant state. Player output
  omits the unclassified interaction summary and any participant outside the authorized actor/party
  set.
- Added bounded Combat projection from accepted encounter definition, participation, locked
  Initiative, active round, active turn, and optional turn-budget state.
- Replaced the Exploration-only Current View branching with adaptive Exploration, Conversation, and
  Combat presentation while retaining the existing exact-location unavailable state.

## Verification evidence

- `.\roleplay.cmd validate catalog` — passed: 154 records, catalog valid; existing similarity
  warnings remain advisory and no live data was touched.
- D&D web `npm test` — passed: 125/125.
- D&D web `npm run build:server` — passed: 1,622 modules transformed.
- Focused .NET catalog/component/protocol tests — passed: 12/12. The running local MCP server caused
  temporary locked-copy retries, after which the tests completed successfully.
- `git diff --check` on the changed boundary — no whitespace errors; only existing Windows line-ending
  notices.
- `npx tsc --noEmit` still reports the existing six project configuration/concurrent typing errors in
  `MapFeatureList.tsx`, `server-host/main.tsx`, and `.ts` import-extension configuration. The production
  Vite build passes and this slice introduced no additional TypeScript diagnostic.
- A source-mode visual launch was attempted through the in-app browser. The existing Vite development
  URL resolves `../src/server-host/main.tsx` to `/ui/src/server-host/main.tsx`, so it remained on the
  loading shell. Production bundle compilation is verified; visual acceptance remains pending.

## Deliberate exclusions

- No live SQLite component was created or changed.
- No catalog import, page upload, activation, deployment, or public route occurred.
- No scene-authoring UI or website game-state write was added.
- Player-known route identities, declared scene actions, travel execution, dialogue generation,
  tactical maps, and combat actions remain separate work.
- Final feature acceptance and publication still require separate confirmation.
