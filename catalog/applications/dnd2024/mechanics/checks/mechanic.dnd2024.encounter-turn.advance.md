---
id: mechanic.dnd2024.encounter-turn.advance
category: ruleset.dnd2024.core.combat.turns
name: Advance encounter turn
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Moves an active encounter to its next ordered turn, wrapping rounds at the final participant, and
atomically restores only the newly active participant's turn budget from Speed and Exhaustion.

## Matches

advance encounter turn
next combat turn

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active ordered encounter."}},"children":{"budgets":{"mechanicId":"mechanic.dnd2024.turn-budget.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"},"speeds":{"mechanicId":"mechanic.dnd2024.speed.read","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
