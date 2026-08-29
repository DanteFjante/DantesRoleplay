---
id: mechanic.dnd2024.healing.apply
category: ruleset.dnd2024.core.gameplay.healing
name: Apply healing to Hit Points
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Applies one positive D&D 2024 healing amount to authoritative Hit Points, capped at the existing
maximum. It does not grant, restore, or consume Temporary Hit Points.

## Matches

heal the character
heal hit points
restore hit points
apply healing

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.hit-points"],"description":"The creature whose authoritative Hit Points receive bounded healing."}}}
```
