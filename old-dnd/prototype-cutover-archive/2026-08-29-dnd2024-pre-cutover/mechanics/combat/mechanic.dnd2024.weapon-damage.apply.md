---
id: mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Consumes one declared weapon-damage child result, atomically spends optional target Temporary Hit
Points before current Hit Points, and automatically applies positive damage to an optional active
standard-rest episode.

## Matches

apply weapon damage
damage target with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores"],"description":"The attacker."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The static weapon profile."},"target":{"components":["dnd2024.creature.hit-points","dnd2024.creature.temporary-hit-points","dnd2024.rest-episode"],"includeRelationships":true,"description":"The damaged target with authoritative HP, optional Temporary HP, and optional source-bound rest state."}},"children":{"damage":{"mechanicId":"mechanic.dnd2024.weapon-damage.roll","roleBindings":{"subject":"subject","weapon":"weapon"},"inheritInput":true},"mitigation":{"mechanicId":"mechanic.dnd2024.damage.resolve","roleBindings":{"defender":"target"},"inheritInput":false,"input":"{}"}}}
```
