# D&D 2024 web UI Slice 2C completion receipt

Status: **accepted 2026-08-27**
Implementation: [Slice 2C](DND2024-WEB-UI-SLICE-2C-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), partial Order 2/B2/C3
Ruleset alignment: **dnd2024-compatible**; no D&D rule, schema, mechanic, procedure, authored
content, application registration, or live game state changed.

## Delivered boundary

- Extended the generic activated-application catalog projection with kind `entity` for exact text
  JSON winners beneath an authored `content/entities/` boundary. It reuses `EntityFile.Parse`,
  retains exact content/provenance, qualifies navigation identity by application, and keeps the
  authored entity ID as an exact alias.
- Bumped the materializer identity to version 2 so immutable snapshots and cursors cannot silently
  reuse the earlier action-only projection identity. Existing mechanic, procedure, and query
  records retain their behavior.
- Added the private read-only route
  `/api/applications/{applicationId}/catalog/records/{qualifiedId}` over the existing explicitly
  published application catalog. Remote access permits only that exact read shape.
- Enriched each direct inventory item card from the exact activated definition ID. Cards now show
  authored name, kind, stack policy, rational weight/capacity, equipment modes, denomination when
  present, and exact source ID/locator in compact game-style detail strips.
- Unknown, unpublished, drifted, corrupt, or mismatched definitions show “Definition unavailable”
  on only that item. Runtime identity, quantity, equipment state, custody, and the rest of the
  character viewport remain visible.
- Preserved the existing bounded read, partial-panel failure, no-storage, no-write, and
  application-isolation boundaries. No static definition is installed into campaign state.

## Acceptance evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  — passed.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- Focused `ActivatedApplicationCatalogTests` — 5 passed, including exact entity JSON, qualified
  identity/path/alias, exact search/inspect, publication closure, provenance, and source drift.
- Focused D&D 2024 compatibility suite — 175 passed while materializing the real activated D&D
  catalog and the current authored content set.
- Focused `WebInterfaceTests` — 88 passed, including exact route/rate-limit inventory, remote-path
  closure, browser catalog hydration/failure presentation, and no-write assertions.
- `roleplay validate catalog` — accepted 144 records (14 mechanics, 50 procedures, 33 components,
  10 event types, 2 subscriptions, and 35 entities); 21 advisory near-duplicate warnings; no live
  data touched.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — 1,204 passed.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore` — 21 passed.
- `git diff --check` passed; the reported line-ending notices include pre-existing/user-owned
  working-tree files and no whitespace error.
- The disposable private preview returned 200 for the exact D&D page and reviewed browser module,
  confirmed the catalog hydration asset, and was handed to the existing app browser. The preview
  server was then stopped. No DOM inspection, clicking, screenshot, live database, or live page
  activation was used.

## Deliberate exclusions and stop

Nested containment, generic catalog browsing, content editing/installation, burden or capacity
totals, currency conversion, price inference, profile expansion, quantity steppers,
equip/unequip/use/move actions, action preparation/execution, page association, live invalidation,
and live activation remain excluded. No POST/PUT/DELETE route, typed effect, transaction, database
migration, catalog artifact, MCP operation, or D&D-specific C# rule logic was added.

Order 2 remains partially accepted because nested container traversal is still absent. After that
bounded read-only remainder, Order 3 owns the generic action seam required before the requested
game-like dice controls and safe +/- character or inventory mutations can be implemented.
