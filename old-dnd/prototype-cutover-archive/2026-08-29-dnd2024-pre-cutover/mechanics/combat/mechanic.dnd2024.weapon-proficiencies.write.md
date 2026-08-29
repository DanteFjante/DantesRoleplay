---
id: mechanic.dnd2024.weapon-proficiencies.write
category: ruleset.dnd2024.core.data.weapon-proficiencies
name: Record weapon proficiencies
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records canonical complete Simple/Martial weapon-category membership plus any property-qualified
Martial membership. `restrictedMartialProperties` uses any-of semantics for Finesse and Light.
Omitted writer input remains compatible and records known empty; successful writes always emit the
explicit current shape.

## Matches

record weapon proficiencies
set weapon proficiencies
record simple weapons and Martial Light weapons

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.proficiencies"],"description":"The creature whose complete-category and property-qualified Martial membership is recorded."}}}
```
