# Feature 16 — Slice 3 receipt

Date: 2026-08-21

## Delivered behavior

- Revised `mechanic.dnd2024.weapon-damage.apply` so mitigated damage spends a valid Temporary Hit
  Point buffer before current Hit Points.
- The buffer is set when it remains positive, removed when exhausted, and untouched for zero
  damage; the Hit Point `component.set` remains the final effect in every successful application.
- `dnd2024.damage.dealt` now records `temporaryBefore`, `temporaryAfter`, and
  `temporaryAbsorbed`; overkill is calculated after absorption.
- Corrupt stored buffer state rejects the whole damage application before effects or events.

## Evidence

- Focused Feature 9, 15, and 16 compatibility coverage: 10 passed, 0 failed.
- `roleplay validate catalog`: 334 records valid in a fresh disposable database; 50 existing
  near-duplicate warnings; no live data touched.
- Full suite: 638 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.

No persistent catalog import was performed.
