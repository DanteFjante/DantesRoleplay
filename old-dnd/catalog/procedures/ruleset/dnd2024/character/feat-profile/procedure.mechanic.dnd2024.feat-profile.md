---
id: procedure.mechanic.dnd2024.feat-profile
category: ruleset.dnd2024.character.feat-profile
name: Define immutable D&D 2024 Origin-feat profiles
governs: catalog authoring of dnd2024.feat-profile on versioned dnd2024.character.content-definition feature entities
status: active
---

## Description

Defines the source-cited immutable catalog identity for the initial D&D 2024 Origin feats. A
profile belongs only to a versioned feature definition and records no active benefit.

## Instructions

1. Attach `dnd2024.feat-profile` only to an entity whose `dnd2024.character.content-definition`
   has `kind: feature`. Its content key, content version, and source reference must agree exactly
   with the profile.
2. The initial closed catalog contains only Alert and Savage Attacker at `Feats > Origin Feats`,
   PDF page 87. Both have `category: "origin"` and `repeatable: false`.
3. A profile is immutable source identity. A correction requires a distinct reviewed content
   version; never rewrite an established definition or add an actor-specific value.

## Constraints

- This component cannot select or grant a feat, record a feat receipt, evaluate a prerequisite, or
  write actor state.
- It cannot add Initiative Proficiency, swap initiative, reroll weapon damage, spend a turn use,
  or alter an attack/damage/initiative result. Those remain separate mechanics with their own
  confirmed composition boundaries.
- Do not encode feat rules prose, a benefit/effect key, dice, target, duration, resource, ability
  score, choice, skill, spell, item, or executable payload in this static profile.
