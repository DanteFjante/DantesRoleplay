---
id: mechanic.dnd2024.speed.read
category: dnd2024.core.data.speed
name: Read creature Speed diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Reads one creature's current Speed without changing it. It reports absent, malformed, or invalid
state rather than inventing a default walking speed.

## Matches
inspect creature speed
read creature speed diagnostics

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.creature.movement"],"description":"The creature whose present, absent, or malformed movement state is reported."}}}
```
