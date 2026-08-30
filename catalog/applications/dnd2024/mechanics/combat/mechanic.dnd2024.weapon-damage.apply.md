---
id: mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Consumes one declared weapon-damage child result and atomically spends optional target Temporary
Hit Points before current Hit Points. Canonical rest interruption remains a separate event-owned
lifecycle.

## Matches

apply weapon damage
damage target with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores"],"description":"The attacker bound to the damage child."},"weapon":{"components":[],"description":"The weapon identity bound to the damage child."},"activity":{"components":[],"description":"The selected weapon activity identity bound to the damage child."},"target":{"components":["dnd2024.creature.hit-points","dnd2024.creature.temporary-hit-points"],"description":"The damaged target with authoritative Hit Points and Temporary Hit Points."}},"children":{"damage":{"mechanicId":"mechanic.dnd2024.weapon-damage.roll","roleBindings":{"subject":"subject","weapon":"weapon","activity":"activity"},"inheritInput":true},"mitigation":{"mechanicId":"mechanic.dnd2024.damage.resolve","roleBindings":{"defender":"target"},"inheritInput":false,"input":"{}"}}}
```
