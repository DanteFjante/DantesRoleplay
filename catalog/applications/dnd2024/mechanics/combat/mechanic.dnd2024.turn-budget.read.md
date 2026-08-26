---
id: mechanic.dnd2024.turn-budget.read
category: ruleset.dnd2024.core.combat.economy
name: Read turn-budget diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one participant's turn budget and Condition state without changing either. Missing or invalid
state is reported explicitly for lifecycle fan-out and direct diagnostics.

## Matches

inspect action-economy budget diagnostics
read action-economy budget diagnostics

## Requirements

```json
{"roles":{"participant":{"components":["dnd2024.turn-budget","dnd2024.conditions"],"description":"The participant whose budget and Condition state is reported."}}}
```
