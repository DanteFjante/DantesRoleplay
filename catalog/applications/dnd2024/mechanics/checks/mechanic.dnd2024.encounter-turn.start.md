---
id: mechanic.dnd2024.encounter-turn.start
category: ruleset.dnd2024.core.combat.turns
name: Start encounter turns
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Creates active round-one lifecycle state from a valid Initiative snapshot and atomically restores
only the first participant's turn budget from Speed and Exhaustion.

## Matches

start encounter turns
begin combat turns

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The ordered encounter to start."}},"children":{"budgets":{"mechanicId":"mechanic.dnd2024.turn-budget.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"},"speeds":{"mechanicId":"mechanic.dnd2024.speed.read","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
