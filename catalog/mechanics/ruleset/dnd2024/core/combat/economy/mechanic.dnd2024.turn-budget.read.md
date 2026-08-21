---
id: mechanic.dnd2024.turn-budget.read
category: ruleset.dnd2024.core.combat.economy
name: Read turn budget diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one participant's turn-budget state without changing it. It reports missing or invalid domain
state as structured diagnostics so an encounter transition can decide whether that state prevents a
turn from beginning. It is safe for fan-out composition and direct diagnostic use.

## Matches

inspect action-economy budget diagnostics
read action-economy budget diagnostics

## Requirements

```json
{"roles":{"participant":{"components":["dnd2024.turn-budget"],"description":"The participant whose present, absent, or malformed budget state is reported."}}}
```
