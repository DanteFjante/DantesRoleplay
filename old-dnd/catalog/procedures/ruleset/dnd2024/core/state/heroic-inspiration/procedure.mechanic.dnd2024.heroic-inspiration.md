---
id: procedure.mechanic.dnd2024.heroic-inspiration
category: ruleset.dnd2024.core.state.heroic-inspiration
name: Record one available D&D 2024 Heroic Inspiration instance
governs: commit(kind: "component") declaring Heroic Inspiration presence state; commit(kind: "mechanic") authoring its guarded grant recorder; commit(kind: "action") when an authorised rule owner grants one available instance
status: active
---

## Description

Owns the presence state for the one Heroic Inspiration instance that a player character currently
holds. The component is present only while that instance is available. This slice records an
instance; it does not use, transfer, or source it.

## Instructions

1. Declare `dnd2024.heroic-inspiration` as a closed empty object. Presence represents exactly one
   available instance and absence is the only representation of none. Do not add `available`, a
   count, source reference, source/trait/feat key, recipient, roll, die, result, expiry, or
   history field.
2. `mechanic.dnd2024.heroic-inspiration.grant` has one required `subject` role. Its input is
   exactly `{}` and the subject must carry a valid CH1 `dnd2024.character.profile` component.
   This profile is the current player-character eligibility marker; do not infer eligibility from
   an entity name, campaign containment, encounter participation, species, or caller input.
3. A missing Heroic Inspiration component permits one `component.add` with canonical `{}` data.
   A present valid component is a duplicate-grant failure. A present malformed component, missing
   or corrupt profile, or any invalid input fails before proposing effects.
4. Return the subject ID, `heldBefore: false`, `heldAfter: true`, fixed `Rules Glossary > Heroic
   Inspiration` provenance, and exactly one add effect. Use no randomness and inspect no rest,
   species, feat, party, campaign, encounter, item, class, resource, die, or prior action result.

## Constraints

- The component does not establish a character profile, campaign attachment, player control,
  source-specific eligibility, Human Resourceful trigger, or a public character-creation flow.
- Do not clear, consume, transfer, correct, replace, duplicate, or overwrite this state here.
  Feature 39's later source-grant, overflow, correction, and reroll-composition slices own those
  transitions after their dependencies are confirmed.
- This grant does not reroll or change an ability check, saving throw, Initiative, weapon attack,
  weapon damage, generic dice result, Advantage/Disadvantage state, modifier, total, or outcome.
  Every such resolver retains its own die and arithmetic ownership.
