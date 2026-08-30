---
id: mechanic.dnd2024.initiative.roll
category: ruleset.dnd2024.core.gameplay.initiative.roll
name: Roll individual Initiative
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves one effect-free D&D 2024 Initiative count from Dexterity. An actor with exactly one
canonical Alert entitlement may explicitly opt into Initiative Proficiency; the mechanic derives
the current Proficiency Bonus from authoritative character level. Canonical rest interruption is a
separate event-owned lifecycle and is not inferred here.

## Matches

roll initiative
initiative roll
roll for initiative
roll initiative with alert

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores","dnd2024.character.feature-entitlements"],"description":"The creature rolling individual D&D 2024 Initiative."}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
