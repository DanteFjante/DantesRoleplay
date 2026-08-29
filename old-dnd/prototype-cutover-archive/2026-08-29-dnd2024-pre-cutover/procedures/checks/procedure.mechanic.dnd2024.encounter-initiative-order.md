---
id: procedure.mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Set an encounter Initiative order
governs: mechanic.dnd2024.encounter-initiative-order; dnd2024.encounter.participation; dnd2024.combat.initiative
status: active
---

## Description

Establishes encounter participation and locked Initiative by composing the individual Initiative mechanic for every initially contained actor.

## Instructions

1. Use encounter containment only to bootstrap the initial actor roster, invoke the declared individual Initiative child exactly once per actor, and accept only per-actor child input, caller-supplied participation IDs, and actual-tie decisions.
2. Sort derived counts descending. An authorized tie decision supplies order only within a tied group; it never supplies a count.
3. Validate every child's optional rest plan. Create one participation entity and one locked
   Initiative component per actor, connect the participation graph, and apply all active-rest
   consequences in the same transaction.

## Constraints

- Participation and locked Initiative cannot be replaced by this rule; correction, rounds, and turns
  are separate owners.
- No caller may supply a roster, ability score, modifier, die, count, or calculated order.
- Every child, rest-scope, runtime-ID, or input/tie validation failure yields no participation,
  Initiative, relationship, or rest effect.
