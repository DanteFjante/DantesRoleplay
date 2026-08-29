---
id: mechanic.dnd2024.d20-test.state-effects
category: ruleset.dnd2024.core.gameplay.d20-tests
name: Read condition-derived D20 Test effects
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads a creature's stored D&D 2024 conditions and produces the closed, effect-free D20 Test
derivation report shared by later consumers. It is a composition child, not a condition writer or
a rolling mechanic.

## Matches

inspect condition-derived d20 effects
read condition test effects

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.conditions"],"description":"The creature whose absent, known-empty, or valid condition state is translated into D20 Test effects."}}}
```
