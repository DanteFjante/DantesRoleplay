---
id: procedure.mechanic.dnd2024.tactical-move
category: ruleset.dnd2024.core.tactical.movement
name: Govern D&D 2024 voluntary tactical movement
governs: closed voluntary tactical paths, Feature 12 movement-budget composition, and atomic encounter-position updates
status: active
---

Defines ordinary five-foot voluntary movement on the bounded Feature 20 grid.

## Instructions

1. Accept only a closed ordered list of cardinal or diagonal unit directions. Do not accept feet,
   a destination, terrain result, occupancy result, or any budget/outcome field from a caller.
2. Derive every entered Size footprint from committed map, placement, Size, effective
   Incapacitated state, encounter-side relation evidence, and direct encounter roster snapshots.
   Difficult terrain and non-ally/non-Tiny creature spaces cost one extra foot per foot once per
   entered step. Allow pass-through only for an ally, an effectively Incapacitated creature, a Tiny
   creature, or a creature at least two Size ranks different; never allow an occupied final
   footprint. Reject bounds, blocked, malformed, inadmissible, and diagonal corner-cutting paths
   before budget spending.
3. Produce the exact closed movement budget input through a declared dependent child only. The
   Feature 12 spender remains the sole movement-allowance and active-turn authority.
4. Aggregate the spender's sole budget effect with one root position effect in the same transaction.
   A failure may not partially move a creature or spend movement.
5. Do not spend an Action, emit an opportunity candidate, or handle forced/special movement.

## Constraints

- Feature 20 owns the path geometry and position destination.
- Feature 12 owns resource availability and spending.
- E6 owns dependent child input and child-proposal aggregation.
