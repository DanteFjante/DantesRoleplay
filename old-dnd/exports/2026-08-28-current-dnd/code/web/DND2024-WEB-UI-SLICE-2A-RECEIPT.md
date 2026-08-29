# D&D 2024 web UI Slice 2A completion receipt

Status: **accepted 2026-08-27**
Implementation: [Slice 2A](DND2024-WEB-UI-SLICE-2A-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), partial Order 2
Ruleset alignment: **dnd2024-compatible**; no D&D rule, schema, mechanic, procedure, content,
application registration, or live state changed.

## Delivered boundary

- Extended the existing `<dnd2024-workspace>` hydration vocabulary with accepted character profile,
  creature Size, experience, encounter Initiative order, and encounter turn-state components.
- Added a game-styled character dossier with exact Level, Size, experience total, entity revision,
  pronouns, appearance, and biography. Missing optional prose says “Not recorded”; invalid component
  state says “Unavailable.” No XP threshold, progress percentage, level eligibility, or content
  choice is calculated.
- Added ordered Initiative participant cards with stored counts, entity-name lookup with exact-ID
  fallback, lifecycle badge, round, and an accessible marker on the stored turn-index position.
  Browser code does not sort, reroll, resolve ties, or persist a duplicate active participant.
- Preserved the Slice 1 exact reads, bounded pagination, scoped selection, partial panel failure,
  responsive layout, private access, and no-write boundary.

## Acceptance evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  — passed.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- Focused `WebInterfaceTests`, including component vocabulary, dossier/Initiative presentation,
  no-sort/no-storage/no-write assertions, exact asset delivery, and prior route boundaries — 88
  passed.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — 1,163 passed.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore` — 21 passed.
- `git diff --check` and trailing-whitespace checks over the slice files — passed.
- A disposable local database received the authored page, and exact GETs for the page and reviewed
  module both returned 200 before the preview was handed to the app. No browser inspection, clicking,
  screenshot, real database, or live page activation was used.

## Deliberate exclusions and stop

Inventory remains excluded because `dnd2024.item-instance` is identity, not custody, while static
item facts belong to the explicitly published application catalog. A later slice must define bounded
application-scoped containment and public-catalog reads before rendering item cards or equipment
slots. This slice does not infer containment, scan entity names for ownership, or copy catalog facts
into browser state.

No new route/custom-element/page/module ID, C# ruleset literal, action descriptor, action endpoint,
plus/minus mutation, dice/check/save execution, attack/damage/healing, inventory write, encounter
mutation, database migration, MCP change, or live page revision was added.
