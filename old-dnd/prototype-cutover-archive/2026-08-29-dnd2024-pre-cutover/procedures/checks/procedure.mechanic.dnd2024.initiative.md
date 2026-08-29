---
id: procedure.mechanic.dnd2024.initiative
category: ruleset.dnd2024.core.gameplay.initiative.roll
name: Roll individual Initiative
governs: mechanic.dnd2024.initiative.roll; an effect-free individual Initiative action
status: active
---

## Description

Rolls a creature's D&D 2024 Initiative from authoritative Dexterity and a kernel seed, with an
explicit optional Alert Initiative Proficiency contribution derived from authoritative grants and
character level.

## Instructions

1. Accept only optional explicit 7A3-style roll circumstances and optional Boolean
   `useAlertInitiativeProficiency`. Omission or false preserves the base roll.
2. Apply the Dexterity modifier to the selected d20 result, using non-stacking Advantage/Disadvantage when circumstances specify it.
3. When feature-grant state is present, validate its complete closed envelope. Recognize Alert only
   as exactly one non-repeatable `origin-feat` grant of
   `content.dnd2024.feature.alert.v1` with `configurationKey: default` and schema-valid
   declaring-owner/source provenance. Reject malformed, duplicate, or misconfigured Alert state.
4. Only for an Alert holder, require a valid derived character-level result, and apply its
   Proficiency Bonus only on explicit opt-in. Report availability, use,
   the derived eligible bonus, canonical Alert behavior source, and one `feat:alert` modifier when
   used. Only the modifier and final count application are opt-in. The caller cannot supply level,
   bonus, feature identity, grantor, modifier, source, or final Initiative.
5. Project optional subject rest state/relationships, validate its exact source-bound scope, and
   return null, a Short Rest stop plan, or a Long Rest one-hour/count plan for the encounter root.
6. Report the Initiative count and plan without storing an order, consuming a turn, or applying an
   effect in this individual child.

## Constraints

- Initiative count persistence, encounter membership, turn state, tie breaking, and applying the
  rest plan are outside this individual-roll child.
- Alert Initiative Swap, willingness, same-combat proof, Incapacitated gating, and post-roll
  reaction/window handling are outside this slice.
- Natural 1 and natural 20 have no additional effect.
- Persistent/derived conditions are deferred; only explicit audit circumstances are accepted.
