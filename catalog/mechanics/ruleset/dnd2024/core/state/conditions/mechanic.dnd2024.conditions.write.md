---
id: mechanic.dnd2024.conditions.write
category: ruleset.dnd2024.core.state.conditions
name: Record creature conditions
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records an empty known condition state, applies explicit SRD condition instances, or clears them.
An optional source role supplies entity identity rather than allowing caller-supplied source text.
It stores state only and does not infer why a condition applies or any mechanical consequence.

## Matches

record creature conditions
apply the poisoned condition
clear the prone condition

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.conditions"],"description":"The creature whose condition state is recorded, applied, or cleared."},"source":{"components":[],"optional":true,"description":"An optional existing entity whose identity scopes applied or cleared instances."}}}
```
