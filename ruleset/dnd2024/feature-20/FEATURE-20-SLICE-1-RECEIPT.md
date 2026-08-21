# Feature 20 Slice 1 receipt — base Speed and turn-budget migration

Status: **Verified 2026-08-21.**

## Delivered

- `dnd2024.speed` is the source-backed persistent base-Speed profile for a creature. It stores
  walk Speed and optional burrow, climb, fly, and swim Speeds.
- Its writer records/corrects only a complete closed profile; its reader reports absent, malformed,
  and invalid state without applying a default.
- `dnd2024.turn-budget` now stores only per-turn remaining movement. The duplicated persistent
  `movementMaximumFeet` scaffold is gone.
- Encounter turn start/advance refreshes remaining movement from the newly active creature's walk
  Speed atomically with the existing lifecycle transition.
- Normal movement-budget spending rejects absent/corrupt Speed or remaining movement above valid
  walk Speed. Feature 12 remains the sole normal resource spender.

## Not delivered

No grid/position/terrain/path movement, reach, special-movement action, travel pace, world route,
rest, duration, or condition-based Speed modification was added.

## Verification

| Check | Result |
| --- | --- |
| Focused Feature 11/12/20 tests | Passed: 13 tests |
| `roleplay.cmd validate catalog` | Validated 239 records with 0 warnings; no live data touched |
| Full repository suite | Passed: 511 tests |

No persistent catalog import or live campaign change was performed.
