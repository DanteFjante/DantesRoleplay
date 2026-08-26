---
id: mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Consumes one declared weapon-damage child result and atomically replaces only target current Hit Points.

## Matches

apply weapon damage
damage target with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.abilities"],"description":"The attacker."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The static weapon profile."},"target":{"components":["dnd2024.hit-points"],"description":"The damaged target."}},"children":{"damage":{"mechanicId":"mechanic.dnd2024.weapon-damage.roll","roleBindings":{"subject":"subject","weapon":"weapon"},"inheritInput":true}}}
```
