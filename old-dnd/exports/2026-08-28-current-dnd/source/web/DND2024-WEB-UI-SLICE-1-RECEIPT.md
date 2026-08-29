# D&D 2024 web UI Slice 1 completion receipt

Status: **accepted 2026-08-27**
Implementation: [Slice 1](DND2024-WEB-UI-SLICE-1-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 1
Ruleset alignment: **dnd2024-compatible**; no D&D rule, schema, mechanic, procedure, or content
record changed.

## Delivered boundary

- Added five private, read-only registered-application routes for exact state-space, entity, and
  component discovery. State spaces are checked against the requested application before any ECS
  record is returned; cross-application reads fail with `STATE_SPACE_WRONG_APPLICATION`.
- Added a bounded generic browser-component asset handler. The reviewed
  `/components/dnd2024-workspace.js` asset is copied for build/publish without compiling D&D IDs or
  vocabulary into the ruleset-neutral C# host.
- Added `<dnd2024-workspace>` with exact campaign/entity selection, refresh, bounded pagination,
  component-detail hydration, local panel failure isolation, accessible live status, and composed
  progress/error events.
- Added a responsive game-table presentation with HP meter, Temporary HP, AC shield, Speed cards,
  six ability-score tiles, Conditions, Action/Bonus Action/Reaction/Interaction/movement tokens,
  proficiency chips, and mitigation summaries. It displays accepted stored values and does not
  calculate modifiers or outcomes in the browser.
- Added the authored `dnd2024-play` page with shared navigation and an application conversation that
  follows the exact selected state space.
- Extended the private remote-path policy only for the five exact application-state read shapes;
  conversation and action shapes remain excluded.

## Acceptance evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  — passed.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- Focused `WebInterfaceTests`, including exact asset delivery, route inventory/rate limits,
  cross-application/no-change reads, authored game page, and remote path closure — 89 passed.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — 1,138 passed.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore` — 21 passed.
- `git diff --check` over the slice files — passed.

The full-suite architecture guard initially rejected D&D literals in a compiled C# JavaScript
string. The implementation was corrected by moving the script to a copied browser asset behind a
bounded generic host; the guard then passed. No baseline exception was added.

Browser automation and live-page activation were not performed. They remain in Orders 8 and 9;
this slice's presentation boundary is covered by authored markup/script assertions and responsive,
accessible CSS contracts without changing live page state.

## Deliberate exclusions and stop

No action descriptor/adapter, plus/minus mutation, dice/check/save execution, attack/damage/healing,
inventory traversal or writes, encounter mutation, catalog browser, application-page association,
database migration, catalog validation, MCP protocol change, or live page revision was added.

The next ready work is Order 2 under a new active implementation document. It may deepen read-only
character, inventory, encounter, and activated-content views. Writable +/- controls remain blocked
on the generic application action seam so they can submit authoritative commands and show receipts
instead of mutating browser-local state.
