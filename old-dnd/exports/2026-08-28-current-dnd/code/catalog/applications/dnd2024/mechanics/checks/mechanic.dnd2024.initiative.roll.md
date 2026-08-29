---
id: mechanic.dnd2024.initiative.roll
category: ruleset.dnd2024.core.gameplay.initiative.roll
name: Roll individual Initiative
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves one effect-free D&D 2024 Initiative count from Dexterity. An actor with exactly one valid
Alert Origin-feat grant may explicitly opt into Initiative Proficiency; the mechanic derives the
current eligible Proficiency Bonus from authoritative character level and reports the canonical
Alert behavior source whenever available. Only explicit opt-in adds the modifier to the count. It
also returns a closed optional active-rest interruption plan for an authoritative encounter root to
apply.

## Matches

roll initiative
initiative roll
roll for initiative
roll initiative with alert

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores","dnd2024.character-feature-grants","dnd2024.rest-episode"],"includeRelationships":true,"description":"The creature rolling individual D&D 2024 Initiative with optional complete feature grants and optional source-bound rest state."}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
