---
id: mechanic.dnd2024.species-selection.resolve
category: ruleset.dnd2024.character.species-selection
name: Resolve a bound D&D 2024 species selection
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Validates one bound immutable species definition and derives a canonical, zero-effect
character-creation selection plan. Unimplemented special traits remain explicit blockers.

## Matches

resolve character creation species
validate species and size selection

## Requirements

```json
{"roles":{"species":{"components":["dnd2024.character.content-definition","dnd2024.species-profile"],"description":"The exact active immutable species definition selected by the creation root."}}}
```
