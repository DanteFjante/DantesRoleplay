---
id: mechanic.dnd2024.turn-budget.read
category: ruleset.dnd2024.core.combat.economy
name: Read turn budget diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one participant's turn-budget and condition state without changing either. It reports missing
or invalid domain state as structured diagnostics so an encounter transition can decide whether that
state prevents a turn from beginning. A missing condition component is valid level-zero Exhaustion.
It is safe for fan-out composition and direct diagnostic use.

## Matches

inspect action-economy budget diagnostics
read action-economy budget diagnostics

## Requirements

```json
{"roles":{"participant":{"components":["dnd2024.turn-budget","dnd2024.conditions"],"description":"The participant whose present, absent, or malformed budget and condition state is reported."}}}
```
