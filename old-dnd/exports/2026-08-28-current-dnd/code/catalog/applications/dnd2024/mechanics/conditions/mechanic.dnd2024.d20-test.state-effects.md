---
id: mechanic.dnd2024.d20-test.state-effects
category: ruleset.dnd2024.core.gameplay.d20-tests
name: Read Condition-derived D20 Test effects
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads stored Conditions and produces the shared, effect-free D20 Test and resource derivation report.
It is a composition child, not a writer or rolling mechanic.

## Matches

inspect condition-derived d20 effects
read condition test effects

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.conditions"],"description":"The creature whose absent, known-empty, or valid Condition state is translated."}}}
```
