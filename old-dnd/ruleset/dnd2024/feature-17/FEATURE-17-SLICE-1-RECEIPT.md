# Feature 17 — Slice 1 receipt

Date: 2026-08-21

## Delivered behavior

- Added the closed `dnd2024.zero-hit-points-policy` component with exactly two valid outcomes:
  `death-saves` and `die-at-zero`.
- Added its record/correct writer and governing contract. The writer changes no Hit Points,
  Temporary Hit Points, conditions, death state, or other entity, and declares no event.
- Added explicit policies to the Feature 10 hero (`death-saves`) and training target
  (`die-at-zero`) fixtures.

## Evidence

- Focused Feature 10, 16, and 17 regression coverage: 6 passed, 0 failed.
- `roleplay validate catalog`: 352 records valid in a fresh disposable database; 57 existing
  near-duplicate warnings; no live data touched.
- Full suite: 642 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.

No persistent catalog import was performed.
