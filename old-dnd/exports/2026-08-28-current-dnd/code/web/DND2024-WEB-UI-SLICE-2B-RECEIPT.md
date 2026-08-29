# D&D 2024 web UI Slice 2B completion receipt

Status: **accepted 2026-08-27**
Implementation: [Slice 2B](DND2024-WEB-UI-SLICE-2B-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), partial Order 2/C3
Ruleset alignment: **dnd2024-compatible**; no D&D rule, schema, mechanic, procedure, content,
application registration, or live game state changed.

## Delivered boundary

- Added a ruleset-neutral paged direct-containment read to the state-space edge owner. It filters
  before materialization, orders by exact contained entity ID, validates the current container,
  limits pages to 1–100 rows, and rejects a cursor that does not belong to the same direct container.
- Added the private read-only application route
  `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/containments`. It requires exact
  application/state-space/container identity, uses the existing opaque scope-bound cursor contract,
  and retains private read security, caching, and rate limiting.
- Extended remote access only for that exact route shape. Extra path segments and non-read
  application shapes remain denied.
- Added a game-styled Inventory panel to `<dnd2024-workspace>`. It requests only the first 24 direct
  contents, hydrates accepted item-instance/quantity/equipment-state components, and renders item
  cards with entity identity, exact definition ID, stored count/state, slot, and custody revision.
- Direct non-item contents remain visible in a separate group. Empty, unavailable, corrupt, and
  truncated states are explicit, and the panel states that nested container contents are not yet
  expanded.
- Preserved the existing character, encounter, scope, abort, partial-panel failure, no-storage, and
  no-write boundaries.

## Acceptance evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  — passed.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- Focused `StateSpaceEdgeStoreTests` — 3 passed, including direct filtering, paging, cursor scope,
  unknown container, and maximum-limit rejection.
- Focused `WebInterfaceTests` — 88 passed, including exact route/rate-limit inventory,
  cross-application/no-change reads, private remote-path closure, and browser inventory/no-write
  assertions.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — 1,189 passed.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore` — 21 passed.
- `git diff --check` and trailing-whitespace checks over the slice files — passed.
- The disposable private preview returned 200 for the exact D&D page and reviewed browser module,
  then was handed to the existing app browser tab. No DOM inspection, clicking, screenshot, live
  database, or live page activation was used.

## Deliberate exclusions and stop

The activated public catalog currently projects action/procedure/query records, not immutable item
entity records. This slice therefore does not invent item descriptions, kind, mass, capacity,
weapon/armor facts, source text, or currency values. It also does not copy runtime definition
components into a second browser authority.

Nested containment, complete catalog browsing, burden/capacity totals, currency conversion,
quantity steppers, equip/unequip/use/move actions, action preparation/execution, page association,
and live activation remain excluded. No POST/PUT/DELETE route, typed effect, transaction, database
migration, catalog artifact, MCP operation, or D&D-specific C# rule logic was added.
