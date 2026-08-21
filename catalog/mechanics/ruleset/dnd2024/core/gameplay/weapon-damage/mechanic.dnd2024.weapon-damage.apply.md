---
id: mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Composes the canonical confirmed weapon-damage and mitigation-profile resolvers, spends any Temporary Hit Point buffer before real Hit Points, then atomically records the damage instance and post-buffer overkill without inferring a condition or death consequence.

## Matches
apply confirmed weapon damage
deal confirmed weapon damage
damage target with confirmed weapon hit
apply critical weapon damage

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities"],"description":"The creature whose confirmed weapon hit supplies the selected ability."},"target":{"components":["dnd2024.hit-points","dnd2024.temporary-hit-points","dnd2024.damage-mitigation","dnd2024.conditions"],"description":"The creature whose buffer absorbs mitigated damage before its authoritative Hit Points; temporary, mitigation, and condition components may be absent."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The canonical weapon supplying the child damage expression."}},"children":{"damage":{"mechanicId":"mechanic.dnd2024.weapon-damage.roll","roleBindings":{"subject":"subject","weapon":"weapon"},"inheritInput":true},"mitigation":{"mechanicId":"mechanic.dnd2024.damage.resolve","roleBindings":{"defender":"target"},"inheritInput":false,"input":"{}"}}}
```
