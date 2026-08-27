---
id: procedure.mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Set an encounter Initiative order
governs: mechanic.dnd2024.encounter-initiative-order; the immutable encounter Initiative snapshot
status: active
---

## Description

Sets one encounter-owned D&D 2024 Initiative order by composing the individual Initiative mechanic for every contained participant.

## Instructions

1. Treat encounter containment as the sole roster, invoke the declared individual Initiative child exactly once per participant, and accept only per-participant child input plus actual-tie decisions.
2. Sort derived counts descending. An authorized tie decision supplies order only within a tied group; it never supplies a count.
3. Validate every child's optional rest plan. Add exactly one snapshot and apply all active Short
   Rest removals or Long Rest one-hour/count updates in the same transaction. Participants receive
   no persistent Initiative state and no rest benefit.

## Constraints

- The snapshot cannot be replaced by this rule; lifecycle/correction, rounds, and turns are separate owners.
- No caller may supply a roster, ability score, modifier, die, count, or calculated order.
- Every child, rest-scope, or input/tie validation failure yields no snapshot or rest effect.
