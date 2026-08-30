# Feature 20 Slice 4 receipt — voluntary tactical movement

Status: **Verified**
Date: 2026-08-21

## Delivered

Slice 4 adds `procedure.mechanic.dnd2024.tactical-move` with three mechanics:

- `mechanic.dnd2024.tactical-move.path` accepts only a closed ordered list of cardinal or
  diagonal directions, derives the final placement and five-foot-per-step cost, and validates the
  authoritative map, Size-derived footprint, direct roster snapshots, bounds, blocked terrain,
  safe occupancy, and diagonal corner cutting.
- `mechanic.dnd2024.tactical-move.budget-input` accepts only that frozen path evidence and emits
  the narrow `{ resource: "movement", feet }` input required by the existing Feature 12 spender.
- `mechanic.dnd2024.tactical-move.execute` uses E6 dependent child-data binding to run that
  existing spender once, then supplies the one position-set effect. The engine aggregates both in
  one root transaction, so success updates budget and position together and any rejection updates
  neither.

The player-facing input is exactly `{"path":[{"dx":1,"dy":0}]}`. It has no caller-provided
feet, destination, terrain verdict, target, or effects. The implementation creates no Action,
Bonus Action, Reaction, damage, event, opportunity candidate, difficult-terrain cost, or
pass-through exception.

## Evidence

| Check | Result |
| --- | --- |
| Focused Slice 4 coverage | `CatalogFeature20TacticalMovementTests`: **2 passed, 0 failed**. Covers one/many/exact-budget movement, deterministic replay, empty/malformed input, bounds, occupancy, blocked diagonal corner, and off-turn rollback. |
| Tactical compatibility | Features 8, 12, 20 Slice 2, 20 Slice 3, and 20 Slice 4: **15 passed, 0 failed**. |
| Catalog validation | `./roleplay validate catalog`: **380 valid records** (90 mechanics, 106 procedures, 75 components, 12 event types, 5 subscriptions, 92 entities), **65 advisory warnings**, no errors, and no live data touched. |

## Boundary retained

Slice 5 owns difficult-terrain cost and the SRD exceptions for moving through another creature's
space. This slice intentionally treats every other occupied footprint as unsafe and uses a fixed
five-foot cost for each accepted step.
