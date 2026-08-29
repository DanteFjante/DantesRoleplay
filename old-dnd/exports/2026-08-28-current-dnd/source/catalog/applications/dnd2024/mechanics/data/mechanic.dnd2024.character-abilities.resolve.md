---
id: mechanic.dnd2024.character-abilities.resolve
category: ruleset.dnd2024.character.creation.abilities
name: Resolve D&D 2024 character-creation ability scores
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves one source-bound ability allocation and one matching background increase selection into
canonical final raw scores. It is a pure composition leaf and never writes character state.

## Matches

resolve character creation abilities
validate ability assignment and background increases

## Requirements

```json
{"roles":{"policy":{"components":["dnd2024.character.ability-assignment-policy"],"description":"The immutable ability-allocation policy selected by the creation root."},"background":{"components":["dnd2024.character.content-definition","dnd2024.background.ability-increase-options"],"description":"The exact active background definition selected by the creation root."}}}
```
