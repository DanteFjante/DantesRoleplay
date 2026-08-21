---
id: mechanic.dnd2024.saving-throw
category: ruleset.dnd2024.core.gameplay.saving-throws.fixed-dc
name: Resolve a fixed-DC saving throw
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Resolves one D&D 2024 character fixed-DC saving throw from validated abilities, level, and
saving-throw-proficiency state; it merges condition-derived D20 effects and applies no effects.

## Matches
make a saving throw
saving throw against a dc
strength saving throw
dexterity saving throw
constitution saving throw
intelligence saving throw
wisdom saving throw
charisma saving throw

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.character-level","dnd2024.saving-throw-proficiencies"],"description":"The character making a fixed-DC D&D 2024 saving throw."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
