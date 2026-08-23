---
id: mechanic.dnd2024.conditions.guard
category: ruleset.dnd2024.core.state.conditions
name: Guard creature-condition state
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Allows only canonical, complete D&D 2024 condition-list additions and replacements before they
commit. It is middleware only; it never writes or derives a condition.

## Matches

guard creature condition state

## Requirements

```json
{"event":{"mode":"guard","types":["world.component.added","world.component.replaced"],"components":["dnd2024.conditions"]}}
```
