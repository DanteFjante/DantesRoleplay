---
id: mechanic.dnd2024.tactical-melee.attack
category: ruleset.dnd2024.core.tactical.melee
name: Resolve a tactical melee weapon attack
scope: dnd2024-srd-5.2.1
status: active
---

## Matches

make tactical melee attack
tactical melee attack
attack adjacent target

## Requirements

```json
{"roles":{"attacker":{"components":["dnd2024.creature-size","dnd2024.encounter-position","dnd2024.melee-reach"]},"target":{"components":["dnd2024.creature-size","dnd2024.encounter-position"]},"weapon":{"components":["dnd2024.weapon-profile"]},"encounter":{"components":["dnd2024.encounter-space"],"includeContents":true}},"children":{"admission":{"mechanicId":"mechanic.dnd2024.tactical-melee.admit","roleBindings":{"attacker":"attacker","target":"target","weapon":"weapon","encounter":"encounter"},"inheritInput":true},"attack":{"mechanicId":"mechanic.dnd2024.weapon-attack","roleBindings":{"subject":"attacker","target":"target","weapon":"weapon"},"inheritInput":false,"inputFromChildData":{"resultKey":"admission"}}}}
```
