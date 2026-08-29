# D&D code-adoption Slice 8B receipt — turn budget and action economy

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8B admission, diagnostics, lifecycle refresh, and explicit spending

## Delivered

- Completed `dnd2024.turn-budget` with closed administrative write, effect-free diagnostic read,
  and explicit Action, Bonus Action, Reaction, free-interaction, and movement spending.
- Adapted encounter start/advance to restore only the newly active participant in the same atomic
  effect batch, deriving movement from declared Speed and Exhaustion snapshots.
- Fixed the generic composition authority envelope so a parent receives exact child-observed
  component and containment revisions. Conflicting child observations now fail before JavaScript
  execution; no D&D vocabulary or formula entered C#.

## Verification

- Composition/lifecycle/spending regression cases — passed, 4/4.
- Combined activated D&D, application-execution, ECS-effect, and Trail Survival suite — passed,
  75/75.
- All D&D JavaScript syntax checks — passed, 24/24.
- Solution build — passed with 0 warnings and 0 errors.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; no live data
  was touched.
- Full repository suite — passed, 1,041/1,041 plus 20/20 local-AI tests.
- `git diff --check` — passed with only existing line-ending notices.

## Evidence and exclusions

Tests cover exact record/correct/replay, absent and invalid diagnostics, active/off-turn spending,
Reaction, partial movement, repeated/overspend refusal, Conditions prohibitions, Speed and Exhaustion
refresh, one-participant lifecycle mutation, and child-snapshot authorization. Costs remain explicit;
attacks, spells, features, pathfinding, position, terrain, event triggers, fixtures, migrations,
public operations, live state, archive deletion, and automatic death state remain excluded.
