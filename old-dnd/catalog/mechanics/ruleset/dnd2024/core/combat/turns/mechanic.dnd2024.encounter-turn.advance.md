---
id: mechanic.dnd2024.encounter-turn.advance
category: ruleset.dnd2024.core.combat.turns
name: Advance encounter turn
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Advances one active D&D 2024 encounter to the next participant in its existing Initiative snapshot.
After the final participant, it begins exactly one new round at index 0. It neither starts nor ends
an encounter, and it has no action-economy or combat-consequence effect.

## Matches

advance encounter turn
advance combat turn
next encounter turn

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active encounter whose validated Initiative snapshot and lifecycle state determine one next turn."}},"children":{"budgets":{"mechanicId":"mechanic.dnd2024.turn-budget.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"},"speeds":{"mechanicId":"mechanic.dnd2024.speed.read","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
