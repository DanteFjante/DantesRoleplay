---
id: procedure.mechanic.dnd2024.death-state
category: ruleset.dnd2024.core.gameplay.dying
name: Record D&D 2024 death state
governs: commit(kind: "component") declaring death-state storage; commit(kind: "mechanic") validating death-state transitions; commit(kind: "action") beginning, correcting, or ending a non-terminal creature death state
status: active
---

## Description

Owns the closed state for a creature that is dying, Stable, or dead: Death Saving Throw successes,
failures, and terminal flags. It is an administrative transition only; later Feature 17 slices own
damage, unconsciousness, rolls, stabilization causes, and healing exits.

## Instructions

1. Declare closed `dnd2024.death-state` state with exactly integer `successes` and `failures` from
   0 through 2, Boolean `stable` and `dead`, and fixed source reference
   `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Dropping to 0 Hit Points"}`.
   A tally of 3 is never stored. Stable state requires both tallies to be zero; Stable and dead
   cannot both be true.
2. `mechanic.dnd2024.death-state.write` has one required `subject` role declaring that component.
   Its closed modes are `{"mode":"begin"}`, `{"mode":"end"}`, and
   `{"mode":"correct","successes":0..2,"failures":0..2,"stable":true|false,"dead":true|false}`.
3. `begin` requires absence and adds `{successes:0,failures:0,stable:false,dead:false}`. `correct`
   requires complete valid existing state and sets a complete valid state. `end` requires present,
   non-dead state and removes it.
4. Dead state is terminal within this feature: `correct` never clears a recorded `dead: true`, and
   `end` never removes it. Return the mode, prior and resulting state where applicable, and source
   reference. Consume no randomness and declare no event.

## Constraints

- This writer changes no Hit Points, Temporary Hit Points, zero-Hit-Point policy, conditions, turn,
  roll, source creature, or other entity.
- It accepts no caller source reference, policy, damage, healing, condition, roll, Hit Point, event,
  or effects field.
- It neither applies Unconscious nor decides why death state begins, stabilizes, ends, or becomes
  terminal. Those are later Feature 17 owners.
