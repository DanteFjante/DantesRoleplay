---
id: mechanic.dnd2024.tactical-move.execute
category: ruleset.dnd2024.core.tactical.movement
name: Execute voluntary tactical movement
scope: dnd2024-srd-5.2.1
status: active
---

## Matches

move tactically
make tactical move
move on encounter grid

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.encounter-position"]},"encounter":{"components":["dnd2024.encounter-space","dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true}},"children":{"path":{"mechanicId":"mechanic.dnd2024.tactical-move.path","roleBindings":{"subject":"subject","encounter":"encounter"},"inheritInput":true},"budgetInput":{"mechanicId":"mechanic.dnd2024.tactical-move.budget-input","roleBindings":{},"inheritInput":false,"inputFromChildData":{"resultKey":"path"}},"budget":{"mechanicId":"mechanic.dnd2024.turn-budget.spend","roleBindings":{"subject":"subject","encounter":"encounter"},"inheritInput":false,"inputFromChildData":{"resultKey":"budgetInput"}}}}
```
