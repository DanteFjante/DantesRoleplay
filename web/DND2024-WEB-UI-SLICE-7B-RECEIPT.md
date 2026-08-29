# D&D 2024 web UI Slice 7B receipt — player-first character viewport

Status: **accepted 2026-08-27**
Implementation: [Slice 7B](DND2024-WEB-UI-SLICE-7B-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 7B / C5
Ruleset alignment: **dnd2024-compatible**
Model assignment: `gpt-5.6-terra`, high reasoning; Sol high review boundary retained by the plan

## Delivered boundary

- Defined the already-confirmed `<dnd2024-character-sheet>` as a light-DOM composition boundary.
  It owns no state, request, rule calculation, action authority, effect, or transaction.
- Replaced the undifferentiated panel dashboard with three large game-styled views. Character is
  selected by default; Campaign contains the registered campaign dossier; Combat contains the
  existing Initiative and turn-resource controls.
- Kept vitals, ability scores, character dossier, Conditions, movement, proficiencies, mitigation,
  inventory and current action controls together in the Character view. Existing panel instances
  are moved rather than copied, so hydration, receipts, and server revalidation stay unchanged.
- Added native tab/list/panel relationships, roving focus, and Left/Right/Home/End navigation. The
  chosen view is disposable in-memory presentation state and is never stored in game state,
  `localStorage`, or `sessionStorage`.
- Kept Scene, Knowledge, Map, imagery, encounter authoring, attack/damage, and tactical controls
  absent instead of presenting empty or unauthorized promises.
- Reordered the remaining plan around the user's player-information priority and assigned model,
  reasoning effort, and EP ranges to Orders 7C–10.

## Evidence

- Browser module syntax check passed.
- Focused `WebInterfaceTests` passed: **89 passed, 0 failed**. Added assertions cover the character
  sheet element, default view, tab/list/panel semantics, keyboard handlers, exact panel placement,
  and absence of future Scene/Knowledge/Map elements while retaining all existing authority and
  isolation assertions.
- `dotnet build DantesRoleplay.slnx --no-restore` passed with **0 warnings and 0 errors** on the
  final sequential run. An earlier parallel build/test invocation briefly contended for the same
  test assembly; the independent rerun proves the source/build result.
- Focused `git diff --check` passed; only checkout line-ending notices were reported.
- The running private page at `/ui/dnd2024-play` loaded the current Brackenford campaign and Orban
  with Character selected. Clicking Campaign hid Character and revealed the campaign panel;
  pressing Right Arrow selected Combat and revealed only its panel. The page was returned to
  Character before handoff.
- A 520-pixel viewport check kept all three tabs visible and showed equal shell client/scroll width
  (no horizontal overflow). The browser console contained no errors.

## Deliberate exclusions and next gate

No catalog record, component schema, mechanic, procedure, C# route, database record, migration,
page revision, application association, live campaign state, or action contract changed.

The user's instruction to continue accepts Slice 7B and confirms Order 7C's one generic exact
parent-containment read projection. Later knowledge, map, and image slices retain their independent
audience, visibility, and state-owner gates.
