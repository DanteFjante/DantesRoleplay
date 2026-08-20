---
id: mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Composes the canonical confirmed weapon-damage resolver once, then atomically replaces only the target's current D&D 2024 Hit Points. It preserves maximum and source attribution, clamps at zero, and does not infer any condition or death consequence.

## Matches
apply confirmed weapon damage
deal confirmed weapon damage
damage target with confirmed weapon hit
apply critical weapon damage

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities"],"description":"The creature whose confirmed weapon hit supplies the selected ability."},"target":{"components":["dnd2024.hit-points"],"description":"The creature whose authoritative current Hit Points receive the damage."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The canonical weapon supplying the child damage expression."}},"children":{"damage":{"mechanicId":"mechanic.dnd2024.weapon-damage.roll","roleBindings":{"subject":"subject","weapon":"weapon"},"inheritInput":true}}}
```

