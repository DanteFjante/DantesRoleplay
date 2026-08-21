---
id: mechanic.dnd2024.tactical-move.path
category: ruleset.dnd2024.core.tactical.movement
name: Validate a voluntary tactical movement path
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Effect-free internal path validation. It derives every entered footprint and its exact normal
movement cost without spending a resource or changing position.

## Matches

validate tactical movement path

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature-size","dnd2024.encounter-position"]},"encounter":{"components":["dnd2024.encounter-space"],"includeContents":true}},"children":{"participants":{"mechanicId":"mechanic.dnd2024.encounter-participant-tactical-state.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
