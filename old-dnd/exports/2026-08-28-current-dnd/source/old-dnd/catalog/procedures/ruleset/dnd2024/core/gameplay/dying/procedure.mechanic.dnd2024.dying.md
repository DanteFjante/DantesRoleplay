---
id: procedure.mechanic.dnd2024.dying
category: ruleset.dnd2024.core.gameplay.dying
name: Apply dropping-to-zero consequences
governs: commit(kind: "mechanic") authoring mechanic.dnd2024.dying.on-damage; commit(kind: "subscription") registering its damage reaction
status: active
---

## Description

Owns the automatic, transactional consequences of a registered `dnd2024.damage.dealt` fact when
damage leaves a creature at 0 Hit Points. It consumes Feature 15's post-buffer overkill rather than
revising a damage writer or guessing from current Hit Points.

## Instructions

1. React only to the closed damage event. Ignore zero final damage and damage whose recorded after
   value remains above zero.
2. Require a valid zero-Hit-Point policy. `die-at-zero` and overkill at least equal to maximum
   create or replace a terminal death state without adding Unconscious.
3. Damage while a death-saves creature already has valid death state adds one failure, or two for a
   critical hit. A third failure becomes terminal death; damage ends Stable state.
4. Otherwise begin zeroed nonterminal death state and add Unconscious. If the creature has no
   conditions component, add its complete one-entry state; if it has a valid component, preserve
   every entry and append Unconscious only when absent. The conditions guard validates the proposal.

## Constraints

- Declares no child, event, notification, random result, healing, turn change, or Hit Point effect.
- Missing policy and malformed required state fail the whole originating damage transaction. An
  absent conditions component is ordinary no-condition state, not an error.
- Death state is terminal under its own writer; this reaction uses the same zeroed terminal shape.
  Resurrection and the later healing/stabilization exits remain later Feature 17 slices.
