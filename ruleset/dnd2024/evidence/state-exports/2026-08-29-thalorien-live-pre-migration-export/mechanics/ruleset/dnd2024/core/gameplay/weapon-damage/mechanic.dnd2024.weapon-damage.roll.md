---
id: mechanic.dnd2024.weapon-damage.roll
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Roll confirmed weapon damage
scope: dnd2024-srd-5.2.1
status: active
createdBy: "import"
changeNote: "Imported from the catalog."
---

## Description
Resolves seeded base damage for a GM/caller-confirmed successful D&D 2024 weapon hit. It reads a canonical weapon profile and selected ability score, doubles base dice on a Critical Hit, and reports no effects or Hit Point change.

## Matches
roll weapon damage
roll dagger damage
roll damage for a critical hit
resolve confirmed weapon damage

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities"],"description":"The creature whose selected attack ability contributes to confirmed weapon damage."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The canonical weapon supplying base damage facts."}}}
```

