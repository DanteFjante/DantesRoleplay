# Feature 20 Slice 4 plan — voluntary tactical movement

Status: **Verified** — see `FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md`.
Last updated: 2026-08-21

## Capability

An active encounter participant can move a Size-derived footprint along one closed ordered path of
five-foot cardinal or diagonal steps. The path result derives the only budget-spend input, and the
existing turn-budget spender plus the position update commit atomically.

## Dependencies and ownership

- Feature 20 Slice 2 owns map, Size, placement, blocked cells, and occupied-footprint safety.
- Feature 12 owns active-turn authorization and the sole normal movement budget spender.
- E6 owns dependent child-data transport and atomic child-effect aggregation.

~~~text
player path
└─ validated path child
   ├─ map / Size / placement / direct roster snapshots
   └─ derives closed path evidence
      └─ budget-input adapter child
         └─ existing turn-budget spender
            └─ root writes one new position in the same transaction
~~~

## Runtime contract

New permanent ids:

- procedure.mechanic.dnd2024.tactical-move
- mechanic.dnd2024.tactical-move.path
- mechanic.dnd2024.tactical-move.budget-input
- mechanic.dnd2024.tactical-move.execute

The root input is exactly:

~~~json
{"path":[{"dx":1,"dy":0},{"dx":1,"dy":1}]}
~~~

Each step is a closed nonzero pair of integers in -1, 0, 1. It is a direction, not a final
coordinate, distance, terrain result, target, or cost. Each step translates the anchor by two
half-square units and costs exactly five feet; an empty path and more than 200 steps reject.

Every entered footprint must remain in bounds, avoid blocked terrain, and avoid every valid placed
other participant. A diagonal cannot pass between two blocked axial alternatives. Difficult terrain
is deliberately ignored in this slice. The path child returns complete closed evidence. A dependent
adapter validates that evidence and returns only { resource, feet }; the existing budget spender
receives only that object. The root validates frozen child identities/evidence and proposes exactly
one position set effect. Child budget and root position effects commit or roll back together.

## Acceptance

- zero, one, and multiple steps; cardinal/diagonal movement; exact and one-short movement budget;
  and replay;
- blocked, out-of-bounds, diagonal corner, occupied, malformed roster/state, off-turn, and closed
  input rejection with no partial budget/position update;
- Feature 12 remains the only budget authority and no caller cost or final position is accepted;
- no Action/Bonus Action/Reaction, damage, difficult-terrain cost, pass-through exception,
  opportunity candidate, event, or position mutation on failed admission.

Slice 5, not this slice, adds difficult-terrain costs and SRD pass-through exceptions.
