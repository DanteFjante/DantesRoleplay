# Feature 14 — Slice 3 receipt

Date: 2026-08-21

## Delivered behavior

- The existing `mechanic.dnd2024.turn-budget.read` fan-out now validates a participant's optional
  condition state and reports its Exhaustion level alongside the budget diagnostic.
- Encounter start and advance restore movement as `max(0, walk Speed - 5 × Exhaustion level)`.
  Walk Speed remains the single authoritative maximum; the six-field turn-budget schema was not
  changed or duplicated.
- A malformed or invalid condition component blocks only when that participant becomes active.
  Invalid condition state on another roster member is reported without blocking the transition.
- Transition results retain the prior `walkFeet` field and add the auditable maximum, reduction,
  and restored-remaining values.

## Evidence

- Focused Feature 14 tests: 5 passed, 0 failed.
- Feature 12 and Feature 14 focused tests: 10 passed, 0 failed.
- Feature 14 and Feature 20 focused tests: 8 passed, 0 failed.
- Full suite: 564 passed, 0 failed, 0 skipped.
- `roleplay validate catalog`: valid in a fresh disposable database; no live data touched. The
  validator reported only its existing near-duplicate warnings.
- `git diff --check`: passed.
