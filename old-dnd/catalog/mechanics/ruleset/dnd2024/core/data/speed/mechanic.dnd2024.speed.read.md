---
id: mechanic.dnd2024.speed.read
category: ruleset.dnd2024.core.data.speed
name: Read creature Speed diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one creature's base Speed without changing it. It is safe for turn-lifecycle composition and
reports absent or invalid state as diagnostics rather than guessing a default walk Speed.

## Matches

inspect creature speed
read creature speed diagnostics

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.speed"],"description":"The creature whose present, absent, or malformed base Speed is reported."}}}
```
