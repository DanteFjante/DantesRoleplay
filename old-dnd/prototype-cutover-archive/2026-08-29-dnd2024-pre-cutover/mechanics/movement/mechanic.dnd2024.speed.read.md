---
id: mechanic.dnd2024.speed.read
category: ruleset.dnd2024.core.data.speed
name: Read creature Speed diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one creature's base-Speed profile without changing it. Missing, malformed, and invalid
state are reported explicitly rather than replaced with a guessed default.

## Matches

inspect creature speed
read creature speed diagnostics

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.movement"],"description":"The creature whose present, absent, or malformed base Speed is reported."}}}
```
