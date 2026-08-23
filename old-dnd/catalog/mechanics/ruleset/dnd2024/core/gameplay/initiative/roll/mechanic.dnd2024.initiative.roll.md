---
id: mechanic.dnd2024.initiative.roll
category: ruleset.dnd2024.core.gameplay.initiative.roll
name: Roll individual Initiative
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Resolves one D&D 2024 creature's Initiative from validated Dexterity and derived condition state using
seeded D20 circumstances; it creates no encounter state and applies no effects.

## Matches
roll initiative
initiative roll
roll for initiative

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.conditions"],"description":"The creature rolling individual D&D 2024 Initiative."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
