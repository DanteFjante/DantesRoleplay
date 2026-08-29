---
id: mechanic.dnd2024.speed.write
category: dnd2024.core.data.speed
name: Record creature Speed
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Records or corrects a complete current movement component. The input uses the prototype's keyed
metric distances, enabled flags, and source references; it does not update per-turn expenditure,
terrain, position, route, or travel pace.

## Matches
record creature speed
correct creature speed
set creature speed

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.creature.movement"],"description":"The creature whose absent or valid existing movement state is recorded or corrected."}}}
```
