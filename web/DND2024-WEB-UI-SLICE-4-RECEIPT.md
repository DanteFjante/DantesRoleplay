# D&D 2024 web UI Slice 4 receipt — generic game-style action controls

Status: **accepted 2026-08-27**
Implementation: [Slice 4 implementation](DND2024-WEB-UI-SLICE-4-IMPLEMENTATION.md)
Plan: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 4 / D4
Ruleset alignment: **ruleset-neutral**

## Delivered boundary

Added the reviewed generic browser module `application-workspace.js` with the three previously
confirmed custom elements:

- `<application-entity-picker>` lists one exact application/state space's first 100 current
  entities in server order and makes role selection explicit. It never follows a cursor and marks a
  truncated page visibly.
- `<application-action-button>` loads one exact mechanic descriptor, accepts only explicitly
  supplied role bindings and JSON-object input properties, prepares a server-built proposal, then
  exposes a separate **Confirm and execute** control.
- `<application-form>` composes descriptor-declared role pickers with the same review/confirmation
  flow. It renders ordinary fields only from a future authored input schema. The current descriptor
  advertises `not-authored`, so the component has no raw JSON editor and submits only `{}`.

The controls use the accepted descriptor, prepare, and execute routes. They create disposable,
distinct request keys for prepare and execution; send no revisions, effects, authorization,
confirmation truth, seeds, receipt status, or mechanic source; and contain no D&D rule IDs,
calculations, or outcome branches. They are available as a reusable module but deliberately are
not placed on `dnd2024-play` until a later D&D action slice selects exact supported mechanics.

No C#, route, database, catalog, schema, mechanic, procedure, effect, migration, browser storage,
page activation, or application/game state changed.

## Evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/application-workspace.js`
  passed.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~WebInterfaceTests"` passed: **88/88**. It checks the three exact element
  identities, first-page/100-item picker bound, descriptor/prepare/execute path, explicit
  confirmation, absent-schema behavior, no cursor/control/MCP/conversation/storage/write leakage,
  and serves both reviewed component assets through the private component handler.
- `dotnet build DantesRoleplay.slnx --no-restore` passed: **0 warnings, 0 errors**.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-build` passed: **21/21**.
- `git diff --check` and explicit trailing-whitespace checks over Slice 4 files passed; the checkout
  reported only unrelated line-ending warnings.

The repository-wide core suite remains blocked by the independently in-progress D&D catalog
materialization failure recorded during Slice 2D: `Dnd2024AbilityCheckTests` reaches
`ActivatedApplicationCatalogMaterializer.MechanicRecord` while an active mechanic Markdown record
has no active JavaScript sidecar. That fails before the generic browser module is loaded. This slice
did not alter catalog artifacts; its focused web/build evidence is green. The catalog owner must
resolve that failure before a repository-wide all-green claim is possible.

## Deliberate exclusions and stop

No D&D dice/check/save action, ability or quantity +/- control, HP/inventory/equipment mutation,
encounter action, role visibility model, raw JSON entry surface, live activation, or browser-wide
accessibility pass was added. The next ready work is Order 5: bind only independently accepted D&D
stateless mechanics to purpose-built game controls over this generic layer.
