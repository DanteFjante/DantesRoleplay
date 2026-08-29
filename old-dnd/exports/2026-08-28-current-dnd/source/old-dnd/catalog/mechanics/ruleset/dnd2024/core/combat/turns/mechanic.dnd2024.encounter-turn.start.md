---
id: mechanic.dnd2024.encounter-turn.start
category: ruleset.dnd2024.core.combat.turns
name: Start encounter turns
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Starts one D&D 2024 encounter at round 1, turn index 0, using its existing immutable Initiative
snapshot. The active participant is reported from that index and is never stored separately. This
rule neither advances nor ends the encounter and has no action-economy or combat-consequence effect.

## Matches

start encounter turns
begin combat turns
begin encounter turns

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The encounter with a recorded Initiative snapshot and no lifecycle state yet."}},"children":{"budgets":{"mechanicId":"mechanic.dnd2024.turn-budget.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"},"speeds":{"mechanicId":"mechanic.dnd2024.speed.read","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
