---
id: mechanic.dnd2024.conditions.write
category: ruleset.dnd2024.core.state.conditions
name: Record creature Conditions
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records known-empty Condition state, applies or clears explicit non-Exhaustion instances, or gains
and recovers Exhaustion. An optional source role supplies entity identity; caller text never does.

## Matches

record creature conditions
apply the poisoned condition
clear the prone condition
exhaust the character
recover a level of exhaustion

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.conditions"],"description":"The creature whose Condition state changes."},"source":{"components":[],"optional":true,"description":"An optional existing non-self source for scoped non-Exhaustion instances."}}}
```
