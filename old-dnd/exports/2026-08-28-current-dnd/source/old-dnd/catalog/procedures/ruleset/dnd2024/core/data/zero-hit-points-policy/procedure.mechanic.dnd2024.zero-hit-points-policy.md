---
id: procedure.mechanic.dnd2024.zero-hit-points-policy
category: ruleset.dnd2024.core.data.zero-hit-points-policy
name: Record D&D 2024 zero-Hit-Point policy
governs: commit(kind: "component") declaring zero-Hit-Point policy storage; commit(kind: "mechanic") validating zero-Hit-Point policy records; commit(kind: "action") recording or correcting a creature's policy at 0 Hit Points
status: active
---

## Description

Owns the explicit outcome policy for a creature reduced to 0 Hit Points. It records whether that
creature makes Death Saving Throws or dies at 0; it does not claim a creature type or apply any
death, unconsciousness, damage, healing, or condition consequence.

## Instructions

1. Declare closed `dnd2024.zero-hit-points-policy` state with exactly `policy` and fixed
   `sourceRef` `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Dropping to 0 Hit Points"}`.
   Policy is exactly `death-saves` or `die-at-zero`.
2. `mechanic.dnd2024.zero-hit-points-policy.write` has one required `subject` role declaring that
   component. It accepts exactly `{"mode":"record"|"correct","policy":"death-saves"|"die-at-zero"}`.
3. `record` requires absence and proposes exactly one `component.add`; `correct` requires complete
   valid existing state and proposes exactly one `component.set`. Malformed existing state is
   rejected rather than repaired.
4. Return mode, prior policy (null on record), resulting policy, and source reference. Consume no
   randomness and declare no event.

## Constraints

- This policy is neither a creature type, species, class, monster stat block, nor player-account
  association. Feature 35 may replace it with richer creature data only at an explicit migration
  boundary.
- The writer changes no Hit Points, Temporary Hit Points, conditions, death state, or other entity.
- It accepts no caller source reference, species, class, stat block, Hit Point, damage, healing,
  dead, event, or effects field.
