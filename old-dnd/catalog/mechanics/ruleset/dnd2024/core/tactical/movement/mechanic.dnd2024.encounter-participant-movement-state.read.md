---
id: mechanic.dnd2024.encounter-participant-movement-state.read
category: ruleset.dnd2024.core.tactical.movement
name: Read participant movement state
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Effect-free internal movement diagnostic. It reuses tactical Size/position diagnostics and the
condition owner’s effective Incapacitated result without copying condition rules.

## Matches

inspect participant movement state

## Requirements

```json
{"roles":{"participant":{"components":["dnd2024.creature-size","dnd2024.encounter-position","dnd2024.conditions"]}},"children":{"tactical":{"mechanicId":"mechanic.dnd2024.encounter-participant-tactical-state.read","roleBindings":{"participant":"participant"},"inheritInput":false,"input":"{}"},"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"participant"},"inheritInput":false,"input":"{}"}}}
```
