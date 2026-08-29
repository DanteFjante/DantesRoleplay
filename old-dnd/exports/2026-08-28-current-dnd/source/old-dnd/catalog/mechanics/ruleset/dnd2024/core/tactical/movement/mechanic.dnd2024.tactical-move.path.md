---
id: mechanic.dnd2024.tactical-move.path
category: ruleset.dnd2024.core.tactical.movement
name: Validate a voluntary tactical movement path
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Effect-free internal path validation. It derives every entered footprint, exact difficult-terrain
and occupied-space cost, and lawful passage evidence without spending a resource or changing
position.

## Matches

validate tactical movement path

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature-size","dnd2024.encounter-position","dnd2024.conditions"]},"encounter":{"components":["dnd2024.encounter-space","dnd2024.encounter-sides"],"includeContents":true}},"children":{"participants":{"mechanicId":"mechanic.dnd2024.encounter-participant-movement-state.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"},"relations":{"mechanicId":"mechanic.dnd2024.encounter-sides.relation","roleBindings":{"encounter":"encounter","first":"subject","second":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
