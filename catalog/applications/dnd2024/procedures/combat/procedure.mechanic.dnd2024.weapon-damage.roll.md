---
id: procedure.mechanic.dnd2024.weapon-damage.roll
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Roll confirmed weapon damage
governs: mechanic.dnd2024.weapon-damage.roll
status: active
---

## Description

Owns effect-free activity-defined damage for a confirmed normal or critical weapon hit.

## Instructions

Bind the weapon and its selected active member activity. Accept exactly one activity-permitted
Strength or Dexterity choice and a critical Boolean. For dice amounts, double base dice on a
critical hit, add the dice expression's static modifier and the ability modifier once, clamp below
zero, and return ordered rolls and damage. A fixed base amount stays fixed and gains no ability
modifier, including on a critical hit.

## Constraints

This does not confirm the hit, add PB, persist outcomes, apply damage, or own mitigation, Temporary
HP, Conditions, death, equipment, tactical range, property behavior, or extra damage.
